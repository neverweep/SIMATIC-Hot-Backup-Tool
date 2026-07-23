// <summary>
// 备份执行引擎。在后台线程中启动 ShadowSpawn 与 7z（隐藏窗口），
// 逐字节流式读取标准输出，解析进度，并通过事件上报阶段、进度、日志与完成状态，
// 同时支持协作式取消（终止整个进程树）。移植自 core/backup.py 的 run_backup。
// 增强版（R4）：入参由单盘改为有序多目标盘列表，主目标归档成功后复制到其余目标；
// 新增 Copy 阶段；进度解析沿用既有实现（R7 引擎侧已具备）；全仓不写盘日志（R6）。
// </summary>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SHBT.Core
{
    /// <summary>单次备份运行的结果。</summary>
    public class BackupResult
    {
        /// <summary>是否成功完成备份（主目标归档成功即视为成功）。</summary>
        public bool Success { get; set; }

        /// <summary>结果消息：成功时为产物 zip 路径，失败时为错误说明。</summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// 在后台线程执行热备份，并通过事件上报进度。
    /// 事件在 worker 线程上触发，订阅者需自行将其封送到 UI 线程（例如通过 Control.BeginInvoke）。
    /// </summary>
    public class BackupWorker
    {
        /// <summary>进度上报事件，参数为完成百分比；<c>null</c> 表示进度未知（不确定进度）。</summary>
        public event EventHandler<int?> Progress;

        /// <summary>阶段切换事件，参数为当前所处阶段。</summary>
        public event EventHandler<StageKey> StageChanged;

        /// <summary>底层工具输出日志行上报事件。</summary>
        public event EventHandler<string> Log;

        /// <summary>完成事件，无论成功或失败都会触发，参数为结果对象。</summary>
        public event EventHandler<BackupResult> Completed;

        private bool _archiveStarted;

        /// <summary>
        /// 执行多目标备份。应在后台 Task 中调用，以避免阻塞 UI 线程。
        /// </summary>
        /// <param name="projectPath">待备份项目目录的绝对路径。</param>
        /// <param name="drives">有序目标盘符列表（可含或不含尾随冒号）；首个为 ★ 主目标。</param>
        /// <param name="typeKey">项目类型键（来自 ProjectDetectionResult.Type 的枚举名）。</param>
        /// <param name="opts">本次备份的选项；为 <c>null</c> 时使用默认值。</param>
        /// <param name="token">用于协作式取消的令牌。</param>
        public void Run(string projectPath, List<string> drives, string typeKey, BackupOptions opts, CancellationToken token)
        {
            try
            {
                RaiseStage(StageKey.Prepare);

                if (drives == null || drives.Count == 0)
                {
                    RaiseStage(StageKey.Error);
                    RaiseCompleted(false, "未选择任何目标盘符 / No target drive selected");
                    return;
                }

                // R4：首个勾选盘为 ★ 主目标，其余为目标副本。
                string mainDrive = (drives[0] ?? string.Empty).TrimEnd(':').ToUpperInvariant();

                // 将全部勾选目标盘（含主目标）一并排除出 VSS 卷影盘符候选，
                // 防止 ShadowSpawn 把源卷挂到某个目标盘上导致备份写错位置（#2）。
                var shadowExclude = new HashSet<string>(
                    drives.Select(d => (d ?? string.Empty).TrimEnd(':').ToUpperInvariant()),
                    StringComparer.OrdinalIgnoreCase);
                BackupCommand cmd = BackupCommandBuilder.Build(projectPath, mainDrive, typeKey, opts, shadowExclude);

                try
                {
                    Directory.CreateDirectory(cmd.TargetDir);
                }
                catch (Exception exc)
                {
                    RaiseStage(StageKey.Error);
                    RaiseCompleted(false, "无法创建目标目录 " + cmd.TargetDir + "：" + exc.Message);
                    return;
                }

                RaiseLog("> " + cmd.FullCommand);
                RaiseLog(string.Format(SHBT.Ui.Localization.Get("log_target_file"), cmd.TargetZip));

                var psi = new ProcessStartInfo
                {
                    FileName = cmd.ShadowSpawnExe,
                    Arguments = BuildArguments(cmd.PidCommand),
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = cmd.ShadowSource,
                    Verb = string.Empty
                };

                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();

                    // #1：stdout 由独立线程逐字节读取，stderr 同样必须排空，否则一旦写入
                    // 超过管道缓冲（约 4KB）子进程会阻塞，导致 HasExited 永远为 false 而轮询死循环。
                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            RaiseLog(e.Data);
                        }
                    };
                    proc.BeginErrorReadLine();

                    RaiseStage(StageKey.Shadow);

                    _archiveStarted = false;
                    var lineBuf = new List<byte>(1024);
                    // 独立的读取线程负责解析标准输出并触发事件，避免阻塞主轮询循环。
                    var readerTask = Task.Run(() => ReadOutputLoop(proc, token, lineBuf));

                    // 轮询进程完成或取消状态，避免阻塞读取线程。
                    while (true)
                    {
                        if (token.IsCancellationRequested)
                        {
                            KillTree(proc);
                            try { readerTask.Wait(2000); }
                            catch { /* 忽略 */ }
                            RaiseStage(StageKey.Cancel);
                            RaiseCompleted(false, "用户已取消备份 / Backup cancelled by user");
                            return;
                        }

                        if (proc.HasExited)
                        {
                            break;
                        }

                        Thread.Sleep(100);
                    }

                    try { readerTask.Wait(2000); }
                    catch { /* 忽略 */ }

                    int exitCode = 0;
                    try { exitCode = proc.ExitCode; }
                    catch { exitCode = -1; }

                    // 7-Zip 退出码语义：0 = 成功；1 = 警告（个别文件未能读取，但归档通常
                    // 仍有效可用）——两者均视为成功并完成复制阶段；2 及以上为致命错误。
                    if (exitCode == 0 || exitCode == 1)
                    {
                        if (exitCode == 1)
                        {
                            RaiseLog(SHBT.Ui.Localization.Get("warn_7zip_warning"));
                        }

                        // R4：主目标归档成功 → 进入复制阶段，将产物复制到其余勾选目标。
                        RaiseStage(StageKey.Copy);
                        CopyToTargets(cmd, drives, opts, mainDrive, cmd.TargetZip);

                        RaiseStage(StageKey.Done);
                        // 成功以"主目标归档成功"为准；最近备份记录的落盘由 UI 层（MainForm）负责（R9）。
                        RaiseCompleted(true, cmd.TargetZip);
                    }
                    else
                    {
                        RaiseStage(StageKey.Error);
                        RaiseCompleted(false, "7-Zip 异常退出（代码 " + exitCode + "），请检查磁盘空间或写权限。");
                    }
                }
            }
            catch (Exception exc)
            {
                RaiseStage(StageKey.Error);
                RaiseCompleted(false, "启动 ShadowSpawn 失败：" + exc.Message);
            }
        }

        /// <summary>
        /// 将主目标产物 zip 复制到其余勾选目标盘。复制失败仅记 warn 日志，
        /// 不阻断整体流程、不重试（R4）。
        /// </summary>
        private void CopyToTargets(BackupCommand cmd, List<string> drives, BackupOptions opts, string mainDrive, string mainZip)
        {
            if (drives == null || drives.Count <= 1)
            {
                return;
            }

            string outputSubdir = string.IsNullOrEmpty(opts.OutputSubdir) ? "Backups" : opts.OutputSubdir;
            string mainTargetDir = mainDrive + ":\\" + outputSubdir;

            foreach (string drive in drives)
            {
                string other = (drive ?? string.Empty).TrimEnd(':').ToUpperInvariant();
                if (string.Equals(other, mainDrive, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // 跳过主目标自身
                }

                string otherTargetDir = other + ":\\" + outputSubdir;
                string destZip = mainZip;
                // 用"其余目标根目录"替换"主目标根目录"前缀，得到同名复制目的地。
                if (mainZip.Length > mainTargetDir.Length &&
                    mainZip.StartsWith(mainTargetDir, StringComparison.OrdinalIgnoreCase))
                {
                    destZip = otherTargetDir + mainZip.Substring(mainTargetDir.Length);
                }

                // 防范前缀替换失败时 destZip 兜底等于 mainZip 导致的自我复制。
                if (string.Equals(destZip, mainZip, StringComparison.OrdinalIgnoreCase))
                {
                    RaiseLog("Skipped self-copy to " + destZip);
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(otherTargetDir);
                    File.Copy(mainZip, destZip, true);
                    RaiseLog("Copied to " + destZip);
                }
                catch (Exception exc)
                {
                    // R4：复制失败仅告警并跳过，不影响其它目标与主结果。
                    RaiseLog("Failed to copy to " + destZip + " (skipped): " + exc.Message);
                }
            }
        }

        private void ReadOutputLoop(Process proc, CancellationToken token, List<byte> lineBuf)
        {
            try
            {
                Stream stream = proc.StandardOutput.BaseStream;
                int b;
                while ((b = stream.ReadByte()) != -1)
                {
                    if (b == '\r' || b == '\n')
                    {
                        if (lineBuf.Count > 0)
                        {
                            string text = DecodeLine(lineBuf);
                            lineBuf.Clear();
                            if (!string.IsNullOrEmpty(text))
                            {
                                RaiseLog(text);
                                if (!_archiveStarted)
                                {
                                    _archiveStarted = true;
                                    RaiseStage(StageKey.Archive);
                                }

                                ParseProgress(text);
                            }
                        }
                    }
                    else
                    {
                        lineBuf.Add((byte)b);
                    }
                }

                // 刷新缓冲区中残留的末行（以文件结尾而非换行符结束的情况）。
                if (lineBuf.Count > 0)
                {
                    string text = DecodeLine(lineBuf);
                    if (!string.IsNullOrEmpty(text))
                    {
                        RaiseLog(text);
                        if (!_archiveStarted)
                        {
                            _archiveStarted = true;
                            RaiseStage(StageKey.Archive);
                        }

                        ParseProgress(text);
                    }
                }
            }
            catch
            {
                // 进程因取消被终止导致流关闭，此处捕获并忽略。
            }
        }

        private static string BuildArguments(List<string> command)
        {
            if (command == null || command.Count == 0)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            // command[0] 为可执行文件本身，已对应 ProcessStartInfo.FileName，故从下标 1 起拼接参数。
            for (int i = 1; i < command.Count; i++)
            {
                parts.Add(QuoteIfNeeded(command[i]));
            }

            return string.Join(" ", parts);
        }

        private static string QuoteIfNeeded(string text)
        {
            if (text.IndexOf(' ') >= 0 || text.IndexOf('\t') >= 0)
            {
                return "\"" + text + "\"";
            }

            return text;
        }

        private static string DecodeLine(List<byte> bytes)
        {
            byte[] arr = bytes.ToArray();
            try
            {
                return Encoding.UTF8.GetString(arr).TrimEnd('\r', '\n');
            }
            catch
            {
                try
                {
                    // 回退到系统默认代码页（通常为本地 ANSI），以兼容非 UTF-8 输出。
                    return Encoding.GetEncoding(0).GetString(arr).TrimEnd('\r', '\n');
                }
                catch
                {
                    return Encoding.ASCII.GetString(arr);
                }
            }
        }

        private void ParseProgress(string line)
        {
            string stripped = line.Trim();
            if (!stripped.EndsWith("%"))
            {
                return;
            }

            string digits = stripped.Substring(0, stripped.Length - 1).Trim();
            if (digits.Length == 0)
            {
                return;
            }

            bool allDigits = true;
            foreach (char c in digits)
            {
                if (c < '0' || c > '9')
                {
                    allDigits = false;
                    break;
                }
            }

            if (allDigits && int.TryParse(digits, out int value) && value >= 0 && value <= 100)
            {
                RaiseProgress(value);
            }
        }

        private static void KillTree(Process proc)
        {
            try
            {
                int pid = proc.Id;
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/F /T /PID " + pid,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var killer = Process.Start(psi))
                {
                    killer?.WaitForExit(2000);
                }
            }
            catch
            {
                try { proc.Kill(); }
                catch { /* 忽略 */ }
            }
        }

        private void RaiseStage(StageKey key) { StageChanged?.Invoke(this, key); }
        private void RaiseProgress(int pct) { Progress?.Invoke(this, pct); }
        private void RaiseLog(string msg) { Log?.Invoke(this, msg); }
        private void RaiseCompleted(bool success, string message)
        {
            Completed?.Invoke(this, new BackupResult { Success = success, Message = message });
        }
    }
}

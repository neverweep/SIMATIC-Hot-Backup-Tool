// <summary>
// 为热备份构建 ShadowSpawn + 7z 的命令结构。
// 忠实对应 core/backup.py 的 build_archive_command / find_free_drive_letter。
// 增强版（R2/R3/R4）：仅取启用规则进 -xr!。
// R3 恢复记录（-rr）因当前 7-Zip 19.00 不支持 -rr 开关且输出为 .zip 格式（恢复记录为 7z 专有）
// 而暂未实现，详见 Build 内说明。
// 新增 ComputeTargetZip 供多目标复制阶段计算目标路径。
// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SHBT.Core
{
    /// <summary>一次备份运行所需的、已完全解析的命令结构。</summary>
    public class BackupCommand
    {
        /// <summary>ShadowSpawn 可执行文件完整路径。</summary>
        public string ShadowSpawnExe { get; set; }
        /// <summary>7-Zip 可执行文件完整路径。</summary>
        public string SevenZipExe { get; set; }
        /// <summary>需快照的源卷目录（项目所在父目录）。</summary>
        public string ShadowSource { get; set; }
        /// <summary>卷影副本挂载所用的盘符（不含冒号）。</summary>
        public string ShadowLetter { get; set; }
        /// <summary>项目目录名（不含路径）。</summary>
        public string ProjectName { get; set; }
        /// <summary>备份产物所在的目标目录。</summary>
        public string TargetDir { get; set; }
        /// <summary>备份产物 zip 文件的完整路径。</summary>
        public string TargetZip { get; set; }
        /// <summary>7-Zip 归档参数列表（argv[0] 为 7z.exe）。</summary>
        public List<string> ArchiveArgs { get; set; }
        /// <summary>供 ShadowSpawn 启动的整条命令（argv[0] 为 ShadowSpawn.exe）。</summary>
        public List<string> PidCommand { get; set; }
        /// <summary>已加引号转义、可直接展示的完整命令行字符串。</summary>
        public string FullCommand { get; set; }
    }

    /// <summary>
    /// 构建备份所需的 ShadowSpawn + 7z 调用，包含为 VSS 挂载点自动查找空闲盘符。
    /// </summary>
    public static class BackupCommandBuilder
    {
        /// <summary>
        /// 构建将 <paramref name="projectPath"/> 作为 <paramref name="typeKey"/> 项目
        /// 归档到 <paramref name="drive"/> 盘的命令。
        /// </summary>
        /// <param name="projectPath">待备份项目目录的绝对路径。</param>
        /// <param name="drive">目标盘符（可含或不含尾随冒号）。</param>
        /// <param name="typeKey">项目类型键（枚举名）。</param>
        /// <param name="opts">本次备份的选项；为 <c>null</c> 时使用默认值。</param>
        /// <returns>已完全解析的 <see cref="BackupCommand"/>。</returns>
        public static BackupCommand Build(string projectPath, string drive, string typeKey, BackupOptions opts, HashSet<string> excludeShadowLetters = null)
        {
            opts = opts ?? new BackupOptions();
            projectPath = Path.GetFullPath(projectPath);
            string projectName = Path.GetFileName(projectPath.TrimEnd('\\', '/'));
            // 需快照的卷为项目所在的父目录，而非项目目录本身（以便归档其中的整个项目）。
            string shadowSource = Path.GetDirectoryName(projectPath);

            string compressionLevel = string.IsNullOrEmpty(opts.CompressionLevel) ? "max" : opts.CompressionLevel;
            // R2：仅取「启用且 Pattern 非空」的规则进入 -xr!。
            List<string> excludeRules = NormalizeEnabledRules(opts.ExcludeRules);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // 计算 VSS 卷影挂载盘符：以 opts.ShadowLetter（默认 Q）为首选，
            // 排除所有已挂载盘符以及用户勾选的目标盘，避免挂到真实盘/目标盘上（#2）。
            var usedLetters = new HashSet<string>(DriveEnumerator.GetUsedLetters(), StringComparer.OrdinalIgnoreCase);
            if (excludeShadowLetters != null)
            {
                foreach (string ex in excludeShadowLetters)
                {
                    if (!string.IsNullOrEmpty(ex))
                    {
                        usedLetters.Add(ex.ToUpperInvariant());
                    }
                }
            }

            string preferred = string.IsNullOrEmpty(opts.ShadowLetter) ? "Q" : opts.ShadowLetter;
            string shadowLetter = FindFreeDriveLetter(preferred, usedLetters).ToUpperInvariant();

            string sevenZipExe = BinLocator.GetToolPath("7z.exe");
            string shadowSpawnExe = BinLocator.GetToolPath("ShadowSpawn.exe");

            drive = (drive ?? string.Empty).TrimEnd(':').ToUpperInvariant();
            // 复用统一路径生成器，确保主目标 zip 命名与复制目的地一致（#5）。
            string targetZip = BuildTargetZipPath(drive, opts, projectPath, typeKey, timestamp);
            string targetDir = Path.GetDirectoryName(targetZip);

            string mxFlag = ConfigManager.CompressionLevels.TryGetValue(compressionLevel, out string mx)
                ? mx
                : "-mx5";

            // 归档源位于卷影挂载盘上的项目目录。
            string archiveSource = shadowLetter + ":\\" + projectName;

            var archiveArgs = new List<string>
            {
                sevenZipExe,
                "a",
                "-bb0",
                "-bsp1",
                mxFlag,
                targetZip,
                archiveSource
            };

            foreach (string rule in excludeRules)
            {
                archiveArgs.Add("-xr!" + rule);
            }

            // ShadowSpawn 调用参数：可执行文件、源卷、挂载点，其后追加 7z 的全部参数。
            var pidCommand = new List<string> { shadowSpawnExe, shadowSource, shadowLetter + ":" };
            pidCommand.AddRange(archiveArgs);

            string fullCommand = string.Join(" ", pidCommand.ConvertAll(QuoteIfNeeded));

            return new BackupCommand
            {
                ShadowSpawnExe = shadowSpawnExe,
                SevenZipExe = sevenZipExe,
                ShadowSource = shadowSource,
                ShadowLetter = shadowLetter,
                ProjectName = projectName,
                TargetDir = targetDir,
                TargetZip = targetZip,
                ArchiveArgs = archiveArgs,
                PidCommand = pidCommand,
                FullCommand = fullCommand
            };
        }

        /// <summary>
        /// 将项目类型枚举名映射为备份文件名中的简短类型标记：
        /// WinCC_V7X → "CLS"（Classic），STEP7_V5X → "STEP7_CLS"，TIA_Portal → "TIA"；
        /// 其余原样返回。文件名形如 <c>项目名_CLS_20260101_120000.zip</c>。
        /// </summary>
        /// <param name="typeKey">项目类型枚举名（<c>ProjectType.ToString()</c>）。</param>
        /// <returns>文件名用的类型标记。</returns>
        private static string FileTypeToken(string typeKey)
        {
            if (string.IsNullOrEmpty(typeKey))
            {
                return "Unknown";
            }

            if (typeKey.IndexOf("WinCC", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "CLS";
            }

            if (typeKey.IndexOf("STEP7", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "STEP7_CLS";
            }

            if (typeKey.IndexOf("TIA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "TIA";
            }

            return typeKey;
        }

        /// <summary>
        /// 返回任意目标盘上备份 zip 的完整路径（命名规则与主目标完全一致）。
        /// 供多目标复制阶段（R4）计算"其余目标盘的复制目的地"时使用。
        /// </summary>
        /// <param name="drive">目标盘符（可含或不含尾随冒号）。</param>
        /// <param name="opts">本次备份选项；为 <c>null</c> 时使用默认值。</param>
        /// <param name="projectPath">待备份项目目录的绝对路径。</param>
        /// <param name="typeKey">项目类型键（枚举名）。</param>
        /// <returns>目标盘上备份产物的完整路径。</returns>
        /// <summary>
        /// 返回目标盘上备份 zip 的完整路径（命名规则与主目标完全一致）。
        /// 供多目标复制阶段（R4）计算"其余目标盘的复制目的地"时使用。
        /// 为避免与主目标归档跨秒导致时间戳不一致（#5），可传入与主目标相同的
        /// <paramref name="timestamp"/>；为 <c>null</c> 时自动取当前时间。
        /// </summary>
        public static string ComputeTargetZip(string drive, BackupOptions opts, string projectPath, string typeKey, string timestamp = null)
        {
            return BuildTargetZipPath(drive, opts, projectPath, typeKey, timestamp);
        }

        /// <summary>按统一命名规则生成目标 zip 完整路径；供 <see cref="Build"/> 与
        /// <see cref="ComputeTargetZip"/> 复用，确保主目标与复制目的地时间戳一致（#5）。</summary>
        private static string BuildTargetZipPath(string drive, BackupOptions opts, string projectPath, string typeKey, string timestamp)
        {
            opts = opts ?? new BackupOptions();
            projectPath = Path.GetFullPath(projectPath);
            string projectName = Path.GetFileName(projectPath.TrimEnd('\\', '/'));
            string outputSubdir = string.IsNullOrEmpty(opts.OutputSubdir) ? "Backups" : opts.OutputSubdir;
            if (string.IsNullOrEmpty(timestamp))
            {
                timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }

            drive = (drive ?? string.Empty).TrimEnd(':').ToUpperInvariant();
            string targetDir = drive + ":\\" + outputSubdir;
            string zipName = string.Format("{0}_{1}_{2}.zip", projectName, FileTypeToken(typeKey), timestamp);
            return Path.Combine(targetDir, zipName);
        }

        /// <summary>
        /// 从排除规则集合中提取「启用且 Pattern 非空」的规则模式并规范化。
        /// </summary>
        /// <param name="rules">原始规则集合；为 <c>null</c> 时返回空列表。</param>
        /// <returns>规范化后的启用规则模式列表。</returns>
        private static List<string> NormalizeEnabledRules(List<ExcludeRule> rules)
        {
            var enabled = new List<string>();
            if (rules == null)
            {
                return enabled;
            }

            foreach (ExcludeRule rule in rules)
            {
                if (rule != null && rule.Enabled && !string.IsNullOrWhiteSpace(rule.Pattern))
                {
                    enabled.Add(rule.Pattern.Trim());
                }
            }

            return ConfigManager.NormalizeExcludes(enabled);
        }

        /// <summary>
        /// 为 VSS 挂载点查找一个未被占用的盘符。优先使用 <paramref name="preferred"/>，
        /// 其后按 Z..A 逆序扫描，最终仍无可用盘符时回退到首选盘符。
        /// </summary>
        /// <param name="preferred">首选盘符（如 "Q"）。</param>
        /// <param name="exclude">需排除占用的盘符集合（可为 <c>null</c>）。</param>
        /// <returns>可用的盘符（大写，不含冒号）。</returns>
        public static string FindFreeDriveLetter(string preferred, HashSet<string> exclude)
        {
            var used = new HashSet<string>(DriveEnumerator.GetUsedLetters(), StringComparer.OrdinalIgnoreCase);
            if (exclude != null)
            {
                foreach (string e in exclude)
                {
                    used.Add(e.ToUpperInvariant());
                }
            }

            preferred = (preferred ?? "Q").ToUpperInvariant();
            if (!used.Contains(preferred))
            {
                return preferred;
            }

            for (char c = 'Z'; c >= 'A'; c--)
            {
                string letter = c.ToString();
                if (!used.Contains(letter))
                {
                    return letter;
                }
            }

            return preferred;
        }

        private static string QuoteIfNeeded(string text)
        {
            if (text.IndexOf(' ') >= 0 || text.IndexOf('\t') >= 0)
            {
                return "\"" + text + "\"";
            }

            return text;
        }
    }
}

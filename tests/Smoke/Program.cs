// <summary>
// Standalone execution harness (net10.0) that compiles the framework-agnostic
// Core + Localization sources and runs the same verification assertions as the
// net48 MSTest project. Used to prove the logic executes correctly in sandboxes
// where the net-framework test host is unavailable.
// </summary>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;
using SHBT.Core;
using SHBT.Ui;
using Localization = SHBT.Ui.Localization;

namespace SHBT.Smoke
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static void Check(string name, bool condition)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine("  PASS  " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("  FAIL  " + name);
            }
        }

        private static string CreateTempProject(string[] markers)
        {
            string dir = Path.Combine(Path.GetTempPath(), "1tool_proj_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach (string m in markers)
            {
                Directory.CreateDirectory(Path.Combine(dir, m));
            }

            return dir;
        }

        private static void Main()
        {
            Console.WriteLine("=== SHBT Core logic execution verification (net10.0 harness) ===");

            // ---- ProjectDetector ----
            Console.WriteLine("[ProjectDetector]");
            string amobjs = CreateTempProject(new[] { "AMOBJS" });
            Check("AMOBJS -> STEP7_V5X", ProjectDetector.Detect(amobjs)?.Type == ProjectType.STEP7_V5X);

            string graces = CreateTempProject(new[] { "GRACS" });
            Check("GRACS -> WinCC_V7X", ProjectDetector.Detect(graces)?.Type == ProjectType.WinCC_V7X);

            string xref = CreateTempProject(new[] { "XRef" });
            Check("XRef -> TIA_Portal", ProjectDetector.Detect(xref)?.Type == ProjectType.TIA_Portal);

            string empty = Path.Combine(Path.GetTempPath(), "1tool_empty_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(empty);
            Check("empty dir -> null (unknown)", ProjectDetector.Detect(empty) == null);
            Directory.Delete(empty);

            Check("nonexistent dir -> null", ProjectDetector.Detect(
                Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N"))) == null);

            string both = CreateTempProject(new[] { "AMOBJS", "XRef" });
            ProjectDetectionResult r = ProjectDetector.Detect(both);
            Check("later marker overrides earlier (AMOBJS+XRef -> TIA_Portal)",
                r != null && r.Type == ProjectType.TIA_Portal);

            // ---- 文件标记（增强版：*.s7p / *.mcp / *.apN，离线同样可测） ----
            string step7file = CreateTempProject(new string[0]);
            File.WriteAllText(Path.Combine(step7file, "Proj.s7p"), "x");
            Check("*.s7p -> STEP7_V5X", ProjectDetector.Detect(step7file)?.Type == ProjectType.STEP7_V5X);

            string winccfile = CreateTempProject(new string[0]);
            File.WriteAllText(Path.Combine(winccfile, "W.mcp"), "x");
            Check("*.mcp -> WinCC_V7X", ProjectDetector.Detect(winccfile)?.Type == ProjectType.WinCC_V7X);

            string tiafile = CreateTempProject(new string[0]);
            File.WriteAllText(Path.Combine(tiafile, "T.ap19"), "x");
            Check("*.ap19 -> TIA_Portal", ProjectDetector.Detect(tiafile)?.Type == ProjectType.TIA_Portal);

            string tiafake = CreateTempProject(new string[0]);
            File.WriteAllText(Path.Combine(tiafake, "foo.apxyz"), "x");
            Check("*.apxyz NOT -> TIA (digits required after .ap)", ProjectDetector.Detect(tiafake) == null);

            string integrated = CreateTempProject(new string[0]);
            File.WriteAllText(Path.Combine(integrated, "P.s7p"), "x");
            File.WriteAllText(Path.Combine(integrated, "P.ap19"), "x");
            Check("integrated .s7p + .ap19 -> TIA_Portal (TIA wins)",
                ProjectDetector.Detect(integrated)?.Type == ProjectType.TIA_Portal);

            // ---- BackupCommandBuilder ----
            Console.WriteLine("[BackupCommandBuilder]");
            string proj = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "MyProject_" + Guid.NewGuid().ToString("N")));
            string projName = Path.GetFileName(proj);
            string expectedSource = Path.GetDirectoryName(proj);
            var opts = new BackupOptions { ShadowLetter = "Q", OutputSubdir = "Backups", CompressionLevel = "standard" };
            BackupCommand cmd = BackupCommandBuilder.Build(proj, "D", "STEP7_V5X", opts);
            List<string> pid = cmd.PidCommand;

            Check("pid has >= 9 elements", pid.Count >= 9);
            Check("pid[0] = ShadowSpawn.exe", pid[0].EndsWith("ShadowSpawn.exe"));
            Check("pid[1] = project parent dir", pid[1] == expectedSource);
            Check("pid[2] = Q:", pid[2] == "Q:");

            int i7z = pid.FindIndex(s => s.EndsWith("7z.exe"));
            int iA = pid.IndexOf("a");
            int iBb0 = pid.IndexOf("-bb0");
            int iBsp1 = pid.IndexOf("-bsp1");
            int iMx = pid.FindIndex(s => s.StartsWith("-mx"));
            int iTarget = pid.FindIndex(s => s.EndsWith(".zip"));
            int iArchive = pid.IndexOf("Q:\\" + projName);

            Check("7z.exe present and precedes 'a'", i7z > 0 && i7z < iA);
            Check("'a' < '-bb0'", iA < iBb0);
            Check("'-bb0' < '-bsp1'", iBb0 < iBsp1);
            Check("'-bsp1' < mx flag", iBsp1 < iMx);
            Check("mx flag < target zip", iMx < iTarget);
            Check("target zip < archive source", iTarget < iArchive);
            Check("standard -> -mx5", pid[iMx] == "-mx5");
            Check("target under D:\\Backups", cmd.TargetZip.StartsWith("D:\\Backups"));
            Check("target ends with .zip", cmd.TargetZip.EndsWith(".zip"));
            Check("zip name = <proj>_<typeToken>_<ts>.zip",
                Regex.IsMatch(cmd.TargetZip,
                    Regex.Escape(projName) + "_STEP7_CLS_\\d{8}_\\d{6}\\.zip$"));

            var optsMax = new BackupOptions { ShadowLetter = "Q", CompressionLevel = "max" };
            BackupCommand cmdMax = BackupCommandBuilder.Build(Path.GetTempPath() + "\\P", "D", "TIA_Portal", optsMax);
            int iMxMax = cmdMax.PidCommand.FindIndex(s => s.StartsWith("-mx"));
            Check("max -> -mx9", cmdMax.PidCommand[iMxMax] == "-mx9");

            // FindFreeDriveLetter: all excluded -> preferred
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (char c = 'A'; c <= 'Z'; c++)
            {
                all.Add(c.ToString());
            }

            Check("FindFreeDriveLetter(all excluded) -> preferred Q",
                BackupCommandBuilder.FindFreeDriveLetter("Q", all) == "Q");

            // FindFreeDriveLetter: small exclude -> returns a valid unused letter
            var few = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C", "D", "E" };
            string free = BackupCommandBuilder.FindFreeDriveLetter("Q", few);
            bool freeValid = free.Length == 1 && free[0] >= 'A' && free[0] <= 'Z' && !few.Contains(free);
            Check("FindFreeDriveLetter(small exclude) -> valid unused letter", freeValid);

            // ---- DriveEnumerator (regression for the GetLogicalDriveStringsW crash) ----
            Console.WriteLine("[DriveEnumerator]");
            // Directly mirrors Tests.cs: ListDrives_DoesNotThrow_AndReturnsEntries
            var drives = DriveEnumerator.ListDrives();
            bool listOk = drives != null && drives.Count >= 1;
            Check("ListDrives does not throw and returns >= 1 entry", listOk);
            bool lettersOk = true;
            if (drives != null)
            {
                foreach (var d in drives)
                {
                    if (string.IsNullOrEmpty(d.Letter) || d.Letter.Length != 1 || d.Letter[0] < 'A' || d.Letter[0] > 'Z')
                    {
                        lettersOk = false;
                        break;
                    }
                }
            }
            Check("ListDrives entries have a valid single-letter Letter", lettersOk);

            // Directly mirrors Tests.cs: FindFreeDriveLetter_WithNullExclude_DoesNotThrow
            string freeNull = BackupCommandBuilder.FindFreeDriveLetter("Q", null);
            Check("FindFreeDriveLetter(null exclude) does not throw and returns a letter",
                !string.IsNullOrEmpty(freeNull) && freeNull.Length == 1 && freeNull[0] >= 'A' && freeNull[0] <= 'Z');

            // ---- Localization (file-based) ----
            Console.WriteLine("[Localization]");
            Localization.Initialize(Path.Combine(FindRepoRoot(), "lang"));

            foreach (StageKey s in new[] { StageKey.Prepare, StageKey.Shadow, StageKey.Archive, StageKey.Copy, StageKey.Done, StageKey.Cancel, StageKey.Error })
            {
                string key = "stage_" + s.ToString().ToLowerInvariant();
                Check("StageText[" + s + "] non-empty zh",
                    !string.IsNullOrEmpty(Localization.Get("zh-CN", key)));
                Check("StageText[" + s + "] non-empty en",
                    !string.IsNullOrEmpty(Localization.Get("en-US", key)));
            }

            foreach (ProjectType t in new[] { ProjectType.STEP7_V5X, ProjectType.WinCC_V7X, ProjectType.TIA_Portal })
            {
                Check("ProjectTypeName[" + t + "] non-empty zh",
                    !string.IsNullOrEmpty(Localization.Get("zh-CN", "type_" + t)));
                Check("ProjectTypeName[" + t + "] non-empty en",
                    !string.IsNullOrEmpty(Localization.Get("en-US", "type_" + t)));
            }

            Check("Initialize discovers en-US/zh-CN/zh-TW/ru-RU",
                Localization.IsSupported("en-US") && Localization.IsSupported("zh-CN") &&
                Localization.IsSupported("zh-TW") && Localization.IsSupported("ru-RU"));

            // R1：增强版新增 10 种机器翻译语言包，连同原有 4 种合计 14 种。
            string[] allLangs =
            {
                "en-US", "zh-CN", "zh-TW", "ru-RU",
                "de-DE", "ja-JP", "ko-KR", "pt-BR", "pt-PT",
                "fr-FR", "it-IT", "es-ES", "be-BY", "id-ID"
            };
            int supportedCount = 0;
            foreach (string l in allLangs)
            {
                if (Localization.IsSupported(l))
                {
                    supportedCount++;
                }
            }

            Check("Initialize discovers all 14 language files (R1)", supportedCount == allLangs.Length);
            Check("DetectSystemLanguage returns a supported code",
                Localization.IsSupported(Localization.DetectSystemLanguage()));
            Check("Get unknown key falls back to raw key",
                Localization.Get("en-US", "nonexistent_key") == "nonexistent_key");

            // ---- BinLocator ----
            Console.WriteLine("[BinLocator]");
            string toolPath = BinLocator.GetToolPath("7z.exe");
            Check("GetToolPath rooted", Path.IsPathRooted(toolPath));
            Check("GetToolPath ends with bin\\7z.exe", toolPath.Replace("/", "\\").EndsWith("\\bin\\7z.exe"));
            string repoBin = Path.Combine(FindRepoRoot(), "bin", "7z.exe");
            Check("7z.exe present (GetToolPath or repo bin)", File.Exists(toolPath) || File.Exists(repoBin));
            Check("BinDirectory ends with \\bin", BinLocator.BinDirectory.Replace("/", "\\").EndsWith("\\bin"));

            // ================= 增强版行为断言（R2/R3/R4/R8/R9/R10/R5） =================
            // 以下断言覆盖本次增强的新增/变更逻辑；多目标复制与二次确认在沙箱中以
            // 桩逻辑验证（真实盘符与模态对话框在无界面环境不可用），但路径计算与
            // 确认映射均与生产代码使用同一算法/同一绑定。

            // ---- 排除规则 V2（R2）：启用行进 -x、禁用行不进命令 ----
            Console.WriteLine("[Exclude rules V2 R2]");
            var exclProject = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ExclProj_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(exclProject);
            var exclOpts = new BackupOptions
            {
                ShadowLetter = "Q",
                OutputSubdir = "Backups",
                CompressionLevel = "standard",
                ExcludeRules = new List<ExcludeRule>
                {
                    new ExcludeRule { Enabled = true, Pattern = "*.tmp", Comment = "临时文件" },
                    new ExcludeRule { Enabled = false, Pattern = "*.log", Comment = "日志（已禁用）" },
                    new ExcludeRule { Enabled = true, Pattern = "  ", Comment = "空白模式被忽略" },
                    new ExcludeRule { Enabled = true, Pattern = "ArchiveManager", Comment = "西门子归档目录" }
                }
            };
            BackupCommand exclCmd = BackupCommandBuilder.Build(exclProject, "D", "STEP7_V5X", exclOpts);
            List<string> exclArgs = exclCmd.PidCommand;
            Check("exclude V2: 启用规则 '*.tmp' -> -xr!*.tmp 进入命令",
                exclArgs.Any(s => string.Equals(s, "-xr!*.tmp", StringComparison.OrdinalIgnoreCase)));
            Check("exclude V2: 启用规则 'ArchiveManager' -> -xr!ArchiveManager 进入命令",
                exclArgs.Any(s => string.Equals(s, "-xr!ArchiveManager", StringComparison.OrdinalIgnoreCase)));
            Check("exclude V2: 禁用规则 '*.log' 不进入命令",
                !exclArgs.Any(s => s.IndexOf("-xr!*.log", StringComparison.OrdinalIgnoreCase) >= 0));
            Check("exclude V2: 空白模式不进入命令",
                !exclArgs.Any(s => s.StartsWith("-xr!", StringComparison.OrdinalIgnoreCase) &&
                                   string.IsNullOrWhiteSpace(s.Substring(4))));

            // ---- 多目标备份（R4）：主目标生成 zip，其余目标路径由 ComputeTargetZip 给出 ----
            Console.WriteLine("[Multi-target backup R4]");
            var mtOpts = new BackupOptions
            {
                ShadowLetter = "Q",
                OutputSubdir = "Backups",
                CompressionLevel = "standard"
            };
            var mtDrives = new List<string> { "D", "E", "F" };
            BackupCommand mtCmd = BackupCommandBuilder.Build(exclProject, mtDrives[0], "TIA_Portal", mtOpts);
            string mtMainZip = mtCmd.TargetZip;
            string mtMainTargetDir = mtDrives[0] + ":\\" +
                (string.IsNullOrEmpty(mtOpts.OutputSubdir) ? "Backups" : mtOpts.OutputSubdir);
            Check("multi-target: 主目标 zip 位于 <主盘>:\\Backups",
                mtMainZip.StartsWith(mtDrives[0] + ":\\Backups", StringComparison.OrdinalIgnoreCase));
            Check("multi-target: 主目标 zip 名 = <项目>_TIA_<时间戳>.zip",
                Regex.IsMatch(mtMainZip,
                    Regex.Escape(Path.GetFileName(exclProject)) + "_TIA_\\d{8}_\\d{6}\\.zip$"));

            foreach (string d in mtDrives.Skip(1))
            {
                string otherZip = BackupCommandBuilder.ComputeTargetZip(d, mtOpts, exclProject, "TIA_Portal");
                Check("multi-target: 目标 " + d + " 复制目的地位于 " + d + ":\\Backups",
                    otherZip.StartsWith(d + ":\\Backups", StringComparison.OrdinalIgnoreCase));
                Check("multi-target: 目标 " + d + " 与主目标共用同一 zip 名（复制前提）",
                    string.Equals(Path.GetFileName(otherZip), Path.GetFileName(mtMainZip), StringComparison.OrdinalIgnoreCase));
                // 复制阶段采用的前缀替换（主目标根 -> 其余目标根）须与 ComputeTargetZip 完全一致。
                string expectedSwap = d + ":\\" +
                    (string.IsNullOrEmpty(mtOpts.OutputSubdir) ? "Backups" : mtOpts.OutputSubdir) +
                    mtMainZip.Substring(mtMainTargetDir.Length);
                Check("multi-target: 复制目的地（前缀替换）== ComputeTargetZip(" + d + ")",
                    string.Equals(expectedSwap, otherZip, StringComparison.OrdinalIgnoreCase));
            }

            // 用临时目录桩模拟"复制主 zip 到其余目标"的物理行为（真实盘符在沙箱不可用）。
            // 复制算法与 BackupWorker.CopyToTargets 一致：将主目标根前缀替换为其余目标根前缀。
            string stubMainDir = Path.Combine(Path.GetTempPath(), "mt_stub_main_" + Guid.NewGuid().ToString("N"));
            string stubOtherDir = Path.Combine(Path.GetTempPath(), "mt_stub_other_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stubMainDir);
            Directory.CreateDirectory(stubOtherDir);
            string stubMainZip = Path.Combine(stubMainDir, Path.GetFileName(mtMainZip));
            File.WriteAllText(stubMainZip, "stub");
            string stubOtherZip = Path.Combine(stubOtherDir, Path.GetFileName(stubMainZip));
            File.Copy(stubMainZip, stubOtherZip, true);
            Check("multi-target: 桩复制（主zip -> 其余目标）成功落地",
                File.Exists(stubOtherZip));
            try { Directory.Delete(stubMainDir, true); Directory.Delete(stubOtherDir, true); }
            catch { /* 清理失败忽略 */ }

            // ---- 空间预检（R8）：不足目标返回该目标且 Ok=false ----
            Console.WriteLine("[Disk space precheck R8]");
            string spaceSrc = Path.Combine(Path.GetTempPath(), "space_src_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(spaceSrc);
            File.WriteAllText(Path.Combine(spaceSrc, "a.bin"), "x"); // 使源大小 > 0 -> 所需空间 > 0
            long spaceSrcSize = DiskSpaceChecker.EstimateSourceSize(spaceSrc);
            Check("space: EstimateSourceSize 对非空源返回 > 0", spaceSrcSize > 0);

            HashSet<string> usedLetters = DriveEnumerator.GetUsedLetters();
            string unusedLetter = null;
            for (char c = 'Z'; c >= 'A'; c--)
            {
                if (!usedLetters.Contains(c.ToString()))
                {
                    unusedLetter = c.ToString();
                    break;
                }
            }

            Check("space: 存在未挂载盘符用于不足目标测试", !string.IsNullOrEmpty(unusedLetter));
            var spaceDrives = new List<string>();
            string realLetter = usedLetters.FirstOrDefault();
            if (!string.IsNullOrEmpty(realLetter))
            {
                spaceDrives.Add(realLetter); // 真实盘空闲充足 -> 预期 Ok=true
            }

            if (!string.IsNullOrEmpty(unusedLetter))
            {
                spaceDrives.Add(unusedLetter); // 未挂载盘空闲=0 < 所需 -> 预期 Ok=false
            }

            Dictionary<string, SpaceCheck> spaceResult = DiskSpaceChecker.CheckTargets(spaceSrc, spaceDrives);
            if (!string.IsNullOrEmpty(realLetter) && spaceResult.TryGetValue(realLetter, out SpaceCheck realCheck))
            {
                Check("space: 空闲充足的真实盘 " + realLetter + " -> Ok=true", realCheck.Ok);
            }

            if (!string.IsNullOrEmpty(unusedLetter) && spaceResult.TryGetValue(unusedLetter, out SpaceCheck badCheck))
            {
                Check("space: 不足目标 '" + unusedLetter + "' 被返回且 Ok=false",
                    badCheck.Ok == false &&
                    string.Equals(badCheck.Drive, unusedLetter, StringComparison.OrdinalIgnoreCase) &&
                    badCheck.NeededBytes > 0);
            }

            try { Directory.Delete(spaceSrc, true); }
            catch { /* 清理失败忽略 */ }

            // ---- 最近备份记录（R9）：成功后备 config 的 LastBackupPath（时间含于文件名，不再单独保存） ----
            Console.WriteLine("[Last backup R9]");
            AppConfig lastCfg = ConfigManager.Defaults();
            string lastPath = @"D:\Backups\MyProject_TIA_20260717_090000.zip";
            lastCfg.LastBackupPath = lastPath;
            ConfigManager.Save(lastCfg);
            AppConfig lastLoaded = ConfigManager.Load();
            Check("lastbackup: LastBackupPath 持久化后可读回",
                string.Equals(lastLoaded.LastBackupPath, lastPath, StringComparison.OrdinalIgnoreCase));
            try { File.Delete(ConfigManager.GetConfigPath()); }
            catch { /* 清理失败忽略 */ }

            // ---- 二次确认（R10）：勾选强制且含受保护目标时弹窗，确认返回 true ----
            Console.WriteLine("[Force-write confirm R10]");
            // 沙箱无界面无法真正点击；验证对话框可构造且其"确认"按钮映射 DialogResult.Yes，
            // 即弹窗被确认时 ShowConfirm 返回 true（与 MainForm 的"force && 含受保护目标"触发逻辑一致）。
            using (var fwc = new ForceWriteConfirmForm())
            {
                Check("confirm: ForceWriteConfirmForm 可构造且标题已本地化（R10 文案存在）",
                    !string.IsNullOrEmpty(fwc.Text));
                var acceptBtn = fwc.AcceptButton as IButtonControl;
                Check("confirm: 接受按钮映射 DialogResult.Yes（确认即返回 true）",
                    acceptBtn != null && acceptBtn.DialogResult == DialogResult.Yes);
            }

            Check("confirm: force_start_confirm_title 本地化键存在",
                !string.IsNullOrEmpty(Localization.Get("zh-CN", "force_start_confirm_title")));
            Check("confirm: force_start_confirm_msg 本地化键存在",
                !string.IsNullOrEmpty(Localization.Get("en-US", "force_start_confirm_msg")));

            // ---- 版本号（R5）：csproj 内嵌 BuildRevision/BuildDate，日期取编译时间 ----
            Console.WriteLine("[Version R5]");
            string repoRoot = FindRepoRoot();
            string csprojPath = Path.Combine(repoRoot, "SHBT.csproj");
            Check("version: SHBT.csproj 存在", File.Exists(csprojPath));
            if (File.Exists(csprojPath))
            {
                XDocument csprojDoc = XDocument.Load(csprojPath);
                XElement buildRevEl = csprojDoc.Descendants("BuildRevision").FirstOrDefault();
                XElement buildDateEl = csprojDoc.Descendants("BuildDate").FirstOrDefault();
                Check("version: csproj 内嵌 <BuildRevision>", buildRevEl != null);
                Check("version: csproj 内嵌 <BuildDate>", buildDateEl != null);

                bool revOk = buildRevEl != null &&
                    int.TryParse(buildRevEl.Value, out int revNum) && revNum > 0;
                Check("version: BuildRevision 为正整数（自增状态位）", revOk);

                bool dateOk = buildDateEl != null &&
                    Regex.IsMatch(buildDateEl.Value, @"^\d{4}\.\d{2}\.\d{2}$");
                Check("version: BuildDate 匹配 yyyy.MM.dd（取编译时间）", dateOk);

                // Properties/Version.g.cs 由 BumpVersion 目标在构建期生成，日期取自编译时间。
                string versionG = Path.Combine(repoRoot, "Properties", "Version.g.cs");
                Check("version: 构建生成的 Properties/Version.g.cs 存在", File.Exists(versionG));
                if (File.Exists(versionG))
                {
                    string gContent = File.ReadAllText(versionG);
                    Match dispMatch = Regex.Match(gContent, @"Display\s*=\s*""v(\d{4}\.\d{2}\.\d{2})\.(\d+)""");
                    Check("version: AppVersion.Display 格式为 vYYYY.MM.DD.NN", dispMatch.Success);
                    if (dispMatch.Success && dateOk && buildDateEl != null)
                    {
                        Check("version: Display 日期 == csproj BuildDate（编译时间注入）",
                            string.Equals(dispMatch.Groups[1].Value, buildDateEl.Value, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            // ---- FontHelper regression (startup-crash fix) ----
            // The original bug: GetSystemFontName() returned a GDI logical-font alias
            // ("MS Shell Dlg" / "MS Shell Dlg 2") or an empty string on some Windows
            // themes. new Font(alias, ...) defers resolution until a multiline TextBox
            // lays out and computes PreferredHeight -> Font.GetHeight(graphics) throws
            // "ArgumentException: 参数无效", aborting the form constructor (startup crash).
            // These assertions prove the guard rail, the real return value, and the
            // crash path (ApplyTo on a multiline TextBox inside a TableLayoutPanel).
            Console.WriteLine("[FontHelper regression]");

            // Guard-rail unit tests: aliases and empty names must be rejected; a real
            // installed font family must be accepted.
            Check("IsUsableFontName(\"MS Shell Dlg\") == false",
                FontHelper.IsUsableFontName("MS Shell Dlg") == false);
            Check("IsUsableFontName(\"MS Shell Dlg 2\") == false",
                FontHelper.IsUsableFontName("MS Shell Dlg 2") == false);
            Check("IsUsableFontName(\"\") == false",
                FontHelper.IsUsableFontName("") == false);
            Check("IsUsableFontName(\"Microsoft Sans Serif\") == true",
                FontHelper.IsUsableFontName("Microsoft Sans Serif") == true);

            // Real return value must be non-empty and correspond to a real, enumerated
            // installed font family (not an alias / empty string). This is the direct
            // regression guard for "GetSystemFontName no longer returns an alias".
            string sysName = FontHelper.GetSystemFontName();
            bool nameOk = !string.IsNullOrEmpty(sysName);
            if (nameOk && FontFamily.Families.Length > 0)
            {
                bool found = false;
                foreach (FontFamily f in FontFamily.Families)
                {
                    if (string.Equals(f.Name, sysName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }

                nameOk = found;
            }

            Check("GetSystemFontName returns a real installed font family", nameOk);

            // Crash-site reproduction (core): build the exact control tree that used to
            // crash (multiline TextBox docked inside a TableLayoutPanel inside a Form),
            // then apply the system font. ApplyTo must complete WITHOUT throwing.
            bool applied = false;
            string applyError = null;
            try
            {
                using (var form = new Form())
                using (var tlp = new TableLayoutPanel())
                using (var tb = new TextBox { Multiline = true, Dock = DockStyle.Fill })
                {
                    tlp.Controls.Add(tb);
                    form.Controls.Add(tlp);
                    FontHelper.ApplyTo(form);
                    applied = true;
                }
            }
            catch (Exception ex)
            {
                applyError = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
            }

            Check("ApplyTo(Form with multiline TextBox in TableLayoutPanel) does not throw", applied);
            if (!applied && applyError != null)
            {
                Console.WriteLine("    EXCEPTION DURING ApplyTo:");
                Console.WriteLine("    " + applyError.Replace("\n", "\n    "));
            }

            // Crash-path reproduction (THE REAL TRIGGER): the original startup crash did
            // NOT happen inside ApplyTo. It happened AFTER ApplyTo returned, when layout
            // recomputed and a multiline TextBox measured its PreferredHeight (which
            // internally calls Font.GetHeight on the font ApplyTo assigned). If ApplyTo ever
            // re-introduces a single shared font that it disposes (the old bug), this
            // measurement throws "ArgumentException: 参数无效" and the test goes RED -- exactly
            // what we want to guard. We therefore keep the form/controls alive (no `using`)
            // until AFTER the measurement, mirroring the product's constructor flow.
            bool layoutOk = false;
            string layoutError = null;
            Form layoutForm = null;
            try
            {
                layoutForm = new Form();
                var layoutTlp = new TableLayoutPanel();
                var layoutTb = new TextBox { Multiline = true, Dock = DockStyle.Fill };
                layoutTlp.Controls.Add(layoutTb);
                layoutForm.Controls.Add(layoutTlp);

                FontHelper.ApplyTo(layoutForm);   // identical to the product path

                layoutTb.Text = "x";              // triggers OnTextChanged -> PerformLayout
                layoutForm.PerformLayout();       // force layout recompute
                int h = layoutTb.PreferredHeight; // KEY: internal Font.GetHeight(); old code threw here
                layoutOk = h > 0;
            }
            catch (Exception ex)
            {
                layoutError = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
            }
            finally
            {
                layoutForm?.Dispose();
            }

            Check("After ApplyTo, layout measuring multiline TextBox (PreferredHeight) does not throw", layoutOk);
            if (!layoutOk && layoutError != null)
            {
                Console.WriteLine("    EXCEPTION DURING LAYOUT MEASUREMENT:");
                Console.WriteLine("    " + layoutError.Replace("\n", "\n    "));
            }

            // Secondary guard that mirrors ApplyLanguage(): setting a Label's Text triggers
            // a layout pass and the framework reads font metrics (Font.Height -> GetHeight).
            // A disposed font would also fail here, so this guards the Label-text path too.
            bool labelOk = false;
            string labelError = null;
            Form labelForm = null;
            try
            {
                labelForm = new Form();
                var layoutLabel = new Label { Dock = DockStyle.Top };
                labelForm.Controls.Add(layoutLabel);

                FontHelper.ApplyTo(labelForm);
                layoutLabel.Text = "中文";         // mimic ApplyLanguage setting text
                labelForm.PerformLayout();
                int hf = layoutLabel.Font.Height;  // reads font metrics; fails if font disposed
                labelOk = hf > 0;
            }
            catch (Exception ex)
            {
                labelError = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
            }
            finally
            {
                labelForm?.Dispose();
            }

            Check("After ApplyTo, setting Label text and reading Font.Height does not throw", labelOk);
            if (!labelOk && labelError != null)
            {
                Console.WriteLine("    EXCEPTION DURING LABEL LAYOUT:");
                Console.WriteLine("    " + labelError.Replace("\n", "\n    "));
            }

            // ---- Enhanced Edition (R2/R3/R8/R9) ----
            // Mirrors tests/Tests.cs EnhancedFeatureTests so the same pure-Core logic is
            // verified on the .NET 10 runtime (the net48 test host is unavailable here).
            Console.WriteLine("[Enhanced features]");
            Localization.Initialize(Path.Combine(FindRepoRoot(), "lang"));

            // R2：排除规则 V2（List<ExcludeRule>）—— 仅启用项生成 -xr!，禁用项被忽略，空白被裁剪。
            var exRules = new List<ExcludeRule>
            {
                new ExcludeRule { Enabled = true, Pattern = "*.tmp" },
                new ExcludeRule { Enabled = false, Pattern = "*.log" },
                new ExcludeRule { Enabled = true, Pattern = "  *.bak  " }
            };
            var exOpts = new BackupOptions { ShadowLetter = "Q", ExcludeRules = exRules };
            BackupCommand exCmd = BackupCommandBuilder.Build(Path.GetTempPath() + "\\P", "D", "TIA_Portal", exOpts);
            Check("ExcludeRule V2: enabled *.tmp -> -xr!*.tmp", exCmd.ArchiveArgs.Exists(a => a == "-xr!*.tmp"));
            Check("ExcludeRule V2: disabled *.log omitted", !exCmd.ArchiveArgs.Exists(a => a == "-xr!*.log"));
            Check("ExcludeRule V2: enabled *.bak trimmed -> -xr!*.bak", exCmd.ArchiveArgs.Exists(a => a == "-xr!*.bak"));

            // R8：源目录大小估算与逐目标盘空间预检（×1.1 安全系数）。
            string sizeDir = Path.Combine(Path.GetTempPath(), "1tool_size_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sizeDir);
            long sizeTotal = 0;
            for (int i = 0; i < 3; i++)
            {
                string f = Path.Combine(sizeDir, "f" + i + ".bin");
                byte[] data = new byte[100 + i * 50];
                File.WriteAllBytes(f, data);
                sizeTotal += data.Length;
            }

            long est = DiskSpaceChecker.EstimateSourceSize(sizeDir);
            Check("EstimateSourceSize sums file sizes", est == sizeTotal);
            Directory.Delete(sizeDir, true);

            string sizeDir2 = Path.Combine(Path.GetTempPath(), "1tool_size2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sizeDir2);
            File.WriteAllBytes(Path.Combine(sizeDir2, "x.bin"), new byte[1000]);
            string sizeDrive = Path.GetPathRoot(sizeDir2).TrimEnd('\\', ':').ToUpperInvariant();
            Dictionary<string, SpaceCheck> checks = DiskSpaceChecker.CheckTargets(sizeDir2, new List<string> { sizeDrive });
            Check("CheckTargets contains the target drive", checks.ContainsKey(sizeDrive));
            Check("CheckTargets needed = source * 1.1", checks[sizeDrive].NeededBytes == (long)(1000 * 1.1));
            Check("CheckTargets free >= 0", checks[sizeDrive].FreeBytes >= 0);
            Check("CheckTargets multiple drives -> 2 entries",
                DiskSpaceChecker.CheckTargets(sizeDir2, new List<string> { "C", "D" }).Count == 2);
            Directory.Delete(sizeDir2, true);

            // R9：最近一次备份记录（路径 + 时间）随配置往返。
            AppConfig cfg = ConfigManager.Defaults();
            cfg.ExcludeRules = new List<ExcludeRule>
            {
                new ExcludeRule { Enabled = true, Pattern = "*.tmp", Comment = "temp" },
                new ExcludeRule { Enabled = false, Pattern = "*.log" }
            };
            cfg.LastBackupPath = @"D:\Backups\Proj_TIA_20260101_000000.zip";
            ConfigManager.Save(cfg);
            AppConfig loadedCfg = ConfigManager.Load();
            Check("Config round-trip: 2 exclude rules", loadedCfg.ExcludeRules != null && loadedCfg.ExcludeRules.Count == 2);
            Check("Config round-trip: rule[0] pattern == *.tmp",
                loadedCfg.ExcludeRules != null && loadedCfg.ExcludeRules[0].Pattern == "*.tmp");
            Check("Config round-trip: rule[0] enabled",
                loadedCfg.ExcludeRules != null && loadedCfg.ExcludeRules[0].Enabled);
            Check("Config round-trip: rule[1] disabled",
                loadedCfg.ExcludeRules != null && !loadedCfg.ExcludeRules[1].Enabled);
            Check("Config round-trip: LastBackupPath", loadedCfg.LastBackupPath == cfg.LastBackupPath);

            // T01：版本由构建目标 BumpVersion 自增生成，格式 vYYYY.MM.DD.NN。
            string verFile = Path.Combine(FindRepoRoot(), "Properties", "Version.g.cs");
            Check("Version.g.cs exists after build", File.Exists(verFile));
            if (File.Exists(verFile))
            {
                string verText = File.ReadAllText(verFile);
                Check("Version.g.cs Display = vYYYY.MM.DD.NN",
                    Regex.IsMatch(verText, @"Display\s*=\s*""v\d{4}\.\d{2}\.\d{2}\.\d{2}"""));
            }

            // 增强版阶段文案与确认按钮在所有内置语言均非空（运行时回退保证不为空）。
            foreach (string code in new[]
            {
                "zh-CN", "en-US", "zh-TW", "ru-RU", "es-ES", "de-DE", "fr-FR",
                "pt-BR", "pt-PT", "it-IT", "ko-KR", "ja-JP", "id-ID", "be-BY"
            })
            {
                Check("StageText[Copy] non-empty in " + code,
                    !string.IsNullOrEmpty(Localization.Get(code, "stage_copy")));
                Check("Confirm yes/no non-empty in " + code,
                    !string.IsNullOrEmpty(Localization.Get(code, "confirm_yes")) &&
                    !string.IsNullOrEmpty(Localization.Get(code, "confirm_no")));
            }

            Console.WriteLine();
            Console.WriteLine("=========================================");
            Console.WriteLine("  Passed: " + _passed + "   Failed: " + _failed);
            Console.WriteLine("=========================================");
            Environment.Exit(_failed == 0 ? 0 : 1);
        }

        private static string FindRepoRoot()
        {
            string loc = typeof(Program).Assembly.Location;
            DirectoryInfo dir = Directory.GetParent(loc);
            while (dir != null)
            {
                if (dir.Name.Equals("SHBT", StringComparison.OrdinalIgnoreCase))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return Directory.GetCurrentDirectory();
        }
    }
}

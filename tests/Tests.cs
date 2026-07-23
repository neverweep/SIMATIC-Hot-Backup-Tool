// <summary>
// Independent verification tests for the SHBT .NET port. These exercise only
// the pure / unit-testable Core logic and the i18n tables (no VSS, no GUI,
// no real 7z/ShadowSpawn execution).
// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SHBT.Core;
using SHBT.Ui;
using Localization = SHBT.Ui.Localization;

namespace SHBT.Tests
{
    [TestClass]
    public class ProjectDetectorTests
    {
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

        [TestMethod]
        public void Detect_AMOBJS_Returns_STEP7_V5X()
        {
            string dir = CreateTempProject(new[] { "AMOBJS" });
            ProjectDetectionResult r = ProjectDetector.Detect(dir);
            Assert.IsNotNull(r);
            Assert.AreEqual(ProjectType.STEP7_V5X, r.Type);
        }

        [TestMethod]
        public void Detect_GRACS_Returns_WinCC_V7X()
        {
            string dir = CreateTempProject(new[] { "GRACS" });
            ProjectDetectionResult r = ProjectDetector.Detect(dir);
            Assert.IsNotNull(r);
            Assert.AreEqual(ProjectType.WinCC_V7X, r.Type);
        }

        [TestMethod]
        public void Detect_XRef_Returns_TIA_Portal()
        {
            string dir = CreateTempProject(new[] { "XRef" });
            ProjectDetectionResult r = ProjectDetector.Detect(dir);
            Assert.IsNotNull(r);
            Assert.AreEqual(ProjectType.TIA_Portal, r.Type);
        }

        [TestMethod]
        public void Detect_EmptyDir_Returns_Null()
        {
            string dir = Path.Combine(Path.GetTempPath(), "1tool_empty_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.IsNull(ProjectDetector.Detect(dir));
                Assert.IsFalse(ProjectDetector.IsValidProject(dir));
            }
            finally
            {
                Directory.Delete(dir);
            }
        }

        [TestMethod]
        public void Detect_NonexistentDir_Returns_Null()
        {
            Assert.IsNull(ProjectDetector.Detect(
                Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N"))));
        }

        [TestMethod]
        public void Detect_ObjBuildDir_NotMistakenForStep7()
        {
            // 回归：.NET 构建输出目录 obj/ 在 Windows 上大小写不敏感，会与 STEP7 标记目录
            // "OBJ" 同名。仅含 obj/ 子目录的目录绝不能被判为任何西门子工程，否则工具在自身
            // 项目目录（含 obj/）下会被误判成 STEP7 项目。
            string dir = Path.Combine(Path.GetTempPath(), "1tool_objonly_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "obj"));
            try
            {
                Assert.IsNull(ProjectDetector.Detect(dir),
                    "obj/ build output dir must NOT be mistaken for the STEP7 'OBJ' marker");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void Detect_LaterMarkerOverridesEarlier()
        {
            // Both AMOBJS and XRef present; XRef is last in detect order => TIA_Portal.
            string dir = CreateTempProject(new[] { "AMOBJS", "XRef" });
            ProjectDetectionResult r = ProjectDetector.Detect(dir);
            Assert.IsNotNull(r);
            Assert.AreEqual(ProjectType.TIA_Portal, r.Type);
            CollectionAssert.Contains(r.Markers, "XRef");
        }
    }

    [TestClass]
    public class BackupCommandBuilderTests
    {
        [TestMethod]
        public void Build_PidCommand_OrderAndContents()
        {
            string projectPath = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), "MyProject_" + Guid.NewGuid().ToString("N")));
            string projectName = Path.GetFileName(projectPath);
            string expectedSource = Path.GetDirectoryName(projectPath);

            var opts = new BackupOptions
            {
                ShadowLetter = "Q",
                OutputSubdir = "Backups",
                CompressionLevel = "standard"
            };

            BackupCommand cmd = BackupCommandBuilder.Build(projectPath, "D", "STEP7_V5X", opts);
            List<string> pid = cmd.PidCommand;

            Assert.IsTrue(pid.Count >= 9, "pid_command should hold all expected elements");

            // [0] ShadowSpawn.exe
            Assert.IsTrue(pid[0].EndsWith("ShadowSpawn.exe"), "pid[0] should be ShadowSpawn.exe, got " + pid[0]);

            // [1] shadow source = parent directory of the project
            Assert.AreEqual(expectedSource, pid[1], "pid[1] should be the project parent directory");

            // [2] mount point "Q:"
            Assert.AreEqual("Q:", pid[2]);

            // 7z.exe present
            int idx7z = pid.FindIndex(s => s.EndsWith("7z.exe"));
            Assert.IsTrue(idx7z > 0, "7z.exe not found in pid_command");

            int idxA = pid.IndexOf("a");
            int idxBb0 = pid.IndexOf("-bb0");
            int idxBsp1 = pid.IndexOf("-bsp1");
            int idxMx = pid.FindIndex(s => s.StartsWith("-mx"));
            int idxTarget = pid.FindIndex(s => s.EndsWith(".zip"));
            int idxArchiveSrc = pid.IndexOf("Q:\\" + projectName);

            Assert.IsTrue(idx7z < idxA, "7z.exe should precede 'a'");
            Assert.IsTrue(idxA < idxBb0, "'a' should precede '-bb0'");
            Assert.IsTrue(idxBb0 < idxBsp1, "'-bb0' should precede '-bsp1'");
            Assert.IsTrue(idxBsp1 < idxMx, "'-bsp1' should precede mx flag");
            Assert.IsTrue(idxMx < idxTarget, "mx flag should precede target zip");
            Assert.IsTrue(idxTarget < idxArchiveSrc, "target zip should precede archive source");

            // standard compression => -mx5
            Assert.AreEqual("-mx5", pid[idxMx]);

            // target lives under D:\Backups and is named <project>_<type>_<timestamp>.zip
            Assert.IsTrue(cmd.TargetZip.StartsWith("D:\\Backups"), "target zip should be under D:\\Backups");
            Assert.IsTrue(cmd.TargetZip.EndsWith(".zip"));
            Assert.IsTrue(Regex.IsMatch(
                cmd.TargetZip,
                Regex.Escape(projectName) + "_STEP7_V5X_\\d{8}_\\d{6}\\.zip$"),
                "zip name should be <project>_<type>_<timestamp>.zip, got " + cmd.TargetZip);
        }

        [TestMethod]
        public void Build_CompressionMax_UsesMx9()
        {
            var opts = new BackupOptions { ShadowLetter = "Q", CompressionLevel = "max" };
            BackupCommand cmd = BackupCommandBuilder.Build(
                Path.GetTempPath() + "\\P", "D", "TIA_Portal", opts);
            int idxMx = cmd.PidCommand.FindIndex(s => s.StartsWith("-mx"));
            Assert.AreEqual("-mx9", cmd.PidCommand[idxMx]);
        }

        [TestMethod]
        public void FindFreeDriveLetter_AllButZExcluded_ReturnsZ()
        {
            var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (char c = 'A'; c <= 'Y'; c++)
            {
                exclude.Add(c.ToString());
            }

            exclude.Add("Q");
            string letter = BackupCommandBuilder.FindFreeDriveLetter("Q", exclude);
            Assert.AreEqual("Z", letter);
        }

        [TestMethod]
        public void FindFreeDriveLetter_AllUsed_ReturnsPreferred()
        {
            var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (char c = 'A'; c <= 'Z'; c++)
            {
                exclude.Add(c.ToString());
            }

            string letter = BackupCommandBuilder.FindFreeDriveLetter("Q", exclude);
            Assert.AreEqual("Q", letter);
        }

        [TestMethod]
        public void FindFreeDriveLetter_PreferredFree_ReturnsValidUnusedLetter()
        {
            var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C", "D", "E" };
            string letter = BackupCommandBuilder.FindFreeDriveLetter("Q", exclude);
            Assert.AreEqual("Q", letter);
            Assert.IsFalse(exclude.Contains(letter));
            Assert.IsTrue(letter.Length == 1 && letter[0] >= 'A' && letter[0] <= 'Z');
        }
    }

    [TestClass]
    public class LocalizationTests
    {
        private static readonly StageKey[] AllStages =
        {
            StageKey.Prepare, StageKey.Shadow, StageKey.Archive, StageKey.Copy,
            StageKey.Done, StageKey.Cancel, StageKey.Error
        };

        private static readonly ProjectType[] AllTypes =
        {
            ProjectType.STEP7_V5X, ProjectType.WinCC_V7X, ProjectType.TIA_Portal
        };

        [TestInitialize]
        public void Init()
        {
            // Load the lang/*.json resources from the repo so Get() resolves strings.
            Localization.Initialize(Path.Combine(FindRepoRoot(), "lang"));
        }

        [TestMethod]
        public void StageText_NonEmpty_InBothLanguages()
        {
            foreach (StageKey s in AllStages)
            {
                string key = "stage_" + s.ToString().ToLowerInvariant();
                string zh = Localization.Get("zh-CN", key);
                string en = Localization.Get("en-US", key);
                Assert.IsFalse(string.IsNullOrEmpty(zh), "zh stage text empty for " + s);
                Assert.IsFalse(string.IsNullOrEmpty(en), "en stage text empty for " + s);
            }
        }

        [TestMethod]
        public void ProjectTypeName_NonEmpty_InBothLanguages()
        {
            foreach (ProjectType t in AllTypes)
            {
                string zh = Localization.Get("zh-CN", "type_" + t.ToString());
                string en = Localization.Get("en-US", "type_" + t.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(zh), "zh type name empty for " + t);
                Assert.IsFalse(string.IsNullOrEmpty(en), "en type name empty for " + t);
            }
        }

        [TestMethod]
        public void Initialize_DiscoversAllLanguageFiles()
        {
            Assert.IsTrue(Localization.IsSupported("en-US"));
            Assert.IsTrue(Localization.IsSupported("zh-CN"));
            Assert.IsTrue(Localization.IsSupported("zh-TW"));
            Assert.IsTrue(Localization.IsSupported("ru-RU"));
            Assert.IsTrue(Localization.Languages.Count >= 4);
        }

        [TestMethod]
        public void DetectSystemLanguage_ReturnsSupportedCode()
        {
            string detected = Localization.DetectSystemLanguage();
            Assert.IsTrue(Localization.IsSupported(detected), "detected language must be supported");
        }

        [TestMethod]
        public void Get_UnknownKey_FallsBackToRawKey()
        {
            Assert.AreEqual("nonexistent_key", Localization.Get("en-US", "nonexistent_key"));
        }

        private static string FindRepoRoot()
        {
            string loc = typeof(LocalizationTests).Assembly.Location;
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

    [TestClass]
    public class BinLocatorTests
    {
        [TestMethod]
        public void GetToolPath_7z_ConstructionAndPresence()
        {
            string path = BinLocator.GetToolPath("7z.exe");
            Assert.IsTrue(Path.IsPathRooted(path), "tool path should be rooted");
            Assert.IsTrue(
                path.Replace("/", "\\").EndsWith("\\bin\\7z.exe"),
                "tool path should resolve under <exedir>\\bin\\7z.exe, got " + path);

            // The native tool ships with the app: verify it exists in the repo bin folder.
            string repoBin = Path.Combine(FindRepoRoot(), "bin", "7z.exe");
            bool exists = File.Exists(path) || File.Exists(repoBin);
            Assert.IsTrue(exists, "7z.exe should be present either at GetToolPath or in the app bin folder");
        }

        [TestMethod]
        public void BinDirectory_EndsWithBin()
        {
            string bin = BinLocator.BinDirectory;
            Assert.IsTrue(
                bin.Replace("/", "\\").EndsWith("\\bin"),
                "BinDirectory should end with \\bin, got " + bin);
        }

        private static string FindRepoRoot()
        {
            string loc = typeof(BinLocatorTests).Assembly.Location;
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

    [TestClass]
    public class DriveEnumeratorTests
    {
        // Regression coverage for the GetLogicalDriveStringsW P/Invoke fix. The original
        // declaration used an [Out] StringBuilder whose buffer.ToString(0, length) threw
        // ArgumentOutOfRangeException; the fix uses a char[] buffer with a two-call pattern
        // and `new string(buffer, 0, written)`. These tests pass after the fix is applied
        // (verified via the net10 Smoke harness in this sandbox; run here on a real
        // .NET Framework 4.8 host). See QA report.
        [TestMethod]
        public void ListDrives_DoesNotThrow_AndReturnsEntries()
        {
            var drives = DriveEnumerator.ListDrives();
            Assert.IsNotNull(drives);
            Assert.IsTrue(drives.Count >= 1, "expected at least one drive on a normal machine");
            foreach (DriveInfoEx d in drives)
            {
                Assert.IsFalse(string.IsNullOrEmpty(d.Letter));
            }
        }

        [TestMethod]
        public void FindFreeDriveLetter_WithNullExclude_DoesNotThrow()
        {
            string letter = BackupCommandBuilder.FindFreeDriveLetter("Q", null);
            Assert.IsFalse(string.IsNullOrEmpty(letter));
            Assert.IsTrue(letter.Length == 1 && letter[0] >= 'A' && letter[0] <= 'Z');
        }
    }

    [TestClass]
    public class EnhancedFeatureTests
    {
        // 增强版（R2/R3/R8/R9）相关独立验证，覆盖纯 Core 逻辑，不依赖 VSS / GUI。

        [TestMethod]
        public void Build_ExcludeRuleV2_EnabledAddsXr_DisabledOmitted()
        {
            var rules = new List<ExcludeRule>
            {
                new ExcludeRule { Enabled = true, Pattern = "*.tmp" },
                new ExcludeRule { Enabled = false, Pattern = "*.log" },
                new ExcludeRule { Enabled = true, Pattern = "  *.bak  " }
            };
            var opts = new BackupOptions { ShadowLetter = "Q", ExcludeRules = rules };
            BackupCommand cmd = BackupCommandBuilder.Build(Path.GetTempPath() + "\\P", "D", "TIA_Portal", opts);

            Assert.IsTrue(cmd.ArchiveArgs.Exists(a => a == "-xr!*.tmp"), "enabled rule should produce -xr!*.tmp");
            Assert.IsFalse(cmd.ArchiveArgs.Exists(a => a == "-xr!*.log"), "disabled rule should be omitted");
            Assert.IsTrue(cmd.ArchiveArgs.Exists(a => a == "-xr!*.bak"), "enabled rule should be trimmed to -xr!*.bak");
        }

        [TestMethod]
        public void EstimateSourceSize_SumsFileSizes()
        {
            string dir = Path.Combine(Path.GetTempPath(), "1tool_size_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            long total = 0;
            for (int i = 0; i < 3; i++)
            {
                string f = Path.Combine(dir, "f" + i + ".bin");
                byte[] data = new byte[100 + i * 50];
                File.WriteAllBytes(f, data);
                total += data.Length;
            }

            long est = DiskSpaceChecker.EstimateSourceSize(dir);
            Assert.AreEqual(total, est, "estimated size should equal sum of file sizes");
            Directory.Delete(dir, true);
        }

        [TestMethod]
        public void CheckTargets_ComputesNeededWithSafetyFactor()
        {
            string dir = Path.Combine(Path.GetTempPath(), "1tool_size2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "x.bin"), new byte[1000]);
            string drive = Path.GetPathRoot(dir).TrimEnd('\\', ':').ToUpperInvariant();

            Dictionary<string, SpaceCheck> checks = DiskSpaceChecker.CheckTargets(dir, new List<string> { drive });
            Assert.IsTrue(checks.ContainsKey(drive), "check should contain the target drive");
            Assert.AreEqual((long)(1000 * 1.1), checks[drive].NeededBytes, "needed = source size * 1.1");
            Assert.IsTrue(checks[drive].FreeBytes >= 0);
            Directory.Delete(dir, true);
        }

        [TestMethod]
        public void ConfigManager_RoundTrip_EnhancedFields()
        {
            AppConfig cfg = ConfigManager.Defaults();
            cfg.ExcludeRules = new List<ExcludeRule>
            {
                new ExcludeRule { Enabled = true, Pattern = "*.tmp", Comment = "temp" },
                new ExcludeRule { Enabled = false, Pattern = "*.log" }
            };
            cfg.LastBackupPath = @"D:\Backups\Proj_TIA_20260101_000000.zip";
            ConfigManager.Save(cfg);

            AppConfig loaded = ConfigManager.Load();
            Assert.IsNotNull(loaded.ExcludeRules);
            Assert.AreEqual(2, loaded.ExcludeRules.Count);
            Assert.AreEqual("*.tmp", loaded.ExcludeRules[0].Pattern);
            Assert.AreEqual("temp", loaded.ExcludeRules[0].Comment);
            Assert.IsTrue(loaded.ExcludeRules[0].Enabled);
            Assert.IsFalse(loaded.ExcludeRules[1].Enabled);
            Assert.AreEqual(cfg.LastBackupPath, loaded.LastBackupPath);
        }

        [TestMethod]
        public void VersionFile_ContainsExpectedDisplayFormat()
        {
            // 增强版（T01）：版本由构建目标 BumpVersion 自增生成，格式为 vYYYY.MM.DD.NN。
            // AppVersion 为 internal，此处直接校验生成的 Properties/Version.g.cs 内容。
            string file = Path.Combine(FindRepoRoot(), "Properties", "Version.g.cs");
            Assert.IsTrue(File.Exists(file), "generated Version.g.cs should exist after build");
            string content = File.ReadAllText(file);
            Assert.IsTrue(Regex.IsMatch(content, @"Display\s*=\s*""v\d{4}\.\d{2}\.\d{2}\.\d{2}"""),
                "Version.g.cs Display should be vYYYY.MM.DD.NN, got:\n" + content);
        }

        [TestMethod]
        public void Build_MultiTarget_MainAndSecondaryZips()
        {
            // R4：多目标备份。主目标归档到 drives[0]，其余目标路径由 ComputeTargetZip 给出
            // （复制阶段据此前缀替换 File.Copy 主 zip 到其余目标）。
            var opts = new BackupOptions { ShadowLetter = "Q", OutputSubdir = "Backups", CompressionLevel = "standard" };
            var drives = new List<string> { "D", "E", "F" };
            string proj = Path.Combine(Path.GetTempPath(), "MT_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(proj);
            BackupCommand main = BackupCommandBuilder.Build(proj, drives[0], "TIA_Portal", opts);
            Assert.IsTrue(main.TargetZip.StartsWith("D:\\Backups", StringComparison.OrdinalIgnoreCase),
                "主目标 zip 应位于 D:\\Backups");
            Assert.IsTrue(Regex.IsMatch(main.TargetZip,
                Regex.Escape(Path.GetFileName(proj)) + "_TIA_Portal_\\d{8}_\\d{6}\\.zip$"));

            string mainTargetDir = drives[0] + ":\\" + (string.IsNullOrEmpty(opts.OutputSubdir) ? "Backups" : opts.OutputSubdir);
            foreach (string d in drives.Skip(1))
            {
                // #5：传入主目标 zip 的时间戳，确保复制目的地与主目标共用同一文件名（不跨秒）。
                string mainTs = System.Text.RegularExpressions.Regex.Match(main.TargetZip, @"_(\d{8}_\d{6})\.zip$").Groups[1].Value;
                string other = BackupCommandBuilder.ComputeTargetZip(d, opts, proj, "TIA_Portal", mainTs);
                Assert.IsTrue(other.StartsWith(d + ":\\Backups", StringComparison.OrdinalIgnoreCase),
                    "目标 " + d + " 复制目的地应位于 " + d + ":\\Backups");
                Assert.AreEqual(Path.GetFileName(other), Path.GetFileName(main.TargetZip),
                    "其余目标应与主目标共用同一 zip 名（复制前提）");
                // 复制阶段前缀替换须与 ComputeTargetZip 完全一致。
                string expectedSwap = d + ":\\" + (string.IsNullOrEmpty(opts.OutputSubdir) ? "Backups" : opts.OutputSubdir)
                    + main.TargetZip.Substring(mainTargetDir.Length);
                Assert.AreEqual(expectedSwap, other, "复制目的地（前缀替换）应 == ComputeTargetZip(" + d + ")");
            }
        }

        [TestMethod]
        public void ForceWriteConfirm_AcceptButton_MapsToYes()
        {
            // R10：勾选强制且含受保护目标时弹窗；其"确认"按钮映射 DialogResult.Yes，
            // 即用户确认时 ShowConfirm 返回 true。
            using (var fwc = new ForceWriteConfirmForm())
            {
                Assert.IsFalse(string.IsNullOrEmpty(fwc.Text), "对话框应可构造且标题已本地化（R10 文案存在）");
                var accept = fwc.AcceptButton as System.Windows.Forms.IButtonControl;
                Assert.IsNotNull(accept, "应存在接受按钮");
                Assert.AreEqual(System.Windows.Forms.DialogResult.Yes, accept.DialogResult,
                    "确认按钮应映射 DialogResult.Yes（确认即返回 true）");
            }
        }

        private static string FindRepoRoot()
        {
            string loc = typeof(EnhancedFeatureTests).Assembly.Location;
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

    [TestClass]
    public class ProjectDetectorFileMarkerTests
    {
        private static string CreateProject(string[] dirs, string[] files)
        {
            string dir = Path.Combine(Path.GetTempPath(), "1tool_proj_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach (string d in dirs) Directory.CreateDirectory(Path.Combine(dir, d));
            foreach (string f in files) File.WriteAllText(Path.Combine(dir, f), "x");
            return dir;
        }

        [TestMethod]
        public void Detect_Step7_ByS7pFile()
        {
            string dir = CreateProject(new string[0], new[] { "MyProject.s7p" });
            ProjectDetectionResult r = ProjectDetector.Detect(dir);
            Assert.IsNotNull(r);
            Assert.AreEqual(ProjectType.STEP7_V5X, r.Type);
            CollectionAssert.Contains(r.Markers, "MyProject.s7p");
        }

        [TestMethod]
        public void Detect_WinCC_ByMcpFile()
        {
            string dir = CreateProject(new string[0], new[] { "MyWinCC.mcp" });
            ProjectDetectionResult r = ProjectDetector.Detect(dir);
            Assert.IsNotNull(r);
            Assert.AreEqual(ProjectType.WinCC_V7X, r.Type);
        }

        [TestMethod]
        public void Detect_TIA_ByApFile()
        {
            string dir = CreateProject(new string[0], new[] { "MyTIA.ap19" });
            ProjectDetectionResult r = ProjectDetector.Detect(dir);
            Assert.IsNotNull(r);
            Assert.AreEqual(ProjectType.TIA_Portal, r.Type);
        }

        [TestMethod]
        public void Detect_TIA_ApFile_RequiresDigits()
        {
            // ".apxyz" 不应被误判为 TIA 工程文件（扩展名须为 .ap + 数字）。
            string dir = CreateProject(new string[0], new[] { "foo.apxyz" });
            Assert.IsNull(ProjectDetector.Detect(dir));
        }

        [TestMethod]
        public void Detect_Integrated_AmobjsAndApFile_PrefersTIA()
        {
            // STEP7 (.s7p) 内嵌 TIA (.ap19) 时，TIA 优先级更高。
            string dir = CreateProject(new string[0], new[] { "P.s7p", "P.ap19" });
            ProjectDetectionResult r = ProjectDetector.Detect(dir);
            Assert.IsNotNull(r);
            Assert.AreEqual(ProjectType.TIA_Portal, r.Type);
        }
    }

    [TestClass]
    public class RunningProjectDetectorTests
    {
        [TestMethod]
        public void FindRunning_DoesNotThrow_AndReturnsNullOrValidDir()
        {
            foreach (ProjectType t in new[] { ProjectType.WinCC_V7X, ProjectType.TIA_Portal, ProjectType.STEP7_V5X })
            {
                string r = RunningProjectDetector.FindRunning(t);
                Assert.IsTrue(r == null || Directory.Exists(r), "returned path must exist or be null");
            }
        }
    }
}

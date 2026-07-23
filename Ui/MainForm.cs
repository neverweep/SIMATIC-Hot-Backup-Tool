// <summary>
// SHBT main window: auto-locate project, detect type, enumerate drives, edit
// options, run/cancel the backup and present stage/progress/log. Ported from
// ui/main_window.py.
// 增强版（T04）：目标盘改为多选（复选框），首个勾选为 ★ 主目标；排除规则
// 采用单个多行文本框（每行一条规则）；启动前做磁盘空间预检（R8）与受保护盘
// 强制写入确认（R10）。工程类型以纯文本显示（无圆点/颜色），进度条已移除。
// </summary>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SHBT.Core;
using SHBT.Ui;
using Localization = SHBT.Ui.Localization;

namespace SHBT.Ui
{
    public partial class MainForm : Form
    {
        private readonly AppConfig _config;
        private readonly bool _isAdmin;

        private string _projectPath = string.Empty;
        private ProjectDetectionResult _projectType;
        private string _lastDetectedLogKey;   // 已输出"检测到工程"日志的类型键，避免重复刷屏
        private List<string> _selectedDrives;   // 增强版（R4）：有序勾选目标盘列表，首个为 ★ 主目标
        private CancellationTokenSource _cts;
        private Task _backupTask;
        private bool _running;
        private StageKey _stageKey = StageKey.Prepare;
        private int _lastLogPct = -1;
        private string _compKey = "max";
        private List<KeyValuePair<string, string>> _compOptions;
        private List<DriveInfoEx> _driveList;

        // Base font sizes captured once after FontHelper.ApplyTo so that re-applying
        // the language never accumulates (the old bug grew captions/buttons every
        // switch). Style* methods recompute from these instead of the enlarged font.
        private float _baseCaptionPt;
        private float _baseButtonPt;

        // Drive-row highlight colors (consistent with the existing Color.FromArgb(...)
        // style used elsewhere in this file).
        private static readonly Color ProjectDriveColor = Color.FromArgb(0xE6, 0x7E, 0x00); // 橙色，醒目（项目所在盘）
        private static readonly Color ProtectedDriveColor = Color.FromArgb(0xC0, 0x00, 0x00); // 红色（不安全/授权盘）
        private static readonly Color OpticalDriveColor = Color.Gray;                          // 灰色（光盘）

        // Run-mode label colors: distinguish an elevated administrator from a
        // restricted standard user at a glance.
        private static readonly Color AdminModeColor = Color.FromArgb(0x1A, 0x7F, 0x1A); // 绿色：管理员（已提权，具备完整权限）
        private static readonly Color UserModeColor  = Color.FromArgb(0x6A, 0x1B, 0x9A);  // 紫色：普通用户（非管理员）

        public MainForm(AppConfig config, bool isAdmin, string langCode)
        {
            InitializeComponent();

            // 应用图标：从 exe 内嵌的 Win32 图标资源取出，显示在标题栏（exe 图标由 csproj 的 ApplicationIcon 决定）。
            try
            {
                this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            }
            catch { }

            // 左下角开源组件链接：软件名→官网、协议名→本地协议文件（子链接，共享点击处理）。
            SetupOssLinks();

            _config = config;
            _isAdmin = isAdmin;
            Localization.CurrentCode = langCode;

            // Apply system UI font to avoid CJK -> Japanese glyph fallback.
            FontHelper.ApplyTo(this);

            // Capture base font sizes now (all designer controls exist) so that
            // later language switches restyle from the baseline instead of compounding.
            _baseCaptionPt = srcGroup.Font.SizeInPoints;
            _baseButtonPt = startButton.Font.SizeInPoints;

            // Build the language picker once from the discovered lang files.
            PopulateLanguageCombo();

            // Seed controls from persisted config.
            forceCheckBox.Checked = _config.ForceProtected;
            subTextBox.Text = _config.OutputSubdir ?? "Backups";

            // 排除规则恢复为单多行文本框：载入已有规则。
            LoadExcludeTextBox();

            _compKey = string.IsNullOrEmpty(_config.CompressionLevel) ? "max" : _config.CompressionLevel;

            ApplyLanguage();
            AutoPrefillProject();
            RefreshDrives();
            ApplyGeometry();

            // Apply splitter sizing on Load: at construction time the SplitContainer
            // has no real width yet, and setting SplitterDistance/Panel*MinSize would
            // throw. Once the form is laid out, Width is correct so the values are valid.

            // #10：排除规则文本框防抖落盘——输入时不立即写盘，停顿后再保存。
            _exclSaveTimer = new System.Windows.Forms.Timer(components) { Interval = 600 };
            _exclSaveTimer.Tick += (s, e) =>
            {
                _exclSaveTimer.Stop();
                if (_config != null)
                {
                    ConfigManager.Save(_config);
                }
            };
            this.Load += (s, e) =>
            {
                const int panel1Min = 360;
                const int panel2Min = 300;
                const int desiredSplit = 410;
                mainSplit.Panel1MinSize = panel1Min;
                mainSplit.Panel2MinSize = panel2Min;
                int w = mainSplit.Width;
                int dist = w > 0 ? Math.Min(desiredSplit, w - panel2Min) : desiredSplit;
                mainSplit.SplitterDistance = Math.Max(panel1Min, dist);
                // 免责声明：错误级别（红色），提示用户自行测试、风险自担。
                Log(T("disclaimer"), "err");
                // 安全提示（警告级别）：开始备份前，请保存所有编辑并关闭无关 WinCC 窗口，
                // 仅保留运行画面与管理器，确保所有修改已储存。置于后果自负之后。现已本地化（warn_save）。
                Log(T("warn_save"), "warn");
                // 运行模式移出标题区，改为在 Load 时写入日志（管理员绿 / 非管理员紫）。
                Log(T("mode_label") + (_isAdmin ? T("mode_admin") : T("mode_user")), _isAdmin ? "admin" : "user");
            };
        }

        // ----------------------------------------------------------------- //
        // Localization
        // ----------------------------------------------------------------- //
        private string T(string key) => Localization.Get(key);

        // 运行时由 Base64 还原联系邮箱：源码中只存编码串（无 '@' 也无域名结构），
        // 可规避源码/二进制中的邮箱正则抓取；显示效果与明文完全一致。
        private static string ContactMail()
        {
            const string enc = "eGlhb3hpYW14QGdtYWlsLmNvbQ=="; // base64 编码的联系邮箱
            byte[] raw = Convert.FromBase64String(enc);
            return System.Text.Encoding.UTF8.GetString(raw);
        }

        private void ApplyLanguage()
        {
            // 窗口标题：产品名 "SIMATIC Hot Backup Tool"，不本地化、不含版本号。
            // 作者/版权信息已移至窗口底部左下的硬编码版权 LinkLabel（见 MainForm.Designer.cs
            // 的 copyrightLabel），同样不进入语言文件；运行模式在 Load 时写入日志。
            this.Text = "SIMATIC Hot Backup Tool";
            descLabel.Text = T("app_desc");

            // 版权信息：邮箱不以明文写入源码，运行时由 Base64 还原，避免被源码爬虫按
            // 邮箱正则抓取；界面显示效果不变。版本号跟在版权之后，用 "  |  " 分隔，整体居右。
            string email = ContactMail();
            copyrightLabel.Text = "Xiao Xia (" + email + ")  |  " + AppVersion.Display;
            int cidx = copyrightLabel.Text.IndexOf(email);
            if (cidx >= 0) copyrightLabel.LinkArea = new LinkArea(cidx, email.Length);

            // 语言选择标签（languageComboBox 已移至选项组第 0 行）。
            langLabel.Text = T("lang_caption") + "(Language):";

            // Keep the language picker selection in sync with the active language.
            SelectCurrentLanguage();

            srcGroup.Text = T("group_source");
            browseButton.Text = T("browse");
            _btnDetectWinCC.Text = T("detect_running_wincc");
            _btnDetectTIA.Text = T("detect_running_tia");
            _btnDetectStep7.Text = T("detect_running_step7");
            tgtGroup.Text = T("group_target");
            // 增强版（R4）：提示文案改为多目标勾选说明。
            tgtHint.Text = T("target_multi_hint");
            colLetter.Text = T("col_letter");
            colLabel.Text = T("col_label");
            colFree.Text = T("col_free");
            colType.Text = T("col_type");
            colSafety.Text = T("col_safety");

            optGroup.Text = T("group_options");
            compLabel.Text = T("compression");
            subLabel.Text = T("output_subdir");
            exclLabel.Text = T("exclude_rules");
            forceCheckBox.Text = T("force_protected");
            _exclHelpButton.Text = T("rule_help");
            _restoreButton.Text = T("restore_defaults");

            logGroup.Text = T("group_log");

            StyleGroupCaptions();
            StylePrimaryButton();

            RebuildCompressionOptions();
            RefreshActionButton();
            UpdateTypeBadge();
            RefreshDriveSafetyLabels();
            UpdateLastBackupLabel();   // R9：刷新上次备份标签
        }

        /// <summary>Makes each functional-area GroupBox caption a bold title with
        /// the same point size as the body text, so the four sections (source /
        /// target / options / log) are visually distinct but not enlarged. Must run
        /// AFTER FontHelper.ApplyTo, which would otherwise reset the font on every
        /// control. We deliberately do NOT change GroupBox.ForeColor because it is an
        /// ambient property — it would also tint every child control (text boxes,
        /// list view).</summary>
        private void StyleGroupCaptions()
        {
            GroupBox[] groups = { srcGroup, tgtGroup, optGroup, logGroup };
            foreach (GroupBox grp in groups)
            {
                if (grp == null)
                {
                    continue;
                }

                // Recompute from the captured baseline — never from the current
                // (already-enlarged) font, otherwise repeated language switches
                // would keep growing the caption.
                grp.Font = new Font(grp.Font.FontFamily, _baseCaptionPt, FontStyle.Bold);
            }
        }

        /// <summary>The primary action button keeps the same size as the body text
        /// but uses a bold face so it reads as the dominant control on the form.
        /// Recalculated from the baseline each time to avoid cumulative growth on
        /// language switches.</summary>
        private void StylePrimaryButton()
        {
            if (startButton != null)
            {
                startButton.Font = new Font(startButton.Font.FontFamily, _baseButtonPt, FontStyle.Bold);
            }
        }

        private void RebuildCompressionOptions()
        {
            _compOptions = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("store", T("comp_store")),
                new KeyValuePair<string, string>("fast", T("comp_fast")),
                new KeyValuePair<string, string>("standard", T("comp_standard")),
                new KeyValuePair<string, string>("max", T("comp_max"))
            };

            compComboBox.Items.Clear();
            foreach (var kv in _compOptions)
            {
                compComboBox.Items.Add(kv.Value);
            }

            string current = string.IsNullOrEmpty(_config.CompressionLevel) ? "max" : _config.CompressionLevel;
            _compKey = current;
            // #9：临时屏蔽 OnCompressionChanged，避免在这里以编程方式设置 Text 时触发多余写盘。
            _suppressCompEvent = true;
            try
            {
                foreach (var kv in _compOptions)
                {
                    if (kv.Key == current)
                    {
                        compComboBox.Text = kv.Value;
                        break;
                    }
                }
            }
            finally
            {
                _suppressCompEvent = false;
            }
        }

        private bool _suppressLangEvent;

        // #9：重建压缩选项下拉框时临时屏蔽 OnCompressionChanged，避免触发多余的配置写盘。
        private bool _suppressCompEvent;

        // #10：排除规则文本框防抖落盘定时器（输入停顿 600ms 后再保存配置）。
        private System.Windows.Forms.Timer _exclSaveTimer;

        /// <summary>Fills the language picker from the languages discovered at
        /// startup (see <see cref="Localization.Languages"/>).</summary>
        private void PopulateLanguageCombo()
        {
            languageComboBox.Items.Clear();
            foreach (Localization.LanguageInfo lang in Localization.Languages)
            {
                languageComboBox.Items.Add(lang);
            }
        }

        /// <summary>Highlights the entry matching the active language without
        /// firing <see cref="OnLanguageChanged"/>.</summary>
        private void SelectCurrentLanguage()
        {
            _suppressLangEvent = true;
            try
            {
                for (int i = 0; i < languageComboBox.Items.Count; i++)
                {
                    if (string.Equals(((Localization.LanguageInfo)languageComboBox.Items[i]).Code, Localization.CurrentCode, StringComparison.OrdinalIgnoreCase))
                    {
                        languageComboBox.SelectedIndex = i;
                        return;
                    }
                }

                if (languageComboBox.Items.Count > 0)
                {
                    languageComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                _suppressLangEvent = false;
            }
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_suppressLangEvent || languageComboBox.SelectedItem == null)
            {
                return;
            }

            var info = (Localization.LanguageInfo)languageComboBox.SelectedItem;
            Localization.CurrentCode = info.Code;
            _config.Language = info.Code;
            ConfigManager.Save(_config);
            ApplyLanguage();
        }

        /// <summary>在默认浏览器中打开指定 URL（开源组件主页/仓库）。失败静默忽略。</summary>
        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // 链接打开失败（如缺少默认浏览器关联）时静默忽略。
            }
        }

        /// <summary>Opens the author's mail client via a mailto: link when the
        /// bottom-left copyright LinkLabel is clicked. The link area covers only
        /// the email address (set in ApplyLanguage); the surrounding text stays a
        /// normal label. If no default mail client is registered, Process.Start
        /// throws and is silently ignored so the app keeps running.</summary>
        private void OnCopyrightLinkClicked(object sender, System.Windows.Forms.LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("mailto:" + ContactMail());
            }
            catch
            {
                // 无默认邮件客户端时静默失败，不影响程序。
            }
        }

        /// <summary>
        /// 为左下角两个开源组件链接配置"子链接"：软件名点击跳官网/仓库，协议名点击打开
        /// 随附的本地协议文本文件（THIRD_PARTY_LICENSES.txt）。两个 LinkLabel 各自含两段
        /// 可点击文本，共享 OnOssLinkClicked，按 LinkData 决定打开目标。
        /// 仅在构造函数中调用一次（链接文本为固定专有名词，不随语言变化）。
        /// </summary>
        private void SetupOssLinks()
        {
            string licenseFile = System.IO.Path.Combine(
                System.AppContext.BaseDirectory, "THIRD_PARTY_LICENSES.txt");

            // 7-Zip："7-Zip" -> 官网；"LGPL" -> 本地协议文件
            _sevenZipLink.Links.Clear();
            _sevenZipLink.Links.Add(0, "7-Zip".Length, "https://www.7-zip.org/");
            int lgpl = _sevenZipLink.Text.IndexOf("LGPL", System.StringComparison.Ordinal);
            if (lgpl >= 0) _sevenZipLink.Links.Add(lgpl, "LGPL".Length, licenseFile);

            // ShadowSpawn："ShadowSpawn" -> 仓库；"MIT" -> 本地协议文件
            _shadowSpawnLink.Links.Clear();
            _shadowSpawnLink.Links.Add(0, "ShadowSpawn".Length, "https://github.com/candera/shadowspawn");
            int mit = _shadowSpawnLink.Text.IndexOf("MIT", System.StringComparison.Ordinal);
            if (mit >= 0) _shadowSpawnLink.Links.Add(mit, "MIT".Length, licenseFile);

            _sevenZipLink.LinkClicked += OnOssLinkClicked;
            _shadowSpawnLink.LinkClicked += OnOssLinkClicked;
        }

        /// <summary>左下角开源组件子链接点击：按 LinkData 打开（官网 URL 或本地协议文件）。</summary>
        private void OnOssLinkClicked(object sender, System.Windows.Forms.LinkLabelLinkClickedEventArgs e)
        {
            if (e.Link?.LinkData is string target && !string.IsNullOrEmpty(target))
            {
                OpenUrl(target);
            }
        }

        // ----------------------------------------------------------------- //
        // Auto-locate
        // ----------------------------------------------------------------- //
        private void AutoPrefillProject()
        {
            string projectDir = AutoLocate();
            if (!string.IsNullOrEmpty(projectDir))
            {
                _projectPath = projectDir;
                pathTextBox.Text = projectDir;
                OnPathChanged();
                Log(string.Format(T("auto_located"), projectDir), null);
                return;
            }

            if (!string.IsNullOrEmpty(_config.LastProjectPath) && Directory.Exists(_config.LastProjectPath))
            {
                _projectPath = _config.LastProjectPath;
                pathTextBox.Text = _projectPath;
                OnPathChanged();
            }
        }

        private string AutoLocate()
        {
            string exeDir = BinLocator.AppDirectory;

            // 若程序直接放在名为 "SHBT" 的文件夹内，则其父目录即为工程根（优先）。
            string folderName = Path.GetFileName(exeDir.TrimEnd('\\', '/'));
            if (string.Equals(folderName, "SHBT", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(exeDir);
                if (ProjectDetector.IsValidProject(parent))
                {
                    return parent;
                }
            }

            // 优先检测：程序所在目录、上级目录、上上级目录。
            // 覆盖"检测程序就放在工程目录里"的场景（exe 自身目录即工程 → 自动填入）。
            var priority = new List<string> { exeDir };
            string p1 = Path.GetDirectoryName(exeDir);
            if (!string.IsNullOrEmpty(p1)) priority.Add(p1);
            string p2 = p1 != null ? Path.GetDirectoryName(p1) : null;
            if (!string.IsNullOrEmpty(p2)) priority.Add(p2);
            foreach (string d in priority)
            {
                if (ProjectDetector.IsValidProject(d))
                {
                    return d;
                }
            }

            // 兜底：继续向上至多 4 层，兼容更深的项目嵌套。
            string current = p2;
            for (int i = 0; i < 4 && !string.IsNullOrEmpty(current); i++)
            {
                string parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current)
                {
                    break;
                }

                if (ProjectDetector.IsValidProject(parent))
                {
                    return parent;
                }

                current = parent;
            }

            return null;
        }

        // ----------------------------------------------------------------- //
        // Source project
        // ----------------------------------------------------------------- //
        private void OnBrowseClick(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = T("group_source");
                dlg.SelectedPath = pathTextBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    pathTextBox.Text = dlg.SelectedPath;
                    OnPathChanged();
                }
            }
        }

        /// <summary>
        /// 检测当前正在运行的指定类型工程（WinCC / TIA Portal / STEP7），
        /// 命中后自动填入工程路径并识别类型；未检测到则提示。
        /// </summary>
        private void OnDetectRunningClick(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || !(btn.Tag is ProjectType type))
            {
                return;
            }

            Cursor = Cursors.WaitCursor;
            string path;
            try
            {
                path = RunningProjectDetector.FindRunning(type);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            if (!string.IsNullOrEmpty(path))
            {
                _projectPath = path;
                pathTextBox.Text = path;
                OnPathChanged();
                Log(string.Format(T("detect_running_found"), Localization.ProjectTypeName(type), path), "ok");
            }
            else
            {
                string msg = string.Format(T("detect_running_none"), Localization.ProjectTypeName(type));
                Log(msg, "warn");
                MessageBox.Show(msg, T("type_label"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnPathTextChanged(object sender, EventArgs e)
        {
            OnPathChanged();
        }

        private void OnPathChanged()
        {
            _projectPath = pathTextBox.Text.Trim();
            _projectType = ProjectDetector.Detect(_projectPath);
            _config.LastProjectPath = _projectPath;
            UpdateTypeBadge();
            RefreshActionButton();

            // 检测到工程时输出一条日志（按类型去重，避免逐字符输入时重复刷屏）。
            if (_projectType != null)
            {
                string key = _projectType.Type.ToString();
                if (!string.Equals(key, _lastDetectedLogKey, StringComparison.OrdinalIgnoreCase))
                {
                    Log(string.Format(T("detected_project"), Localization.ProjectTypeName(_projectType.Type), _projectPath), "ok");
                    _lastDetectedLogKey = key;
                }
            }
            else
            {
                _lastDetectedLogKey = null;
            }
        }

        private void UpdateTypeBadge()
        {
            // "工程类型：" 前缀始终显示；未识别到工程时显示"未找到项目"，
            // 识别到时显示 "<类型名>项目"（纯文本，无圆点、无颜色，沿用控件默认前景色）。
            if (_projectType == null || string.IsNullOrEmpty(_projectPath))
            {
                typeBadge.Text = T("type_label") + " " + T("type_not_found");
                return;
            }

            string name = Localization.ProjectTypeName(_projectType.Type);
            typeBadge.Text = T("type_label") + " " + name + T("project_suffix");
        }

        // ----------------------------------------------------------------- //
        // Target drives (multi-select via checkboxes, R4)
        // ----------------------------------------------------------------- //
        /// <summary>Returns the localized physical-type text for a drive, shown in
        /// the 4th list-view column ("种类"). Pure device type only — no safety
        /// semantics — so a protected/license drive still shows its physical type
        /// (e.g. "Fixed disk").</summary>
        private string DriveTypeText(DriveInfoEx d)
        {
            switch (d.Type)
            {
                case System.IO.DriveType.Fixed:     return T("drive_cat_fixed");
                case System.IO.DriveType.Removable: return T("drive_cat_removable");
                case System.IO.DriveType.CDRom:     return T("drive_cat_cdrom");
                case System.IO.DriveType.Network:   return T("drive_cat_network");
                case System.IO.DriveType.Ram:       return T("drive_cat_ram");
                default:                            return T("drive_cat_unknown");
            }
        }

        /// <summary>Returns the safety rating for a drive, shown in the 5th
        /// list-view column ("安全性"). Priority: protected -> forbidden; project
        /// drive -> safe (project disk); Fixed/Removable/Network -> safe; else ->
        /// not recommended.</summary>
        private string DriveSafetyText(DriveInfoEx d, bool isSource)
        {
            if (d.IsProtected) return T("safety_forbidden");
            if (isSource)      return T("safety_project");
            if (d.Type == System.IO.DriveType.Fixed ||
                d.Type == System.IO.DriveType.Removable ||
                d.Type == System.IO.DriveType.Network) return T("safety_safe");
            if (d.Type == System.IO.DriveType.CDRom) return T("safety_readonly");
            if (d.Type == System.IO.DriveType.Ram)   return T("safety_volatile");
            return T("safety_not_recommended");
        }

        private void RefreshDrives()
        {
            _driveList = DriveEnumerator.ListDrives();
            string sourceDrive = GetSourceDriveLetter();

            driveListView.Items.Clear();
            foreach (DriveInfoEx d in _driveList)
            {
                bool isSource = !string.IsNullOrEmpty(sourceDrive) &&
                                string.Equals(d.Letter, sourceDrive, StringComparison.OrdinalIgnoreCase);

                var item = new ListViewItem(new[]
                {
                    d.Letter + ":",
                    string.IsNullOrEmpty(d.Label) ? string.Empty : d.Label,
                    Localization.FormatBytes(d.FreeBytes),
                    DriveTypeText(d),
                    DriveSafetyText(d, isSource)
                });

                // Row text color priority (high -> low):
                //   1. protected / license drive -> red    (default not selectable)
                //   2. the project's own drive    -> orange
                //   3. Fixed / Removable / Network -> default black (writable targets)
                //   4. everything else (CDRom/Ram/Unknown) -> gray
                if (d.IsProtected)
                {
                    item.ForeColor = ProtectedDriveColor;
                }
                else if (isSource)
                {
                    item.ForeColor = ProjectDriveColor;
                }
                else if (d.Type == System.IO.DriveType.Fixed ||
                         d.Type == System.IO.DriveType.Removable ||
                         d.Type == System.IO.DriveType.Network)
                {
                    item.ForeColor = driveListView.ForeColor;
                }
                else
                {
                    item.ForeColor = OpticalDriveColor;
                }

                driveListView.Items.Add(item);
            }

            driveListView.Refresh();
        }

        private string GetSourceDriveLetter()
        {
            if (string.IsNullOrEmpty(_projectPath))
            {
                return null;
            }

            try
            {
                string root = Path.GetPathRoot(_projectPath);
                if (!string.IsNullOrEmpty(root))
                {
                    return root.Substring(0, 1).ToUpperInvariant();
                }
            }
            catch
            {
                // Ignore malformed paths.
            }

            return null;
        }

        private void RefreshDriveSafetyLabels()
        {
            if (_driveList == null)
            {
                return;
            }

            string sourceDrive = GetSourceDriveLetter();
            foreach (ListViewItem item in driveListView.Items)
            {
                string letter = item.Text.TrimEnd(':').ToUpperInvariant();
                DriveInfoEx d = _driveList.Find(x => string.Equals(x.Letter, letter, StringComparison.OrdinalIgnoreCase));
                if (d != null)
                {
                    bool isSource = !string.IsNullOrEmpty(sourceDrive) &&
                                    string.Equals(d.Letter, sourceDrive, StringComparison.OrdinalIgnoreCase);
                    item.SubItems[3].Text = DriveTypeText(d);
                    item.SubItems[4].Text = DriveSafetyText(d, isSource);

                    // Same priority as RefreshDrives(): license/protected (red) >
                    // project drive (orange) > Fixed/Removable/Network (black) > others (gray).
                    if (d.IsProtected)
                    {
                        item.ForeColor = ProtectedDriveColor;
                    }
                    else if (isSource)
                    {
                        item.ForeColor = ProjectDriveColor;
                    }
                    else if (d.Type == System.IO.DriveType.Fixed ||
                             d.Type == System.IO.DriveType.Removable ||
                             d.Type == System.IO.DriveType.Network)
                    {
                        item.ForeColor = driveListView.ForeColor;
                    }
                    else
                    {
                        item.ForeColor = OpticalDriveColor;
                    }
                }
            }
        }

        /// <summary>
        /// 目标盘勾选状态变化处理（增强版 R4）。ItemCheck 在勾选状态实际生效前触发，
        /// 故在受保护盘拦截时直接改写 <see cref="ItemCheckEventArgs.NewValue"/>，
        /// 并通过 BeginInvoke 在状态落定后重算有序目标盘列表。
        /// </summary>
        private void OnDriveItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (e == null || e.Index < 0 || e.Index >= driveListView.Items.Count)
            {
                return;
            }

            ListViewItem item = driveListView.Items[e.Index];
            string letter = item.Text.TrimEnd(':').ToUpperInvariant();
            DriveInfoEx drive = _driveList?.Find(d => string.Equals(d.Letter, letter, StringComparison.OrdinalIgnoreCase));

            // 受保护盘默认禁止勾选；仅在显式开启"强制写入受保护盘"时可勾选，
            // 其最终确认推迟到点击"开始备份"时的 ForceWriteConfirmForm（R10）。
            if (e.NewValue == CheckState.Checked && drive != null && drive.IsProtected && !forceCheckBox.Checked)
            {
                e.NewValue = CheckState.Unchecked;
                MessageBox.Show(T("protected_blocked"), T("force_confirm_title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshActionButton();
                return;
            }

            // ItemCheck 在状态生效前触发，延后到下一消息循环重算有序勾选列表。
            BeginInvoke((Action)RefreshSelectedDrives);
        }

        /// <summary>按 ListView 现有顺序收集所有已勾选目标盘，首个即为 ★ 主目标。</summary>
        private void RefreshSelectedDrives()
        {
            _selectedDrives = new List<string>();
            if (driveListView != null)
            {
                foreach (ListViewItem item in driveListView.Items)
                {
                    if (item.Checked)
                    {
                        _selectedDrives.Add(item.Text.TrimEnd(':').ToUpperInvariant());
                    }
                }
            }

            RefreshActionButton();
        }

        /// <summary>返回按界面顺序排序的目标盘列表（首个为 ★ 主目标）。</summary>
        private List<string> GetOrderedCheckedDrives()
        {
            var list = new List<string>();
            if (driveListView != null)
            {
                foreach (ListViewItem item in driveListView.Items)
                {
                    if (item.Checked)
                    {
                        list.Add(item.Text.TrimEnd(':').ToUpperInvariant());
                    }
                }
            }

            return list;
        }

        /// <summary>判断给定目标盘列表中是否包含受保护（授权/加密狗）盘。
        /// 直接基于传入的实时列表判断，不再依赖可能滞后的 <see cref="_selectedDrives"/>（#4）。</summary>
        private bool HasProtectedSelected(List<string> drives)
        {
            if (drives == null || _driveList == null)
            {
                return false;
            }

            foreach (string letter in drives)
            {
                DriveInfoEx drive = _driveList.Find(d => string.Equals(d.Letter, letter, StringComparison.OrdinalIgnoreCase));
                if (drive != null && drive.IsProtected)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnForceToggle(object sender, EventArgs e)
        {
            _config.ForceProtected = forceCheckBox.Checked;
            ConfigManager.Save(_config);
            if (forceCheckBox.Checked)
            {
                return;
            }

            // 关闭强制写入时，取消所有已勾选的受保护盘，避免遗留危险选择。
            bool changed = false;
            if (driveListView != null)
            {
                foreach (ListViewItem item in driveListView.Items)
                {
                    string letter = item.Text.TrimEnd(':').ToUpperInvariant();
                    DriveInfoEx drive = _driveList?.Find(d => string.Equals(d.Letter, letter, StringComparison.OrdinalIgnoreCase));
                    if (drive != null && drive.IsProtected && item.Checked)
                    {
                        item.Checked = false;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                RefreshSelectedDrives();
            }
        }

        private void OnCompressionChanged(object sender, EventArgs e)
        {
            if (_suppressCompEvent)
            {
                return;
            }

            string display = compComboBox.Text;
            foreach (var kv in _compOptions)
            {
                if (kv.Value == display)
                {
                    _compKey = kv.Key;
                    _config.CompressionLevel = kv.Key;
                    ConfigManager.Save(_config);
                    break;
                }
            }
        }

        // ----------------------------------------------------------------- //
        // Exclude rules (single multiline text box: one pattern per line)
        // ----------------------------------------------------------------- //
        /// <summary>将当前配置中的排除规则以"每行一条"形式载入多行文本框。
        /// 注意：WinForms 多行 TextBox 只把 CRLF（\r\n）识别为换行，单个 \n 会
        /// 被合并到同一行显示，因此这里必须用 Environment.NewLine 拼接。
        /// 被禁用的规则以行首 '#' 标记（gitignore 风格），从而保留其 Enabled=false
        /// 状态，避免经文本框编辑后"禁用"被悄悄重新启用（#7）。</summary>
        private void LoadExcludeTextBox()
        {
            var rules = _config.ExcludeRules ?? new List<ExcludeRule>();
            exclTextBox.Lines = rules
                .Select(r =>
                {
                    string p = (r.Pattern ?? "").Trim();
                    return p.Length == 0 ? null : (r.Enabled ? p : "#" + p);
                })
                .Where(s => s != null)
                .ToArray();
        }

        /// <summary>从多行文本框解析排除规则列表：每行一条；行首 '#' 表示禁用
        /// （gitignore 风格），解析时剥离 '#' 并将 Enabled 置为 false，从而保留界面上
        /// "禁用"的规则（#7）。</summary>
        private List<ExcludeRule> GetExcludeRulesFromTextBox()
        {
            var list = new List<ExcludeRule>();
            foreach (var line in exclTextBox.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var raw = line.Trim();
                if (raw.Length == 0)
                {
                    continue;
                }

                bool enabled = true;
                string p = raw;
                if (p.StartsWith("#"))
                {
                    enabled = false;
                    p = p.Substring(1).Trim();
                }

                if (p.Length > 0)
                {
                    list.Add(new ExcludeRule { Enabled = enabled, Pattern = p, Comment = string.Empty });
                }
            }

            return list;
        }

        /// <summary>文本框内容变化时实时同步回配置并落盘。</summary>
        private void OnExcludeTextChanged(object sender, EventArgs e)
        {
            _config.ExcludeRules = GetExcludeRulesFromTextBox();
            // #10：防抖——输入过程中不频繁落盘，停顿 600ms 后再保存配置。
            if (_exclSaveTimer != null)
            {
                _exclSaveTimer.Stop();
                _exclSaveTimer.Start();
            }
            else
            {
                ConfigManager.Save(_config);
            }
        }

        /// <summary>弹出"默认排除项说明"对话框，逐条解释默认配置中排除的每种文件。
        /// 排版：规则名用粗体，下一行缩进以"- "开头给出说明，层次更清晰。</summary>
        private void OnExcludeHelpClick(object sender, EventArgs e)
        {
            bool zh = Localization.CurrentCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            var notes = ConfigManager.DefaultExcludeNotes(zh);

            using (var dlg = new Form())
            {
                // 字体与样式统一为主窗口（FontHelper 已作用于 this.Font）。
                dlg.Font = this.Font;
                dlg.Text = T("exclude_help_title");
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.ClientSize = new Size(540, 380);

                // 内容面板留出 10px 边距，避免文字贴窗边。
                var content = new Panel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(10)
                };

                var rtf = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    WordWrap = true,
                    ScrollBars = RichTextBoxScrollBars.Both,
                    BorderStyle = BorderStyle.None,
                    DetectUrls = false,
                    Font = this.Font
                };
                content.Controls.Add(rtf);

                var baseFont = this.Font;
                var regular = baseFont;
                var bold = new Font(baseFont, FontStyle.Bold);

                // 引导语（常规字体）。
                rtf.SelectionFont = regular;
                rtf.SelectionIndent = 0;
                rtf.AppendText(T("exclude_help_intro"));
                rtf.AppendText("\n\n");

                // 逐条：粗体规则名 + 下一行缩进的 "- 说明"。
                foreach (var kv in notes)
                {
                    rtf.SelectionIndent = 0;
                    rtf.SelectionFont = bold;
                    rtf.AppendText(kv.Key);
                    rtf.AppendText("\n");
                    rtf.SelectionIndent = 20;
                    rtf.SelectionFont = regular;
                    rtf.AppendText("- " + kv.Value);
                    rtf.AppendText("\n\n");
                }

                rtf.SelectionIndent = 0;
                rtf.SelectionStart = 0;
                rtf.SelectionLength = 0;

                var okBtn = new Button
                {
                    Text = T("oss_credits_close"),
                    DialogResult = DialogResult.OK,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MinimumSize = new Size(75, 23),
                    Font = this.Font
                };

                var bottom = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    FlowDirection = FlowDirection.RightToLeft,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(0, 4, 0, 0)
                };
                bottom.Controls.Add(okBtn);

                dlg.Controls.Add(content);
                dlg.Controls.Add(bottom);
                dlg.AcceptButton = okBtn;
                dlg.ShowDialog(this);
            }
        }

        /// <summary>仅将排除规则复位为默认值（保留当前界面语言、压缩级别、输出子目录、
        /// 强制写入及其它所有设置），刷新界面并落盘。</summary>
        private void OnRestoreDefaultsClick(object sender, EventArgs e)
        {
            DialogResult r = MessageBox.Show(T("restore_confirm_msg"), T("restore_confirm_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes)
            {
                return;
            }

            // 仅复位排除规则；其余设置（压缩级别、输出子目录、强制写入、上次工程/备份记录）保持不变。
            _config.ExcludeRules = ConfigManager.DefaultExcludes
                .Select(x => new ExcludeRule { Enabled = true, Pattern = x, Comment = string.Empty })
                .ToList();
            ConfigManager.Save(_config);

            // 仅刷新排除规则文本框。
            LoadExcludeTextBox();
            Log(T("restored_defaults"), "ok");
        }

        // ----------------------------------------------------------------- //
        // Start / stop
        // ----------------------------------------------------------------- //
        private bool CanStart()
        {
            if (!_isAdmin) return false;
            if (_projectType == null) return false;
            // 直接读取界面实时勾选状态，避免依赖异步刷新的 _selectedDrives（#4）。
            if (GetOrderedCheckedDrives().Count == 0) return false;
            return true;
        }

        private void RefreshActionButton()
        {
            startButton.Text = _running ? T("stop") : T("start");
            startButton.Enabled = true;
        }

        private void OnStartClick(object sender, EventArgs e)
        {
            if (_running)
            {
                _cts?.Cancel();
                return;
            }

            if (!CanStart())
            {
                if (!_isAdmin)
                {
                    DialogResult r = MessageBox.Show(T("admin_relaunch_msg"), T("error_title"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes && AdminHelper.RelaunchAsAdmin())
                    {
                        Application.Exit();
                        return;
                    }

                    if (r == DialogResult.Yes)
                    {
                        MessageBox.Show(T("admin_warn"), T("error_title"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else if (_projectType == null)
                {
                    MessageBox.Show(T("type_unknown"), T("error_title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (_selectedDrives == null || _selectedDrives.Count == 0)
                {
                    MessageBox.Show(T("no_selection"), T("error_title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return;
            }

            // R10：勾选目标包含受保护盘时，弹出专用确认对话框；取消则放弃启动。
            // 直接基于界面实时勾选列表判断，避免依赖可能滞后的 _selectedDrives（#4）。
            List<string> ordered = GetOrderedCheckedDrives();
            if (HasProtectedSelected(ordered))
            {
                using (var dlg = new ForceWriteConfirmForm())
                {
                    if (dlg.ShowDialog(this) != DialogResult.Yes)
                    {
                        return;
                    }
                }
            }

            StartBackup();
        }

        private void StartBackup()
        {
            List<ExcludeRule> excludes = GetExcludeRulesFromTextBox();
            var opts = new BackupOptions
            {
                OutputSubdir = string.IsNullOrWhiteSpace(subTextBox.Text) ? "Backups" : subTextBox.Text.Trim(),
                CompressionLevel = _compKey,
                ExcludeRules = excludes,
                ForceProtected = forceCheckBox.Checked,
                ShadowLetter = "Q"
            };

            _config.OutputSubdir = opts.OutputSubdir;
            _config.ExcludeRules = excludes;
            ConfigManager.Save(_config);

            // R8：启动前逐目标盘空闲空间预检；任一目标不足则标红并阻止启动。
            List<string> drives = GetOrderedCheckedDrives();
            Dictionary<string, SpaceCheck> spaceChecks = DiskSpaceChecker.CheckTargets(_projectPath, drives);
            bool insufficient = false;
            foreach (KeyValuePair<string, SpaceCheck> kv in spaceChecks)
            {
                if (!kv.Value.Ok)
                {
                    insufficient = true;
                    Log(string.Format(T("space_insufficient"), kv.Key + ":",
                        Localization.FormatBytes(kv.Value.NeededBytes),
                        Localization.FormatBytes(kv.Value.FreeBytes)), "warn");
                    HighlightDriveRed(kv.Key);
                }
            }

            if (insufficient)
            {
                var firstBad = spaceChecks.FirstOrDefault(kv => !kv.Value.Ok);
                if (firstBad.Value != null)
                {
                    MessageBox.Show(string.Format(T("space_insufficient"), firstBad.Key + ":",
                        Localization.FormatBytes(firstBad.Value.NeededBytes),
                        Localization.FormatBytes(firstBad.Value.FreeBytes)),
                        T("error_title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return;
            }

            _running = true;
            _cts = new CancellationTokenSource();
            RefreshActionButton();

            SetStage(StageKey.Prepare);
            Log(T("running"), null);

            string typeKey = _projectType.Type.ToString();
            string projectPath = _projectPath;
            CancellationToken token = _cts.Token;

            var worker = new BackupWorker();
            worker.Progress += (s, p) => this.BeginInvoke((Action<int?>)OnSetProgress, p);
            worker.StageChanged += (s, k) => this.BeginInvoke((Action<StageKey>)OnSetStage, k);
            worker.Log += (s, m) => this.BeginInvoke((Action<string>)OnLogLine, m);
            worker.Completed += (s, r) => this.BeginInvoke((Action<BackupResult>)OnBackupFinished, r);

            _backupTask = Task.Run(() => worker.Run(projectPath, drives, typeKey, opts, token));
        }

        /// <summary>将指定盘符对应的列表行前景色标红，提示其空间不足（R8）。</summary>
        private void HighlightDriveRed(string letter)
        {
            if (driveListView == null)
            {
                return;
            }

            foreach (ListViewItem item in driveListView.Items)
            {
                if (string.Equals(item.Text.TrimEnd(':').ToUpperInvariant(), letter, StringComparison.OrdinalIgnoreCase))
                {
                    item.ForeColor = ProtectedDriveColor;
                }
            }

            driveListView.Refresh();
        }

        // ----------------------------------------------------------------- //
        // Stage / progress / log (UI thread)
        // ----------------------------------------------------------------- //
        private void OnSetStage(StageKey key)
        {
            SetStage(key);
        }

        private void SetStage(StageKey key)
        {
            _stageKey = key;
            _lastLogPct = -1;

            string text = Localization.StageText(key);
            Log("▶ " + text, "stage");
        }

        private void OnSetProgress(int? pct)
        {
            if (pct == null)
            {
                return;
            }

            int value = Math.Max(0, Math.Min(100, pct.Value));

            if (_stageKey == StageKey.Archive &&
                (_lastLogPct < 0 || value == 100 || value - _lastLogPct >= 5))
            {
                Log(Localization.StageText(StageKey.Archive) + "  " + value + "%", "info");
                _lastLogPct = value;
            }
        }

        private void OnLogLine(string message)
        {
            Log(message, null);
        }

        private void Log(string message, string tag)
        {
            if (logTextBox == null || !logTextBox.IsHandleCreated)
            {
                return;
            }

            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.SelectionLength = 0;
            logTextBox.SelectionColor = LogColor(tag);
            logTextBox.AppendText(message + Environment.NewLine);
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.ScrollToCaret();
        }

        /// <summary>Maps a log tag (or the current backup stage) to a display color
        /// so each pipeline stage is visually distinguishable in the log.</summary>
        private Color LogColor(string tag)
        {
            switch (tag)
            {
                case "admin": return AdminModeColor;                   // 绿色：管理员模式行
                case "user":  return UserModeColor;                    // 紫色：非管理员模式行
                case "ok":    return Color.FromArgb(0x1F, 0x4E, 0x79); // 蓝色：成功/完成高亮
                case "stage": return Color.FromArgb(0x1F, 0x4E, 0x79); // 蓝色：阶段横幅 ▶ …
                case "info":  return Color.Black;                      // 黑色：普通信息
                case "err":   return Color.FromArgb(0xC0, 0x00, 0x00); // 红色：错误
                case "warn":  return Color.FromArgb(0xB8, 0x6A, 0x00); // 橙色：警告
                default:      return Color.Black;                      // 黑色：进度细节等（tag 为 null）
            }
        }

        private void OnBackupFinished(BackupResult result)
        {
            _running = false;
            RefreshActionButton();
            _backupTask = null;
            _cts = null;

            if (result.Success)
            {
                // 记录最近一次成功备份的产物路径（时间已含于文件名，不再单独保存）。
                _config.LastBackupPath = result.Message ?? string.Empty;
                ConfigManager.Save(_config);
                UpdateLastBackupLabel();

                Log(string.Format(T("done_msg"), result.Message), "ok");
                MessageBox.Show(string.Format(T("done_msg"), result.Message), T("done_title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                Log(result.Message, "err");
                if (result.Message.Contains("取消") || result.Message.ToLowerInvariant().Contains("cancel"))
                {
                    MessageBox.Show(T("cancel_msg"), T("cancel_title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(string.Format(T("error_msg"), result.Message), T("error_title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>刷新"上次备份"只读标签：有记录时显示路径（时间已含于文件名，不再单独展示），否则仅显示前缀。</summary>
        private void UpdateLastBackupLabel()
        {
            if (lastBackupLabel == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_config.LastBackupPath))
            {
                lastBackupLabel.Text = T("last_backup") + " " + _config.LastBackupPath;
            }
            else
            {
                lastBackupLabel.Text = T("last_backup");
            }
        }

        // ----------------------------------------------------------------- //
        // Close
        // ----------------------------------------------------------------- //
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_running)
            {
                DialogResult r = MessageBox.Show(T("confirm_quit"), T("app_title"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }

                _cts?.Cancel();
            }

            try
            {
                // Window size is now fixed (see MainForm.Designer.cs) and must NOT
                // be persisted or restored, otherwise a previously-saved small
                // geometry would make the window too cramped to show everything.
                ConfigManager.Save(_config);
            }
            catch
            {
                // Ignore config persistence failures.
            }
        }

        private void ApplyGeometry()
        {
            // Window size is fixed at design time (FixedSingle + Min=Max). Do not
            // restore any persisted geometry — it would fight the fixed size.
        }
    }
}

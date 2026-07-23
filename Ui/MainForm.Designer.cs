// <summary>
// Hand-written WinForms initializer for MainForm (no Visual Studio designer
// required). Layout (top to bottom):
//   info bar   | app description + language picker
//   source-project group (full width)
//   target-drive group (full width, now multi-select with checkboxes)
//   [ options (left) | log (right) ]  <- same row, two columns
//   action bar (full width, bottom): last-backup label + start button + copyright
//
// 增强版（T04）变更：
//   - 目标盘 ListView 改为 MultiSelect + CheckBoxes，勾选即选为目标，首个勾选为 ★ 主目标。
//   - 排除规则恢复为单个多行文本框（exclTextBox），撤销上次的行容器方案。
//   - 操作栏保留只读的"上次备份"标签（仅位置，时间已含于文件名）与开始备份按钮。
//
// Functional areas are marked by GroupBox captions (GroupBox.Text), which draw
// a bordered section with a larger, bold title. Section titles are styled in
// MainForm.ApplyLanguage (after FontHelper applied the base font).
// </summary>
using SHBT.Core;

namespace SHBT.Ui
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        // Top info bar (description + language picker)
        private System.Windows.Forms.TableLayoutPanel infoPanel;
        private System.Windows.Forms.Label descLabel;
        private System.Windows.Forms.ComboBox languageComboBox;
        private System.Windows.Forms.Label langLabel;

        // Full-width rows
        private System.Windows.Forms.GroupBox srcGroup;
        private System.Windows.Forms.TableLayoutPanel srcTable;
        private System.Windows.Forms.TextBox pathTextBox;
        private System.Windows.Forms.Button browseButton;
        private System.Windows.Forms.Label typeBadge;        // 工程类型：常态化纯文本（无圆点、无颜色）

        private System.Windows.Forms.GroupBox tgtGroup;
        private System.Windows.Forms.TableLayoutPanel tgtTable;
        private System.Windows.Forms.Label tgtHint;
        private System.Windows.Forms.ListView driveListView;
        private System.Windows.Forms.ColumnHeader colLetter;
        private System.Windows.Forms.ColumnHeader colLabel;
        private System.Windows.Forms.ColumnHeader colFree;
        private System.Windows.Forms.ColumnHeader colType;   // 第 4 列：种类（设备物理类型）
        private System.Windows.Forms.ColumnHeader colSafety; // 第 5 列：安全性

        private System.Windows.Forms.GroupBox optGroup;
        private System.Windows.Forms.TableLayoutPanel optTable;
        private System.Windows.Forms.Label compLabel;
        private System.Windows.Forms.ComboBox compComboBox;
        private System.Windows.Forms.Label subLabel;
        private System.Windows.Forms.TextBox subTextBox;
        private System.Windows.Forms.CheckBox forceCheckBox;
        private System.Windows.Forms.Label exclLabel;
        private System.Windows.Forms.TextBox exclTextBox;          // 排除规则：恢复为单个多行文本框
        private System.Windows.Forms.Button _exclHelpButton;      // "规则说明"按钮：点击弹出默认排除项含义
        private System.Windows.Forms.Button _restoreButton;       // 恢复默认规则按钮（仅复位排除规则）
        private System.Windows.Forms.FlowLayoutPanel _optBtnPanel;   // 承载"规则说明"+"恢复默认规则"按钮的流式面板（间距同检测按钮）

        // 源工程组：浏览按钮下方的"检测正在运行的项目"三连按钮。
        private System.Windows.Forms.FlowLayoutPanel _detectPanel;   // 承载三个检测按钮的流式面板
        private System.Windows.Forms.Button _btnDetectWinCC;         // 检测 WinCC 项目
        private System.Windows.Forms.Button _btnDetectTIA;           // 检测 TIA Portal 项目
        private System.Windows.Forms.Button _btnDetectStep7;         // 检测 STEP 7 项目
        private System.Windows.Forms.TableLayoutPanel _typeRow;      // 第 2 行容器：工程类型（左，填满）+ 检测按钮（右，自适应）

        private System.Windows.Forms.TableLayoutPanel opPanel;
        private System.Windows.Forms.Label lastBackupLabel;       // 上次备份位置（只读；时间已含于文件名，不再单独记录）
        private System.Windows.Forms.Button startButton;
        private System.Windows.Forms.LinkLabel copyrightLabel;
        private System.Windows.Forms.TableLayoutPanel _ossLinks;     // 版权行左侧：开源组件超链接
        private System.Windows.Forms.LinkLabel _sevenZipLink;
        private System.Windows.Forms.LinkLabel _shadowSpawnLink;
        private System.Windows.Forms.Label _ossSep;

        // Lower row: options (left) | log (right)
        private System.Windows.Forms.SplitContainer mainSplit;
        private System.Windows.Forms.GroupBox logGroup;
        private System.Windows.Forms.TableLayoutPanel logTable;
        private System.Windows.Forms.RichTextBox logTextBox;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            // ---- Form ----
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.ClientSize = new System.Drawing.Size(760, 908);
            // Lock the window size: FixedSingle + Min=Max prevents manual resizing
            // (the user asked for a fixed, fully-visible window).
            this.MinimumSize = this.Size;
            this.MaximumSize = this.Size;
            this.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);
            this.Text = "SIMATIC Hot Backup Tool";

            // ---- Info bar (top): description + language picker ----
            this.infoPanel = new System.Windows.Forms.TableLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Top,
                ColumnCount = 1,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                Padding = new System.Windows.Forms.Padding(10, 4, 10, 4),
                BackColor = System.Drawing.SystemColors.Control,
                Margin = new System.Windows.Forms.Padding(0)
            };
            // 信息栏不外包任何边框容器：直接用 infoPanel（Dock=Top）承载说明文字，
            // 背景色与窗体同为 Control 灰，不画边框即无可见方框；仅靠 Padding 留间距，
            // 以区分"说明"与下方自带边框的功能分区。
            this.infoPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.infoPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));

            this.languageComboBox = new System.Windows.Forms.ComboBox
            {
                DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
                DrawMode = System.Windows.Forms.DrawMode.Normal,
                Width = 158,
                Height = 24,
                ItemHeight = 22,
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
                Margin = new System.Windows.Forms.Padding(0)
            };
            this.descLabel = new System.Windows.Forms.Label
            {
                // 顶部说明：上下间距压缩为 0（留白由 infoPanel Padding 承担），
                // 文字区高度恰好 3 行，使"上下间距 + 文字"总高 ≈ 3 行文字。
                AutoSize = true,
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Top,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                MaximumSize = new System.Drawing.Size(740, 0),
                MinimumSize = new System.Drawing.Size(0, 45),
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0)
            };
            // 信息栏只保留 descLabel（说明文字）；粗体大标题与开源说明按钮均已移除。
            this.infoPanel.Controls.Add(this.descLabel, 0, 0);

            // ---- Source project group (full width) ----
            this.srcGroup = new System.Windows.Forms.GroupBox
            {
                Dock = System.Windows.Forms.DockStyle.Top,
                Height = 108,
                Padding = new System.Windows.Forms.Padding(10, 8, 10, 8)
            };
            this.srcTable = new System.Windows.Forms.TableLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new System.Windows.Forms.Padding(0)
            };
            this.srcTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            // Column 1 is AutoSize (not Absolute) so it hugs the auto-sized browse
            // button. A fixed Absolute width rounded per-DPI could clip the button's
            // right edge on 125%/150%/175% scaling (the original 90F bug).
            this.srcTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.srcTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // 工程路径行（第 0 行）
            // 第 1 行承载跨两列的 _typeRow（Dock=Fill）。该行必须为"确定高度"（Percent），
            // 否则 AutoSize 行内的 Dock=Fill 子容器会塌缩为 0 高度，导致类型文字与检测按钮整体消失。
            this.srcTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F)); // 类型（左）+ 检测按钮（右）

            this.pathTextBox = new System.Windows.Forms.TextBox
            {
                // Dock=Fill 使文本框填满第 0 列（Percent 100%），长度自适应延伸至浏览按钮左侧；
                // 此即"前几个版本"的正确样式（Anchor 方式在 TableLayoutPanel 的 Percent+AutoSize
                // 混合列中会出现长度不足的问题）。右侧留 8px Margin 作为与按钮之间的合适间距。
                Dock = System.Windows.Forms.DockStyle.Fill,
                Height = 23,
                Margin = new System.Windows.Forms.Padding(0, 0, 8, 0)
            };
            this.browseButton = new System.Windows.Forms.Button
            {
                Text = "浏览…",
                // AutoSize so the button width is driven by its (localized) text and
                // never clipped at high DPI. MinimumSize guarantees a usable hit area
                // (~72x24) even for very short labels. Fixed Width/Height removed.
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                MinimumSize = new System.Drawing.Size(72, 24),
                Anchor = System.Windows.Forms.AnchorStyles.Right | System.Windows.Forms.AnchorStyles.Top,
                // 左间距归零：与文本框右侧的 8px 间距共同构成按钮前的留白。
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 8)
            };
            this.typeBadge = new System.Windows.Forms.Label
            {
                // 与右侧三个检测按钮同处一行并垂直居中对齐：AutoSize=false + Dock=Fill 使
                // 标签填满行高，TextAlign=MiddleLeft 令"工程类型：…"文字在垂直方向居中。
                AutoSize = false,
                Dock = System.Windows.Forms.DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0)
            };

            // 检测正在运行的项目：三个按钮，置于流式面板内（自动换行以适配长本地化文案）。
            this._btnDetectWinCC = new System.Windows.Forms.Button
            {
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                MinimumSize = new System.Drawing.Size(84, 24),
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Top,
                Margin = new System.Windows.Forms.Padding(0, 0, 6, 0),
                Tag = ProjectType.WinCC_V7X
            };
            this._btnDetectTIA = new System.Windows.Forms.Button
            {
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                MinimumSize = new System.Drawing.Size(84, 24),
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Top,
                Margin = new System.Windows.Forms.Padding(0, 0, 6, 0),
                Tag = ProjectType.TIA_Portal
            };
            this._btnDetectStep7 = new System.Windows.Forms.Button
            {
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                MinimumSize = new System.Drawing.Size(84, 24),
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Top,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0),
                Tag = ProjectType.STEP7_V5X
            };
            this._detectPanel = new System.Windows.Forms.FlowLayoutPanel
            {
                // 三个检测按钮靠右对齐，并与左侧"工程类型"文字垂直居中于同一行：
                // AutoSize 自适应宽度、Anchor=Right 靠右、无额外顶部 Margin。
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                Anchor = System.Windows.Forms.AnchorStyles.Right,
                Padding = new System.Windows.Forms.Padding(0),
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0)
            };
            this._detectPanel.Controls.Add(this._btnDetectWinCC);
            this._detectPanel.Controls.Add(this._btnDetectTIA);
            this._detectPanel.Controls.Add(this._btnDetectStep7);

            // 第 2 行容器：左侧"工程类型："文字填满剩余宽度，右侧三个检测按钮自适应并靠右。
            // 关键修复：检测按钮面板若直接置于 srcTable 第 1 列（AutoSize），该列宽度会被三个按钮撑宽，
            // 反过来挤窄第 0 列（源文本框）。改为统一放进 _typeRow（跨两列、整行满宽），
            // 使 srcTable 第 1 列宽度仅由浏览按钮决定，不再被检测按钮挤占。
            this._typeRow = new System.Windows.Forms.TableLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new System.Windows.Forms.Padding(0),
                Margin = new System.Windows.Forms.Padding(0, 4, 0, 0)
            };
            this._typeRow.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this._typeRow.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this._typeRow.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this._typeRow.Controls.Add(this.typeBadge, 0, 0);
            this._typeRow.Controls.Add(this._detectPanel, 1, 0);

            this.srcTable.Controls.Add(this.pathTextBox, 0, 0);
            this.srcTable.Controls.Add(this.browseButton, 1, 0);
            // 工程类型文字（左，填满）+ 三个检测按钮（右，自适应靠右）同处一行：
            // 放入跨两列的 _typeRow，使 srcTable 第 1 列宽度仅由浏览按钮决定，不再被检测按钮撑宽。
            this.srcTable.Controls.Add(this._typeRow, 0, 1);
            this.srcTable.SetColumnSpan(this._typeRow, 2);
            this.srcGroup.Controls.Add(this.srcTable);

            // ---- Target drive group (full width) ----
            this.tgtGroup = new System.Windows.Forms.GroupBox
            {
                Dock = System.Windows.Forms.DockStyle.Top,
                Height = 220,
                Padding = new System.Windows.Forms.Padding(10, 8, 10, 8)
            };
            this.tgtTable = new System.Windows.Forms.TableLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new System.Windows.Forms.Padding(0)
            };
            this.tgtTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tgtTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tgtTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 158F));

            this.tgtHint = new System.Windows.Forms.Label
            {
                AutoSize = true,
                Dock = System.Windows.Forms.DockStyle.Fill,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 6)
            };
            this.colLetter = new System.Windows.Forms.ColumnHeader { Width = 60 };
            this.colLabel = new System.Windows.Forms.ColumnHeader { Width = 150 };
            this.colFree = new System.Windows.Forms.ColumnHeader { Width = 100 };
            this.colType = new System.Windows.Forms.ColumnHeader { Width = 100 };   // 种类
            this.colSafety = new System.Windows.Forms.ColumnHeader { Width = 140 }; // 安全性
            this.driveListView = new System.Windows.Forms.ListView
            {
                View = System.Windows.Forms.View.Details,
                FullRowSelect = true,
                GridLines = true,
                // 增强版（R4）：支持多选 + 每行复选框；勾选即选为目标盘，首个勾选为 ★ 主目标。
                MultiSelect = true,
                CheckBoxes = true,
                HideSelection = false,
                HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable,
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right
            };
            this.driveListView.Columns.AddRange(new[] { this.colLetter, this.colLabel, this.colFree, this.colType, this.colSafety });

            this.tgtTable.Controls.Add(this.tgtHint, 0, 0);
            this.tgtTable.Controls.Add(this.driveListView, 0, 1);
            this.tgtGroup.Controls.Add(this.tgtTable);

            // ---- Options group (left column of the lower split) ----
            // Order: language -> compression -> output subdir -> force write
            // -> exclude rules (single multiline text box). The
            // exclude text box row grows (Percent) so the options column height
            // matches the log column.
            this.optGroup = new System.Windows.Forms.GroupBox
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Padding = new System.Windows.Forms.Padding(10, 8, 10, 8)
            };
            this.optTable = new System.Windows.Forms.TableLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new System.Windows.Forms.Padding(0)
            };
            this.optTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 170F));
            this.optTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            // 语言选择行（第 0 行，AutoSize）：在 compression 之前插入，使后续各行整体下移一行。
            this.optTable.RowStyles.Insert(0, new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // 语言选择
            this.optTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // compression
            this.optTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // output subdir
            this.optTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // force write
            this.optTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // 排除规则标签（独立一行，跨两列，宽度与下方文本框一致）
            this.optTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F)); // 多行文本框（跨两列，吸收剩余高度，使选项列与日志列等高）
            this.optTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // 规则说明 + 恢复默认规则 按钮行

            // 语言选择标签：与 compLabel 同款（Dock=Fill + MiddleLeft）。去掉底部 8px Margin，
            // 使该行高度贴合控件高度，Dock=Fill 的 MiddleLeft 文字即与右侧下拉垂直居中对齐。
            this.langLabel = new System.Windows.Forms.Label
            {
                AutoSize = false,
                Dock = System.Windows.Forms.DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new System.Windows.Forms.Padding(0, 0, 8, 8)
            };
            // Labels use Dock=Fill + MiddleLeft so the text is vertically centered
            // against the taller control in the same row. 去掉底部 8px Margin 以对齐。
            this.compLabel = new System.Windows.Forms.Label
            {
                AutoSize = false,
                Dock = System.Windows.Forms.DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new System.Windows.Forms.Padding(0, 0, 8, 8)
            };
            this.compComboBox = new System.Windows.Forms.ComboBox
            {
                DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
                Width = 140,
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0)
            };
            this.subLabel = new System.Windows.Forms.Label
            {
                AutoSize = false,
                Dock = System.Windows.Forms.DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new System.Windows.Forms.Padding(0, 0, 8, 8)
            };
            this.subTextBox = new System.Windows.Forms.TextBox
            {
                Width = 160,
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
                TextAlign = System.Windows.Forms.HorizontalAlignment.Left,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0)
            };
            this.forceCheckBox = new System.Windows.Forms.CheckBox
            {
                AutoSize = true,
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Top,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 8)
            };
            this.exclLabel = new System.Windows.Forms.Label
            {
                // 排除规则小标题：独占一行（跨两列），宽度与下方多行文本框一致；
                // 早期与"?"同列时因宽度不足被挤换行，故改为独立整行。
                AutoSize = false,
                Dock = System.Windows.Forms.DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 4)
            };
            // "规则说明"按钮：文本由 ApplyLanguage 设为 T("rule_help")（多语言）。
            // 与"恢复默认规则"同置于 _optBtnPanel 流式面板内，按钮间距(6px)与检测按钮一致。
            this._exclHelpButton = new System.Windows.Forms.Button
            {
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                MinimumSize = new System.Drawing.Size(72, 24),
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Top,
                Margin = new System.Windows.Forms.Padding(0, 0, 6, 0)
            };
            this.exclTextBox = new System.Windows.Forms.TextBox
            {
                Multiline = true,
                AcceptsReturn = true,   // 允许在文本框内按 Enter 换行（每行一条排除规则）
                ScrollBars = System.Windows.Forms.ScrollBars.Vertical,
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right | System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom,
                Margin = new System.Windows.Forms.Padding(0, 4, 0, 0)
            };
            // 恢复默认规则按钮：置于 _optBtnPanel 内、"规则说明"右侧，自动宽度。
            this._restoreButton = new System.Windows.Forms.Button
            {
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                MinimumSize = new System.Drawing.Size(96, 24),
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Top,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0)
            };

            this.optTable.Controls.Add(this.langLabel, 0, 0);
            this.optTable.Controls.Add(this.languageComboBox, 1, 0);
            this.optTable.Controls.Add(this.compLabel, 0, 1);
            this.optTable.Controls.Add(this.compComboBox, 1, 1);
            this.optTable.Controls.Add(this.subLabel, 0, 2);
            this.optTable.Controls.Add(this.subTextBox, 1, 2);
            this.optTable.Controls.Add(this.forceCheckBox, 0, 3);
            this.optTable.SetColumnSpan(this.forceCheckBox, 2);
            // 排除规则小标题独占第 4 行（跨两列，宽度与文本框一致）。
            this.optTable.Controls.Add(this.exclLabel, 0, 4);
            this.optTable.SetColumnSpan(this.exclLabel, 2);
            // 多行文本框独占第 5 行（跨两列，Percent 吸收剩余高度）。
            this.optTable.Controls.Add(this.exclTextBox, 0, 5);
            this.optTable.SetColumnSpan(this.exclTextBox, 2);
            // 底部按钮行(第6行)：规则说明 + 恢复默认规则 置于流式面板内，间距与检测按钮一致。
            this._optBtnPanel = new System.Windows.Forms.FlowLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                Padding = new System.Windows.Forms.Padding(0),
                Margin = new System.Windows.Forms.Padding(0, 4, 0, 0)
            };
            this._optBtnPanel.Controls.Add(this._exclHelpButton);
            this._optBtnPanel.Controls.Add(this._restoreButton);
            this.optTable.Controls.Add(this._optBtnPanel, 0, 6);
            this.optTable.SetColumnSpan(this._optBtnPanel, 2);
            this.optGroup.Controls.Add(this.optTable);

            // ---- Log group (right column of the lower split) ----
            this.logGroup = new System.Windows.Forms.GroupBox
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Padding = new System.Windows.Forms.Padding(10, 8, 10, 8)
            };
            this.logTable = new System.Windows.Forms.TableLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                Padding = new System.Windows.Forms.Padding(0)
            };
            this.logTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.logTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));

            this.logTextBox = new System.Windows.Forms.RichTextBox
            {
                Multiline = true,
                ReadOnly = true,
                // 自动换行，仅保留垂直滚动条（取消横向滚动条）。
                WordWrap = true,
                ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical,
                Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
                Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point)
            };

            this.logTable.Controls.Add(this.logTextBox, 0, 0);
            this.logGroup.Controls.Add(this.logTable);

            // ---- Lower row: options (left) | log (right) ----
            // SplitterDistance / Panel*MinSize are applied in the form's Load
            // handler (see MainForm ctor) because during InitializeComponent the
            // container has no real width and setting them would throw.
            this.mainSplit = new System.Windows.Forms.SplitContainer
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Vertical,
                FixedPanel = System.Windows.Forms.FixedPanel.Panel1,
                Padding = new System.Windows.Forms.Padding(8, 4, 8, 4)
            };
            this.mainSplit.Panel1.Controls.Add(this.optGroup);
            this.mainSplit.Panel2.Controls.Add(this.logGroup);

            // ---- Start-backup action bar (full width, bottom) ----
            // 三行：上次备份标签（第 0 行，跨两列）/ 开始备份按钮（第 1 行，跨两列）/
            // 版权+版本（第 2 行左）+ 开源说明按钮（第 2 行右）。进度条已移除。
            this.opPanel = new System.Windows.Forms.TableLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Bottom,
                Height = 104,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new System.Windows.Forms.Padding(10, 7, 10, 7)
            };
            // 左列仅决定底部第 2 行"开源链接"的预留宽度（开始备份按钮跨两列，不受此值影响）；
            // 收窄左列即可为右侧版权/版本行腾出宽度，避免版本号被挤到下一行换行。
            this.opPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 300F)); // 左列：开源组件超链接预留宽度
            this.opPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F)); // 右列：版权/版本（填满，足够单行显示）
            this.opPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // 上次备份标签
            this.opPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // 开始备份按钮行
            this.opPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize)); // 开源说明 + 版权行

            this.lastBackupLabel = new System.Windows.Forms.Label
            {
                AutoSize = true,
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Top,
                ForeColor = System.Drawing.SystemColors.GrayText,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 4)
            };
            this.startButton = new System.Windows.Forms.Button
            {
                // 加长更醒目：约与左侧"选项" GroupBox 同宽（≈ SplitterDistance 410）。
                // 用 Anchor Left|Right 横向填满左列（410），固定高度 36 与其余按钮一致；
                // 避免长本地化文案把按钮撑爆、挤压版权行。布局重构后跨两列独占第 2 行。
                Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right,
                Height = 36,
                Margin = new System.Windows.Forms.Padding(0)
            };
            this.copyrightLabel = new System.Windows.Forms.LinkLabel
            {
                // 版权+版本：底部第 3 行右列、居右；文案与可点击邮箱在 ApplyLanguage() 中设置。
                // 顶部留 6px 间距，与上方"开始备份"按钮拉开，避免贴边。
                AutoSize = true,
                Anchor = System.Windows.Forms.AnchorStyles.Right,
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                Margin = new System.Windows.Forms.Padding(0, 6, 0, 0)
            };

            // 开源组件超链接（替代原"开源软件说明"按钮）：底部第 3 行左列，
            // 7-Zip 与 ShadowSpawn 各自可点击跳转其主页/仓库。
            this._ossLinks = new System.Windows.Forms.TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new System.Windows.Forms.Padding(0, 6, 0, 0)   // 顶部 6px，与"开始备份"按钮拉开间距
            };
            this._ossLinks.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this._ossLinks.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this._ossLinks.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this._ossLinks.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this._sevenZipLink = new System.Windows.Forms.LinkLabel
            {
                AutoSize = true,
                Text = "7-Zip (LGPL)",   // 开源协议名直接标注（非"协议"占位）
                Margin = new System.Windows.Forms.Padding(0)
            };
            this._ossSep = new System.Windows.Forms.Label
            {
                AutoSize = true,
                Text = " | ",
                Margin = new System.Windows.Forms.Padding(0)
            };
            this._shadowSpawnLink = new System.Windows.Forms.LinkLabel
            {
                AutoSize = true,
                Text = "ShadowSpawn (MIT)",   // 开源协议名直接标注（非"协议"占位）
                Margin = new System.Windows.Forms.Padding(0)
            };
            this._ossLinks.Controls.Add(this._sevenZipLink, 0, 0);
            this._ossLinks.Controls.Add(this._ossSep, 1, 0);
            this._ossLinks.Controls.Add(this._shadowSpawnLink, 2, 0);

            this.opPanel.Controls.Add(this.lastBackupLabel, 0, 0);
            this.opPanel.SetColumnSpan(this.lastBackupLabel, 2);
            this.opPanel.Controls.Add(this.startButton, 0, 1);
            this.opPanel.SetColumnSpan(this.startButton, 2);
            this.opPanel.Controls.Add(this._ossLinks, 0, 2);
            this.opPanel.Controls.Add(this.copyrightLabel, 1, 2);

            // ---- Compose form ----
            // Fill first, then Bottom, then Top bars in reverse (last Top = topmost).
            this.Controls.Add(this.mainSplit);
            this.Controls.Add(this.opPanel);
            this.Controls.Add(this.tgtGroup);
            this.Controls.Add(this.srcGroup);
            this.Controls.Add(this.infoPanel);

            // ---- Wire events ----
            this.languageComboBox.SelectedIndexChanged += new System.EventHandler(this.OnLanguageChanged);
            this.browseButton.Click += new System.EventHandler(this.OnBrowseClick);
            this._btnDetectWinCC.Click += new System.EventHandler(this.OnDetectRunningClick);
            this._btnDetectTIA.Click += new System.EventHandler(this.OnDetectRunningClick);
            this._btnDetectStep7.Click += new System.EventHandler(this.OnDetectRunningClick);
            // 开源组件链接的子链接（软件名→官网、协议名→本地声明）统一在
            // MainForm.SetupOssLinks() 中接线，由 OnOssLinkClicked 按 LinkData 打开；
            // 此处不再整体绑定网站，否则会无视子链接、恒打开网页。
            // The OSS links' sub-links (software name -> website, license name ->
            // local declaration) are wired in SetupOssLinks(); do NOT bind the
            // whole link to a URL here, otherwise the in-parentheses license
            // links would always open a web page instead of the local file.
            this.pathTextBox.TextChanged += new System.EventHandler(this.OnPathTextChanged);
            // 增强版（R4）：用 ItemCheck 代替 SelectedIndexChanged 来跟踪勾选的目标盘。
            this.driveListView.ItemCheck += new System.Windows.Forms.ItemCheckEventHandler(this.OnDriveItemCheck);
            this.compComboBox.SelectedIndexChanged += new System.EventHandler(this.OnCompressionChanged);
            this.forceCheckBox.CheckedChanged += new System.EventHandler(this.OnForceToggle);
            this.exclTextBox.TextChanged += new System.EventHandler(this.OnExcludeTextChanged);
            this._exclHelpButton.Click += new System.EventHandler(this.OnExcludeHelpClick);
            this._restoreButton.Click += new System.EventHandler(this.OnRestoreDefaultsClick);
            this.startButton.Click += new System.EventHandler(this.OnStartClick);
            this.copyrightLabel.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.OnCopyrightLinkClicked);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.OnFormClosing);
        }
    }
}

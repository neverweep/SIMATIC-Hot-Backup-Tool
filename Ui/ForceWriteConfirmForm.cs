// <summary>
// 启动前的最终危险确认对话框（增强版 R10）：仅当用户点击"开始备份"且所选目标盘包含
// 受保护（授权/加密狗）盘时弹出，明确警告写入可能永久损坏授权。确认则继续，否则取消。
// 手工编写（无 Visual Studio 设计器），采用与主窗口一致的 DPI 友好布局与语言跟随机制。
// </summary>
using System;
using System.Windows.Forms;

namespace SHBT.Ui
{
    /// <summary>目标盘含受保护盘时，启动备份前的最终确认对话框。</summary>
    public partial class ForceWriteConfirmForm : Form
    {
        private TableLayoutPanel _layout;
        private Label _messageLabel;
        private Button _yesButton;
        private Button _noButton;

        /// <summary>构造、应用系统字体与本地化文案，并订阅语言切换事件。</summary>
        public ForceWriteConfirmForm()
        {
            InitializeComponent();
            FontHelper.ApplyTo(this);

            // 跟随主窗口语言实时刷新；关闭时退订，避免静态事件持有本对话框导致内存泄漏。
            Localization.LanguageChanged += OnLocalizationChanged;
            this.FormClosed += (s, e) => Localization.LanguageChanged -= OnLocalizationChanged;
            ApplyLanguage();
        }

        private void OnLocalizationChanged()
        {
            ApplyLanguage();
        }

        /// <summary>从当前语言表读取所有可见文案，使对话框跟随 UI 语言。</summary>
        private void ApplyLanguage()
        {
            this.Text = Localization.Get("force_start_confirm_title");
            _messageLabel.Text = Localization.Get("force_start_confirm_msg");
            _yesButton.Text = Localization.Get("confirm_yes");
            _noButton.Text = Localization.Get("confirm_no");
        }

        private void InitializeComponent()
        {
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new System.Drawing.Size(460, 200);
            this.Padding = new System.Windows.Forms.Padding(16, 14, 16, 14);

            // 单列、AutoSize 行的自适应布局：消息在上、按钮在底部右对齐。
            this._layout = new System.Windows.Forms.TableLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new System.Windows.Forms.Padding(0)
            };
            this._layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this._layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this._layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));

            this._messageLabel = new System.Windows.Forms.Label
            {
                AutoSize = true,
                Dock = System.Windows.Forms.DockStyle.Fill,
                UseMnemonic = false,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 14)
            };

            var buttonRow = new System.Windows.Forms.FlowLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                Padding = new System.Windows.Forms.Padding(0)
            };

            this._noButton = new System.Windows.Forms.Button
            {
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                MinimumSize = new System.Drawing.Size(80, 26),
                // 取消按钮关闭对话框并返回 Cancel，作为默认取消路径。
                DialogResult = System.Windows.Forms.DialogResult.No,
                Margin = new System.Windows.Forms.Padding(0, 0, 0, 0)
            };
            this._yesButton = new System.Windows.Forms.Button
            {
                AutoSize = true,
                AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                MinimumSize = new System.Drawing.Size(80, 26),
                // 确认按钮关闭对话框并返回 Yes，作为危险操作的最终放行。
                DialogResult = System.Windows.Forms.DialogResult.Yes,
                Margin = new System.Windows.Forms.Padding(8, 0, 0, 0)
            };

            buttonRow.Controls.Add(this._noButton);
            buttonRow.Controls.Add(this._yesButton);

            this._layout.Controls.Add(this._messageLabel, 0, 0);
            this._layout.Controls.Add(buttonRow, 0, 1);

            this.Controls.Add(this._layout);
            this.AcceptButton = this._yesButton;
            this.CancelButton = this._noButton;
        }

        /// <summary>以模态方式弹出确认框。</summary>
        /// <param name="owner">父窗口（用于居中）。</param>
        /// <returns><see cref="DialogResult.Yes"/> 表示用户确认继续，否则 <see cref="DialogResult.No"/>。</returns>
        public DialogResult ShowConfirm(IWin32Window owner)
        {
            return this.ShowDialog(owner);
        }
    }
}

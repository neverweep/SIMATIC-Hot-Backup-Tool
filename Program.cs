// <summary>
// SHBT 程序入口：DPI 感知、管理员检测/自我提权、配置加载与主窗口启动。
// 忠实移植自 Python 版 1tool_gui.py 的入口模块。
// </summary>
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SHBT.Core;
using SHBT.Ui;
using Localization = SHBT.Ui.Localization;

namespace SHBT
{
    /// <summary>SHBT 应用程序入口点。</summary>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // DPI 感知兜底（清单虽已声明，但此处可防范清单被剥离或旧加载器场景）。
            SetProcessDpiAwareness();

            // 全局异常处理，使任何崩溃以 MessageBox 呈现而非静默退出
            //（例如随附运行时加载失败或 UI 线程故障）。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.ToString(), "SHBT Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show(((Exception)e.ExceptionObject).ToString(), "SHBT Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

            // 管理员检测；若未提权且尚未尝试过提权，则进行防御性自我提权。
            bool isAdmin = AdminHelper.IsAdmin();
            if (!isAdmin && !AdminHelper.AlreadyElevated(args))
            {
                if (AdminHelper.RelaunchAsAdmin())
                {
                    // 已启动新的（提权）进程；当前进程退出。
                    return;
                }
            }

            // 从可执行文件同级的 lang/ 目录加载语言资源，随后选择 UI 语言：
            // 显式保存的偏好优先；否则探测操作系统 UI 区域，不支持时回退到英语。
            string langFolder = Path.Combine(BinLocator.AppDirectory, "lang");
            Localization.Initialize(langFolder);

            AppConfig config = ConfigManager.Load();

            string preferred = config.Language;
            string chosen;
            if (string.IsNullOrEmpty(preferred) || string.Equals(preferred, "auto", StringComparison.OrdinalIgnoreCase))
            {
                chosen = Localization.DetectSystemLanguage();
            }
            else
            {
                chosen = preferred;
            }

            if (!Localization.IsSupported(chosen))
            {
                chosen = Localization.Fallback;
            }

            Localization.CurrentCode = chosen;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(config, isAdmin, chosen));
        }

        [DllImport("shcore.dll")]
        private static extern int NativeSetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        private static void SetProcessDpiAwareness()
        {
            try
            {
                // PROCESS_PER_MONITOR_DPI_AWARE_V2 = 2（按监视器 DPI 感知 V2）。
                NativeSetProcessDpiAwareness(2);
            }
            catch
            {
                try
                {
                    SetProcessDPIAware();
                }
                catch
                {
                    // 忽略——清单已处理 DPI 感知。
                }
            }
        }
    }
}

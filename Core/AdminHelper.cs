// <summary>
// 管理员权限检测与自身提权（自我提升）。对应 1tool_gui.py 的 is_admin /
// relaunch_as_admin，使用 Shell32 的 P/Invoke 实现。
// 本类不依赖 WinForms，因此留在 Core 层且可独立进行单元测试。
// </summary>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SHBT.Core
{
    /// <summary>
    /// 检测当前进程是否具备管理员权限，以及通过 "runas" 动词重新以提权方式启动自身。
    /// 提权过程由 "--elevated" 标记守卫，防止无限重启循环。
    /// </summary>
    public static class AdminHelper
    {
        private const string ElevatedFlag = "--elevated";

        [DllImport("shell32.dll")]
        private static extern int IsUserAnAdmin();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int ShellExecuteW(
            IntPtr hwnd,
            string lpOperation,
            string lpFile,
            string lpParameters,
            string lpDirectory,
            int nShowCmd);

        /// <summary>
        /// 判断当前进程是否以管理员身份运行。
        /// </summary>
        /// <returns>具备管理员权限时返回 <c>true</c>，否则返回 <c>false</c>（含 P/Invoke 调用失败的情况）。</returns>
        public static bool IsAdmin()
        {
            try
            {
                return IsUserAnAdmin() != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判断是否已经尝试过一次提权。
        /// 通过命令行参数中的 <see cref="ElevatedFlag"/> 或环境变量 ONE_TOOL_ELEVATED 检测，
        /// 以此防止提权重启陷入无限循环。
        /// </summary>
        /// <param name="args">当前进程的命令行参数字符串数组。</param>
        /// <returns>已提权或已携带守卫标记时返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        public static bool AlreadyElevated(string[] args)
        {
            if (args != null && Array.IndexOf(args, ElevatedFlag) >= 0)
            {
                return true;
            }

            return string.Equals(
                Environment.GetEnvironmentVariable("ONE_TOOL_ELEVATED"),
                "1",
                StringComparison.Ordinal);
        }

        /// <summary>
        /// 以提权（runas）方式重新启动当前可执行文件，转发原始参数并附加 <see cref="ElevatedFlag"/> 守卫标记。
        /// 实际的提权授权由 Windows UAC 提示处理。
        /// </summary>
        /// <returns>提权进程成功启动时返回 <c>true</c>；无法获取可执行文件路径或提权启动失败时返回 <c>false</c>。</returns>
        public static bool RelaunchAsAdmin()
        {
            try
            {
                string exe = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrEmpty(exe))
                {
                    exe = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrEmpty(exe))
                {
                    return false;
                }

                var args = new List<string>(Environment.GetCommandLineArgs());
                // 丢弃 argv[0]（程序名本身），仅保留用户参数。
                if (args.Count > 0)
                {
                    args.RemoveAt(0);
                }

                if (!args.Contains(ElevatedFlag))
                {
                    args.Add(ElevatedFlag);
                }

                string paramsStr = string.Join(
                    " ",
                    args.ConvertAll(a => a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a));

                Environment.SetEnvironmentVariable("ONE_TOOL_ELEVATED", "1");

                int ret = ShellExecuteW(IntPtr.Zero, "runas", exe, paramsStr, null, 1);
                return ret > 32;
            }
            catch
            {
                return false;
            }
        }
    }
}

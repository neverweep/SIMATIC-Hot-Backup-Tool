// <summary>
// 解析应用程序所在目录、随附的 bin 目录以及原生工具路径。
// 该类型不依赖任何 WinForms，因此可在单元测试与无界面环境中直接使用。
// </summary>
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SHBT.Core
{
    /// <summary>
    /// 定位当前运行的可执行文件目录，以及随程序一同发布、位于同目录
    /// <c>bin</c> 子文件夹下的原生工具（ShadowSpawn、7z 等）。
    /// </summary>
    /// <remarks>
    /// 设计为静态工具类，不持有状态，且对 Windows 窗体（WinForms）零依赖，
    /// 以便在烟雾测试等无界面场景下直接调用，不影响可测试性。
    /// </remarks>
    public static class BinLocator
    {
        /// <summary>获取当前运行可执行文件所在目录的绝对路径。</summary>
        /// <remarks>
        /// 解析按以下兜底链依次尝试，确保任一来源不可用时仍能定位到目录：
        /// 入口程序集位置 → 当前进程主模块文件名 → 应用程序域基目录。
        /// </remarks>
        public static string AppDirectory
        {
            get
            {
                string location = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrEmpty(location))
                {
                    location = Process.GetCurrentProcess().MainModule?.FileName;
                }

                string dir = Path.GetDirectoryName(location);
                if (string.IsNullOrEmpty(dir))
                {
                    dir = AppDomain.CurrentDomain.BaseDirectory;
                }

                return dir;
            }
        }

        /// <summary>获取与可执行文件同级的 <c>bin</c> 子目录的绝对路径。</summary>
        public static string BinDirectory
        {
            get { return Path.Combine(AppDirectory, "bin"); }
        }

        /// <summary>获取 <c>bin</c> 目录下指定原生工具的绝对路径。</summary>
        /// <param name="name">原生工具的可执行文件名（例如 "7z.exe"）。</param>
        /// <returns>该工具的绝对路径，组合自 <see cref="BinDirectory"/> 与 <paramref name="name"/>。</returns>
        public static string GetToolPath(string name)
        {
            return Path.Combine(BinDirectory, name);
        }
    }
}

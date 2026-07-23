// <summary>
// 通过枚举正在运行的西门子工程软件进程，识别其当前打开的工程目录。
//
// 识别策略（零依赖优先，必要时回退 WMI）：
//   1) 窗口标题解析：TIA Portal 的标题形如 "C:\proj\MyTIA.ap19 - TIA Portal V19"，
//      直接包含完整工程文件路径，最可靠。
//   2) WMI 命令行解析：对 WinCC / STEP7 等标题不含路径的进程，读取其命令行，
//      在参数中找出首个"存在且经 ProjectDetector 校验"的路径令牌（文件取其目录）。
// 任一命中均经 ProjectDetector 校验，避免填入无关路径。
// </summary>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;

namespace SHBT.Core
{
    /// <summary>
    /// 检测当前正在运行的西门子工程（WinCC / STEP7 / TIA Portal）所在目录。
    /// </summary>
    public static class RunningProjectDetector
    {
        /// <summary>每种项目类型对应的进程映像名候选（按优先级尝试）。</summary>
        /// <remarks>
        /// GetProcessesByName 匹配的是真实进程映像名，而非窗口标题中的 "TIA Portal" 等字样，
        /// 因此这里只列真实映像名（#8）：
        ///   - WinCC Classic 工程管理/浏览器为 WinCCExplorer.exe；CCProjectMgr 为部分版本的工程进程。
        ///   - TIA Portal 主进程为 Siemens.Automation.Portal.exe。
        ///   - STEP 7 V5.x SIMATIC Manager 映像名为 s7manager.exe。
        /// 以上仅为快速路径；真正可靠的识别仍依赖下方 WMI 命令行 + 工程目录校验（即便映像名
        /// 匹配不到，也会回退到命令行扫描）。
        /// </remarks>
        private static readonly Dictionary<ProjectType, string[]> ProcessNames = new Dictionary<ProjectType, string[]>
        {
            { ProjectType.WinCC_V7X,  new[] { "WinCCExplorer", "CCProjectMgr", "CCProjectManager" } },
            { ProjectType.TIA_Portal, new[] { "Siemens.Automation.Portal" } },
            { ProjectType.STEP7_V5X,  new[] { "s7manager", "S7Manager" } }
        };

        /// <summary>
        /// 查找指定类型当前正在运行的工程目录；未检测到返回 <c>null</c>。
        /// </summary>
        public static string FindRunning(ProjectType type)
        {
            if (!ProcessNames.TryGetValue(type, out string[] names))
            {
                return null;
            }

            foreach (string name in names)
            {
                Process[] procs;
                try
                {
                    procs = Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                foreach (Process p in procs)
                {
                    string dir = ExtractProjectDir(p);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        return dir;
                    }
                }
            }

            return null;
        }

        /// <summary>从单个进程中提取经校验的工程目录（标题优先，WMI 命令行兜底）。</summary>
        private static string ExtractProjectDir(Process p)
        {
            // 1) 窗口标题解析：TIA Portal 标题含完整 .ap1x 工程文件路径。
            try
            {
                string title = p.MainWindowTitle;
                if (!string.IsNullOrEmpty(title))
                {
                    int idx = title.IndexOf(" - TIA Portal", StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        string candidate = title.Substring(0, idx).Trim();
                        if (File.Exists(candidate))
                        {
                            string dir = Path.GetDirectoryName(candidate);
                            if (ProjectDetector.IsValidProject(dir))
                            {
                                return dir;
                            }
                        }
                    }
                }
            }
            catch
            {
                // 窗口标题不可用时回退到命令行方式。
            }

            // 2) WMI 读取命令行，提取指向工程的有效路径。
            string cmd = GetCommandLine(p);
            if (!string.IsNullOrEmpty(cmd))
            {
                return FindProjectToken(cmd);
            }

            return null;
        }

        /// <summary>通过 WMI 读取进程命令行（失败返回 null）。</summary>
        private static string GetCommandLine(Process p)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + p.Id))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        var cl = mo["CommandLine"] as string;
                        if (!string.IsNullOrEmpty(cl))
                        {
                            return cl;
                        }
                    }
                }
            }
            catch
            {
                // WMI 不可用（服务未启动等）时静默回退。
            }

            return null;
        }

        /// <summary>
        /// 将命令行按引号感知的方式切分为令牌，返回首个"存在且经 ProjectDetector 校验"的路径
        /// （若令牌是文件则取其所在目录）。
        /// </summary>
        private static string FindProjectToken(string cmdLine)
        {
            foreach (string raw in Tokenize(cmdLine))
            {
                string tok = raw.Trim().Trim('"', '\'');
                if (string.IsNullOrEmpty(tok))
                {
                    continue;
                }

                string dir = File.Exists(tok) ? Path.GetDirectoryName(tok) : tok;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && ProjectDetector.IsValidProject(dir))
                {
                    return dir;
                }
            }

            return null;
        }

        /// <summary>引号感知的命令行令牌切分器（双引号内的空格不拆分）。</summary>
        private static IEnumerable<string> Tokenize(string cmd)
        {
            var sb = new System.Text.StringBuilder();
            bool inQuote = false;
            foreach (char c in cmd)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (!inQuote && (c == ' ' || c == '\t'))
                {
                    if (sb.Length > 0)
                    {
                        yield return sb.ToString();
                        sb.Clear();
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (sb.Length > 0)
            {
                yield return sb.ToString();
            }
        }
    }
}

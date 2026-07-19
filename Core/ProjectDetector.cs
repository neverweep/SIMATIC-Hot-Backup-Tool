// <summary>
// 通过特征标记（目录与文件）识别西门子项目类型。
// 移植自 core/detector.py（AMOBJS → STEP7，GRACS → WinCC，XRef → TIA），
// 并增强为「目录标记 + 文件标记」双重判定，使识别更稳健：
//   - STEP7 V5.x：AMOBJS 目录 或 *.s7p 工程文件
//   - WinCC Classic (v7.x/v8.x)：GRACS / ArchiveManager 目录 或 *.mcp 工程文件
//   - TIA Portal：XRef 目录 或 *.ap1x 工程文件（.ap15/.ap16/…/.ap19 等）
// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SHBT.Core
{
    /// <summary>
    /// 通过检查目录中的特征标记（目录与文件），识别西门子项目类型。
    /// </summary>
    /// <remarks>
    /// 识别规则与原始 SHBT.bat 的顺序保持一致：按固定顺序逐个探测签名，
    /// 靠后出现者覆盖先前者（后匹配优先），使内嵌 WinCC 的 TIA / STEP7 工程
    /// 仍能正确归类为最外层工程类型。
    /// </remarks>
    public static class ProjectDetector
    {
        /// <summary>
        /// 识别 <paramref name="path"/> 对应的西门子项目类型。
        /// </summary>
        /// <param name="path">待识别项目目录的绝对路径。</param>
        /// <returns>
        /// 识别成功时返回 <see cref="ProjectDetectionResult"/>；路径为空、目录不存在或
        /// 无任何已知标记时返回 <c>null</c>。
        /// </returns>
        public static ProjectDetectionResult Detect(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return null;
            }

            var foundTypes = new List<ProjectType>();
            var markers = new List<string>();

            foreach (ProjectSignature sig in ProjectTypeInfo.Signatures)
            {
                bool matched = false;

                // 目录标记：工程目录下存在该子目录即命中。
                foreach (string d in sig.DirectoryMarkers)
                {
                    if (Directory.Exists(Path.Combine(path, d)))
                    {
                        matched = true;
                        if (!markers.Contains(d))
                        {
                            markers.Add(d);
                        }
                    }
                }

                // 文件标记：工程目录下存在匹配通配符的文件即命中。
                foreach (string glob in sig.FileMarkers)
                {
                    string m = MatchFileMarker(path, glob);
                    if (m != null)
                    {
                        matched = true;
                        if (!markers.Contains(m))
                        {
                            markers.Add(m);
                        }
                    }
                }

                if (matched && !foundTypes.Contains(sig.Type))
                {
                    foundTypes.Add(sig.Type);
                }
            }

            if (foundTypes.Count == 0)
            {
                return null;
            }

            // 靠后的匹配覆盖靠前的匹配，与原始脚本行为一致（TIA 优先级最高）。
            ProjectType typeKey = foundTypes[foundTypes.Count - 1];
            return new ProjectDetectionResult
            {
                Type = typeKey,
                DisplayName = typeKey.ToString(),
                Markers = markers
            };
        }

        /// <summary>
        /// 在 <paramref name="dir"/> 中按 <paramref name="glob"/> 查找首个命中的特征文件。
        /// 对 TIA 专用通配符 "*.ap*" 额外要求扩展名形如 .ap + 数字（如 .ap19），
        /// 避免误匹配其它以 .ap 开头的扩展名。
        /// </summary>
        private static string MatchFileMarker(string dir, string glob)
        {
            bool tia = string.Equals(glob, "*.ap*", StringComparison.OrdinalIgnoreCase);
            try
            {
                foreach (string f in Directory.GetFiles(dir, glob))
                {
                    if (!tia)
                    {
                        return Path.GetFileName(f);
                    }

                    string ext = Path.GetExtension(f);
                    if (ext.Length > 3 && ext.StartsWith(".ap", StringComparison.OrdinalIgnoreCase))
                    {
                        string digits = ext.Substring(3);
                        if (digits.Length > 0 && digits.All(char.IsDigit))
                        {
                            return Path.GetFileName(f);
                        }
                    }
                }
            }
            catch
            {
                // 目录不可访问时视为无标记。
            }

            return null;
        }

        /// <summary>判断 <paramref name="path"/> 是否为可识别的西门子项目目录。</summary>
        public static bool IsValidProject(string path)
        {
            return Detect(path) != null;
        }
    }
}

// <summary>
// 源目录大小估算与逐目标盘空闲空间预检（R8）。
// 归入 Core 层以保持可单测、不依赖 WinForms。
// </summary>
using System;
using System.Collections.Generic;
using System.IO;

namespace SHBT.Core
{
    /// <summary>单个目标盘的空闲空间预检结果。</summary>
    public class SpaceCheck
    {
        /// <summary>目标盘符（大写，不含冒号）。</summary>
        public string Drive { get; set; }

        /// <summary>该盘可用空闲空间（字节）。</summary>
        public long FreeBytes { get; set; }

        /// <summary>所需空间（源目录估算大小 × 1.1，字节）。</summary>
        public long NeededBytes { get; set; }

        /// <summary>是否满足备份需求（FreeBytes ≥ NeededBytes）。</summary>
        public bool Ok { get; set; }
    }

    /// <summary>
    /// 估算源目录大小并逐目标盘校验剩余空间是否充足。
    /// </summary>
    public static class DiskSpaceChecker
    {
        /// <summary>空间预留系数：所需空间 = 源大小 × 该系数（R8 取 1.1）。</summary>
        private const double SafetyFactor = 1.1;

        /// <summary>
        /// 递归估算 <paramref name="source"/> 的总字节数（文件 + 目录均可）。
        /// 任意子项读取失败均被忽略，不中断整体估算。
        /// </summary>
        /// <param name="source">待估算的目录或文件路径。</param>
        /// <returns>估算出的总字节数（失败或不存在时返回 0）。</returns>
        public static long EstimateSourceSize(string source)
        {
            long total = 0;
            if (string.IsNullOrEmpty(source))
            {
                return 0;
            }

            try
            {
                if (File.Exists(source))
                {
                    try { total += new FileInfo(source).Length; }
                    catch { /* 读取失败忽略 */ }
                    return total;
                }

                if (!Directory.Exists(source))
                {
                    return 0;
                }

                // 使用显式栈进行迭代遍历，避免海量小文件目录导致递归过深。
                var pending = new Stack<string>();
                pending.Push(source);
                while (pending.Count > 0)
                {
                    string current = pending.Pop();
                    try
                    {
                        foreach (string file in Directory.GetFiles(current))
                        {
                            try { total += new FileInfo(file).Length; }
                            catch { /* 单文件读取失败忽略 */ }
                        }

                        foreach (string sub in Directory.GetDirectories(current))
                        {
                            pending.Push(sub);
                        }
                    }
                    catch { /* 单目录访问失败忽略 */ }
                }
            }
            catch
            {
                // 顶层异常（如权限不足）忽略，返回已累加的部分结果。
            }

            return total;
        }

        /// <summary>
        /// 对每个目标盘校验空闲空间是否足以容纳源目录（× 1.1 预留）。
        /// </summary>
        /// <param name="source">待备份的源目录。</param>
        /// <param name="drives">有序目标盘符列表（可含或不含尾随冒号）。</param>
        /// <returns>盘符 → 预检结果 的字典（盘符为大写键）。</returns>
        public static Dictionary<string, SpaceCheck> CheckTargets(string source, List<string> drives)
        {
            var result = new Dictionary<string, SpaceCheck>(StringComparer.OrdinalIgnoreCase);

            long needed = (long)(EstimateSourceSize(source) * SafetyFactor);
            if (drives != null)
            {
                foreach (string drive in drives)
                {
                    string letter = (drive ?? string.Empty).TrimEnd(':').ToUpperInvariant();
                    if (string.IsNullOrEmpty(letter))
                    {
                        continue;
                    }

                    long free = GetFreeBytes(letter);
                    result[letter] = new SpaceCheck
                    {
                        Drive = letter,
                        FreeBytes = free,
                        NeededBytes = needed,
                        Ok = free >= needed
                    };
                }
            }

            return result;
        }

        /// <summary>
        /// 获取指定盘符的可用空闲空间；优先使用托管 <see cref="DriveInfo"/>，
        /// 失败时回退到 <see cref="DriveEnumerator"/> 的元数据。
        /// </summary>
        private static long GetFreeBytes(string letter)
        {
            try
            {
                var info = new DriveInfo(letter + ":");
                return info.AvailableFreeSpace;
            }
            catch
            {
                try
                {
                    foreach (DriveInfoEx d in DriveEnumerator.ListDrives())
                    {
                        if (string.Equals(d.Letter, letter, StringComparison.OrdinalIgnoreCase))
                        {
                            return d.FreeBytes;
                        }
                    }
                }
                catch { /* 回退失败忽略 */ }
                return 0;
            }
        }
    }
}

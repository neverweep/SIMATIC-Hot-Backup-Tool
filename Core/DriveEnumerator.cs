// <summary>
// 通过 Windows API 枚举逻辑驱动器，并依据卷标识别西门子许可证/加密狗盘符。
// 对应 core/drives.py（不依赖 wmic/WMI，仅处理固定盘，并支持许可证关键字识别）。
// </summary>
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SHBT.Core
{
    /// <summary>
    /// <see cref="DriveEnumerator"/> 返回的、包含完整元数据的驱动器信息。
    /// </summary>
    public class DriveInfoEx
    {
        /// <summary>不含冒号、大写的盘符（例如 "C"）。</summary>
        public string Letter { get; set; }

        /// <summary>卷标（无卷标时为空字符串）。</summary>
        public string Label { get; set; }

        /// <summary>可用空闲空间（字节）。</summary>
        public long FreeBytes { get; set; }

        /// <summary>总容量（字节）。</summary>
        public long TotalBytes { get; set; }

        /// <summary>当卷标命中西门子许可证/加密狗关键字时为 <c>true</c>。</summary>
        public bool IsProtected { get; set; }

        /// <summary>驱动器类型（Fixed / CDRom / ...），用于单独标记光驱等介质。</summary>
        public System.IO.DriveType Type { get; set; }
    }

    /// <summary>
    /// 通过 P/Invoke 枚举固定逻辑驱动器并读取其卷元数据。
    /// </summary>
    public static class DriveEnumerator
    {
        private const int DRIVE_UNKNOWN = 0;
        private const int DRIVE_NO_ROOT_DIR = 1;
        private const int DRIVE_REMOVABLE = 2;
        private const int DRIVE_FIXED = 3;
        private const int DRIVE_REMOTE = 4;
        private const int DRIVE_CDROM = 5;
        private const int DRIVE_RAMDISK = 6;
        private static readonly string[] LicenseKeywords = { "AX NF ZZ", "AXNFZZ", "LICENSE" };

        // 注意：lpBuffer 必须为 char[]（而非 StringBuilder）。使用 StringBuilder 时，
        // 封送器只会填充到首个内嵌 '\0' 为止，而 Win32 返回值统计的是每个盘符字符串
        //（含各自的尾随 '\0'，但不含最终的终止 '\0'）的总长度。
        // 因此 StringBuilder.ToString(0, length) 会抛出 ArgumentOutOfRangeException。
        // char[] 能完整接收原始缓冲区。
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLogicalDriveStringsW(uint nBufferLength, [Out] char[] lpBuffer);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetDriveTypeW(string lpRootPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformationW(
            string lpRootPathName,
            StringBuilder lpVolumeNameBuffer,
            uint nVolumeNameSize,
            out uint lpVolumeSerialNumber,
            out uint lpMaximumComponentLength,
            out uint lpFileSystemFlags,
            StringBuilder lpFileSystemNameBuffer,
            uint nFileSystemNameSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceExW(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailableToCaller,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        /// <summary>
        /// 枚举全部可用逻辑驱动器，附带其卷标、空闲/总空间以及许可证盘符判定，
        /// 返回顺序与系统枚举顺序一致。
        /// </summary>
        /// <returns>包含所有可用驱动器的 <see cref="DriveInfoEx"/> 列表（可能为空，但正常情况下非空）。</returns>
        public static List<DriveInfoEx> ListDrives()
        {
            var drives = new List<DriveInfoEx>();

            // 首选数据源：托管的 DriveInfo API。它不依赖 wmic/WMI，
            // 在所有 Windows 版本（包括 GetLogicalDriveStringsW 可能异常或返回空的受限 UAC 环境）下均可靠。
            try
            {
                foreach (System.IO.DriveInfo drive in System.IO.DriveInfo.GetDrives())
                {
                    // 枚举所有可用类型的驱动器（Fixed / Removable / CDRom / Network / Ram）。
                    // 仅跳过 Unknown（0）与 NoRootDirectory（1），因为它们没有可供读取元数据的有效根路径。
                    if (drive.DriveType == System.IO.DriveType.Unknown ||
                        drive.DriveType == System.IO.DriveType.NoRootDirectory)
                    {
                        continue;
                    }

                    string root = drive.RootDirectory.FullName;
                    if (!root.EndsWith("\\", StringComparison.Ordinal))
                    {
                        root += "\\";
                    }

                    string letter = char.ToUpperInvariant(root[0]).ToString();
                    string label = string.Empty;
                    long free = 0;
                    long total = 0;

                    // 部分驱动器虽被识别为 Fixed 但尚未就绪（如空读卡器）。
                    // 在此处做防御式读取，任一驱动器失败都不会导致整个列表为空。
                    try
                    {
                        label = drive.VolumeLabel ?? string.Empty;
                        free = (long)drive.TotalFreeSpace;
                        total = (long)drive.TotalSize;
                    }
                    catch
                    {
                        // 未就绪驱动器：保留 label/free/total 的默认值。
                    }

                    drives.Add(new DriveInfoEx
                    {
                        Letter = letter,
                        Label = label,
                        FreeBytes = free,
                        TotalBytes = total,
                        IsProtected = IsSiemensLicenseDrive(label),
                        Type = drive.DriveType,
                    });
                }
            }
            catch
            {
                // DriveInfo 在极少数系统上整体失败；继续走 P/Invoke 兜底。
            }

            // 最后兜底：若 DriveInfo 也未能给出任何结果，再尝试 P/Invoke 路径，
            // 以保证在真实机器上目标盘列表不会为空。
            if (drives.Count == 0)
            {
                EnumerateViaPInvoke(drives);
            }

            return drives;
        }

        private static void EnumerateViaPInvoke(List<DriveInfoEx> drives)
        {
            uint needed = GetLogicalDriveStringsW(0, null);
            if (needed == 0)
            {
                return;
            }

            var buffer = new char[needed];
            uint written = GetLogicalDriveStringsW(needed, buffer);
            if (written == 0)
            {
                return;
            }

            string raw = new string(buffer, 0, (int)written);
            string[] parts = raw.Split('\0');
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                string driveRoot = part.EndsWith("\\", StringComparison.Ordinal) ? part : part + "\\";
                string letter = char.ToUpperInvariant(part[0]).ToString();

                // 将原始 GetDriveTypeW 结果映射到 System.IO.DriveType，
                // 以忠实上报全部驱动器类型（而非仅 Fixed）。Unknown（0）与 NoRootDirectory（1）无有效根路径，跳过。
                uint typeCode = GetDriveTypeW(driveRoot);
                System.IO.DriveType mappedType;
                switch (typeCode)
                {
                    case DRIVE_UNKNOWN:      // 0
                    case DRIVE_NO_ROOT_DIR:  // 1
                        continue;
                    case DRIVE_REMOVABLE:   // 2
                        mappedType = System.IO.DriveType.Removable;
                        break;
                    case DRIVE_FIXED:       // 3
                        mappedType = System.IO.DriveType.Fixed;
                        break;
                    case DRIVE_REMOTE:      // 4
                        mappedType = System.IO.DriveType.Network;
                        break;
                    case DRIVE_CDROM:       // 5
                        mappedType = System.IO.DriveType.CDRom;
                        break;
                    case DRIVE_RAMDISK:     // 6
                        mappedType = System.IO.DriveType.Ram;
                        break;
                    default:
                        continue;
                }

                var labelBuf = new StringBuilder(256);
                uint serial = 0, maxComponent = 0, flags = 0;
                var fsBuf = new StringBuilder(256);
                string label = string.Empty;
                if (GetVolumeInformationW(
                        driveRoot,
                        labelBuf,
                        (uint)labelBuf.Capacity,
                        out serial,
                        out maxComponent,
                        out flags,
                        fsBuf,
                        (uint)fsBuf.Capacity))
                {
                    label = labelBuf.ToString();
                }

                ulong freeBytes = 0, totalBytes = 0, freeToCaller = 0;
                if (GetDiskFreeSpaceExW(driveRoot, out freeToCaller, out totalBytes, out freeBytes))
                {
                    // freeBytes（lpTotalNumberOfFreeBytes）是最准确的空闲空间数值。
                }

                drives.Add(new DriveInfoEx
                {
                    Letter = letter,
                    Label = label,
                    FreeBytes = (long)freeBytes,
                    TotalBytes = (long)totalBytes,
                    IsProtected = IsSiemensLicenseDrive(label),
                    Type = mappedType,
                });
            }
        }

        /// <summary>
        /// 获取当前已被占用的盘符集合（大写）。
        /// </summary>
        /// <returns>包含所有已使用盘符的哈希集合（大写，不含冒号）。</returns>
        public static HashSet<string> GetUsedLetters()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DriveInfoEx drive in ListDrives())
            {
                set.Add(drive.Letter);
            }

            return set;
        }

        /// <summary>
        /// 判断给定卷标是否属于西门子许可证/加密狗盘符
        /// （对 AX NF ZZ / AXNFZZ / LICENSE 进行不区分大小写的包含匹配）。
        /// </summary>
        /// <param name="label">驱动器卷标（可为 <c>null</c> 或空）。</param>
        /// <returns>命中许可证关键字时返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        private static bool IsSiemensLicenseDrive(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return false;
            }

            string upper = label.ToUpperInvariant();
            foreach (string keyword in LicenseKeywords)
            {
                if (upper.Contains(keyword.ToUpperInvariant()))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

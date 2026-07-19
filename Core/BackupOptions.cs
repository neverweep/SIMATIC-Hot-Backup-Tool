// <summary>
// 单次备份运行的选项集合。对应 core/backup.py 中的 options 字典（增强版 R2/R3）。
// </summary>
using System.Collections.Generic;

namespace SHBT.Core
{
    /// <summary>单次备份的配置选项。</summary>
    /// <remarks>
    /// 所有字段允许为 null；调用方（如 BackupCommandBuilder）会在使用时
    /// 以默认值进行兜底，因此该类型本身不做强制校验。
    /// </remarks>
    public class BackupOptions
    {
        /// <summary>目标盘上的输出子目录（默认 "Backups"）。</summary>
        public string OutputSubdir { get; set; }

        /// <summary>压缩级别键：store/fast/standard/max（详见 ConfigManager.CompressionLevels）。</summary>
        public string CompressionLevel { get; set; }

        /// <summary>
        /// 7z 排除规则集合（R2 增强版）。每条 <see cref="ExcludeRule"/> 独立控制启用状态，
        /// 仅 <see cref="ExcludeRule.Enabled"/> 为 true 且 <see cref="ExcludeRule.Pattern"/> 非空的规则生效。
        /// </summary>
        public List<ExcludeRule> ExcludeRules { get; set; }


        /// <summary>允许写入受保护（许可证）盘；为 false 时此类目标将被拒绝。</summary>
        public bool ForceProtected { get; set; }

        /// <summary>用于挂载卷影副本（VSS）的盘符（默认 "Q"）。</summary>
        public string ShadowLetter { get; set; }
    }
}

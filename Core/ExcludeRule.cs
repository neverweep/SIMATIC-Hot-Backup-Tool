// <summary>
// 单条 7z 排除规则的值类型。对应增强版需求 R2（可视化、可启停的排除规则）。
// </summary>
using System.Runtime.Serialization;

namespace SHBT.Core
{
    /// <summary>单条备份排除规则。</summary>
    /// <remarks>
    /// 仅当 <see cref="Enabled"/> 为 <c>true</c> 且 <see cref="Pattern"/> 非空时，
    /// 该规则才会被纳入 7z 的 -xr! 参数。
    /// <see cref="Comment"/> 仅供界面备注使用，永不进入命令行。
    /// 该类型以 <see cref="DataContract"/>/> 标注，便于 <see cref="ConfigManager"/>
    /// 通过 <c>DataContractJsonSerializer</c> 直接序列化/反序列化。
    /// </remarks>
    [DataContract]
    public class ExcludeRule
    {
        /// <summary>是否启用该规则；禁用项会被完全忽略，不进入任何命令。</summary>
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        /// <summary>排除模式（支持 7z 通配符，如 *.tmp、ArchiveManager）；为空表示占位未填。</summary>
        [DataMember(Name = "pattern")]
        public string Pattern { get; set; }

        /// <summary>仅用于界面展示的说明文字，不参与备份命令构建。</summary>
        [DataMember(Name = "comment")]
        public string Comment { get; set; }
    }
}

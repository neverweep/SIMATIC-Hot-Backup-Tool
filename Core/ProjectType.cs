// <summary>
// 项目类型枚举、标记元数据与识别结果模型。
// 对应 core/detector.py 中的 PROJECT_TYPES。
// </summary>
using System.Collections.Generic;

namespace SHBT.Core
{
    /// <summary>SHBT 可识别的西门子项目家族（工程类型）。</summary>
    /// <remarks>
    /// 枚举顺序与原始脚本保持一致；Unknown 仅在无法识别时使用，
    /// 不应作为有效工程的类型参与后续逻辑。
    /// </remarks>
    public enum ProjectType
    {
        STEP7_V5X,
        WinCC_V7X,
        TIA_Portal,
        Unknown
    }

    /// <summary>
    /// 特征标记目录到项目类型的静态映射元数据，以及探测顺序
    /// （后匹配覆盖先匹配，对应原始 SHBT.bat 的 IF 顺序）。
    /// </summary>
    /// <remarks>
    /// 此类仅承载不可变的映射常量，不含任何实例状态，可安全在多线程间共享。
    /// </remarks>
    public static class ProjectTypeInfo
    {
        /// <summary>特征标记目录名到项目类型的映射表。</summary>
        /// <remarks>
        /// 键为工程目录下的标记子目录名（如 AMOBJS），值为对应的项目类型。
        /// 与 <see cref="DetectOrder"/> 共同决定识别结果。
        /// </remarks>
        public static readonly Dictionary<string, ProjectType> MarkerToType = new Dictionary<string, ProjectType>
        {
            { "AMOBJS", ProjectType.STEP7_V5X },
            { "GRACS", ProjectType.WinCC_V7X },
            { "XRef", ProjectType.TIA_Portal }
        };

        /// <summary>标记目录的探测顺序（后匹配优先）。保留以兼容旧调用。</summary>
        public static readonly string[] DetectOrder = { "AMOBJS", "GRACS", "XRef" };

        /// <summary>
        /// 更健壮的识别签名表：每个项目类型同时支持「特征目录」与「特征文件」。
        /// 顺序与 <see cref="DetectOrder"/> 一致（后匹配优先）：
        /// STEP7 → WinCC → TIA。TIA 置于末位，可覆盖内嵌 WinCC 的 STEP7 / TIA 工程。
        /// </summary>
        public static readonly ProjectSignature[] Signatures = new[]
        {
            // STEP 7 V5.x：AMOBJS 为 STEP 7 工程对象管理目录（核心标记）；*.s7p 为工程文件。
            // 注意：切勿加入 OBJ / SUBBLK 这类过于通用的目录名——OBJ 会与 .NET 构建输出目录
            // obj/ 冲突，导致在工具自身目录（含 obj/）下被误判成 STEP7 工程。
            new ProjectSignature(ProjectType.STEP7_V5X,
                new[] { "AMOBJS" },
                new[] { "*.s7p" }),
            // WinCC Classic (v7.x/v8.x)：GRACS 为图形运行期目录，ArchiveManager 为归档目录；
            // *.mcp 为 WinCC 工程管理器文件。
            new ProjectSignature(ProjectType.WinCC_V7X,
                new[] { "GRACS", "ArchiveManager" },
                new[] { "*.mcp" }),
            // TIA Portal：XRef 是交叉引用目录；*.ap1x 是 TIA 工程文件（.ap15/.ap16/…/.ap19 等）。
            new ProjectSignature(ProjectType.TIA_Portal,
                new[] { "XRef" },
                new[] { "*.ap*" })
        };
    }

    /// <summary>单种项目类型的识别签名：一组特征目录名与一组特征文件通配符。</summary>
    public class ProjectSignature
    {
        /// <summary>该签名对应的项目类型。</summary>
        public ProjectType Type { get; }

        /// <summary>特征目录名（工程目录下若存在即命中）。</summary>
        public string[] DirectoryMarkers { get; }

        /// <summary>特征文件通配符（如 "*.s7p"；"*.ap*" 仅匹配 .ap + 数字 的扩展名）。</summary>
        public string[] FileMarkers { get; }

        /// <summary>构造一个识别签名。</summary>
        public ProjectSignature(ProjectType type, string[] directoryMarkers, string[] fileMarkers)
        {
            Type = type;
            DirectoryMarkers = directoryMarkers ?? new string[0];
            FileMarkers = fileMarkers ?? new string[0];
        }
    }

    /// <summary>一次项目类型识别的结果。</summary>
    public class ProjectDetectionResult
    {
        /// <summary>识别到的项目类型（未识别时为 Unknown）。</summary>
        public ProjectType Type { get; set; }

        /// <summary>显示名/类型键（本地化名称由 Localization 模块另行提供）。</summary>
        public string DisplayName { get; set; }

        /// <summary>实际命中的标记目录集合（按探测顺序去重后记录）。</summary>
        public List<string> Markers { get; set; }
    }
}

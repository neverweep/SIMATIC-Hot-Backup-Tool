// <summary>
// 应用配置模型，以及基于 DataContractJsonSerializer 的加载/保存实现
// （运行期零额外 NuGet 依赖，该序列化器随 .NET Framework 一同提供）。
// 对应 core/config.py。增强版（R2/R3/R9）：排除规则升级为 ExcludeRule 列表并兼容旧字符串数组；
// 新增恢复数据开关（默认开）与最近一次备份记录。
// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SHBT.Core
{
    /// <summary>
    /// 持久化的应用配置。
    /// </summary>
    [DataContract]
    public class AppConfig
    {
        [DataMember(Name = "language")] public string Language { get; set; }
        [DataMember(Name = "output_subdir")] public string OutputSubdir { get; set; }
        [DataMember(Name = "compression_level")] public string CompressionLevel { get; set; }

        /// <summary>排除规则集合（R2 增强版，V2 键 exclude_rules_v2）。</summary>
        [DataMember(Name = "exclude_rules_v2")]
        public List<ExcludeRule> ExcludeRules { get; set; }

        /// <summary>
        /// 兼容旧版配置中的字符串数组排除规则（键 exclude_rules）。
        /// 加载时若存在则迁移为 <see cref="ExcludeRules"/>；保存时置 null 且不写出（EmitDefaultValue=false）。
        /// </summary>
        [DataMember(Name = "exclude_rules", EmitDefaultValue = false)]
        public List<string> ExcludeRulesLegacy { get; set; }

        [DataMember(Name = "force_protected")] public bool ForceProtected { get; set; }
        [DataMember(Name = "last_project_path")] public string LastProjectPath { get; set; }
        [DataMember(Name = "window_geometry")] public string WindowGeometry { get; set; }

        /// <summary>最近一次成功备份产物的完整路径（时间已含于文件名，仅记录位置）。</summary>
        [DataMember(Name = "last_backup_path")] public string LastBackupPath { get; set; }
    }

    /// <summary>
    /// 负责 <see cref="AppConfig"/> 的加载、保存与默认值供应，
    /// 包含默认排除规则与压缩级别的映射关系。
    /// </summary>
    public static class ConfigManager
    {
        /// <summary>配置文件名，与可执行文件位于同一目录。</summary>
        public const string ConfigFileName = "config.json";

        /// <summary>默认排除规则集合（与原始 SHBT.bat 保持一致）。</summary>
        public static readonly List<string> DefaultExcludes = new List<string>
        {
            "*.dc_bck", "*.dcf", "*.xfs", "ArchiveManager", "ProjectOpened.lck",
            "wincc.lck", "*.sav", "*.saf"
        };

        /// <summary>压缩级别关键字到 7-Zip -mx 标志的映射。</summary>
        public static readonly Dictionary<string, string> CompressionLevels = new Dictionary<string, string>
        {
            { "store", "-mx0" },
            { "fast", "-mx1" },
            { "standard", "-mx5" },
            { "max", "-mx9" },
            { "ultra", "-mx9" }
        };

        /// <summary>
        /// 返回默认排除项的中文/英文说明列表（用于"排除项说明"帮助对话框）。
        /// 每行对应 <see cref="DefaultExcludes"/> 中的一条模式，解释其在
        /// WinCC / STEP 7 / TIA 工程中的含义，以及为何无需备份。
        /// </summary>
        /// <param name="chinese">为 <c>true</c> 时返回中文说明，否则返回英文。</param>
        /// <returns>模式 → 说明 的键值对列表。</returns>
        public static List<KeyValuePair<string, string>> DefaultExcludeNotes(bool chinese)
        {
            var list = new List<KeyValuePair<string, string>>();
            void Add(string pattern, string zh, string en)
            {
                list.Add(new KeyValuePair<string, string>(pattern, chinese ? zh : en));
            }

            Add("*.dc_bck", "WinCC 运行时数据缓存的备份文件，每次启动自动重建，无需备份。",
                "WinCC runtime data-cache backup; auto-rebuilt on startup, not needed for restore.");
            Add("*.dcf", "WinCC / STEP 7 编译缓存文件，可由工程重新生成。",
                "WinCC / STEP 7 compile-cache files; regenerated from the project.");
            Add("*.xfs", "WinCC 文件系统镜像，运行期生成，恢复工程时自动重建。",
                "WinCC file-system image; regenerated when the project is restored.");
            Add("ArchiveManager", "WinCC 报警和趋势记录（Alarm & Trend）数据，运行期自动生成，恢复工程时无需备份。",
                "WinCC Alarm & Trend logging data; generated at runtime, not needed for restore.");
            Add("ProjectOpened.lck", "工程被打开时的锁文件，运行时占用，不应被复制。",
                "Project-open lock file; held while the project is open, must not be copied.");
            Add("wincc.lck", "WinCC 运行锁文件，运行时占用。",
                "WinCC runtime lock file; held while running.");
            Add("*.sav", "画面（Picture）备份文件，非工程源文件，可由工程重新生成。",
                "Picture backup file; not a source project file, regenerable from the project.");
            Add("*.saf", "面板实例（Faceplate）备份文件，可由工程重新生成。",
                "Faceplate instance backup file; regenerable from the project.");

            return list;
        }

        /// <summary>获取 config.json 的绝对路径（位于程序所在目录）。</summary>
        /// <returns>配置文件的完整路径。</returns>
        public static string GetConfigPath()
        {
            return Path.Combine(BinLocator.AppDirectory, ConfigFileName);
        }

        /// <summary>构造一份全新的默认配置。</summary>
        /// <returns>字段均填充为默认值的 <see cref="AppConfig"/> 实例。</returns>
        public static AppConfig Defaults()
        {
            return new AppConfig
            {
                Language = "auto",
                OutputSubdir = "Backups",
                CompressionLevel = "max",
                // R2：默认排除规则全部以"启用"状态呈现。
                ExcludeRules = DefaultExcludes
                    .Select(rule => new ExcludeRule { Enabled = true, Pattern = rule, Comment = string.Empty })
                    .ToList(),
                ForceProtected = false,
                LastProjectPath = string.Empty,
                WindowGeometry = "720x640",
                LastBackupPath = string.Empty
            };
        }

        /// <summary>
        /// 从磁盘加载配置，缺失字段自动以默认值补全。
        /// 文件缺失或损坏时静默回退到默认配置。
        /// 旧版字符串数组排除规则（exclude_rules）会自动迁移为 <see cref="ExcludeRule"/> 列表。
        /// </summary>
        /// <returns>合并默认值后的有效配置对象。</returns>
        public static AppConfig Load()
        {
            AppConfig config = Defaults();
            string path = GetConfigPath();
            if (File.Exists(path))
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                        var loaded = serializer.ReadObject(fs) as AppConfig;
                        if (loaded != null)
                        {
                            // 仅当磁盘值非空时才覆盖对应字段，确保缺失字段保留默认值。
                            if (!string.IsNullOrEmpty(loaded.Language))
                            {
                                config.Language = loaded.Language;
                            }

                            if (!string.IsNullOrEmpty(loaded.OutputSubdir))
                            {
                                config.OutputSubdir = loaded.OutputSubdir;
                            }

                            if (!string.IsNullOrEmpty(loaded.CompressionLevel))
                            {
                                config.CompressionLevel = loaded.CompressionLevel;
                            }

                            // R2：排除规则兼容迁移。优先采用 V2；否则从旧字符串数组迁移。
                            MigrateExcludes(config, loaded);

                            config.ForceProtected = loaded.ForceProtected;
                            config.LastProjectPath = loaded.LastProjectPath ?? string.Empty;
                            if (!string.IsNullOrEmpty(loaded.WindowGeometry))
                            {
                                config.WindowGeometry = loaded.WindowGeometry;
                            }

                            // R9：最近备份记录（仅路径）直接采用磁盘值（缺失则保持空字符串）。
                            config.LastBackupPath = loaded.LastBackupPath ?? string.Empty;
                        }
                    }
                }
                catch
                {
                    // 文件损坏：保留默认配置，不向上抛出。
                }
            }

            // 排除规则为空时回退到默认集合，避免后续归档命令缺失 -xr! 参数。
            if (config.ExcludeRules == null || config.ExcludeRules.Count == 0)
            {
                config.ExcludeRules = DefaultExcludes
                    .Select(rule => new ExcludeRule { Enabled = true, Pattern = rule, Comment = string.Empty })
                    .ToList();
            }

            return config;
        }

        /// <summary>
        /// 将配置以 UTF-8 编码的 JSON 形式保存到磁盘。
        /// 保存时仅写出 V2 排除规则（exclude_rules_v2），旧键 exclude_rules 置 null 不写出。
        /// </summary>
        /// <param name="config">待保存的配置；为 <c>null</c> 时直接返回，不做任何操作。</param>
        public static void Save(AppConfig config)
        {
            if (config == null)
            {
                return;
            }

            string path = GetConfigPath();
            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    // 仅写出 V2 排除规则：将旧键置 null，配合 EmitDefaultValue=false 不落盘。
                    config.ExcludeRulesLegacy = null;
                    var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    serializer.WriteObject(fs, config);
                }
            }
            catch
            {
                // 尽力持久化；写入失败（如权限不足）时忽略。
            }
        }

        /// <summary>
        /// 将旧版字符串数组排除规则迁移为 <see cref="ExcludeRule"/> 列表（全部标记为启用）。
        /// 仅当 V2 字段为空且旧字段非空时生效。
        /// </summary>
        private static void MigrateExcludes(AppConfig config, AppConfig loaded)
        {
            if (loaded.ExcludeRules != null && loaded.ExcludeRules.Count > 0)
            {
                config.ExcludeRules = loaded.ExcludeRules;
                return;
            }

            if (loaded.ExcludeRulesLegacy != null && loaded.ExcludeRulesLegacy.Count > 0)
            {
                config.ExcludeRules = loaded.ExcludeRulesLegacy
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => new ExcludeRule { Enabled = true, Pattern = s.Trim(), Comment = string.Empty })
                    .ToList();
            }
        }

        /// <summary>
        /// 将多行或逗号分隔的排除规则文本规范化为规则列表：
        /// 去除每项两端空白、丢弃空项、去重并保持原有顺序。
        /// </summary>
        /// <param name="text">用户输入的排除规则文本块。</param>
        /// <returns>规范化后的规则列表（可能为空）。</returns>
        public static List<string> NormalizeExcludes(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            // 先将逗号统一为换行，再按行拆分，以兼容"逗号分隔"与"每行一条"两种输入习惯。
            string[] parts = text.Replace(",", "\n").Split('\n');
            foreach (string item in parts)
            {
                string cleaned = (item ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(cleaned) && !result.Contains(cleaned))
                {
                    result.Add(cleaned);
                }
            }

            return result;
        }

        /// <summary>
        /// 将排除规则集合规范化（去除空白项、去重、保持顺序）。
        /// </summary>
        /// <param name="rules">原始规则集合；为 <c>null</c> 时返回空列表。</param>
        /// <returns>规范化后的规则列表。</returns>
        public static List<string> NormalizeExcludes(IEnumerable<string> rules)
        {
            var result = new List<string>();
            if (rules == null)
            {
                return result;
            }

            foreach (string item in rules)
            {
                string cleaned = (item ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(cleaned) && !result.Contains(cleaned))
                {
                    result.Add(cleaned);
                }
            }

            return result;
        }
    }
}

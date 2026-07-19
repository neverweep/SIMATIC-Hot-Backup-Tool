// <summary>
// Internationalization service. UI strings are loaded at runtime from
// lang/*.json resource files (kept independent from the executable), so new
// languages can be added simply by dropping a file into the lang/ folder.
// The dropdown is built from whatever files are discovered there.
//
// A minimal English table is embedded as an ultimate safety net, so the app
// still runs (showing English with a few raw keys at worst) even if the lang/
// folder is missing or every file is corrupt.
// </summary>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SHBT.Core;

namespace SHBT.Ui
{
    /// <summary>
    /// Runtime localization: loads <c>lang/*.json</c> resources, resolves the
    /// active UI language (config / system detection / English fallback) and
    /// exposes string lookups plus the discovered language list.
    /// </summary>
    public static class Localization
    {
        /// <summary>Metadata about one supported UI language.</summary>
        public class LanguageInfo
        {
            /// <summary>Culture code, e.g. "en-US", "zh-TW".</summary>
            public string Code;

            /// <summary>Display name in the language itself (e.g. "English", "Русский").</summary>
            public string NativeName;

            /// <summary>Display name in English (fallback / tooltip).</summary>
            public string EnglishName;

            /// <summary>ComboBox 直接显示母语名（纯文本），去掉国旗图标。</summary>
            public override string ToString() => NativeName;
        }

        /// <summary>The language used when nothing else matches.</summary>
        private const string FallbackCode = "en-US";

        private static Dictionary<string, Dictionary<string, string>> _tables;
        private static List<LanguageInfo> _languages;
        private static string _currentCode = FallbackCode;

        /// <summary>Languages discovered in the lang/ folder (plus the embedded fallback).</summary>
        public static IReadOnlyList<LanguageInfo> Languages => _languages;

        /// <summary>The active language code shown in the UI.</summary>
        public static string CurrentCode
        {
            get => _currentCode;
            set
            {
                string next = IsSupported(value) ? value : FallbackCode;
                if (!string.Equals(next, _currentCode, StringComparison.OrdinalIgnoreCase))
                {
                    _currentCode = next;
                    LanguageChanged?.Invoke();
                }
            }
        }

        /// <summary>语言切换后触发，便于打开中的对话框（如开源软件说明窗口）实时本地化。</summary>
        public static event Action LanguageChanged;

        /// <summary>The fallback language code (always English).</summary>
        public static string Fallback => FallbackCode;

        /// <summary>
        /// Embedded English table — the last-resort fallback so the program never
        /// crashes on a missing/empty lang/ folder. Kept in sync with lang/en-US.json.
        /// </summary>
        private static readonly Dictionary<string, string> EmbeddedEnUs = new Dictionary<string, string>
        {
            ["app_title"] = "SIMATIC Hot Backup Tool",
            ["admin_relaunch_msg"] = "This tool needs administrator privileges to create a shadow copy.\nRestart as administrator now?",
            ["admin_ok"] = "Running as Administrator",
            ["admin_warn"] = "Please run as Administrator (right-click -> Run as admin), otherwise shadow copy cannot be created",
            ["admin_retry"] = "Attempting to elevate privileges…",
            ["group_source"] = "Source Project",
            ["browse"] = "Browse…",
            ["project_path"] = "Project path:",
            ["type_unknown"] = "No Siemens project signature found (missing AMOBJS / GRACS / XRef)",
            ["type_label"] = "Project type:",
            ["type_not_found"] = "No project found",
            ["project_suffix"] = " project",
            ["group_target"] = "Target Drive",
            ["col_letter"] = "Drive",
            ["col_label"] = "Label",
            ["col_free"] = "Free Space",
            ["col_safety"] = "Safety",
            ["col_type"] = "Type",
            ["safety_safe"] = "Safe",
            ["safety_forbidden"] = "Forbidden",
            ["safety_not_recommended"] = "Not recommended",
            ["safety_project"] = "Safe (project disk)",
            ["safety_readonly"] = "Read-only",
            ["safety_volatile"] = "Volatile",
            ["project_drive"] = "project drive",
            ["drive_cat_fixed"] = "Fixed disk",
            ["drive_cat_removable"] = "Removable disk",
            ["drive_cat_cdrom"] = "Optical disc",
            ["drive_cat_network"] = "Network drive",
            ["drive_cat_ram"] = "RAM disk",
            ["drive_cat_unknown"] = "Unknown",
            ["hint_target"] = "Click a row to select target drive; red rows are protected and disabled by default.",
            ["no_selection"] = "Please select a target drive first",
            ["group_options"] = "Options",
            ["compression"] = "Compression:",
            ["comp_store"] = "Store only",
            ["comp_fast"] = "Fastest",
            ["comp_standard"] = "Standard",
            ["comp_max"] = "Ultra",
            ["output_subdir"] = "Output subfolder:",
            ["exclude_rules"] = "Exclude rules (one per line):",
            ["force_protected"] = "Force write to protected drive",
            ["force_confirm_title"] = "Dangerous Operation Confirm",
            ["force_confirm_msg"] = "The selected drive is detected as a Siemens license/dongle drive.\nForcing a write may permanently corrupt software licenses.\nAre you sure you want to force the write?",
            ["start"] = "Start Backup",
            ["stop"] = "Stop",
            ["running"] = "Backing up…",
            ["stage_prepare"] = "Preparing backup…",
            ["stage_shadow"] = "Creating volume shadow copy (VSS)…",
            ["stage_archive"] = "Archiving project with 7-Zip…",
            ["stage_copy"] = "Copying archive to other targets…",
            ["stage_done"] = "Backup complete",
            ["stage_cancel"] = "Backup cancelled",
            ["stage_error"] = "Backup failed",
            ["done_title"] = "Backup Complete",
            ["done_msg"] = "Backup complete:\n{0}",
            ["error_title"] = "Backup Failed",
            ["error_msg"] = "Backup failed: {0}",
            ["cancel_title"] = "Cancelled",
            ["cancel_msg"] = "The backup was cancelled by the user.",
            ["group_log"] = "Log / Progress",
            ["progress"] = "Progress:",
            ["confirm_quit"] = "A backup is in progress. Are you sure you want to quit?",
            ["protected_blocked"] = "This drive is detected as a protected (Siemens license/dongle) drive and is blocked by default.\nTo write here, check \"Force write to protected drive\" first, then select it.",
            ["auto_located"] = "Auto-located project directory: {0}",
            ["type_STEP7_V5X"] = "STEP7 V5.X",
            ["type_WinCC_V7X"] = "WinCC Classic (v7.x/v8.x)",
            ["type_TIA_Portal"] = "TIA Portal",
            ["app_desc"] = "Hot-backup for WinCC / Step7 / TIA projects using VSS shadow copy — no downtime, no license risk.",
            ["no_label"] = "(No label)",
            ["mode_label"] = "Run mode: ",
            ["mode_admin"] = "Administrator",
            ["mode_user"] = "Standard user (limited)",
            ["last_backup"] = "Last backup:",
            ["disclaimer"] = "⚠ Test before use; you bear all risks.",
            ["exclude_help_title"] = "About Default Exclude Rules",
            ["exclude_help_intro"] = "Default exclude rules (runtime-generated files that need not be backed up):",
            ["restore_defaults"] = "Restore Default Rules",
            ["rule_help"] = "Rule help",
            ["restore_confirm_title"] = "Restore Default Rules",
            ["restore_confirm_msg"] = "Reset exclude rules to defaults? Your custom rules will be cleared.",
            ["restored_defaults"] = "Default exclude rules restored.",
            ["detect_running_wincc"] = "Detect WinCC",
            ["detect_running_tia"] = "Detect TIA",
            ["detect_running_step7"] = "Detect STEP 7",
            ["detect_running_found"] = "Detected running {0} project:\n{1}",
            ["detect_running_none"] = "No running {0} project detected.",
            ["detect_running_hint"] = "Auto-detect a currently running project:",
            ["detected_project"] = "Project detected — {0}: {1}",
            ["copy_ok"] = "Copied to {0}",
            ["copy_warn"] = "Copy to {0} failed (skipped): {1}",
            ["space_insufficient"] = "Insufficient free space on {0} (need {1}, free {2}).",
            ["force_start_confirm_title"] = "Confirm force write",
            ["force_start_confirm_msg"] = "You are about to write to one or more protected drives (license/dongle).\nThis may permanently damage licenses. Continue?",
            ["target_multi_hint"] = "Check multiple drives to back up to multiple targets. The first checked drive is the main target.",
            ["confirm_yes"] = "Continue",
            ["confirm_no"] = "Cancel",
            ["oss_credits"] = "Open Source",
            ["oss_credits_title"] = "Open Source Credits",
            ["oss_credits_header"] = "This software uses the following open-source components:",
            ["oss_credits_7zip_note"] = "High-compression file archiver (LGPL)",
            ["oss_credits_shadowspawn_note"] = "Volume shadow copy command-line tool (MIT)",
            ["oss_credits_close"] = "Close",
            ["__native_name"] = "English",
            ["confirm_yes"] = "OK",
            ["confirm_no"] = "Cancel",
            ["__english_name"] = "English"
        };

        /// <summary>
        /// Loads every <c>*.json</c> file in <paramref name="langFolder"/> as a
        /// language table, builds the supported-language list and guarantees the
        /// English fallback exists. Safe to call once at startup.
        /// </summary>
        public static void Initialize(string langFolder)
        {
            _tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            _languages = new List<LanguageInfo>();

            // English is always present as the fallback, straight from the embedded copy.
            _tables[FallbackCode] = new Dictionary<string, string>(EmbeddedEnUs, StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(langFolder))
            {
                foreach (string file in Directory.GetFiles(langFolder, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(code))
                    {
                        continue;
                    }

                    try
                    {
                        Dictionary<string, string> table = LoadTable(file);
                        if (table != null && table.Count > 0)
                        {
                            _tables[code] = table;
                        }
                    }
                    catch
                    {
                        // A single malformed language file must never block startup.
                    }
                }
            }

            // Build the dropdown list from every discovered table.
            foreach (var kvp in _tables)
            {
                Dictionary<string, string> table = kvp.Value;
                string native = table.TryGetValue("__native_name", out string n) && !string.IsNullOrEmpty(n) ? n : kvp.Key;
                string english = table.TryGetValue("__english_name", out string e) && !string.IsNullOrEmpty(e) ? e : kvp.Key;
                _languages.Add(new LanguageInfo { Code = kvp.Key, NativeName = native, EnglishName = english });
            }

            if (_languages.Count == 0)
            {
                _languages.Add(new LanguageInfo { Code = FallbackCode, NativeName = "English", EnglishName = "English" });
            }

            if (!IsSupported(_currentCode))
            {
                _currentCode = FallbackCode;
            }
        }

        /// <summary>Parses a flat JSON object file (<c>{"key":"value", ...}</c>) into a
        /// case-insensitive table. A minimal hand-written parser is used on purpose:
        /// <c>DataContractJsonSerializer</c> serializes a dictionary as a
        /// <c>[{"Key":..,"Value":..}]</c> array and will NOT read the flat object
        /// form, and <c>System.Text.Json</c> would add a NuGet dependency. Our lang
        /// files are always flat string maps, so this is sufficient and dependency-free
        /// (and works identically on net48 and net10).</summary>
        private static Dictionary<string, string> LoadTable(string path)
        {
            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return ParseFlatObject(json);
        }

        /// <summary>Parses a flat <c>{"k":"v"}</c> JSON object into a string dictionary,
        /// honoring the standard string escapes (\", \\, \/, \b, \f, \n, \r, \t, \uXXXX)
        /// and an optional UTF-8 BOM.</summary>
        private static Dictionary<string, string> ParseFlatObject(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            if (json.Length > 0 && json[0] == '\uFEFF')
            {
                i = 1; // skip BOM
            }

            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{')
            {
                return result;
            }

            i++; // consume '{'
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == '}')
                {
                    i++;
                    break;
                }

                string key = ParseString(json, ref i);
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ':')
                {
                    i++;
                }

                SkipWs(json, ref i);
                string val = ParseString(json, ref i);
                if (!string.IsNullOrEmpty(key))
                {
                    result[key] = val;
                }

                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < json.Length && json[i] == '}')
                {
                    i++;
                    break;
                }
            }

            return result;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
            {
                i++;
            }
        }

        private static string ParseString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"')
            {
                return string.Empty;
            }

            i++; // skip opening quote
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '\\')
                {
                    if (i >= json.Length)
                    {
                        break;
                    }

                    char e = json[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= json.Length &&
                                int.TryParse(json.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                            {
                                sb.Append((char)cp);
                            }

                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>True when <paramref name="code"/> matches a discovered language file.</summary>
        public static bool IsSupported(string code)
        {
            return !string.IsNullOrEmpty(code) && _tables != null && _tables.ContainsKey(code);
        }

        /// <summary>
        /// Maps the current OS UI culture to the best supported language: exact
        /// full-code match first (e.g. "zh-TW"), then a two-letter prefix match
        /// (e.g. "zh" -> "zh-CN"), otherwise the English fallback.
        /// </summary>
        public static string DetectSystemLanguage()
        {
            CultureInfo ui = CultureInfo.CurrentUICulture;
            string full = ui.Name;                       // "zh-CN"
            string two = ui.TwoLetterISOLanguageName;    // "zh"

            if (_languages != null)
            {
                foreach (LanguageInfo lang in _languages)
                {
                    if (string.Equals(lang.Code, full, StringComparison.OrdinalIgnoreCase))
                    {
                        return lang.Code;
                    }
                }

                foreach (LanguageInfo lang in _languages)
                {
                    if (string.Equals(lang.Code, two, StringComparison.OrdinalIgnoreCase) ||
                        lang.Code.StartsWith(two + "-", StringComparison.OrdinalIgnoreCase))
                    {
                        return lang.Code;
                    }
                }
            }

            return FallbackCode;
        }

        /// <summary>Returns the string for <paramref name="key"/> in the active language.</summary>
        public static string Get(string key)
        {
            return Get(CurrentCode, key);
        }

        /// <summary>Returns the string for <paramref name="key"/> in the given language code.</summary>
        public static string Get(string code, string key)
        {
            if (_tables != null &&
                _tables.TryGetValue(code ?? FallbackCode, out Dictionary<string, string> table) &&
                table.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (_tables != null &&
                _tables.TryGetValue(FallbackCode, out Dictionary<string, string> fb) &&
                fb.TryGetValue(key, out string fallback) && !string.IsNullOrEmpty(fallback))
            {
                return fallback;
            }

            return key;
        }

        /// <summary>Returns the localized text for a backup stage.</summary>
        public static string StageText(StageKey key)
        {
            return Get("stage_" + key.ToString().ToLowerInvariant());
        }

        /// <summary>Returns the localized display name for a project type.</summary>
        public static string ProjectTypeName(ProjectType type)
        {
            return Get("type_" + type.ToString());
        }

        /// <summary>Formats a byte count into a human-readable string (e.g. "1.2 GB").</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            double size = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (size >= 1024.0 && unit < units.Length - 1)
            {
                size /= 1024.0;
                unit++;
            }

            if (units[unit] == "B")
            {
                return string.Format("{0:0} {1}", size, units[unit]);
            }

            return string.Format("{0:0.0} {1}", size, units[unit]);
        }
    }
}

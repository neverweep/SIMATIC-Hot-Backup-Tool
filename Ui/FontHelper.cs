// <summary>
// Reads the system UI font via SystemParametersInfo(SPI_GETNONCLIENTMETRICS) and
// applies it recursively to a control tree. Using the real system font (e.g.
// "Microsoft YaHei" on Chinese Windows) prevents Windows from falling back CJK
// glyphs to Japanese forms (门 vs 門). Ported from ui/fonts.py.
//
// Root-cause of the previous startup crash: the applied Font must never be
// disposed before the form is done using it. Earlier code created ONE shared
// Font inside a `using` block in ApplyTo and assigned that same reference to
// every control. When ApplyTo returned the `using` disposed it, so every control
// held a dead Font. Later, ApplyLanguage() set a Label's Text, which triggered
// layout; a multiline TextBox recomputing PreferredHeight called
// Font.GetHeight() on the disposed Font and GDI+ threw
// "ArgumentException: 参数无效". The fix: every control gets its OWN fresh Font
// instance, and none of them are disposed here (the form disposes them on close).
//
// Secondary guard: some Windows themes return a GDI logical-font alias
// (e.g. "MS Shell Dlg" / "MS Shell Dlg 2") or an empty string. We only ever
// create Font instances from names that resolve to a real, enumerated
// FontFamily, falling back to known-good installed UI fonts.
// </summary>
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SHBT.Ui
{
    /// <summary>
    /// Helpers for resolving and applying the operating system's default UI font.
    /// </summary>
    public static class FontHelper
    {
        private const int SPI_GETNONCLIENTMETRICS = 0x0029;
        private const int LF_FACESIZE = 32;

        // GDI logical-font aliases that must never be fed to new Font(...).
        private const string MsShellDlg = "MS Shell Dlg";
        private const string MsShellDlg2 = "MS Shell Dlg 2";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LOGFONT
        {
            public int lfHeight;
            public int lfWidth;
            public int lfEscapement;
            public int lfOrientation;
            public int lfWeight;
            public byte lfItalic;
            public byte lfUnderline;
            public byte lfStrikeOut;
            public byte lfCharSet;
            public byte lfOutPrecision;
            public byte lfClipPrecision;
            public byte lfQuality;
            public byte lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = LF_FACESIZE)]
            public string lfFaceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NONCLIENTMETRICS
        {
            public uint cbSize;
            public int iBorderWidth;
            public int iScrollWidth;
            public int iScrollHeight;
            public int iCaptionWidth;
            public int iCaptionHeight;
            public LOGFONT lfCaptionFont;
            public int iSmCaptionWidth;
            public int iSmCaptionHeight;
            public LOGFONT lfSmCaptionFont;
            public int iMenuWidth;
            public int iMenuHeight;
            public LOGFONT lfMenuFont;
            public LOGFONT lfStatusFont;
            public LOGFONT lfMessageFont;
            public int iPaddedBorderWidth;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint uiAction, uint cbSize, ref NONCLIENTMETRICS pvParam, uint fWinIni);

        /// <summary>
        /// Returns <c>true</c> only when <paramref name="name"/> refers to a real,
        /// enumerated font family that GDI+ can resolve. Logical aliases and empty
        /// names return <c>false</c> because <c>new Font("MS Shell Dlg", ...)</c>
        /// defers resolution until text is measured, at which point it throws.
        /// </summary>
        public static bool IsUsableFontName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (string.Equals(name, MsShellDlg, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, MsShellDlg2, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (FontFamily family in FontFamily.Families)
            {
                if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves a real <see cref="FontFamily"/> for <paramref name="name"/> by
        /// matching against the enumerated system families (case-insensitive). When
        /// no match exists it falls back to the system message-box font family, which
        /// is always a real, installed family.
        /// </summary>
        public static FontFamily ResolveFamily(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                foreach (FontFamily family in FontFamily.Families)
                {
                    if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return family;
                    }
                }
            }

            // Guaranteed-real fallback: the message-box font exists on every Windows
            // install; GenericSansSerif guards against a theoretically null reference.
            return SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        }

        /// <summary>
        /// Returns the system message-box font face name, or a guaranteed-usable
        /// fallback when the query yields an alias, empty string, or an uninstalled
        /// font. The fallback chain keeps the system UI font first so CJK glyphs are
        /// still rendered natively (no 门 -> 門 regression).
        /// </summary>
        public static string GetSystemFontName()
        {
            string candidate = null;
            try
            {
                var ncm = new NONCLIENTMETRICS
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(NONCLIENTMETRICS))
                };

                if (SystemParametersInfo(SPI_GETNONCLIENTMETRICS, ncm.cbSize, ref ncm, 0))
                {
                    candidate = ncm.lfMessageFont.lfFaceName;
                    if (!string.IsNullOrEmpty(candidate))
                    {
                        candidate = candidate.TrimEnd('\0').Trim();
                    }
                }
            }
            catch
            {
                candidate = null;
            }

            if (IsUsableFontName(candidate))
            {
                return candidate;
            }

            // Fallback chain: prefer the real system message-box font (zh-CN => 微软雅黑/宋体,
            // native CJK), then other well-known installed UI fonts, finally the framework's
            // always-resolvable default.
            if (SystemFonts.MessageBoxFont != null && IsUsableFontName(SystemFonts.MessageBoxFont.Name))
            {
                return SystemFonts.MessageBoxFont.Name;
            }

            if (IsUsableFontName("Microsoft YaHei"))
            {
                return "Microsoft YaHei";
            }

            if (IsUsableFontName("Segoe UI"))
            {
                return "Segoe UI";
            }

            return "Microsoft Sans Serif";
        }

        /// <summary>
        /// Applies the system UI font to <paramref name="root"/> and all of its
        /// descendant controls. The font size is inherited from the root control.
        /// Any failure leaves the default font in place instead of crashing the form.
        /// </summary>
        public static void ApplyTo(Control root)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                FontFamily family = ResolveFamily(GetSystemFontName());

                float emSize = root.Font?.SizeInPoints ?? 9f;
                if (!IsValidSize(emSize))
                {
                    emSize = 9f;
                }

                // Each control creates its own Font instance inside ApplyFontRecursive.
                // We must NOT share one Font wrapped in `using`: disposing it here (when
                // ApplyTo returns) would leave every control holding a disposed Font
                // reference. A later layout pass (e.g. ApplyLanguage sets a Label's Text
                // and a multiline TextBox recomputes PreferredHeight) calls Font.GetHeight()
                // on that dead Font and GDI+ throws "ArgumentException: 参数无效". The form
                // owns and disposes these fonts when it closes, so we never dispose them.
                ApplyFontRecursive(root, family, emSize);
            }
            catch
            {
                // If font creation or application fails, leave the default font in place.
            }
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="size"/> is a finite, positive
        /// em-size that <c>new Font</c> can use. Avoids <c>float.IsFinite</c> (not
        /// available on .NET Framework 4.8).
        /// </summary>
        private static bool IsValidSize(float size)
        {
            return !float.IsNaN(size) && !float.IsInfinity(size) && size > 0f;
        }

        private static void ApplyFontRecursive(Control control, FontFamily family, float emSize)
        {
            try
            {
                // Fresh instance per control. The form owns and disposes it on close.
                // Must NOT be a shared/disposed-early instance: a disposed Font makes
                // Font.GetHeight() throw "ArgumentException: 参数无效" during later layout
                // passes (e.g. when ApplyLanguage sets a Label's Text and the multiline
                // TextBox recomputes PreferredHeight).
                control.Font = new Font(family, emSize, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                // A few control types reject font assignment; keep their default font
                // and skip without aborting the rest of the tree.
            }

            foreach (Control child in control.Controls)
            {
                ApplyFontRecursive(child, family, emSize);
            }
        }
    }
}

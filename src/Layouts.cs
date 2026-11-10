using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace LSwitcher
{
    internal enum CaseMode { Uppercase, Lowercase, Invert }
    internal static class CaseConverter
    {
        internal static string Convert(string text, CaseMode mode)
        {
            // Work by Unicode scalar so supplementary-plane letters are handled too.
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                int length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                string scalar = text.Substring(i, length);
                if (mode == CaseMode.Uppercase) result.Append(scalar.ToUpperInvariant());
                else if (mode == CaseMode.Lowercase) result.Append(scalar.ToLowerInvariant());
                else result.Append(char.IsUpper(text, i) ? scalar.ToLowerInvariant() : char.IsLower(text, i) ? scalar.ToUpperInvariant() : scalar);
                i += length - 1;
            }
            return result.ToString();
        }
    }
    internal sealed class KeyboardLayout
    {
        internal IntPtr Handle;
        internal string Id, Name, Label;
        public override string ToString() { return Name; }
    }
    internal static class Layouts
    {
        internal static string Id(IntPtr handle) { return unchecked((uint)handle.ToInt64()).ToString("X8"); }
        internal static List<KeyboardLayout> Installed()
        {
            var handles = new IntPtr[Native.GetKeyboardLayoutList(0, null)]; Native.GetKeyboardLayoutList(handles.Length, handles);
            var result = new List<KeyboardLayout>();
            foreach (var h in handles)
            {
                string name = Id(h), label = "??"; int language = (int)(h.ToInt64() & 0xffff);
                try { var culture = CultureInfo.GetCultureInfo(language); name = culture.EnglishName; label = culture.TwoLetterISOLanguageName.ToUpperInvariant(); } catch (CultureNotFoundException) { }
                string registryId = Id(h);
                // Standard HKLs repeat the language ID in the high word; registry KLIDs do not.
                if (((h.ToInt64() >> 16) & 0xffff) == language) registryId = language.ToString("X8");
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\" + registryId))
                { var text = key == null ? null : key.GetValue("Layout Text") as string; if (!string.IsNullOrEmpty(text)) name += " — " + text; }
                var input = System.Windows.Forms.InputLanguage.InstalledInputLanguages.Cast<System.Windows.Forms.InputLanguage>().FirstOrDefault(l => l.Handle == h);
                if (input != null) name = input.Culture.EnglishName + " — " + input.LayoutName;
                result.Add(new KeyboardLayout { Handle = h, Id = Id(h), Name = name, Label = label.Length > 2 ? label.Substring(0, 2) : label });
            }
            return result;
        }
        internal static IntPtr ForegroundLayout() { uint pid; return Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid)); }
        internal static Dictionary<char, char> BuildMap(IntPtr from, IntPtr to)
        {
            var candidates = new Dictionary<char, HashSet<char>>();
            // Match scan codes, including the ISO extra key, rather than virtual-key meanings.
            for (uint scan = 1; scan <= 86; scan++)
                for (int state = 0; state < 4; state++)
                {
                    char? a = Character(from, scan, state), b = Character(to, scan, state);
                    if (!a.HasValue || !b.HasValue || char.IsControl(a.Value) || char.IsWhiteSpace(a.Value)) continue;
                    if (!candidates.ContainsKey(a.Value)) candidates[a.Value] = new HashSet<char>();
                    candidates[a.Value].Add(b.Value);
                }
            return candidates.Where(p => p.Value.Count == 1).ToDictionary(p => p.Key, p => p.Value.First());
        }
        private static char? Character(IntPtr layout, uint scan, int modifiers)
        {
            uint vk = Native.MapVirtualKeyEx(scan, 3, layout); if (vk == 0) return null;
            var state = new byte[256]; if ((modifiers & 1) != 0) state[0x10] = 0x80;
            if ((modifiers & 2) != 0) { state[0x11] = 0x80; state[0x12] = 0x80; state[0xA5] = 0x80; }
            var text = new StringBuilder(8);
            int count = Native.ToUnicodeEx(vk, scan, state, text, text.Capacity, 4, layout); // No dead-key state mutation (Windows 11).
            return count == 1 ? (char?)text[0] : null;
        }
    }
    internal sealed class TextConverter
    {
        private readonly Dictionary<char, char> forward, reverse;
        internal TextConverter(Dictionary<char, char> first, Dictionary<char, char> second) { forward = first; reverse = second; }
        internal string Convert(string text, ConversionDirection direction)
        {
            if (direction != ConversionDirection.SwapBothWays) return Map(text, direction == ConversionDirection.FirstToSecond ? forward : reverse);
            var output = new StringBuilder(text.Length); bool previousFirst = true;
            for (int i = 0; i < text.Length;)
            {
                if (char.IsLetter(text[i]))
                {
                    int end = i; int first = 0, second = 0;
                    while (end < text.Length && char.IsLetter(text[end])) { char c = text[end++]; if (forward.ContainsKey(c) && !reverse.ContainsKey(c)) first++; if (reverse.ContainsKey(c) && !forward.ContainsKey(c)) second++; }
                    previousFirst = first == second ? previousFirst : first > second;
                    var preferred = previousFirst ? forward : reverse; var other = previousFirst ? reverse : forward;
                    for (; i < end; i++) { char mapped; char c = text[i]; output.Append(preferred.TryGetValue(c, out mapped) || other.TryGetValue(c, out mapped) ? mapped : c); }
                }
                else
                {
                    char c = text[i++], mapped; var preferred = previousFirst ? forward : reverse; var other = previousFirst ? reverse : forward;
                    output.Append(preferred.TryGetValue(c, out mapped) || other.TryGetValue(c, out mapped) ? mapped : c);
                }
            }
            return output.ToString();
        }
        private static string Map(string text, Dictionary<char, char> map) { return new string(text.Select(c => { char value; return map.TryGetValue(c, out value) ? value : c; }).ToArray()); }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LSwitcher
{
    internal sealed class TapTracker
    {
        private readonly Dictionary<int, long> starts = new Dictionary<int, long>();
        private readonly HashSet<int> used = new HashSet<int>();
        internal void Down(int key, long now)
        {
            if (starts.ContainsKey(key)) return;
            foreach (var held in starts.Keys) used.Add(held);
            if (starts.Count > 0) used.Add(key);
            starts[key] = now;
        }
        internal void Interrupt() { foreach (var held in starts.Keys) used.Add(held); }
        internal bool Up(int key, long now, int limit)
        {
            long start; bool tap = starts.TryGetValue(key, out start) && !used.Contains(key) && now - start <= limit;
            starts.Remove(key); used.Remove(key); return tap;
        }
        internal void Clear() { starts.Clear(); used.Clear(); }
    }
    internal sealed class HotkeyEngine : IDisposable
    {
        internal const int WinModifier = 0x80000;
        private readonly Native.HookProc keyboardProc, mouseProc;
        private readonly IntPtr keyboardHook, mouseHook;
        private readonly HashSet<int> held = new HashSet<int>(), suppressed = new HashSet<int>();
        private readonly TapTracker taps = new TapTracker();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        internal Settings Settings;
        internal bool Paused;
        internal IntPtr SettingsWindow;
        internal Func<int, bool, bool> Capture;
        internal HashSet<string> AvailableLayouts = new HashSet<string>();
        internal event Action<LayoutBinding, IntPtr> LayoutRequested;
        internal event Action<IntPtr> ConversionRequested;
        internal event Action<CaseMode, IntPtr> CaseRequested;
        internal event Action PauseRequested;
        private IntPtr cachedWindow;
        private string cachedPath = "";
        internal HotkeyEngine(Settings settings)
        {
            Settings = settings; AvailableLayouts = new HashSet<string>(Layouts.Installed().Select(l => l.Id)); keyboardProc = Keyboard; mouseProc = Mouse;
            keyboardHook = Native.SetWindowsHookEx(13, keyboardProc, Native.GetModuleHandle(null), 0);
            mouseHook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero) { Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot install input hooks."); }
        }
        internal static bool IsModifier(int key) { return key == 0x10 || key == 0x11 || key == 0x12 || key == 0x5b || key == 0x5c || (key >= 0xa0 && key <= 0xa5); }
        internal static int ModifierFlag(int key)
        {
            if (key == 0x5b || key == 0x5c) return WinModifier;
            if (key == 0x10 || key == 0xa0 || key == 0xa1) return (int)Keys.Shift;
            if (key == 0x11 || key == 0xa2 || key == 0xa3) return (int)Keys.Control;
            if (key == 0x12 || key == 0xa4 || key == 0xa5) return (int)Keys.Alt;
            return 0;
        }
        internal bool Excluded(IntPtr window)
        {
            if (window != cachedWindow)
            {
                cachedWindow = window; cachedPath = "";
                try { uint pid; Native.GetWindowThreadProcessId(window, out pid); using (var process = Process.GetProcessById((int)pid)) cachedPath = process.MainModule.FileName; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is ArgumentException) { }
            }
            return Settings.Exclusions.Any(e => e.Matches(cachedPath));
        }
        internal void Reset() { taps.Clear(); cachedWindow = IntPtr.Zero; }
        private IntPtr Mouse(int code, IntPtr w, IntPtr l)
        {
            int msg = w.ToInt32();
            if (code >= 0 && (msg == 0x201 || msg == 0x204 || msg == 0x207 || msg == 0x20b || msg == 0x20a || msg == 0x20e)) taps.Interrupt();
            return Native.CallNextHookEx(mouseHook, code, w, l);
        }
        private IntPtr Keyboard(int code, IntPtr w, IntPtr l)
        {
            if (code < 0) return Native.CallNextHookEx(keyboardHook, code, w, l);
            try
            {
                var data = (Native.KeyboardData)Marshal.PtrToStructure(l, typeof(Native.KeyboardData));
                if ((data.flags & 0x10) != 0) return Native.CallNextHookEx(keyboardHook, code, w, l);
                int key = (int)data.vk;
                if (key == 0x10) key = data.scan == 0x36 ? 0xa1 : 0xa0;
                if (key == 0x11) key = (data.flags & 1) != 0 ? 0xa3 : 0xa2;
                if (key == 0x12) key = (data.flags & 1) != 0 ? 0xa5 : 0xa4;
                bool up = w.ToInt32() == 0x101 || w.ToInt32() == 0x105;
                bool repeat = !up && held.Contains(key);
                int modifiers = held.Aggregate(0, (flags, k) => flags | ModifierFlag(k));
                if (up) held.Remove(key); else held.Add(key);
                IntPtr target = Native.GetForegroundWindow();
                if (Capture != null && target == SettingsWindow)
                {
                    taps.Clear();
                    if (Capture(key, up)) { if (!up) suppressed.Add(key); else suppressed.Remove(key); return (IntPtr)1; }
                }
                bool wasSuppressed = up && suppressed.Remove(key);
                bool enabled = target != SettingsWindow && !Excluded(target);
                bool tap = false;
                if (up) tap = taps.Up(key, clock.ElapsedMilliseconds, Settings.TapMilliseconds);
                else if (!repeat)
                {
                    taps.Down(key, clock.ElapsedMilliseconds);
                    if (new[] { 1, 2, 4, 5, 6 }.Any(k => (Native.GetAsyncKeyState(k) & 0x8000) != 0)) taps.Interrupt();
                }
                if (!enabled) { taps.Interrupt(); return wasSuppressed ? (IntPtr)1 : Native.CallNextHookEx(keyboardHook, code, w, l); }
                if (up && IsModifier(key) && tap)
                {
                    if (Trigger(key, 0, target) && (key == 0xa4 || key == 0xa5 || key == 0x5b || key == 0x5c))
                    { Native.Key(0xff, false); Native.Key(0xff, true); } // Prevent Alt/Win tap menus while retaining real shortcut behavior.
                }
                else if (!up && !IsModifier(key))
                {
                    if (suppressed.Contains(key) || (!repeat && Trigger(key, modifiers, target))) { suppressed.Add(key); return (IntPtr)1; }
                }
                if (wasSuppressed) return (IntPtr)1;
            }
            catch (Exception ex) { Debug.WriteLine(ex); taps.Clear(); }
            return Native.CallNextHookEx(keyboardHook, code, w, l);
        }
        internal bool Trigger(int key, int modifiers, IntPtr target)
        {
            if (target == SettingsWindow || Excluded(target)) return false;
            if (Matches(Settings.PauseShortcut, key, modifiers)) { PauseRequested?.Invoke(); return true; }
            if (Paused) return false;
            if (Matches(Settings.UppercaseShortcut, key, modifiers)) { CaseRequested?.Invoke(CaseMode.Uppercase, target); return true; }
            if (Matches(Settings.LowercaseShortcut, key, modifiers)) { CaseRequested?.Invoke(CaseMode.Lowercase, target); return true; }
            if (Matches(Settings.InvertCaseShortcut, key, modifiers)) { CaseRequested?.Invoke(CaseMode.Invert, target); return true; }
            var binding = Settings.Bindings.FirstOrDefault(b => b.Shortcut.Key == key && b.Shortcut.Modifiers == modifiers);
            if (binding != null)
            {
                if (!AvailableLayouts.Contains(binding.LayoutId)) return false;
                LayoutRequested?.Invoke(binding, target); return true;
            }
            var shortcut = Settings.ConversionShortcut;
            if (shortcut.Key == key && shortcut.Modifiers == modifiers && shortcut.Key != 0 && AvailableLayouts.Contains(Settings.FirstLayout ?? "") && AvailableLayouts.Contains(Settings.SecondLayout ?? ""))
            { ConversionRequested?.Invoke(target); return true; }
            return false;
        }
        private static bool Matches(Shortcut shortcut, int key, int modifiers) { return shortcut.Key != 0 && shortcut.Key == key && shortcut.Modifiers == modifiers; }
        public void Dispose() { if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook); if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook); }
    }
}

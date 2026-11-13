using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LSwitcher
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int left, top, right, bottom; }
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { public uint vk, scan, flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public INPUTUNION data; }
        [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT keyboard; [FieldOffset(0)] public MOUSEINPUT mouse; }
        [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int x, y; public uint data, flags, time; public UIntPtr extra; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] internal static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] internal static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] layouts);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr layout);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int ToUnicodeEx(uint key, uint scan, byte[] state, StringBuilder result, int count, uint flags, IntPtr layout);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("oleacc.dll")] internal static extern int AccessibleObjectFromWindow(IntPtr window, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
        internal delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostThreadMessage(uint thread, uint message, IntPtr w, IntPtr l);
        [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr window, EnumWindowsProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int max);
        internal static void Key(int key, bool up)
        {
            var input = new INPUT { type = 1, data = new INPUTUNION { keyboard = new KEYBDINPUT { vk = (ushort)key, flags = up ? 2u : 0u } } };
            if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) != 1) throw new InvalidOperationException("Windows rejected keyboard input.");
        }
        internal static void Chord(int key) { Key(0x11, false); try { Key(key, false); Key(key, true); } finally { Key(0x11, true); } }
    }
}

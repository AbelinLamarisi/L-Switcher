using System;
using System.Diagnostics;
using System.Linq;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Security.Principal;
using System.Threading;

namespace LSwitcher
{
    // Ownership stays on the UI thread until its hooks, tray and windows are disposed.
    internal sealed class SingleInstance : IDisposable
    {
        private readonly Mutex instance, launch;
        private readonly EventWaitHandle shutdown;
        private readonly bool newShutdown;
        private readonly MemoryMappedFile versionMap;
        private readonly MemoryMappedViewAccessor versionView;
        private bool ownsInstance, ownsLaunch;
        internal SingleInstance(string name)
        {
            try
            {
                instance = new Mutex(false, name);
                launch = new Mutex(false, name + "-Launch");
                shutdown = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-Shutdown", out newShutdown);
                versionMap = MemoryMappedFile.CreateOrOpen(name + "-BuildIdentity", 4096);
                versionView = versionMap.CreateViewAccessor();
            }
            catch { Dispose(); throw; }
        }
        private static bool Take(Mutex mutex, int milliseconds)
        {
            try { return mutex.WaitOne(milliseconds); } catch (AbandonedMutexException) { return true; }
        }
        internal bool Acquire(Action requestLegacyShutdown = null, int timeoutMilliseconds = 10000, string buildIdentity = null, Action activateExisting = null)
        {
            if (ownsInstance || ownsLaunch) throw new InvalidOperationException("This launch already owns the instance lock.");
            var elapsed = Stopwatch.StartNew();
            ownsLaunch = Take(launch, timeoutMilliseconds);
            if (!ownsLaunch) throw new TimeoutException("Another L-Switcher launch is still in progress. Please try again.");
            try
            {
                ownsInstance = Take(instance, 0);
                if (!ownsInstance)
                {
                    if (!string.IsNullOrEmpty(buildIdentity) && string.Equals(ReadIdentity(), buildIdentity, StringComparison.Ordinal))
                    {
                        // Recheck ownership in case the existing instance exited during inspection.
                        ownsInstance = Take(instance, 0);
                        if (!ownsInstance) { activateExisting?.Invoke(); Ready(); return false; }
                    }
                }
                if (!ownsInstance)
                {
                    // Old builds predate the shutdown event; keep a graceful migration path.
                    if (newShutdown && requestLegacyShutdown != null) requestLegacyShutdown();
                    else shutdown.Set();
                    ownsInstance = Take(instance, Math.Max(0, timeoutMilliseconds - (int)elapsed.ElapsedMilliseconds));
                    if (!ownsInstance) throw new TimeoutException("The running L-Switcher instance did not exit in time. No second instance was started. Close it through the tray and try again.");
                }
                WriteIdentity(buildIdentity);
                shutdown.Reset();
                return true;
            }
            catch { Ready(); throw; }
        }
        private string ReadIdentity()
        {
            int length = versionView.ReadInt32(0);
            if (length <= 0 || length > 4092) return null;
            var bytes = new byte[length]; versionView.ReadArray(4, bytes, 0, length); return Encoding.UTF8.GetString(bytes);
        }
        private void WriteIdentity(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            if (bytes.Length > 4092) throw new InvalidOperationException("The build identifier is too long.");
            versionView.Write(0, 0); versionView.WriteArray(4, bytes, 0, bytes.Length); versionView.Write(0, bytes.Length);
        }
        // Serialize handover through startup, not just through acquisition of the lock.
        internal void Ready() { if (ownsLaunch) { launch.ReleaseMutex(); ownsLaunch = false; } }
        internal bool ShutdownRequested { get { return shutdown.WaitOne(0); } }
        public void Dispose()
        {
            Ready();
            if (ownsInstance) { versionView.Write(0, 0); instance.ReleaseMutex(); ownsInstance = false; }
            versionView?.Dispose(); versionMap?.Dispose();
            shutdown?.Dispose(); launch?.Dispose(); instance?.Dispose();
        }
    }

    internal static class LegacyInstance
    {
        internal static void RequestShutdown()
        {
            // Only legacy L-Switcher processes in this user's interactive session qualify.
            using (var current = Process.GetCurrentProcess())
            {
                string user = WindowsIdentity.GetCurrent().User.Value;
                var candidates = Process.GetProcessesByName("L-Switcher");
                try
                {
                    var matching = candidates.Where(p => p.Id != current.Id && SameUserAndSession(p, current.SessionId, user)).ToArray();
                    if (matching.Length != 1) throw new InvalidOperationException("Cannot identify the running legacy L-Switcher instance. Close it through the tray and launch again.");
                    RequestShutdownForProcess(matching[0].Id);
                }
                finally { foreach (var process in candidates) process.Dispose(); }
            }
        }
        internal static void RequestShutdownForProcess(int processId)
        {
            using (var current = Process.GetCurrentProcess())
            using (var process = Process.GetProcessById(processId))
            {
                if (!SameUserAndSession(process, current.SessionId, WindowsIdentity.GetCurrent().User.Value)) throw new InvalidOperationException("The legacy instance belongs to another user or session.");
                uint thread = 0;
                Native.EnumWindows((window, data) =>
                {
                    uint pid; uint foundThread = Native.GetWindowThreadProcessId(window, out pid);
                    if (pid != processId) return true;
                    thread = foundThread; return false;
                }, IntPtr.Zero);
                // Keep the process handle open through posting to prevent PID reuse.
                // WM_QUIT exits the message loop; Program's using blocks dispose resources.
                if (thread == 0 || !Native.PostThreadMessage(thread, 0x12, IntPtr.Zero, IntPtr.Zero))
                    throw new InvalidOperationException("Cannot request shutdown of the legacy instance. Close it through the tray and launch again.");
            }
        }
        private static bool SameUserAndSession(Process process, int session, string user)
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (process.SessionId != session || !Native.OpenProcessToken(process.Handle, 8, out token)) return false;
                using (var identity = new WindowsIdentity(token)) return identity.User.Value == user;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { return false; }
            finally { if (token != IntPtr.Zero) Native.CloseHandle(token); }
        }
    }
}

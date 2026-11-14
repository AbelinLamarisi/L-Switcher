using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace LSwitcher
{
    internal static partial class Tests
    {
        private static int InstanceWorker(string[] args)
        {
            try
            {
                string mode = args[0], name = args[1], ready = args[2], version = args.Length > 3 ? args[3] : null;
                if (mode == "--instance-probe")
                {
                    using (var instance = new SingleInstance(name))
                    { try { instance.Acquire(timeoutMilliseconds: 300); return 2; } catch (TimeoutException) { return 3; } }
                }
                if (mode == "--instance-legacy")
                {
                    Application.EnableVisualStyles();
                    using (var mutex = new Mutex(false, name))
                    using (var window = new Form())
                    {
                        if (!mutex.WaitOne(0)) return 2;
                        try { IntPtr handle = window.Handle; File.WriteAllText(ready, "ready"); Application.Run(); }
                        finally { mutex.ReleaseMutex(); }
                    }
                    return 0;
                }
                using (var instance = new SingleInstance(name))
                {
                    instance.Acquire(buildIdentity: version);
                    if (mode == "--instance-tray")
                    {
                        Application.EnableVisualStyles(); Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                        var store = new SettingsStore(ready + ".settings.json"); if (version != null) store.Save(new Settings());
                        using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-Open"))
                        using (var context = new TrayApplication(activate, store, () => instance.ShutdownRequested))
                        { instance.Ready(); File.WriteAllText(ready, "ready"); Application.Run(context); }
                    }
                    else
                    {
                        instance.Ready(); File.WriteAllText(ready, "ready");
                        while (!File.Exists(ready + ".exit"))
                        {
                            if (instance.ShutdownRequested && mode != "--instance-stubborn") { File.WriteAllText(ready + ".stopping", "cleanup"); Thread.Sleep(600); break; }
                            Thread.Sleep(10);
                        }
                    }
                }
                File.WriteAllText(ready + ".exited", "disposed"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static Process StartInstanceWorker(string mode, string name, string ready, string version = null)
        {
            return Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, mode + " \"" + name + "\" \"" + ready + "\"" + (version == null ? "" : " \"" + version + "\"")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        private static void WaitForReady(Process process, string ready)
        {
            var wait = Stopwatch.StartNew();
            while (!File.Exists(ready) && !process.HasExited && wait.ElapsedMilliseconds < 10000) Thread.Sleep(20);
            Check(File.Exists(ready), "Isolated instance worker started");
        }
        private static void StopWorker(Process process, string ready)
        {
            if (process.HasExited) return;
            File.WriteAllText(ready + ".exit", "exit");
            if (!process.WaitForExit(5000)) { process.Kill(); process.WaitForExit(); } // Test helper only, never the user's application.
        }
        private static void TestSingleInstance()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instance-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                string name = @"Local\L-Switcher-Test-" + Guid.NewGuid().ToString("N"), ready = Path.Combine(directory, "owner");
                using (var worker = StartInstanceWorker("--instance-worker", name, ready))
                {
                    try
                    {
                        WaitForReady(worker, ready);
                        using (var replacement = new SingleInstance(name))
                        {
                            var elapsed = Stopwatch.StartNew(); replacement.Acquire();
                            Check(elapsed.ElapsedMilliseconds >= 550, "Replacement waits for old instance cleanup and lock release");
                            Check(worker.WaitForExit(5000), "Replaced process exits gracefully"); Equal(0, worker.ExitCode, "Old worker finished normally");
                            Check(File.Exists(ready + ".exited"), "Old resources disposed before takeover");
                            using (var contender = StartInstanceWorker("--instance-probe", name, ready + "-contender"))
                            { Check(contender.WaitForExit(5000), "Concurrent launcher completes"); Equal(3, contender.ExitCode, "Launch gate blocks a contender during startup"); }
                            Check(!replacement.ShutdownRequested, "Blocked contender did not interrupt startup"); replacement.Ready();
                        }
                    }
                    finally { StopWorker(worker, ready); }
                }
                ready = Path.Combine(directory, "stubborn");
                using (var worker = StartInstanceWorker("--instance-stubborn", name, ready))
                {
                    try
                    {
                        WaitForReady(worker, ready); bool timedOut = false;
                        using (var replacement = new SingleInstance(name)) { try { replacement.Acquire(timeoutMilliseconds: 300); } catch (TimeoutException) { timedOut = true; } }
                        Check(timedOut, "Unresponsive owner reports timeout"); Check(!worker.HasExited, "Unresponsive owner is never forcibly terminated");
                        using (var mutex = new Mutex(false, name)) { bool obtained = mutex.WaitOne(0); if (obtained) mutex.ReleaseMutex(); Check(!obtained, "No second owner starts after timeout"); }
                    }
                    finally { StopWorker(worker, ready); }
                }
                using (var fresh = new SingleInstance(name)) { fresh.Acquire(); fresh.Ready(); Check(!fresh.ShutdownRequested, "New launch works after previous timeout and shutdown"); }
                ready = Path.Combine(directory, "legacy");
                using (var worker = StartInstanceWorker("--instance-legacy", name, ready))
                {
                    try
                    {
                        WaitForReady(worker, ready); bool legacyRequested = false;
                        using (var replacement = new SingleInstance(name))
                        { replacement.Acquire(() => { legacyRequested = true; LegacyInstance.RequestShutdownForProcess(worker.Id); }); replacement.Ready(); }
                        Check(legacyRequested, "Pre-shutdown-event build uses the legacy graceful path"); Check(worker.WaitForExit(5000), "Legacy UI loop exits"); Equal(0, worker.ExitCode, "Legacy process disposes and exits normally");
                    }
                    finally { StopWorker(worker, ready); }
                }
                foreach (string version in new[] { "commit:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "release:v1.2.3" })
                {
                    ready = Path.Combine(directory, "version-owner");
                    using (var worker = StartInstanceWorker("--instance-worker", name, ready, version))
                    {
                        try
                        {
                            WaitForReady(worker, ready); int activations = 0;
                            using (var same = new SingleInstance(name))
                            {
                                Check(!same.Acquire(buildIdentity: version, activateExisting: () => activations++), "Same build reuses existing instance");
                                Equal(1, activations, "Same build requests settings exactly once"); Check(!worker.HasExited && !File.Exists(ready + ".stopping"), "Same build does not restart the owner");
                            }
                            using (var different = new SingleInstance(name))
                            { Check(different.Acquire(buildIdentity: version + "-different"), "Different build acquires instance after graceful shutdown"); different.Ready(); }
                            Check(worker.WaitForExit(5000) && worker.ExitCode == 0, "Different build closes previous process normally");
                        }
                        finally { StopWorker(worker, ready); foreach (string file in Directory.GetFiles(directory, "version-owner*")) File.Delete(file); }
                    }
                }
            }
            finally { foreach (string path in Directory.GetFiles(directory)) File.Delete(path); Directory.Delete(directory); }
        }
        private static void TestInstanceTrayShutdown()
        {
            string name = @"Local\L-Switcher-Tray-Test-" + Guid.NewGuid().ToString("N"), ready = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray-instance-" + Guid.NewGuid().ToString("N"));
            using (var worker = StartInstanceWorker("--instance-tray", name, ready))
            {
                try
                {
                    WaitForReady(worker, ready);
                    using (var replacement = new SingleInstance(name))
                    { replacement.Acquire(); replacement.Ready(); Check(worker.WaitForExit(5000), "Tray receives shutdown and exits before replacement runs"); Equal(0, worker.ExitCode, "Tray cleanup finished normally"); }
                    Check(!File.Exists(ready + ".settings.json"), "Replacing open settings does not save unapplied edits");
                }
                finally { StopWorker(worker, ready); foreach (string path in new[] { ready, ready + ".exit", ready + ".exited" }) if (File.Exists(path)) File.Delete(path); }
            }
            Run(TestSameVersionSettings);
        }
        private static void TestSameVersionSettings()
        {
            string name = @"Local\L-Switcher-Same-Version-Test-" + Guid.NewGuid().ToString("N"), ready = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "same-version-" + Guid.NewGuid().ToString("N"));
            const string version = "release:v2.0.0";
            using (var worker = StartInstanceWorker("--instance-tray", name, ready, version))
            {
                try
                {
                    WaitForReady(worker, ready);
                    using (var same = new SingleInstance(name))
                    using (var activate = EventWaitHandle.OpenExisting(name + "-Open"))
                        Check(!same.Acquire(buildIdentity: version, activateExisting: () => activate.Set()), "Same version forwards activation to the running tray instance");
                    var elapsed = Stopwatch.StartNew();
                    while (elapsed.ElapsedMilliseconds < 5000) { worker.Refresh(); if (worker.MainWindowTitle == "L-Switcher Settings") break; Thread.Sleep(50); }
                    Check(!worker.HasExited && worker.MainWindowTitle == "L-Switcher Settings", "Same version actually opens settings in the existing process");
                    using (var replacement = new SingleInstance(name)) { replacement.Acquire(buildIdentity: "release:v1.0.0"); replacement.Ready(); }
                    Check(worker.WaitForExit(5000) && worker.ExitCode == 0, "A different version replaces the running instance even when its tag is older");
                }
                finally
                {
                    StopWorker(worker, ready);
                    foreach (string path in new[] { ready, ready + ".exit", ready + ".exited", ready + ".settings.json", ready + ".settings.json.bak" }) if (File.Exists(path)) File.Delete(path);
                }
            }
        }
    }
}

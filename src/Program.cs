using System;
using System.Threading;
using System.Windows.Forms;

namespace LSwitcher
{
    internal static class Program
    {
        [STAThread] private static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var mutex = new Mutex(false, @"Local\L-Switcher-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value))
            using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\L-Switcher-Open-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value))
            {
                bool owner;
                try { owner = mutex.WaitOne(0); } catch (AbandonedMutexException) { owner = true; }
                if (!owner) { activate.Set(); return; }
                try { using (var context = new TrayApplication(activate)) Application.Run(context); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "L-Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}

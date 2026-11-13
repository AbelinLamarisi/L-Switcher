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
            try
            {
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                using (var instance = new SingleInstance(@"Local\L-Switcher-" + user))
                using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\L-Switcher-Open-" + user))
                {
                    instance.Acquire(LegacyInstance.RequestShutdown);
                    using (var context = new TrayApplication(activate, shutdownRequested: () => instance.ShutdownRequested))
                    {
                        instance.Ready();
                        Application.Run(context);
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "L-Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}

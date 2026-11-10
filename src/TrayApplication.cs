using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LSwitcher
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly SettingsStore store;
        private Settings settings;
        private readonly NotifyIcon tray;
        private readonly HotkeyEngine engine;
        private readonly SelectionConversion conversion = new SelectionConversion();
        private readonly Control dispatcher = new Control();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
        private readonly EventWaitHandle activation;
        private readonly ToolStripMenuItem pause;
        private SettingsForm form;
        private string lastIconState;
        private Icon icon;
        private System.Collections.Generic.List<KeyboardLayout> installed = Layouts.Installed();
        private int refresh;
        internal TrayApplication(EventWaitHandle activation, SettingsStore settingsStore = null)
        {
            store = settingsStore ?? new SettingsStore();
            this.activation = activation; bool firstLaunch = !store.Exists; settings = store.Load();
            dispatcher.CreateControl(); engine = new HotkeyEngine(settings);
            engine.LayoutRequested += (binding, target) => dispatcher.BeginInvoke((Action)(() =>
            {
                if (target != Native.GetForegroundWindow() || engine.Paused || engine.Excluded(target)) return;
                var layout = Layouts.Installed().FirstOrDefault(l => l.Id == binding.LayoutId);
                if (layout != null) Native.PostMessage(target, 0x50, IntPtr.Zero, layout.Handle);
            }));
            engine.ConversionRequested += target => dispatcher.BeginInvoke((Action)(() => { if (!engine.Paused && !engine.Excluded(target)) conversion.Run(target, settings.Clone()); }));
            engine.CaseRequested += (mode, target) => dispatcher.BeginInvoke((Action)(() => { if (!engine.Paused && !engine.Excluded(target)) conversion.RunCase(target, mode); }));
            engine.PauseRequested += () => dispatcher.BeginInvoke((Action)TogglePause);
            conversion.Notice += message => { tray.ShowBalloonTip(4000, "L-Switcher", message, ToolTipIcon.Info); };
            var menu = new ContextMenuStrip(); menu.Items.Add("Settings…", null, (s, e) => OpenSettings());
            pause = new ToolStripMenuItem("Pause hotkeys", null, (s, e) => TogglePause()); menu.Items.Add(pause);
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit", null, (s, e) => ExitThread());
            tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };
            timer.Tick += (s, e) => { if (activation.WaitOne(0)) OpenSettings(); UpdateTray(); }; timer.Start(); UpdateTray();
            if (firstLaunch || store.LoadWarning != null) dispatcher.BeginInvoke((Action)(() => { OpenSettings(); if (store.LoadWarning != null) MessageBox.Show(form, store.LoadWarning, "L-Switcher", MessageBoxButtons.OK, MessageBoxIcon.Warning); }));
        }
        private void TogglePause() { engine.Paused = !engine.Paused; engine.Reset(); UpdateTray(); }
        private void OpenSettings()
        {
            if (form != null) { if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal; form.Show(); form.Activate(); return; }
            engine.Reset();
            form = new SettingsForm(settings, engine, value =>
            {
                // Roll startup back if the durable settings write fails.
                SettingsStore.SetStartup(value.StartWithWindows);
                try { store.Save(value); } catch { SettingsStore.SetStartup(settings.StartWithWindows); throw; }
                settings = value.Clone(); engine.Settings = settings; engine.Reset();
            });
            engine.SettingsWindow = form.Handle;
            form.FormClosed += (s, e) => { form.Dispose(); form = null; engine.Capture = null; engine.SettingsWindow = IntPtr.Zero; engine.Reset(); };
            form.Show(); form.Activate();
        }
        private void UpdateTray()
        {
            if (refresh++ % 8 == 0) { installed = Layouts.Installed(); engine.AvailableLayouts = new System.Collections.Generic.HashSet<string>(installed.Select(l => l.Id)); }
            IntPtr active = Layouts.ForegroundLayout(); var layout = installed.FirstOrDefault(l => l.Handle == active);
            string label = layout?.Label ?? "??", name = layout?.Name ?? "Unknown layout";
            bool excluded = engine.Excluded(Native.GetForegroundWindow()), marked = excluded || engine.Paused;
            string status = excluded ? " — Hotkeys disabled for this app" : engine.Paused ? " — Hotkeys paused" : "";
            string tooltip = name + status; if (tooltip.Length > 63) tooltip = name.Substring(0, Math.Max(0, 62 - status.Length)) + "…" + status;
            tray.Text = tooltip; pause.Text = engine.Paused ? "Resume hotkeys" : "Pause hotkeys";
            Color labelColor = settings.LabelColor(layout?.Id);
            string state = (layout?.Id ?? "") + label + marked + labelColor.ToArgb(); if (state == lastIconState) return; lastIconState = state;
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font(SystemFonts.MessageBoxFont.FontFamily, 18, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.Clear(Color.Transparent);
                using (var background = new SolidBrush(SystemColors.WindowText)) graphics.FillRectangle(background, 0, 2, 32, 27);
                using (var foreground = new SolidBrush(labelColor)) graphics.DrawString(label, font, foreground, new RectangleF(0, 4, 32, 26), new StringFormat { Alignment = StringAlignment.Center });
                if (marked) { graphics.FillRectangle(Brushes.Orange, 21, 21, 4, 10); graphics.FillRectangle(Brushes.Orange, 27, 21, 4, 10); }
                IntPtr handle = bitmap.GetHicon(); Icon replacement; try { replacement = (Icon)Icon.FromHandle(handle).Clone(); } finally { Native.DestroyIcon(handle); }
                tray.Icon = replacement; var old = icon; icon = replacement; old?.Dispose();
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Stop(); timer.Dispose(); engine.Dispose(); form?.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); icon?.Dispose(); dispatcher.Dispose(); }
            base.Dispose(disposing);
        }
    }
}

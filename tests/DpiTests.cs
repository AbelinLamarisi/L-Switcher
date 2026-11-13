using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LSwitcher
{
    internal static partial class Tests
    {
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in Descendants(child)) yield return descendant; }
        }
        private static void ChangeDpi(DpiForm form, int dpi)
        {
            float scale = (float)dpi / form.LayoutDpi;
            var rect = new Native.RECT { left = form.Left, top = form.Top, right = form.Left + (int)(form.Width * scale), bottom = form.Top + (int)(form.Height * scale) };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.RECT)));
            try { Marshal.StructureToPtr(rect, pointer, false); SendMessage(form.Handle, 0x02e0, (IntPtr)(dpi | (dpi << 16)), pointer); }
            finally { Marshal.FreeHGlobal(pointer); }
            form.PerformLayout(); form.Refresh(); Application.DoEvents(); Equal(dpi, form.LayoutDpi, "WM_DPICHANGED applied");
        }
        private static void CheckButtons(Form form, int dpi)
        {
            foreach (var button in Descendants(form).OfType<Button>().Where(b => b.Visible))
            {
                var preferred = button.GetPreferredSize(Size.Empty);
                Check(button.Height >= preferred.Height && button.Width >= preferred.Width, dpi + " DPI button fits text: " + button.Text);
                Check(button.Top >= button.Parent.Padding.Top && button.Bottom <= button.Parent.ClientSize.Height - button.Parent.Padding.Bottom, dpi + " DPI button fits its row: " + button.Text);
            }
        }
        private static void TestDpi()
        {
            Check(AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), (IntPtr)(-4)), "Manifest activates PerMonitorV2");
            using (var engine = new HotkeyEngine(new Settings()))
            using (var form = new SettingsForm(new Settings(), engine, settings => { throw new Exception("DPI smoke must not save"); }))
            {
                engine.SettingsWindow = form.Handle;
                Exception failure = null;
                form.Shown += (s, e) =>
                {
                    try
                    {
                        var tabs = form.Controls.OfType<TabControl>().Single();
                        ChangeDpi(form, form.LayoutDpi);
                        CheckButtons(form, form.LayoutDpi);
                        form.Location = new Point(Screen.FromControl(form).WorkingArea.Left + 10, Screen.FromControl(form).WorkingArea.Top + 10);
                        foreach (int dpi in new[] { 96, 120, 144, 192, 240, 288, 192, 96 })
                        {
                            ChangeDpi(form, dpi);
                            foreach (TabPage page in tabs.TabPages)
                            {
                                tabs.SelectedTab = page; page.PerformLayout(); form.PerformLayout(); form.Refresh(); Application.DoEvents(); CheckButtons(form, dpi);
                                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dpi-" + dpi + "-" + page.Text.ToLowerInvariant() + ".png")); }
                            }
                            tabs.SelectedIndex = 0;
                            ((Button)form.Controls.Find("AddLayout", true).Single()).PerformClick();
                            var bindings = tabs.TabPages[0].Controls.OfType<FlowLayoutPanel>().Single(p => p.Dock == DockStyle.Fill);
                            var added = bindings.Controls[bindings.Controls.Count - 1];
                            Equal((int)Math.Round(205 * dpi / 96.0, MidpointRounding.AwayFromZero), Descendants(added).OfType<ShortcutBox>().Single().Width, "New bindings use current DPI");
                            CheckButtons(form, dpi);
                        }
                        var area = Screen.FromControl(form).WorkingArea; Check(form.Height <= area.Height && form.Width <= area.Width, "Window fits monitor work area");
                        // Exercise the real modal exclusion editor, not a copy of its layout.
                        tabs.SelectedIndex = 2;
                        Exception dialogFailure = null;
                        using (var timer = new Timer { Interval = 100 })
                        {
                            timer.Tick += (sender, args) =>
                            {
                                var dialog = Application.OpenForms.OfType<DpiForm>().FirstOrDefault(f => f.Text == "Add application exclusion");
                                if (dialog == null) return;
                                timer.Stop();
                                try
                                {
                                    foreach (int dpi in new[] { 96, 120, 144, 192, 288 })
                                    {
                                        ChangeDpi(dialog, dpi); CheckButtons(dialog, dpi);
                                        using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dpi-" + dpi + "-exclusion-dialog.png")); }
                                    }
                                }
                                catch (Exception ex) { dialogFailure = ex; }
                                finally { dialog.Close(); }
                            };
                            timer.Start();
                            tabs.TabPages[2].Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == "Add").PerformClick();
                        }
                        if (dialogFailure != null) throw dialogFailure;
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { form.Close(); }
                };
                Application.Run(form); if (failure != null) throw failure;
            }
        }
    }
}

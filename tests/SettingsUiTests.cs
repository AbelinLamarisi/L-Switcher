using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace LSwitcher
{
    internal static partial class Tests
    {
        private static bool DialogKey(Control control, Keys key)
        {
            return (bool)control.GetType().GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control, new object[] { key });
        }
        private static void Record(ShortcutBox field, int key, int modifier = 0)
        {
            field.BeginRecording(); if (modifier != 0) field.CaptureKey(modifier, false);
            field.CaptureKey(key, false); field.CaptureKey(key, true); if (modifier != 0) field.CaptureKey(modifier, true);
        }
        private static void TestShortcutRecording()
        {
            using (var engine = new HotkeyEngine(new Settings()))
            using (var field = new ShortcutBox(engine, new Shortcut { Key = (int)Keys.F24 }))
            {
                int changes = 0, samples = 0; field.ValueChanged += () => changes++; field.Tested += (s, ms) => samples++;
                Check(!field.CaptureKey((int)Keys.Tab, false), "Idle field does not capture Tab"); Equal((int)Keys.F24, field.Value.Key, "Idle navigation keeps assignment");
                Check(DialogKey(field, Keys.Enter), "Enter starts keyboard-only recording"); Check(field.IsRecording && field.Text == "Press a shortcut…", "Recording prompt shown");
                field.CaptureKey((int)Keys.Enter, false); field.CaptureKey((int)Keys.Enter, true);
                Check(field.IsRecording && field.Value.Key == (int)Keys.F24, "Activation Enter and repeats are not recorded");
                field.CaptureKey((int)Keys.Tab, false); field.CaptureKey((int)Keys.Tab, true);
                Equal((int)Keys.Tab, field.Value.Key, "Tab can be assigned during explicit recording"); Check(!field.IsRecording && engine.Capture == null, "Completed capture detaches hook handler");
                Equal(1, changes, "One completed assignment change"); Equal(1, samples, "One timing sample");
                field.BeginRecording(); field.CaptureKey(0xa2, false); field.CaptureKey((int)Keys.K, false); field.CaptureKey((int)Keys.Escape, false);
                Equal((int)Keys.Tab, field.Value.Key, "Esc restores prior assignment"); Check(!field.IsRecording && field.Text == field.Value.ToString(), "Esc exits recording and restores display"); Equal(1, changes, "Canceled capture does not change draft"); Equal(1, samples, "Canceled capture has no tester result");
                Record(field, (int)Keys.Tab, 0xa0); Equal((int)Keys.Shift, field.Value.Modifiers, "Shift+Tab is captured when recording");
                field.BeginRecording(); field.CaptureKey((int)Keys.Enter, false); field.CaptureKey((int)Keys.Enter, true); Equal((int)Keys.Enter, field.Value.Key, "Enter itself remains assignable after recording starts");
                field.BeginRecording(); field.CaptureKey(0xa2, false); field.CaptureKey(0xa0, false); field.CaptureKey(0xa0, true); field.CaptureKey(0xa2, true);
                Check(field.IsRecording && field.Value.Key == (int)Keys.Enter, "A modifier used in a modifier chord is not mistaken for a standalone tap"); field.CancelRecording();
            }
        }
        private static T Find<T>(Control form, string name) where T : Control { return (T)form.Controls.Find(name, true).Single(); }
        private static Button ClearFor(ShortcutBox field) { return field.Parent.Controls.OfType<Button>().Single(b => b.Text == "Clear"); }
        private static void SavePreview(Form form, string name)
        {
            using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".png")); }
        }
        private static void TestSettingsImprovements()
        {
            var layout = Layouts.Installed().First();
            var settings = new Settings { FirstLayout = "test-first", SecondLayout = "test-second", ConversionShortcut = new Shortcut { Key = (int)Keys.F24 }, PauseShortcut = new Shortcut { Key = (int)Keys.F23 } };
            settings.Bindings.Add(new LayoutBinding { LayoutId = layout.Id, Shortcut = new Shortcut { Key = (int)Keys.K, Modifiers = (int)Keys.Control } });
            settings.LabelColors.Add(new LayoutLabelColor { LayoutId = layout.Id, Argb = Color.Red.ToArgb() });
            settings.Exclusions.Add(new Exclusion { Executable = "notepad.exe" });
            using (var engine = new HotkeyEngine(settings))
            {
                int saves = 0, executions = 0; bool failSave = false; string message = null, title = null;
                engine.LayoutRequested += (b, w) => executions++; engine.ConversionRequested += w => executions++; engine.PauseRequested += () => executions++;
                using (var form = new SettingsForm(settings, engine, result => { if (failSave) throw new IOException("Test save failure"); result.Validate(); saves++; }, (t, m) => { title = t; message = m; }))
                {
                    engine.SettingsWindow = form.Handle; Exception failure = null;
                    form.Shown += (s, e) =>
                    {
                        try
                        {
                            FocusTestWindow(form.Handle);
                            var tabs = Find<TabControl>(form, "SettingsTabs"); var apply = Find<Button>(form, "ApplySettings");
                            var binding = Find<ShortcutBox>(form, "LayoutShortcut"); var conversion = Find<ShortcutBox>(form, "ConversionShortcut");
                            Equal(FormBorderStyle.FixedDialog, form.FormBorderStyle, "Settings has fixed size"); Check(!form.MaximizeBox, "Settings cannot maximize"); Check(!apply.Enabled, "Clean draft disables Apply");
                            Check(form.ApplyChanges(), "OK can persist initially unchanged settings"); Equal(1, saves, "Clean settings are saved to complete first-launch setup");
                            binding.Focus(); Check(binding.Focused && !binding.IsRecording && engine.Capture == null, "Focus alone leaves assignment idle");
                            Check(DialogKey(binding, Keys.Tab), "Tab is handled by normal dialog navigation"); Check(!binding.Focused && engine.Capture == null, "Tab leaves idle field without capture");
                            Check(DialogKey(ClearFor(binding), Keys.Shift | Keys.Tab), "Shift+Tab navigates backward"); Check(binding.Focused && !binding.IsRecording, "Backward navigation focuses idle assignment");
                            DialogKey(binding, Keys.Enter); binding.CaptureKey((int)Keys.Enter, true); Check(binding.IsRecording, "Keyboard Enter starts recording in an assignment");
                            binding.CaptureKey((int)Keys.J, false); binding.CaptureKey((int)Keys.Escape, false); Equal((int)Keys.K, binding.Value.Key, "Cancel preserves prior assignment"); Check(form.Visible && !apply.Enabled, "Recording Esc keeps settings open and clean");
                            SendMessage(binding.Handle, 0x201, (IntPtr)1, (IntPtr)(4 | (4 << 16))); SendMessage(binding.Handle, 0x202, IntPtr.Zero, (IntPtr)(4 | (4 << 16)));
                            Check(binding.IsRecording, "Mouse click starts recording"); binding.CancelRecording();
                            var startup = Find<CheckBox>(form, "Startup"); startup.Checked = !startup.Checked; Check(apply.Enabled, "Startup edit enables Apply"); startup.Checked = !startup.Checked; Check(!apply.Enabled, "Reverting startup edit disables Apply");
                            var tap = Find<NumericUpDown>(form, "TapLimit"); tap.Value += 50; Check(apply.Enabled, "Tap limit edit enables Apply"); tap.Value -= 50; Check(!apply.Enabled, "Reverting tap limit disables Apply");
                            Record(conversion, (int)Keys.K, 0xa2); Check(apply.Enabled, "Shortcut edit enables Apply"); tabs.SelectedIndex = 3;
                            Check(!form.ApplyChanges(), "Conflicting save is rejected"); Check(title == "Conflicting shortcuts" && message.Contains(binding.Value.ToString()) && message.Contains("Activate " + layout.Name) && message.Contains("Convert keyboard layout"), "Conflict reports shortcut and both actions");
                            Equal(1, saves, "Conflict did not save"); Equal(1, tabs.SelectedIndex, "Conflict selects latest edited field's tab"); Check(conversion.Focused && !conversion.IsRecording, "Conflict focuses idle conversion field after message closes");
                            Record(binding, (int)Keys.K, 0xa2); tabs.SelectedIndex = 3;
                            Check(!form.ApplyChanges(), "Reverse-order conflict is rejected"); Equal(0, tabs.SelectedIndex, "Most recent layout edit takes focus"); Check(binding.Focused, "Conflicting layout field focused"); SavePreview(form, "settings-conflict-focus");
                            Record(conversion, (int)Keys.F24); Check(!apply.Enabled, "Resolving back to original draft disables Apply");
                            var upper = Find<ShortcutBox>(form, "UppercaseShortcut"); Record(upper, (int)Keys.F22); failSave = true;
                            Check(!form.ApplyChanges() && apply.Enabled && title == "Settings could not be saved", "Failed save keeps dirty draft"); failSave = false;
                            Check(form.ApplyChanges() && !apply.Enabled, "Successful Apply resets dirty state"); Equal(2, saves, "Only successful saves counted"); upper.ClearShortcut(); Check(apply.Enabled, "Clear changes last applied draft"); Record(upper, (int)Keys.F22); Check(!apply.Enabled, "Reverting shortcut to last applied value disables Apply");
                            tabs.SelectedIndex = 1; var first = Find<ComboBox>(form, "FirstLayout"); var second = Find<ComboBox>(form, "SecondLayout"); var direction = Find<ComboBox>(form, "ConversionDirection"); var hint = Find<Label>(form, "WordDetectionHint");
                            var deselect = Find<CheckBox>(form, "DeselectAfterConversion"); Check(!deselect.Checked, "Deselect checkbox defaults off");
                            Equal("Keyboard layout conversion", deselect.Parent.Parent.Text, "Deselect checkbox belongs to layout conversion group");
                            deselect.Checked = true; Check(apply.Enabled && form.ReadDraft().DeselectAfterLayoutConversion, "Deselect checkbox updates draft and Apply"); deselect.Checked = false; Check(!apply.Enabled, "Reverting deselect preference restores clean draft");
                            Equal(first.SelectedItem + " → " + second.SelectedItem, direction.Items[1].ToString(), "Forward direction names chosen layouts"); Equal(second.SelectedItem + " → " + first.SelectedItem, direction.Items[2].ToString(), "Reverse direction names chosen layouts");
                            direction.SelectedIndex = 1; Check(!hint.Visible && apply.Enabled, "One-way direction hides word detection hint"); direction.SelectedIndex = 0; Check(hint.Visible && !apply.Enabled, "Swap restores explanation and clean state");
                            object original = first.SelectedItem; first.SelectedIndex = 0; Check(direction.Items[1].ToString().StartsWith(first.SelectedItem.ToString()), "Layout change refreshes direction names"); first.SelectedItem = original; Check(!apply.Enabled, "Reverting layout selector disables Apply");
                            original = second.SelectedItem; second.SelectedIndex = 0; Check(direction.Items[2].ToString().StartsWith(second.SelectedItem.ToString()), "Second layout also refreshes direction names"); second.SelectedItem = original; Check(!apply.Enabled, "Reverting second selector disables Apply");
                            tabs.SelectedIndex = 3; var tester = Find<ShortcutBox>(form, "ShortcutTest"); var feedback = Find<Label>(form, "ShortcutTestFeedback"); string idle = feedback.Text;
                            tester.Focus(); Check(!tester.IsRecording && !ClearFor(tester).Enabled, "Idle tester neither captures nor allows empty Clear");
                            Check(DialogKey(tester, Keys.Tab) && !tester.Focused && engine.Capture == null, "Tab leaves idle tester without recording");
                            var next = Descendants(form).First(c => c.Focused); Check(DialogKey(next, Keys.Shift | Keys.Tab) && tester.Focused && !tester.IsRecording, "Shift+Tab returns to idle tester");
                            DialogKey(tester, Keys.Enter); tester.CaptureKey((int)Keys.Enter, true); tester.CaptureKey((int)Keys.F23, false); tester.CaptureKey((int)Keys.F23, true);
                            Check(feedback.Text.Contains("Pause / Resume") && feedback.Text.Contains("Short tap"), "Keyboard-only tester reports timing and assigned action"); Check(!apply.Enabled && ClearFor(tester).Enabled, "Tester changes do not dirty settings");
                            ClearFor(tester).PerformClick(); Equal(idle, feedback.Text, "Clearing tester resets action and timing feedback"); Check(!ClearFor(tester).Enabled && !apply.Enabled, "Empty tester Clear is disabled"); Equal(0, executions, "Testing did not execute any assignment");
                            tester.Focus(); tester.BeginRecording(); tester.CaptureKey((int)Keys.F24, false); tap.Focus(); Check(!tester.IsRecording && tester.Value.Key == 0 && engine.Capture == null && feedback.Text == idle, "Losing focus cancels pending tester capture without stale feedback");
                            tabs.SelectedIndex = 2; var list = Find<ListBox>(form, "Exclusions"); var remove = Find<Button>(form, "RemoveExclusion"); Check(!remove.Enabled, "Exclusion Remove is disabled without selection"); list.SelectedIndex = 0; Check(remove.Enabled && !apply.Enabled, "Selecting an exclusion enables Remove without dirtying settings"); remove.PerformClick(); Check(list.Items.Count == 0 && !remove.Enabled && apply.Enabled, "Removing selected exclusion updates button and draft");
                            Exception modalFailure = null;
                            using (var timer = new Timer { Interval = 100 })
                            {
                                timer.Tick += (a, b) =>
                                {
                                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Add application exclusion"); if (dialog == null) return; timer.Stop();
                                    try { dialog.Controls.OfType<TextBox>().Single().Text = "notepad.exe"; dialog.Controls.OfType<Button>().Single(x => x.Text == "Add").PerformClick(); }
                                    catch (Exception ex) { modalFailure = ex; dialog.Close(); }
                                };
                                timer.Start(); tabs.TabPages[2].Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == "Add").PerformClick();
                            }
                            if (modalFailure != null) throw modalFailure;
                            Check(!apply.Enabled && remove.Enabled, "Re-adding original exclusion restores clean draft and selects it");
                            tabs.SelectedIndex = 0; form.Controls.Find("LayoutColorPreview", true).Single().Parent.Controls.OfType<Button>().Single(b => b.Text == "Default").PerformClick();
                            Check(apply.Enabled, "Resetting a custom color dirties settings"); Check(form.ApplyChanges() && !apply.Enabled, "Color reset can be applied");
                            var add = Find<Button>(form, "AddLayout"); Check(add.Width < form.ClientSize.Width / 2, "Add layout button is compact");
                            for (int i = 0; i < 25; i++) add.PerformClick();
                            var latest = form.Controls.Find("BindingLayout", true).Cast<ComboBox>().Last(); var table = (ScrollableControl)latest.Parent.Parent;
                            Check(latest.Focused && engine.Capture == null, "Added row focuses layout selector, not recording"); Check(table.AutoScrollPosition.Y < 0, "Adding beyond viewport scrolls table");
                            var bounds = table.RectangleToClient(latest.RectangleToScreen(latest.ClientRectangle)); Check(bounds.Top >= 0 && bounds.Bottom <= table.ClientSize.Height, "Added layout selector is in view"); SavePreview(form, "settings-added-layout");
                        }
                        catch (Exception ex) { failure = ex; }
                        finally { if (!form.IsDisposed) form.Close(); }
                    };
                    Application.Run(form); if (failure != null) throw failure;
                }
            }
        }
        private static void TestTrayStatuses()
        {
            var icons = new System.Collections.Generic.HashSet<string>();
            foreach (bool paused in new[] { false, true }) foreach (bool excluded in new[] { false, true })
            {
                string tip = TrayPresentation.Tooltip("English (United States)", paused, excluded);
                Equal(paused, tip.Contains("Hotkeys paused"), "Tooltip accurately reports manual pause"); Equal(excluded, tip.Contains("Hotkeys disabled for this app"), "Tooltip accurately reports application exclusion");
                string longTip = TrayPresentation.Tooltip(new string('X', 200), paused, excluded); Check(longTip.Length <= 63, "Tray tooltip fits Windows limit");
                Equal(paused, longTip.Contains("Hotkeys paused"), "Long name keeps manual pause explanation"); Equal(excluded, longTip.Contains("Hotkeys disabled for this app"), "Long name keeps exclusion explanation");
                using (var icon = TrayPresentation.Draw("EN", Color.White, paused, excluded))
                using (var stream = new MemoryStream())
                { icon.Save(stream, System.Drawing.Imaging.ImageFormat.Png); icons.Add(Convert.ToBase64String(stream.ToArray())); icon.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray-" + paused + "-" + excluded + ".png")); }
            }
            Equal(4, icons.Count, "All four combinations have distinct tray marks");
        }
    }
}

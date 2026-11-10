using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace LSwitcher
{
    internal sealed class ShortcutBox : TextBox
    {
        private readonly HotkeyEngine engine;
        private readonly HashSet<int> pressed = new HashSet<int>();
        private readonly Dictionary<int, long> starts = new Dictionary<int, long>();
        private bool combined;
        internal Shortcut Value = new Shortcut();
        internal event Action<Shortcut, long> Tested;
        internal ShortcutBox(HotkeyEngine engine, Shortcut value)
        {
            this.engine = engine; Value = value ?? new Shortcut(); ReadOnly = true; Width = 230; Text = Value.ToString();
            GotFocus += (s, e) => { pressed.Clear(); starts.Clear(); combined = false; engine.Capture = CaptureKey; BackColor = SystemColors.Info; };
            LostFocus += (s, e) => { if (engine.Capture == CaptureKey) engine.Capture = null; BackColor = SystemColors.Window; };
        }
        internal bool CaptureKey(int key, bool up)
        {
            if (!up)
            {
                if (!pressed.Add(key)) return true;
                starts[key] = Stopwatch.GetTimestamp();
                if (!HotkeyEngine.IsModifier(key))
                {
                    Value = new Shortcut { Key = key, Modifiers = pressed.Aggregate(0, (flags, k) => flags | HotkeyEngine.ModifierFlag(k)) }; combined = true;
                }
            }
            else
            {
                bool standalone = HotkeyEngine.IsModifier(key) && !combined && pressed.Count == 1;
                if (standalone) Value = new Shortcut { Key = key };
                long started;
                if ((standalone || !HotkeyEngine.IsModifier(key)) && starts.TryGetValue(key, out started)) Tested?.Invoke(Value, (Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency);
                starts.Remove(key);
                pressed.Remove(key); if (pressed.Count == 0) combined = false;
            }
            Text = Value.ToString(); return true;
        }
        internal void ClearShortcut() { Value = new Shortcut(); Text = ""; }
        protected override void Dispose(bool disposing) { if (disposing && engine.Capture == CaptureKey) engine.Capture = null; base.Dispose(disposing); }
    }
    internal sealed class SettingsForm : DpiForm
    {
        private readonly Settings draft;
        private readonly HotkeyEngine engine;
        private readonly Action<Settings> save;
        private readonly List<KeyboardLayout> layouts = Layouts.Installed();
        private readonly FlowLayoutPanel bindingRows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        private readonly List<Tuple<ComboBox, ShortcutBox, Panel>> rows = new List<Tuple<ComboBox, ShortcutBox, Panel>>();
        private ComboBox first, second, direction;
        private ShortcutBox conversion;
        private ShortcutBox pauseShortcut, uppercase, lowercase, invertCase;
        private NumericUpDown tap;
        private CheckBox startup;
        private readonly ListBox exclusions = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        internal SettingsForm(Settings settings, HotkeyEngine engine, Action<Settings> save)
        {
            draft = settings.Clone(); this.engine = engine; this.save = save;
            Text = "L-Switcher Settings"; ClientSize = new Size(800, 650); MinimumSize = new Size(780, 580);
            StartPosition = FormStartPosition.CenterScreen;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var layoutsTab = new TabPage("Layouts") { Padding = new Padding(10) };
            bindingRows.BackColor = SystemColors.Window;
            bindingRows.Padding = new Padding(8);
            var header = FitRow(700); header.Padding = new Padding(4, 8, 4, 8); header.BackColor = SystemColors.Control;
            header.Controls.AddRange(new Control[] {
                new Label { Text = "Keyboard layout", Width = 290, Height = 22, TextAlign = ContentAlignment.MiddleLeft },
                new Label { Text = "Activation shortcut", Width = 205, Height = 22, TextAlign = ContentAlignment.MiddleLeft },
                new Label { Text = "Actions", Width = 145, Height = 22, TextAlign = ContentAlignment.MiddleLeft }
            });
            bindingRows.Controls.Add(header);
            layoutsTab.Controls.Add(bindingRows);
            var add = ButtonFor("Add"); add.Dock = DockStyle.Bottom; add.Click += (s, e) => AddBinding(new LayoutBinding()); layoutsTab.Controls.Add(add);
            var hint = new Label { Text = "Click a shortcut field, then press a key or combination. Standalone modifiers trigger on a short release.", Dock = DockStyle.Top, Height = 40 }; layoutsTab.Controls.Add(hint);
            layoutsTab.Controls.Add(ColorSettings());
            tabs.TabPages.Add(layoutsTab); foreach (var binding in draft.Bindings) AddBinding(binding);
            var conversionTab = new TabPage("Conversion") { Padding = new Padding(14) };
            var conversionPanel = Vertical(); first = LayoutCombo(draft.FirstLayout); second = LayoutCombo(draft.SecondLayout);
            var layoutGroup = SettingsGroup("Keyboard layout conversion");
            layoutGroup.Controls.Add(FieldRow("First layout", first));
            layoutGroup.Controls.Add(FieldRow("Second layout", second));
            direction = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
            direction.Items.AddRange(new object[] { "Swap both ways", "First → Second", "Second → First" }); direction.SelectedIndex = (int)draft.Direction;
            layoutGroup.Controls.Add(FieldRow("Direction", direction));
            conversion = new ShortcutBox(engine, draft.ConversionShortcut);
            layoutGroup.Controls.Add(NamedShortcutRow("Shortcut", conversion));
            layoutGroup.Controls.Add(new Label { AutoSize = false, Width = 660, Height = 58, Margin = new Padding(3, 10, 3, 3), ForeColor = SystemColors.GrayText, Text = "Swap both ways identifies each word’s layout; punctuation follows the preceding word. Ambiguous words follow the preceding direction (first layout initially)." });
            conversionPanel.Controls.Add(layoutGroup.Parent);
            var caseGroup = SettingsGroup("Selection case");
            uppercase = new ShortcutBox(engine, draft.UppercaseShortcut); lowercase = new ShortcutBox(engine, draft.LowercaseShortcut); invertCase = new ShortcutBox(engine, draft.InvertCaseShortcut);
            caseGroup.Controls.Add(NamedShortcutRow("UPPERCASE", uppercase));
            caseGroup.Controls.Add(NamedShortcutRow("lowercase", lowercase));
            caseGroup.Controls.Add(NamedShortcutRow("Invert case", invertCase));
            conversionPanel.Controls.Add(caseGroup.Parent);
            conversionPanel.Controls.Add(new Label { Width = 700, Height = 40, Margin = new Padding(3, 8, 3, 3), ForeColor = SystemColors.GrayText, Text = "All text commands require an editable selection. The active keyboard layout remains unchanged." });
            conversionTab.Controls.Add(conversionPanel); tabs.TabPages.Add(conversionTab);
            var exclusionsTab = new TabPage("Exclusions") { Padding = new Padding(12) }; exclusionsTab.Controls.Add(exclusions);
            var exclusionButtons = FitRow(); exclusionButtons.Dock = DockStyle.Bottom;
            var addExclusion = ButtonFor("Add"); var removeExclusion = ButtonFor("Remove");
            addExclusion.Click += (s, e) => AddExclusion(); removeExclusion.Click += (s, e) => { if (exclusions.SelectedItem != null) exclusions.Items.Remove(exclusions.SelectedItem); };
            exclusionButtons.Controls.AddRange(new Control[] { addExclusion, removeExclusion }); exclusionsTab.Controls.Add(exclusionButtons);
            exclusionsTab.Controls.Add(new Label { Text = "All hotkeys pass through while a matching application is in the foreground.", Dock = DockStyle.Top, Height = 32 });
            foreach (var exclusion in draft.Exclusions) exclusions.Items.Add(exclusion); tabs.TabPages.Add(exclusionsTab);
            var generalTab = new TabPage("General") { Padding = new Padding(14) }; var general = Vertical();
            startup = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = draft.StartWithWindows };
            tap = new NumericUpDown { Minimum = 50, Maximum = 2000, Increment = 50, Value = draft.TapMilliseconds, Width = 150 };
            general.Controls.Add(startup); general.Controls.Add(LabelFor("Short-tap time limit (milliseconds)")); general.Controls.Add(tap);
            pauseShortcut = new ShortcutBox(engine, draft.PauseShortcut); general.Controls.Add(LabelFor("Pause / Resume hotkeys")); general.Controls.Add(ShortcutRow(pauseShortcut));
            general.Controls.Add(LabelFor("Shortcut test — focus here and press a key or combination"));
            var test = new ShortcutBox(engine, new Shortcut()) { Name = "ShortcutTest" }; general.Controls.Add(ShortcutRow(test));
            var feedback = new Label { Name = "ShortcutTestFeedback", Width = 700, Height = 75, Text = "Shows tap/hold and the assigned action from current settings. No action is executed." }; general.Controls.Add(feedback);
            test.Tested += (shortcut, milliseconds) => feedback.Text = shortcut + " — " + (milliseconds <= tap.Value ? "Short tap" : "Hold") + " (" + milliseconds + " ms)\nAssigned action: " + DescribeAction(shortcut) + "\nTest only — action not executed.";
            general.Controls.Add(new Label { Width = 700, Height = 90, Text = "Settings: %LOCALAPPDATA%\\L-Switcher\\settings.json\n\nPause is temporary and resets when the application restarts.\nElevated applications, Remote Desktop, and games are outside prototype support." });
            generalTab.Controls.Add(general); tabs.TabPages.Add(generalTab); Controls.Add(tabs);
            var buttons = FitRow(); buttons.Dock = DockStyle.Bottom; buttons.FlowDirection = FlowDirection.RightToLeft; buttons.Padding = new Padding(8);
            var apply = ButtonFor("Apply"); apply.Click += (s, e) => ApplyChanges();
            var cancel = ButtonFor("Cancel"); cancel.DialogResult = DialogResult.Cancel; cancel.Click += (s, e) => Close();
            var ok = ButtonFor("OK"); ok.Click += (s, e) => { if (ApplyChanges()) Close(); };
            buttons.Controls.AddRange(new Control[] { apply, cancel, ok }); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
        }
        private static FlowLayoutPanel Vertical() { return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }; }
        private static Button ButtonFor(string text, int width = 75) { return new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(width, 28) }; }
        private static FlowLayoutPanel FitRow(int width = 0) { return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(width, 0), WrapContents = false, Margin = Padding.Empty }; }
        private static FlowLayoutPanel SettingsGroup(string title)
        {
            var group = new GroupBox { Text = title, MinimumSize = new Size(710, 0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12) };
            var content = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
            group.Controls.Add(content);
            return content;
        }
        private static Panel FieldRow(string title, Control field)
        {
            var row = FitRow(660); row.Margin = new Padding(0, 3, 0, 3);
            row.Controls.Add(new Label { Text = title, Width = 150, Height = 28, TextAlign = ContentAlignment.MiddleLeft });
            row.Controls.Add(field); return row;
        }
        private Control ColorSettings()
        {
            var group = new GroupBox { Text = "Tray label colors (independent of key assignments)", Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var panel = FitRow(); panel.Dock = DockStyle.Top; panel.WrapContents = true; panel.Padding = new Padding(5, 8, 5, 5); group.Controls.Add(panel);
            var selector = LayoutCombo(null); selector.Name = "LayoutColorLayout";
            foreach (var color in draft.LabelColors)
                if (!selector.Items.Cast<KeyboardLayout>().Any(l => l.Id == color.LayoutId)) selector.Items.Add(new KeyboardLayout { Id = color.LayoutId, Name = "Layout unavailable (" + color.LayoutId + ")", Label = "??" });
            var choose = ButtonFor("Color…", 80); var reset = ButtonFor("Default", 80);
            var preview = new Label { Name = "LayoutColorPreview", Width = 55, Height = 27, BackColor = SystemColors.WindowText, TextAlign = ContentAlignment.MiddleCenter };
            Action update = () => { var layout = selector.SelectedItem as KeyboardLayout; preview.Text = layout?.Label ?? "??"; preview.ForeColor = draft.LabelColor(layout?.Id); choose.Enabled = reset.Enabled = layout != null; };
            selector.SelectedIndexChanged += (s, e) => update();
            choose.Click += (s, e) =>
            {
                var layout = selector.SelectedItem as KeyboardLayout; if (layout == null) return;
                using (var dialog = new ColorDialog { Color = draft.LabelColor(layout.Id), FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { draft.LabelColors.RemoveAll(c => c.LayoutId == layout.Id); draft.LabelColors.Add(new LayoutLabelColor { LayoutId = layout.Id, Argb = dialog.Color.ToArgb() }); update(); }
            };
            reset.Click += (s, e) => { var layout = selector.SelectedItem as KeyboardLayout; if (layout != null) draft.LabelColors.RemoveAll(c => c.LayoutId == layout.Id); update(); };
            panel.Controls.AddRange(new Control[] { selector, choose, reset, preview }); if (selector.Items.Count > 0) selector.SelectedIndex = 0; update(); return group;
        }
        private static Label LabelFor(string text) { return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 10, 3, 5) }; }
        private ComboBox LayoutCombo(string id)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
            foreach (var layout in layouts) box.Items.Add(layout);
            if (!string.IsNullOrEmpty(id) && !layouts.Any(x => x.Id == id)) box.Items.Add(new KeyboardLayout { Id = id, Name = "Layout unavailable (" + id + ")" });
            if (id != null) box.SelectedItem = box.Items.Cast<KeyboardLayout>().FirstOrDefault(x => x.Id == id);
            return box;
        }
        private static Panel ShortcutRow(ShortcutBox box)
        {
            var row = FitRow(350);
            var clear = ButtonFor("Clear", 65); clear.Click += (s, e) => box.ClearShortcut(); row.Controls.Add(box); row.Controls.Add(clear); return row;
        }
        private static Panel NamedShortcutRow(string label, ShortcutBox box)
        {
            var row = FitRow(660); row.Margin = new Padding(0, 3, 0, 3);
            row.Controls.Add(new Label { Text = label, Width = 150, AutoSize = false, Height = 25, TextAlign = ContentAlignment.MiddleLeft }); row.Controls.Add(ShortcutRow(box)); return row;
        }
        private string DescribeAction(Shortcut shortcut)
        {
            var actions = new List<string>();
            foreach (var row in rows) if (row.Item2.Value.Key != 0 && row.Item2.Value.Identity == shortcut.Identity) actions.Add("Activate " + (row.Item1.SelectedItem?.ToString() ?? "Layout unavailable"));
            foreach (var action in new[] { Tuple.Create(conversion, "Convert keyboard layout"), Tuple.Create(pauseShortcut, "Pause / Resume"), Tuple.Create(uppercase, "Uppercase selection"), Tuple.Create(lowercase, "Lowercase selection"), Tuple.Create(invertCase, "Invert selection case") })
                if (action.Item1.Value.Key != 0 && action.Item1.Value.Identity == shortcut.Identity) actions.Add(action.Item2);
            return actions.Count == 0 ? "None" : string.Join("; ", actions);
        }
        private void AddBinding(LayoutBinding binding)
        {
            var panel = FitRow(700); panel.Margin = new Padding(0, 0, 0, 1); panel.Padding = new Padding(4, 8, 4, 8);
            var layout = LayoutCombo(binding.LayoutId); layout.Width = 290;
            var shortcut = new ShortcutBox(engine, binding.Shortcut) { Width = 205 };
            var clear = ButtonFor("Clear", 65); clear.Click += (s, e) => shortcut.ClearShortcut();
            var remove = ButtonFor("Remove");
            var row = Tuple.Create<ComboBox, ShortcutBox, Panel>(layout, shortcut, panel); rows.Add(row);
            remove.Click += (s, e) => { rows.Remove(row); bindingRows.Controls.Remove(panel); panel.Dispose(); RefreshBindingColors(); };
            panel.Controls.AddRange(new Control[] { layout, shortcut, clear, remove }); AddDpiControl(bindingRows, panel); RefreshBindingColors();
        }
        private void RefreshBindingColors()
        {
            for (int i = 0; i < rows.Count; i++)
                rows[i].Item3.BackColor = i % 2 == 0 ? SystemColors.Window : SystemColors.Control;
        }
        private void AddExclusion()
        {
            using (var dialog = new DpiForm { Text = "Add application exclusion", ClientSize = new Size(530, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var path = new TextBox { Left = 12, Top = 15, Width = 410 }; var browse = ButtonFor("Browse…", 85); browse.Location = new Point(430, 13);
                var full = new CheckBox { Text = "Match full executable path (otherwise match executable name)", Left = 12, Top = 50, Width = 500 };
                var add = ButtonFor("Add", 85); add.Location = new Point(430, 100);
                browse.Click += (s, e) => { using (var file = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true }) if (file.ShowDialog(dialog) == DialogResult.OK) path.Text = file.FileName; };
                add.Click += (s, e) =>
                {
                    string value = path.Text.Trim().Trim('"'); if (!full.Checked) value = System.IO.Path.GetFileName(value);
                    if (string.IsNullOrWhiteSpace(value) || (full.Checked && !Exclusion.IsAbsolute(value))) { MessageBox.Show(dialog, "Enter an executable name or an absolute path."); return; }
                    exclusions.Items.Add(new Exclusion { Executable = value, FullPath = full.Checked }); dialog.Close();
                };
                dialog.Controls.AddRange(new Control[] { path, browse, full, add }); dialog.AcceptButton = add; dialog.ShowDialog(this);
            }
        }
        private bool ApplyChanges()
        {
            try
            {
                var result = draft.Clone();
                result.Bindings = rows.Select(r => new LayoutBinding { LayoutId = (r.Item1.SelectedItem as KeyboardLayout)?.Id, Shortcut = r.Item2.Value }).ToList();
                result.FirstLayout = (first.SelectedItem as KeyboardLayout)?.Id; result.SecondLayout = (second.SelectedItem as KeyboardLayout)?.Id;
                result.ConversionShortcut = conversion.Value; result.Direction = (ConversionDirection)direction.SelectedIndex;
                result.PauseShortcut = pauseShortcut.Value; result.UppercaseShortcut = uppercase.Value; result.LowercaseShortcut = lowercase.Value; result.InvertCaseShortcut = invertCase.Value;
                result.TapMilliseconds = (int)tap.Value; result.StartWithWindows = startup.Checked; result.Exclusions = exclusions.Items.Cast<Exclusion>().ToList();
                result.Validate(); save(result); return true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Settings could not be saved", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        }
    }
}

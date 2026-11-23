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
        private Shortcut pending;
        private int activationKey;
        internal Shortcut Value { get; private set; } = new Shortcut();
        internal bool IsRecording { get; private set; }
        internal event Action<Shortcut, long> Tested;
        internal event Action ValueChanged;
        internal event Action Cleared;
        internal ShortcutBox(HotkeyEngine engine, Shortcut value)
        {
            this.engine = engine; Value = value ?? new Shortcut(); ReadOnly = true; Width = 230; Text = Value.ToString();
            LostFocus += (s, e) => CancelRecording();
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) BeginRecording(); }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (!IsRecording && keyData == Keys.Enter) { BeginRecording((int)Keys.Enter); return true; }
            if (IsRecording && keyData == Keys.Escape) { CancelRecording(); return true; }
            return base.ProcessDialogKey(keyData);
        }
        internal void BeginRecording(int initiatingKey = 0)
        {
            if (IsRecording) return;
            pressed.Clear(); starts.Clear(); combined = false; pending = null; activationKey = initiatingKey;
            IsRecording = true; engine.Capture = CaptureKey; BackColor = SystemColors.Info; Text = "Press a shortcut…";
        }
        internal void CancelRecording()
        {
            if (engine.Capture == CaptureKey) engine.Capture = null;
            IsRecording = false; pending = null; pressed.Clear(); starts.Clear(); activationKey = 0;
            BackColor = SystemColors.Window; Text = Value.ToString();
        }
        internal bool CaptureKey(int key, bool up)
        {
            if (!IsRecording) return false;
            if (key == activationKey) { if (up) activationKey = 0; return true; }
            if (key == (int)Keys.Escape) { if (!up) CancelRecording(); return true; }
            if (!up)
            {
                if (!pressed.Add(key)) return true;
                starts[key] = Stopwatch.GetTimestamp();
                if (HotkeyEngine.IsModifier(key) && pressed.Count > 1) combined = true;
                if (!HotkeyEngine.IsModifier(key))
                {
                    pending = new Shortcut { Key = key, Modifiers = pressed.Aggregate(0, (flags, k) => flags | HotkeyEngine.ModifierFlag(k)) }; combined = true;
                }
            }
            else
            {
                bool standalone = HotkeyEngine.IsModifier(key) && !combined && pressed.Count == 1;
                if (standalone) pending = new Shortcut { Key = key };
                long started;
                if ((standalone || !HotkeyEngine.IsModifier(key)) && pending != null && pending.Key == key && starts.TryGetValue(key, out started))
                {
                    long elapsed = (Stopwatch.GetTimestamp() - started) * 1000 / Stopwatch.Frequency;
                    var result = pending; bool changed = result.Identity != Value.Identity; Value = result; CancelRecording();
                    if (changed) ValueChanged?.Invoke(); Tested?.Invoke(Value, elapsed); return true;
                }
                starts.Remove(key);
                pressed.Remove(key); if (pressed.Count == 0) combined = false;
            }
            return true;
        }
        internal void ClearShortcut() { bool changed = Value.Key != 0 || Value.Modifiers != 0; CancelRecording(); Value = new Shortcut(); Text = ""; if (changed) ValueChanged?.Invoke(); Cleared?.Invoke(); }
        protected override void Dispose(bool disposing) { if (disposing) CancelRecording(); base.Dispose(disposing); }
    }
    internal sealed class SettingsForm : DpiForm
    {
        private readonly Settings draft;
        private readonly HotkeyEngine engine;
        private readonly Action<Settings> save;
        private readonly Action<string, string> showMessage;
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill, Name = "SettingsTabs" };
        private readonly Dictionary<ShortcutBox, long> edited = new Dictionary<ShortcutBox, long>();
        private long editSequence;
        private string appliedSignature;
        private bool initialized;
        private Button apply;
        private Label detectionHint;
        private readonly List<KeyboardLayout> layouts = Layouts.Installed();
        private readonly FlowLayoutPanel bindingRows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        private readonly List<Tuple<ComboBox, ShortcutBox, Panel>> rows = new List<Tuple<ComboBox, ShortcutBox, Panel>>();
        private ComboBox first, second, direction;
        private ShortcutBox conversion;
        private ShortcutBox pauseShortcut, uppercase, lowercase, invertCase;
        private NumericUpDown tap;
        private CheckBox startup;
        private CheckBox deselectAfterConversion;
        private readonly ListBox exclusions = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        private readonly Point? savedLocation;
        internal SettingsForm(Settings settings, HotkeyEngine engine, Action<Settings> save, Action<string, string> showMessage = null, Point? savedLocation = null)
        {
            draft = settings.Clone(); this.engine = engine; this.save = save;
            this.showMessage = showMessage ?? ((title, message) => MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning));
            Text = "L-Switcher Settings"; ClientSize = new Size(800, 650);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            var screen = savedLocation.HasValue ? Screen.FromPoint(savedLocation.Value) : null;
            if (screen != null && screen.WorkingArea.Contains(savedLocation.Value))
            {
                this.savedLocation = savedLocation;
                StartPosition = FormStartPosition.Manual;
                Location = CenterIn(screen.WorkingArea);
            }
            var layoutsTab = new TabPage("Layouts") { Padding = new Padding(10) };
            bindingRows.BackColor = SystemColors.Window;
            bindingRows.Padding = new Padding(8);
            var header = FitRow(700); header.Padding = new Padding(4, 8, 4, 8); header.BackColor = SystemColors.Control;
            header.Controls.AddRange(new Control[] {
                new Label { Text = "Keyboard layout", Width = 260, Height = 22, TextAlign = ContentAlignment.MiddleLeft },
                new Label { Text = "Activation shortcut", Width = 205, Height = 22, TextAlign = ContentAlignment.MiddleLeft },
                new Label { Text = "Colors", Width = 68, Height = 22, TextAlign = ContentAlignment.MiddleLeft },
                new Label { Text = "Actions", Width = 145, Height = 22, TextAlign = ContentAlignment.MiddleLeft }
            });
            bindingRows.Controls.Add(header);
            layoutsTab.Controls.Add(bindingRows);
            var addArea = FitRow(); addArea.Dock = DockStyle.Bottom; var add = ButtonFor("Add layout", 100); add.Name = "AddLayout";
            add.Click += (s, e) => AddBinding(new LayoutBinding(), true); addArea.Controls.Add(add); layoutsTab.Controls.Add(addArea);
            var hint = new Label { Text = "Click or press Enter to record a shortcut; Esc cancels. Tab moves between idle fields.", Dock = DockStyle.Top, Height = 40 }; layoutsTab.Controls.Add(hint);
            layoutsTab.Controls.Add(ColorSettings());
            tabs.TabPages.Add(layoutsTab); foreach (var binding in draft.Bindings) AddBinding(binding);
            var conversionTab = new TabPage("Conversion") { Padding = new Padding(14) };
            var conversionPanel = Vertical(); first = LayoutCombo(draft.FirstLayout); second = LayoutCombo(draft.SecondLayout); first.Name = "FirstLayout"; second.Name = "SecondLayout";
            var layoutGroup = SettingsGroup("Keyboard layout conversion");
            layoutGroup.Controls.Add(FieldRow("First layout", first));
            layoutGroup.Controls.Add(FieldRow("Second layout", second));
            direction = new ComboBox { Name = "ConversionDirection", DropDownStyle = ComboBoxStyle.DropDownList, Width = 480 };
            direction.Items.AddRange(new object[] { "Swap both ways", "First → Second", "Second → First" }); direction.SelectedIndex = (int)draft.Direction;
            layoutGroup.Controls.Add(FieldRow("Direction", direction));
            conversion = AssignmentBox(draft.ConversionShortcut, "ConversionShortcut");
            layoutGroup.Controls.Add(NamedShortcutRow("Shortcut", conversion));
            deselectAfterConversion = new CheckBox { Name = "DeselectAfterConversion", Text = "Deselect text after conversion", AutoSize = true, Checked = draft.DeselectAfterLayoutConversion, Margin = new Padding(3, 8, 3, 3) };
            deselectAfterConversion.CheckedChanged += (s, e) => RefreshDraft(); layoutGroup.Controls.Add(deselectAfterConversion);
            detectionHint = new Label { Name = "WordDetectionHint", AutoSize = false, Width = 660, Height = 58, Margin = new Padding(3, 10, 3, 3), ForeColor = SystemColors.GrayText, Text = "Swap both ways identifies each word’s layout; punctuation follows the preceding word. Ambiguous words follow the preceding direction (first layout initially)." }; layoutGroup.Controls.Add(detectionHint);
            first.SelectedIndexChanged += (s, e) => UpdateDirections(); second.SelectedIndexChanged += (s, e) => UpdateDirections();
            direction.SelectedIndexChanged += (s, e) => detectionHint.Visible = direction.SelectedIndex == 0; UpdateDirections();
            conversionPanel.Controls.Add(layoutGroup.Parent);
            var caseGroup = SettingsGroup("Selection case");
            uppercase = AssignmentBox(draft.UppercaseShortcut, "UppercaseShortcut"); lowercase = AssignmentBox(draft.LowercaseShortcut, "LowercaseShortcut"); invertCase = AssignmentBox(draft.InvertCaseShortcut, "InvertCaseShortcut");
            caseGroup.Controls.Add(NamedShortcutRow("UPPERCASE", uppercase));
            caseGroup.Controls.Add(NamedShortcutRow("lowercase", lowercase));
            caseGroup.Controls.Add(NamedShortcutRow("Invert case", invertCase));
            conversionPanel.Controls.Add(caseGroup.Parent);
            conversionPanel.Controls.Add(new Label { Width = 700, Height = 40, Margin = new Padding(3, 8, 3, 3), ForeColor = SystemColors.GrayText, Text = "All text commands require an editable selection. The active keyboard layout remains unchanged." });
            conversionTab.Controls.Add(conversionPanel); tabs.TabPages.Add(conversionTab);
            var exclusionsTab = new TabPage("Exclusions") { Padding = new Padding(12) }; exclusionsTab.Controls.Add(exclusions);
            var exclusionButtons = FitRow(); exclusionButtons.Dock = DockStyle.Bottom;
            exclusions.Name = "Exclusions";
            var addExclusion = ButtonFor("Add"); var removeExclusion = ButtonFor("Remove"); removeExclusion.Name = "RemoveExclusion"; removeExclusion.Enabled = false;
            exclusions.SelectedIndexChanged += (s, e) => removeExclusion.Enabled = exclusions.SelectedItem != null;
            addExclusion.Click += (s, e) => AddExclusion(); removeExclusion.Click += (s, e) => { if (exclusions.SelectedItem != null) { exclusions.Items.Remove(exclusions.SelectedItem); RefreshDraft(); } };
            exclusionButtons.Controls.AddRange(new Control[] { addExclusion, removeExclusion }); exclusionsTab.Controls.Add(exclusionButtons);
            exclusionsTab.Controls.Add(new Label { Text = "All hotkeys pass through while a matching application is in the foreground.", Dock = DockStyle.Top, Height = 32 });
            foreach (var exclusion in draft.Exclusions) exclusions.Items.Add(exclusion); tabs.TabPages.Add(exclusionsTab);
            var generalTab = new TabPage("General") { Padding = new Padding(14) }; var general = Vertical();
            startup = new CheckBox { Name = "Startup", Text = "Start with Windows", AutoSize = true, Checked = draft.StartWithWindows };
            tap = new NumericUpDown { Name = "TapLimit", Minimum = 50, Maximum = 2000, Increment = 50, Value = draft.TapMilliseconds, Width = 150 };
            general.Controls.Add(startup); general.Controls.Add(LabelFor("Short-tap time limit (milliseconds)")); general.Controls.Add(tap);
            pauseShortcut = AssignmentBox(draft.PauseShortcut, "PauseShortcut"); general.Controls.Add(LabelFor("Pause / Resume hotkeys")); general.Controls.Add(ShortcutRow(pauseShortcut));
            general.Controls.Add(LabelFor("Shortcut test — click or press Enter to record"));
            var test = new ShortcutBox(engine, new Shortcut()) { Name = "ShortcutTest" }; general.Controls.Add(ShortcutRow(test));
            var feedback = new Label { Name = "ShortcutTestFeedback", Width = 700, Height = 75, Text = "Shows tap/hold and the assigned action from current settings. No action is executed." }; general.Controls.Add(feedback);
            string idleFeedback = feedback.Text; test.Cleared += () => feedback.Text = idleFeedback;
            test.Tested += (shortcut, milliseconds) => feedback.Text = shortcut + " — " + (milliseconds <= tap.Value ? "Short tap" : "Hold") + " (" + milliseconds + " ms)\nAssigned action: " + DescribeAction(shortcut) + "\nTest only — action not executed.";
            general.Controls.Add(new Label { Width = 700, Height = 90, Text = "Settings: %LOCALAPPDATA%\\L-Switcher\\settings.json\n\nPause is temporary and resets when the application restarts.\nElevated applications, Remote Desktop, and games are outside prototype support." });
            generalTab.Controls.Add(general); tabs.TabPages.Add(generalTab); Controls.Add(tabs);
            var buttons = FitRow(); buttons.Dock = DockStyle.Bottom; buttons.FlowDirection = FlowDirection.RightToLeft; buttons.Padding = new Padding(8);
            apply = ButtonFor("Apply"); apply.Name = "ApplySettings"; apply.TabIndex = 2; apply.Click += (s, e) => ApplyChanges();
            var cancel = ButtonFor("Cancel"); cancel.TabIndex = 1; cancel.DialogResult = DialogResult.Cancel; cancel.Click += (s, e) => Close();
            var ok = ButtonFor("OK"); ok.TabIndex = 0; ok.Click += (s, e) => { if (ApplyChanges()) Close(); };
            buttons.Controls.AddRange(new Control[] { apply, cancel, ok }); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
            appliedSignature = Signature(settings); initialized = true;
            foreach (var combo in AllControls(this).OfType<ComboBox>()) combo.SelectedIndexChanged += (s, e) => RefreshDraft();
            startup.CheckedChanged += (s, e) => RefreshDraft(); tap.ValueChanged += (s, e) => RefreshDraft(); RefreshDraft();
        }
        protected override void OnLoad(EventArgs e)
        {
            // The handle has been created and DpiForm has applied the monitor's scale.
            var area = Screen.FromHandle(Handle).WorkingArea;
            Location = savedLocation.HasValue && area.Contains(new Rectangle(savedLocation.Value, Size))
                ? savedLocation.Value : CenterIn(area);
            base.OnLoad(e);
        }
        private Point CenterIn(Rectangle area)
        {
            return new Point(area.Left + Math.Max(0, (area.Width - Width) / 2),
                             area.Top + Math.Max(0, (area.Height - Height) / 2));
        }
        private static FlowLayoutPanel Vertical() { return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true }; }
        private static IEnumerable<Control> AllControls(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in AllControls(child)) yield return descendant; }
        }
        private ShortcutBox AssignmentBox(Shortcut value, string name)
        {
            var box = new ShortcutBox(engine, value) { Name = name };
            box.ValueChanged += () => { edited[box] = ++editSequence; RefreshDraft(); };
            box.Tested += (shortcut, elapsed) => edited[box] = ++editSequence; return box;
        }
        private void UpdateDirections()
        {
            string a = first.SelectedItem?.ToString() ?? "First layout", b = second.SelectedItem?.ToString() ?? "Second layout";
            direction.Items[1] = a + " → " + b; direction.Items[2] = b + " → " + a;
            direction.DropDownWidth = Math.Max(direction.Width, TextRenderer.MeasureText(a + " → " + b, direction.Font).Width + 30);
            detectionHint.Visible = direction.SelectedIndex == 0;
        }
        private static string Signature(Settings settings)
        {
            var normalized = settings.Clone(); normalized.LabelColors = normalized.LabelColors.OrderBy(c => c.LayoutId, StringComparer.Ordinal).ToList();
            using (var stream = new System.IO.MemoryStream()) { Settings.Serializer.WriteObject(stream, normalized); return Convert.ToBase64String(stream.ToArray()); }
        }
        private void RefreshDraft() { if (initialized) apply.Enabled = Signature(ReadDraft()) != appliedSignature; }
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
        private readonly Label defaultPreview = new Label { Name = "DefaultColorPreview", Text = "Aa", Width = 55, Height = 27, TextAlign = ContentAlignment.MiddleCenter };
        private Control ColorSettings()
        {
            var group = new GroupBox { Text = "Default Color", Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var panel = FitRow(); panel.Dock = DockStyle.Top; panel.Padding = new Padding(5, 8, 5, 5); group.Controls.Add(panel);
            var text = ButtonFor("Text…"); var background = ButtonFor("Background…");
            text.Click += (s, e) => PickColor(null, false);
            background.Click += (s, e) => PickColor(null, true);
            foreach (Control control in new Control[] { text, background, defaultPreview })
            {
                control.Anchor = AnchorStyles.Left; control.Margin = new Padding(3); panel.Controls.Add(control);
            }
            RefreshColorPreviews();
            return group;
        }
        private void PickColor(string layoutId, bool background)
        {
            using (var dialog = new ColorDialog { FullOpen = true, Color = background ? draft.LabelBackground(layoutId) : draft.LabelColor(layoutId) })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                int color = dialog.Color.ToArgb();
                if (layoutId == null)
                {
                    if (background) draft.DefaultBackgroundArgb = color;
                    else draft.DefaultTextArgb = color;
                }
                else
                {
                    var entry = draft.LabelColors.FirstOrDefault(c => c.LayoutId == layoutId);
                    if (entry == null) { entry = new LayoutLabelColor { LayoutId = layoutId }; draft.LabelColors.Add(entry); }
                    if (background) entry.BackgroundArgb = color;
                    else entry.Argb = color;
                }
            }
            RefreshColorPreviews(); RefreshDraft();
        }
        private void RefreshColorPreviews()
        {
            defaultPreview.ForeColor = draft.LabelColor(null); defaultPreview.BackColor = draft.LabelBackground(null);
            foreach (var row in rows)
            {
                var layout = row.Item1.SelectedItem as KeyboardLayout;
                var button = (Button)row.Item3.Controls["LayoutColors"];
                button.Enabled = layout != null;
                button.Text = (layout?.Label ?? "??") + " ▾";
                button.ForeColor = draft.LabelColor(layout?.Id);
                button.BackColor = draft.LabelBackground(layout?.Id);
            }
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
            var clear = ClearButton(box); row.Controls.Add(box); row.Controls.Add(clear); return row;
        }
        private static Button ClearButton(ShortcutBox box)
        {
            var clear = ButtonFor("Clear", 65); clear.Name = "ClearShortcut"; clear.Enabled = box.Value.Key != 0;
            box.ValueChanged += () => clear.Enabled = box.Value.Key != 0; clear.Click += (s, e) => box.ClearShortcut(); return clear;
        }
        private static Panel NamedShortcutRow(string label, ShortcutBox box)
        {
            var row = FitRow(660); row.Margin = new Padding(0, 3, 0, 3);
            row.Controls.Add(new Label { Text = label, Width = 150, AutoSize = false, Height = 25, TextAlign = ContentAlignment.MiddleLeft }); row.Controls.Add(ShortcutRow(box)); return row;
        }
        private string DescribeAction(Shortcut shortcut)
        {
            var actions = Assignments().Where(a => a.Item1.Value.Key != 0 && a.Item1.Value.Identity == shortcut.Identity).Select(a => a.Item2).ToList();
            return actions.Count == 0 ? "None" : string.Join("; ", actions);
        }
        private IEnumerable<Tuple<ShortcutBox, string>> Assignments()
        {
            foreach (var row in rows) yield return Tuple.Create(row.Item2, "Activate " + (row.Item1.SelectedItem?.ToString() ?? "Layout unavailable"));
            yield return Tuple.Create(conversion, "Convert keyboard layout"); yield return Tuple.Create(pauseShortcut, "Pause / Resume");
            yield return Tuple.Create(uppercase, "Uppercase selection"); yield return Tuple.Create(lowercase, "Lowercase selection"); yield return Tuple.Create(invertCase, "Invert selection case");
        }
        private void AddBinding(LayoutBinding binding, bool focus = false)
        {
            var panel = FitRow(700); panel.Margin = new Padding(0, 0, 0, 1); panel.Padding = new Padding(4, 8, 4, 8);
            var layout = LayoutCombo(binding.LayoutId); layout.Name = "BindingLayout"; layout.Width = 260;
            var shortcut = AssignmentBox(binding.Shortcut, "LayoutShortcut"); shortcut.Width = 205;
            var clear = ClearButton(shortcut);
            var remove = ButtonFor("Remove");
            var row = Tuple.Create<ComboBox, ShortcutBox, Panel>(layout, shortcut, panel); rows.Add(row);
            var colors = ButtonFor("", 68); colors.Name = "LayoutColors"; colors.UseVisualStyleBackColor = false;
            colors.AccessibleName = "Tray colors for this layout";
            var menu = new ContextMenuStrip();
            var text = menu.Items.Add("Text…"); var background = menu.Items.Add("Background…");
            menu.Items.Add(new ToolStripSeparator());
            var inherit = menu.Items.Add("Use default colors");
            Func<string> selectedId = () => (layout.SelectedItem as KeyboardLayout)?.Id;
            text.Click += (s, e) => PickColor(selectedId(), false);
            background.Click += (s, e) => PickColor(selectedId(), true);
            inherit.Click += (s, e) => { draft.LabelColors.RemoveAll(c => c.LayoutId == selectedId()); RefreshColorPreviews(); RefreshDraft(); };
            menu.Opening += (s, e) => inherit.Enabled = draft.LabelColors.Any(c => c.LayoutId == selectedId());
            colors.Click += (s, e) => menu.Show(colors, 0, colors.Height);
            colors.ContextMenuStrip = menu;
            colors.Disposed += (s, e) => menu.Dispose();
            layout.SelectedIndexChanged += (s, e) => { RefreshColorPreviews(); RefreshDraft(); };
            remove.Click += (s, e) => { rows.Remove(row); edited.Remove(shortcut); bindingRows.Controls.Remove(panel); panel.Dispose(); RefreshBindingColors(); RefreshDraft(); };
            panel.Controls.AddRange(new Control[] { layout, shortcut, colors, clear, remove }); AddDpiControl(bindingRows, panel); RefreshBindingColors(); RefreshColorPreviews();
            RefreshDraft(); if (focus) { bindingRows.PerformLayout(); bindingRows.ScrollControlIntoView(panel); layout.Focus(); }
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
                    exclusions.SelectedIndex = exclusions.Items.Add(new Exclusion { Executable = value, FullPath = full.Checked }); RefreshDraft(); dialog.Close();
                };
                dialog.Controls.AddRange(new Control[] { path, browse, full, add }); dialog.AcceptButton = add; dialog.ShowDialog(this);
            }
        }
        internal Settings ReadDraft()
        {
            var result = draft.Clone();
            result.Bindings = rows.Select(r => new LayoutBinding { LayoutId = (r.Item1.SelectedItem as KeyboardLayout)?.Id, Shortcut = r.Item2.Value }).ToList();
            result.FirstLayout = (first.SelectedItem as KeyboardLayout)?.Id; result.SecondLayout = (second.SelectedItem as KeyboardLayout)?.Id;
            result.ConversionShortcut = conversion.Value; result.Direction = (ConversionDirection)direction.SelectedIndex;
            result.DeselectAfterLayoutConversion = deselectAfterConversion.Checked;
            result.PauseShortcut = pauseShortcut.Value; result.UppercaseShortcut = uppercase.Value; result.LowercaseShortcut = lowercase.Value; result.InvertCaseShortcut = invertCase.Value;
            result.TapMilliseconds = (int)tap.Value; result.StartWithWindows = startup.Checked; result.Exclusions = exclusions.Items.Cast<Exclusion>().ToList();
            return result;
        }
        private void FocusAssignment(ShortcutBox field)
        {
            Control parent = field;
            while (parent != null && !(parent is TabPage)) parent = parent.Parent;
            if (parent != null) tabs.SelectedTab = (TabPage)parent;
            for (Control owner = field.Parent; owner != null; owner = owner.Parent)
            { var scroll = owner as ScrollableControl; if (scroll != null && scroll.AutoScroll) scroll.ScrollControlIntoView(field); }
            field.Focus();
        }
        internal bool ApplyChanges()
        {
            try
            {
                foreach (var field in AllControls(this).OfType<ShortcutBox>()) field.CancelRecording();
                var conflict = Assignments().Where(a => a.Item1.Value.Key != 0).GroupBy(a => a.Item1.Value.Identity).Where(g => g.Count() > 1)
                    .OrderByDescending(g => g.Max(a => edited.ContainsKey(a.Item1) ? edited[a.Item1] : 0)).FirstOrDefault();
                if (conflict != null)
                {
                    var target = conflict.OrderByDescending(a => edited.ContainsKey(a.Item1) ? edited[a.Item1] : 0).First().Item1;
                    showMessage("Conflicting shortcuts", "Shortcut " + target.Value + " is assigned to:\n\n" + string.Join("\n", conflict.Select(a => "• " + a.Item2)) + "\n\nChoose a different shortcut for one of these actions.");
                    FocusAssignment(target); return false;
                }
                var result = ReadDraft(); result.Validate();
                // OK must still persist initially unchanged defaults so the next launch
                // is no longer treated as a first launch. Apply itself is disabled when clean.
                string signature = Signature(result);
                save(result); appliedSignature = signature; RefreshDraft(); return true;
            }
            catch (Exception ex) { showMessage("Settings could not be saved", ex.Message); return false; }
        }
    }
}

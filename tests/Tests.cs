using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LSwitcher
{
    internal static partial class Tests
    {
        private static int assertions;
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
        private static void FocusTestWindow(IntPtr window)
        {
            uint pid; uint foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid), thread = GetCurrentThreadId();
            bool attached = foregroundThread != thread && AttachThreadInput(thread, foregroundThread, true);
            try { Native.SetForegroundWindow(window); } finally { if (attached) AttachThreadInput(thread, foregroundThread, false); }
        }
        private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual, string message) { Check(Equals(expected, actual), message + ": expected " + expected + ", got " + actual); }
        private static void Run(Action test)
        {
            Console.WriteLine("RUN: " + test.Method.Name);
            test();
            Console.WriteLine("PASS: " + test.Method.Name);
        }
        [STAThread] private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("--instance-", StringComparison.Ordinal)) return InstanceWorker(args);
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Application.EnableVisualStyles();
                Run(TestSingleInstance); Run(TestShortcutRecording); Run(TestTrayStatuses); Run(TestTaps); Run(TestValidation); Run(TestStorage); Run(TestConversion); Run(TestCase); Run(TestRichText); Run(TestLayouts); Run(TestPauseAndCapture); Run(TestColors);
                // Run COM checks before Application.Run tears down a WinForms synchronization context.
                if (args.Contains("--word-smoke")) Run(TestWord);
                if (args.Contains("--clipboard-smoke")) Run(TestClipboard);
                if (args.Contains("--ui-smoke")) { Run(TestSettingsWindow); Run(TestTrayLifecycle); Run(TestInstanceTrayShutdown); Run(TestSettingsImprovements); }
                if (args.Contains("--dpi-smoke")) Run(TestDpi);
                Console.WriteLine("PASS: " + assertions + " assertions"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void TestTaps()
        {
            var taps = new TapTracker(); taps.Down(0xa2, 0); Check(taps.Up(0xa2, 250, 300), "Short standalone Ctrl fires");
            taps.Down(0xa2, 0); Check(!taps.Up(0xa2, 301, 300), "Long Ctrl does not fire");
            taps.Down(0xa2, 0); taps.Down(65, 10); taps.Up(65, 20, 300); Check(!taps.Up(0xa2, 30, 300), "Ctrl+A does not fire Ctrl action");
            taps.Down(0xa2, 0); taps.Interrupt(); Check(!taps.Up(0xa2, 100, 300), "Mouse invalidates tap");
            taps.Down(0xa2, 0); taps.Down(0xa0, 10); Check(!taps.Up(0xa0, 20, 300), "Second modifier is used"); Check(!taps.Up(0xa2, 30, 300), "First modifier is used");
            taps.Down(0xa2, 0); taps.Clear(); Check(!taps.Up(0xa2, 10, 300), "Reset cancels pending tap");
            taps.Down(0xa3, 0); taps.Down(0xa3, 200); Check(!taps.Up(0xa3, 350, 300), "Repeat does not reset hold timer");
        }
        private static void TestValidation()
        {
            var settings = new Settings(); settings.Validate(); Equal(0, settings.Bindings.Count, "No initial assignments");
            settings.Bindings.Add(new LayoutBinding { LayoutId = "00000409", Shortcut = new Shortcut { Key = 65, Modifiers = (int)Keys.Control } });
            settings.Bindings.Add(new LayoutBinding { LayoutId = "00000419", Shortcut = new Shortcut { Key = 65, Modifiers = (int)Keys.Control } });
            bool rejected = false; try { settings.Validate(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Duplicate shortcuts rejected");
            settings.Bindings.RemoveAt(1); settings.FirstLayout = "first"; settings.SecondLayout = "second"; settings.ConversionShortcut = new Shortcut { Key = 65, Modifiers = (int)Keys.Control };
            rejected = false; try { settings.Validate(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Conversion shortcut conflict rejected");
            settings.ConversionShortcut = new Shortcut(); var clone = settings.Clone(); clone.Bindings[0].Shortcut.Key = 66; Equal(65, settings.Bindings[0].Shortcut.Key, "Cancel draft is independent");
            Check(new Exclusion { Executable = "Code.exe" }.Matches(@"C:\Apps\CODE.EXE"), "Executable exclusion ignores case");
            Check(!new Exclusion { FullPath = true, Executable = @"C:\One\Code.exe" }.Matches(@"C:\Two\Code.exe"), "Full path exclusion is exact");
            settings.PauseShortcut = new Shortcut { Key = 65, Modifiers = (int)Keys.Control };
            rejected = false; try { settings.Validate(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Pause shortcut conflicts with layout action");
            settings.PauseShortcut = new Shortcut { Key = 66 }; settings.UppercaseShortcut = new Shortcut { Key = 66 };
            rejected = false; try { settings.Validate(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Case shortcut conflicts with pause action");
        }
        private static void TestStorage()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LSwitcher.Tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                var store = new SettingsStore(Path.Combine(dir, "settings.json")); var settings = store.Load(); store.Save(settings);
                settings.TapMilliseconds = 500; store.Save(settings); Equal(500, store.Load().TapMilliseconds, "Round trip");
                File.WriteAllText(store.FilePath, "broken"); Equal(300, store.Load().TapMilliseconds, "Corruption recovers previous backup");
                Check(store.LoadWarning != null, "Recovery warns user"); settings.TapMilliseconds = 600; store.Save(settings);
                File.WriteAllText(store.FilePath, "broken again"); Equal(300, store.Load().TapMilliseconds, "Invalid file never overwrites good backup");
                settings.SchemaVersion = 2; bool rejected = false; try { store.Save(settings); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Unknown schema cannot be saved");
                File.WriteAllText(store.FilePath, "{\"SchemaVersion\":1,\"TapMilliseconds\":300,\"Bindings\":[],\"Exclusions\":[],\"ConversionShortcut\":{\"Key\":0,\"Modifiers\":0},\"Direction\":0}");
                var upgraded = new SettingsStore(store.FilePath).Load(); Equal(0, upgraded.PauseShortcut.Key, "Old schema-one settings gain an unassigned pause shortcut"); Equal(0, upgraded.UppercaseShortcut.Key, "Old settings gain unassigned case shortcuts");
                Equal(0, upgraded.LabelColors.Count, "Old settings use default tray colors");
            }
            finally { foreach (string path in Directory.GetFiles(dir)) File.Delete(path); Directory.Delete(dir); }
        }
        private static TextConverter Converter()
        {
            string a = "qwertyuiop[]asdfghjkl;'zxcvbnm,./QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?";
            string b = "йцукенгшщзхъфывапролджэячсмитьбю.ЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,";
            var f = new Dictionary<char, char>(); var r = new Dictionary<char, char>(); for (int i = 0; i < a.Length; i++) { f[a[i]] = b[i]; r[b[i]] = a[i]; } return new TextConverter(f, r);
        }
        private static void TestConversion()
        {
            var c = Converter(); Equal("руддщю ghbdtn/", c.Convert("hello. привет.", ConversionDirection.SwapBothWays), "Punctuation follows word layout");
            Equal("Руддщ\r\nGhbdtn 😀", c.Convert("Hello\r\nПривет 😀", ConversionDirection.SwapBothWays), "Case, lines, mixed text and emoji");
            Equal("руддщю приветю", c.Convert("hello. привет.", ConversionDirection.FirstToSecond), "Explicit one-way conversion");
            Equal("hello/ ghbdtn/", c.Convert("hello. привет.", ConversionDirection.SecondToFirst), "Reverse conversion");
            Equal("ю", c.Convert(".", ConversionDirection.SwapBothWays), "No previous word uses first layout");
            Equal("", c.Convert("", ConversionDirection.SwapBothWays), "Empty selection");
            Equal("руддщghbdtn", c.Convert("helloпривет", ConversionDirection.SwapBothWays), "Mixed alphabets inside one word swap both ways");
            var shared = new TextConverter(new Dictionary<char, char> { ['y'] = 'z', ['z'] = 'y' }, new Dictionary<char, char> { ['y'] = 'z', ['z'] = 'y' });
            Equal("zy", shared.Convert("yz", ConversionDirection.SwapBothWays), "Shared alphabet physical key swap");
        }
        private static void TestRichText()
        {
            using (var rich = new RichTextBox())
            {
                rich.Text = "hello\nworld"; rich.Select(0, 2); rich.SelectionFont = new Font("Arial", 14, FontStyle.Bold | FontStyle.Italic); rich.SelectionColor = Color.Red;
                string converted = Converter().Convert(rich.Text, ConversionDirection.FirstToSecond);
                string rtf = SelectionConversion.ConvertRtf(rich.Rtf, rich.Text, converted); Check(rtf != null, "RTF converts"); rich.Rtf = rtf;
                Equal(converted, rich.Text, "Rich text content"); rich.Select(0, 2); Check(rich.SelectionFont.Bold && rich.SelectionFont.Italic, "Bold and italic retained"); Equal(Color.Red.ToArgb(), rich.SelectionColor.ToArgb(), "Color retained");
                Check(SelectionConversion.ConvertRtf(rich.Rtf, "mismatch", "different") == null, "Mismatching RTF uses plain fallback");
            }
        }
        private static void TestCase()
        {
            Equal("HELLO ПРИВЕТ É\r\n😀 123!", CaseConverter.Convert("Hello Привет é\r\n😀 123!", CaseMode.Uppercase), "Uppercase preserves punctuation, lines and emoji");
            Equal("hello привет é", CaseConverter.Convert("HELLO ПРИВЕТ É", CaseMode.Lowercase), "Lowercase supports both alphabets");
            Equal("hELLO пРИВЕТ Éé 😀", CaseConverter.Convert("Hello Привет éÉ 😀", CaseMode.Invert), "Invert flips each cased character");
            Equal("", CaseConverter.Convert("", CaseMode.Invert), "Case conversion empty selection");
        }
        private static void TestColors()
        {
            var layouts = Layouts.Installed(); var settings = new Settings();
            Equal(SystemColors.Window.ToArgb(), settings.LabelColor(layouts[0].Id).ToArgb(), "Tray color defaults to system foreground");
            settings.LabelColors.Add(new LayoutLabelColor { LayoutId = layouts[0].Id, Argb = Color.Red.ToArgb() });
            if (layouts.Count > 1) settings.LabelColors.Add(new LayoutLabelColor { LayoutId = layouts[1].Id, Argb = Color.Blue.ToArgb() });
            settings.Validate(); var clone = settings.Clone(); Equal(Color.Red.ToArgb(), clone.LabelColor(layouts[0].Id).ToArgb(), "Per-layout color survives JSON serialization");
            if (layouts.Count > 1) Equal(Color.Blue.ToArgb(), clone.LabelColor(layouts[1].Id).ToArgb(), "Other layout keeps its separate color");
            clone.LabelColors[0].Argb = Color.Green.ToArgb(); Equal(Color.Red.ToArgb(), settings.LabelColors[0].Argb, "Draft color does not modify saved settings");
            using (var engine = new HotkeyEngine(settings))
            using (var form = new SettingsForm(settings, engine, value => { throw new Exception("Color test must not save"); }))
            {
                var preview = (Label)form.Controls.Find("LayoutColorPreview", true).Single(); Equal(Color.Red.ToArgb(), preview.ForeColor.ToArgb(), "Color preview uses selected layout");
                if (layouts.Count > 1) { var selector = (ComboBox)form.Controls.Find("LayoutColorLayout", true).Single(); selector.SelectedIndex = 1; Equal(Color.Blue.ToArgb(), preview.ForeColor.ToArgb(), "Changing layout updates color preview"); }
            }
            clone.LabelColors.Add(new LayoutLabelColor { LayoutId = layouts[0].Id, Argb = Color.White.ToArgb() });
            bool rejected = false; try { clone.Validate(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Duplicate per-layout color is rejected");
        }
        private static void TestPauseAndCapture()
        {
            var settings = new Settings { PauseShortcut = new Shortcut { Key = (int)Keys.F23 }, UppercaseShortcut = new Shortcut { Key = (int)Keys.F22 } };
            using (var engine = new HotkeyEngine(settings))
            using (var target = new Form())
            {
                int pauses = 0, cases = 0; engine.PauseRequested += () => { pauses++; engine.Paused = !engine.Paused; }; engine.CaseRequested += (mode, window) => { cases++; Equal(CaseMode.Uppercase, mode, "Case action direction"); };
                engine.Paused = true; Check(engine.Trigger((int)Keys.F23, 0, target.Handle), "Pause shortcut works while manually paused"); Equal(false, engine.Paused, "Pause shortcut resumes");
                Check(engine.Trigger((int)Keys.F23, 0, target.Handle), "Pause shortcut pauses"); Check(engine.Paused, "Manual pause enabled");
                Check(!engine.Trigger((int)Keys.F22, 0, target.Handle), "Other actions are disabled during pause"); Equal(0, cases, "Paused case action did not execute");
                engine.Paused = false; Check(engine.Trigger((int)Keys.F22, 0, target.Handle), "Case action works without a conversion pair"); Equal(1, cases, "Case action dispatched once");
                settings.Exclusions.Add(new Exclusion { Executable = System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe" }); engine.Reset();
                Check(!engine.Trigger((int)Keys.F23, 0, target.Handle), "Excluded app disables pause shortcut too"); Equal(2, pauses, "Excluded app did not toggle pause"); settings.Exclusions.Clear(); engine.Reset();
                using (var form = new SettingsForm(settings, engine, value => { throw new Exception("Capture test must not save"); }))
                {
                    var field = (ShortcutBox)form.Controls.Find("ShortcutTest", true).Single(); var feedback = (Label)form.Controls.Find("ShortcutTestFeedback", true).Single();
                    field.BeginRecording(); field.CaptureKey((int)Keys.F23, false); field.CaptureKey((int)Keys.F23, true);
                    Check(feedback.Text.Contains("Short tap") && feedback.Text.Contains("Pause / Resume"), "Shortcut test identifies tap and assignment"); Equal(2, pauses, "Shortcut test never executes assigned action");
                    field.BeginRecording(); field.CaptureKey(0xa2, false); System.Threading.Thread.Sleep(350); field.CaptureKey(0xa2, true);
                    Check(feedback.Text.Contains("Hold") && feedback.Text.Contains("LControlKey"), "Shortcut test detects a standalone modifier hold");
                }
            }
        }
        private static void TestLayouts()
        {
            var layouts = Layouts.Installed(); Check(layouts.Count > 0, "Installed layouts enumerated");
            foreach (var layout in layouts) { var identity = Layouts.BuildMap(layout.Handle, layout.Handle); Check(identity.Count > 10, "Physical keyboard map built for " + layout.Name); Check(identity.All(p => p.Key == p.Value), "Identity map for " + layout.Name); }
            var en = layouts.FirstOrDefault(l => (l.Handle.ToInt64() & 0xffff) == 0x409); var ru = layouts.FirstOrDefault(l => (l.Handle.ToInt64() & 0xffff) == 0x419);
            if (en != null && ru != null)
            {
                var c = new TextConverter(Layouts.BuildMap(en.Handle, ru.Handle), Layouts.BuildMap(ru.Handle, en.Handle));
                Equal("руддщю ghbdtn/", c.Convert("hello. привет.", ConversionDirection.SwapBothWays), "Real Windows EN/RU physical mapping");
            }
            Console.WriteLine("Layouts: " + string.Join(", ", layouts.Select(l => l.Name)));
        }
        private static void TestSettingsWindow()
        {
            using (var engine = new HotkeyEngine(new Settings()))
            using (var form = new SettingsForm(new Settings(), engine, settings => { throw new Exception("Smoke test must not save"); }))
            {
                engine.SettingsWindow = form.Handle; engine.Paused = false;
                form.Shown += (s, e) =>
                {
                    var tabs = form.Controls.OfType<TabControl>().Single();
                    foreach (TabPage page in tabs.TabPages)
                    {
                        tabs.SelectedTab = page; page.Refresh();
                        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-" + page.Text.ToLowerInvariant() + ".png")); }
                    }
                    var timer = new Timer { Interval = 1500 }; timer.Tick += (a, b) => { timer.Stop(); timer.Dispose(); form.Close(); }; timer.Start();
                };
                Application.Run(form); Check(true, "Settings window opened and closed without saving");
            }
        }
        private static void TestWord()
        {
            object appObject = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application", true)); dynamic app = appObject;
            object docObject = null;
            try
            {
                app.Visible = true; app.DisplayAlerts = 0;
                docObject = app.Documents.Add(); dynamic doc = docObject;
                doc.Content.Text = "hello. привет.\rWorld";
                doc.Range(0, 5).Font.Bold = -1; doc.Range(7, 13).Font.Italic = -1; doc.Range(0, 5).Font.Color = 255;
                app.Selection.SetRange(0, 20); app.Activate();
                IntPtr window = (IntPtr)(int)app.ActiveWindow.Hwnd;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    app.Activate(); FocusTestWindow(window);
                    for (int tick = 0; tick < 10; tick++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    if (Native.GetForegroundWindow() == window) break;
                }
                var targetClass = new System.Text.StringBuilder(128); var foregroundClass = new System.Text.StringBuilder(128);
                Native.GetClassName(window, targetClass, 128); Native.GetClassName(Native.GetForegroundWindow(), foregroundClass, 128);
                Console.WriteLine("Word target: " + window + " (" + targetClass + "); foreground: " + Native.GetForegroundWindow() + " (" + foregroundClass + ")");
                Check(Native.GetForegroundWindow() == window, "Word test document is foreground");
                IntPtr before = Layouts.ForegroundLayout();
                Task<bool> task = SelectionConversion.TryWord(window, Converter(), ConversionDirection.SwapBothWays);
                while (!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                Check(task.GetAwaiter().GetResult(), "Word native selection path available");
                Equal("руддщю ghbdtn/\rЦщкдв", ((string)app.Selection.Text).TrimEnd('\r'), "Word mixed text and paragraph conversion");
                Equal(-1, (int)doc.Range(0, 5).Font.Bold, "Word bold retained"); Equal(-1, (int)doc.Range(7, 13).Font.Italic, "Word italic retained");
                Equal(255, (int)doc.Range(0, 5).Font.Color, "Word color retained"); Equal(0, (int)app.Selection.Start, "Word selection start retained"); Equal(20, (int)app.Selection.End, "Word selection end retained");
                Equal(before, Layouts.ForegroundLayout(), "Word conversion leaves layout unchanged");
                task = SelectionConversion.TransformWord(window, text => CaseConverter.Convert(text, CaseMode.Uppercase));
                while (!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                Check(task.GetAwaiter().GetResult(), "Word case selection path"); Equal("РУДДЩЮ GHBDTN/\rЦЩКДВ", ((string)app.Selection.Text).TrimEnd('\r'), "Word uppercase conversion"); Equal(-1, (int)doc.Range(0, 5).Font.Bold, "Word case conversion retains bold");
                app.Selection.SetRange(0, 0); task = SelectionConversion.TryWord(window, Converter(), ConversionDirection.SwapBothWays);
                while (!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                Check(task.GetAwaiter().GetResult(), "Word empty selection no-op"); Equal(0, (int)app.Selection.End, "Empty selection remains empty");
                doc.Protect(3); app.Selection.SetRange(0, 20); string protectedText = app.Selection.Text;
                task = SelectionConversion.TryWord(window, Converter(), ConversionDirection.SwapBothWays);
                while (!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                Check(task.GetAwaiter().GetResult(), "Word protected selection handled"); Equal(protectedText, (string)app.Selection.Text, "Read-only Word selection unchanged");
            }
            finally
            {
                try { if (docObject != null) { dynamic doc = docObject; doc.Close(0); } } finally { app.Quit(0); if (docObject != null) Marshal.ReleaseComObject(docObject); Marshal.ReleaseComObject(appObject); }
            }
        }
        private static void TestClipboard()
        {
            var installed = Layouts.Installed(); var en = installed.First(l => (l.Handle.ToInt64() & 0xffff) == 0x409); var ru = installed.First(l => (l.Handle.ToInt64() & 0xffff) == 0x419);
            var saved = SelectionConversion.SnapshotClipboard();
            using (var form = new Form { Text = "L-Switcher temporary clipboard test", Width = 600, Height = 300 })
            using (var rich = new RichTextBox { Dock = DockStyle.Fill, Text = "hello. привет." })
            {
                string failure = null; var service = new SelectionConversion(); service.Notice += message => failure = message;
                form.Controls.Add(rich);
                form.Shown += async (s, e) =>
                {
                    try
                    {
                        FocusTestWindow(form.Handle); rich.Focus(); rich.Select(0, 5); rich.SelectionFont = new Font("Arial", 12, FontStyle.Bold); rich.SelectAll();
                        await Task.Delay(200);
                        FocusTestWindow(form.Handle); rich.Focus();
                        var element = System.Windows.Automation.AutomationElement.FocusedElement;
                        object textPattern;
                        Console.WriteLine("Clipboard test foreground: " + (Native.GetForegroundWindow() == form.Handle) + "; focus: " + element.Current.ClassName);
                        Check(Native.GetForegroundWindow() == form.Handle, "Clipboard test control must be foreground");
                        if (element.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out textPattern))
                        {
                            foreach (var range in ((System.Windows.Automation.TextPattern)textPattern).GetSelection()) Console.WriteLine("UIA selection: [" + range.GetText(-1) + "]; read-only: " + range.GetAttributeValue(System.Windows.Automation.TextPattern.IsReadOnlyAttribute));
                        }
                        var sentinel = new DataObject(); sentinel.SetText("L-Switcher clipboard test"); sentinel.SetData("L-Switcher.Test", "preserve this format"); Clipboard.SetDataObject(sentinel, true);
                        service.Run(form.Handle, new Settings { FirstLayout = en.Id, SecondLayout = ru.Id });
                        for (int i = 0; i < 100 && rich.Text != "руддщю ghbdtn/" && failure == null; i++) await Task.Delay(30);
                        await Task.Delay(300);
                        Equal("руддщю ghbdtn/", rich.Text, "Clipboard path converts editable selection; notice: " + failure);
                        Equal(rich.Text, rich.SelectedText, "Clipboard path keeps converted text selected");
                        rich.Select(0, 5); Check(rich.SelectionFont.Bold, "Clipboard RTF retains bold"); rich.SelectAll();
                        Equal("L-Switcher clipboard test", Clipboard.GetText(), "Clipboard text restored"); Equal("preserve this format", Clipboard.GetData("L-Switcher.Test") as string, "Custom clipboard format restored");
                        service.RunCase(form.Handle, CaseMode.Uppercase);
                        for (int i = 0; i < 100 && rich.Text != "РУДДЩЮ GHBDTN/"; i++) await Task.Delay(30);
                        await Task.Delay(300);
                        Equal("РУДДЩЮ GHBDTN/", rich.Text, "Case conversion uses editable selection without layout pair"); Equal(rich.Text, rich.SelectedText, "Case conversion keeps selection"); Equal("L-Switcher clipboard test", Clipboard.GetText(), "Case conversion restores clipboard");
                        rich.ReadOnly = true; string beforeReadOnly = rich.Text;
                        service.RunCase(form.Handle, CaseMode.Lowercase); await Task.Delay(200);
                        Equal(beforeReadOnly, rich.Text, "Read-only control is unchanged");
                    }
                    catch (Exception ex) { failure = ex.ToString(); }
                    finally { form.Close(); }
                };
                try { Application.Run(form); if (failure != null) throw new Exception(failure); }
                finally { if (Clipboard.ContainsData("L-Switcher.Test")) { if (saved.GetFormats(false).Length == 0) Clipboard.Clear(); else Clipboard.SetDataObject(saved, true); } }
            }
        }
        private static void TestTrayLifecycle()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unsaved-" + Guid.NewGuid().ToString("N"), "settings.json");
            using (var activation = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset))
            using (var context = new TrayApplication(activation, new SettingsStore(path)))
            using (var timer = new Timer { Interval = 500 })
            {
                Exception failure = null; int tick = 0;
                timer.Tick += (s, e) =>
                {
                    try
                    {
                        Equal(1, Application.OpenForms.Count, "First launch opens one settings window");
                        if (tick++ == 0) { activation.Set(); return; }
                        Equal(1, Application.OpenForms.Count, "Activation event reuses existing settings window");
                    }
                    catch (Exception ex) { failure = ex; }
                    timer.Stop(); context.ExitThread();
                };
                timer.Start(); Application.Run(context); if (failure != null) throw failure;
            }
            Check(!File.Exists(path), "Closing without apply does not create settings");
        }
    }
}

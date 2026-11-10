using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace LSwitcher
{
    internal sealed class SelectionConversion
    {
        private bool busy;
        internal event Action<string> Notice;
        internal void Run(IntPtr target, Settings settings) { Run(target, settings, null); }
        internal void RunCase(IntPtr target, CaseMode mode) { Run(target, null, mode); }
        private async void Run(IntPtr target, Settings settings, CaseMode? caseMode)
        {
            if (busy) return; busy = true;
            try
            {
                // Let the triggering shortcut finish. Never release keys still held by the user.
                for (int i = 0; i < 100 && ModifiersDown(); i++) await Task.Delay(20);
                if (ModifiersDown() || target != Native.GetForegroundWindow()) return;
                Func<string, string> transform;
                if (caseMode.HasValue) transform = text => CaseConverter.Convert(text, caseMode.Value);
                else
                {
                    var layouts = Layouts.Installed();
                    var first = layouts.FirstOrDefault(x => x.Id == settings.FirstLayout); var second = layouts.FirstOrDefault(x => x.Id == settings.SecondLayout);
                    if (first == null || second == null) { Notice?.Invoke("Conversion layout unavailable."); return; }
                    var converter = new TextConverter(Layouts.BuildMap(first.Handle, second.Handle), Layouts.BuildMap(second.Handle, first.Handle));
                    transform = text => converter.Convert(text, settings.Direction);
                }
                if (await TransformWord(target, transform)) return;
                await ClipboardConvert(target, transform);
            }
            catch (Exception ex) { Notice?.Invoke("Conversion stopped: " + ex.Message); }
            finally { busy = false; }
        }
        private static bool ModifiersDown() { return new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c }.Any(k => (Native.GetAsyncKeyState(k) & 0x8000) != 0); }

        internal static Task<bool> TryWord(IntPtr window, TextConverter converter, ConversionDirection direction) { return TransformWord(window, text => converter.Convert(text, direction)); }
        internal static async Task<bool> TransformWord(IntPtr window, Func<string, string> transform)
        {
            IntPtr document = IntPtr.Zero;
            Native.EnumChildWindows(window, (child, unused) => { var name = new StringBuilder(128); Native.GetClassName(child, name, name.Capacity); if (name.ToString() == "_WwG") document = child; return true; }, IntPtr.Zero);
            if (document == IntPtr.Zero) return false;
            object native = null, appObject = null, selectionObject = null, rangeObject = null, docObject = null, undoObject = null;
            bool recording = false;
            try
            {
                var iid = new Guid("00020400-0000-0000-C000-000000000046");
                if (Native.AccessibleObjectFromWindow(document, 0xfffffff0, ref iid, out native) < 0) return false;
                dynamic wordWindow = native; appObject = wordWindow.Application; dynamic app = appObject;
                selectionObject = app.Selection; dynamic selection = selectionObject;
                rangeObject = selection.Range; dynamic range = rangeObject;
                docObject = range.Document; dynamic doc = docObject;
                if ((bool)doc.ReadOnly || (int)doc.ProtectionType != -1 || (bool)doc.TrackRevisions) return true;
                string original = range.Text as string;
                if (string.IsNullOrEmpty(original) || (int)range.Start == (int)range.End) return true;
                string converted = transform(original);
                if (converted.Length != original.Length) throw new InvalidOperationException("This conversion changes text length and cannot preserve character positions.");
                int start = range.Start, end = range.End;
                if (original == converted) return true;
                try { undoObject = app.UndoRecord; dynamic undo = undoObject; undo.StartCustomRecord("Convert keyboard layout"); recording = true; } catch (COMException) { } catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                // Edit changed characters from the end. Each Word range retains its own formatting.
                for (int i = original.Length - 1; i >= 0; i--)
                {
                    if (original[i] == converted[i]) continue;
                    if (window != Native.GetForegroundWindow()) throw new InvalidOperationException("The foreground window changed.");
                    object characterObject = doc.Range(start + i, start + i + 1);
                    try { dynamic character = characterObject; character.Text = converted[i].ToString(); }
                    finally { Release(characterObject); }
                    if (i % 16 == 0)
                    {
                        int expectedStart = selection.Start, expectedEnd = selection.End;
                        string expectedText = range.Text;
                        await Task.Delay(1);
                        if ((int)selection.Start != expectedStart || (int)selection.End != expectedEnd || (string)range.Text != expectedText) throw new InvalidOperationException("The selection changed during conversion.");
                    }
                }
                selection.SetRange(start, end); return true;
            }
            finally
            {
                try { if (recording) { dynamic undo = undoObject; undo.EndCustomRecord(); } }
                finally { Release(undoObject); Release(docObject); Release(rangeObject); Release(selectionObject); Release(appObject); Release(native); }
            }
        }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

        private async Task ClipboardConvert(IntPtr window, Func<string, string> transform)
        {
            var focused = AutomationElement.FocusedElement;
            object patternObject;
            if (focused == null || !focused.TryGetCurrentPattern(TextPattern.Pattern, out patternObject)) { Notice?.Invoke("This control does not expose an editable text selection."); return; }
            var pattern = (TextPattern)patternObject; var selections = pattern.GetSelection();
            if (selections.Length != 1) return;
            var selection = selections[0]; string selected = selection.GetText(-1);
            if (string.IsNullOrEmpty(selected)) return;
            object readOnly = selection.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
            if (!(readOnly is bool) || (bool)readOnly) return; // Never paste into an unknown/read-only target.
            var original = SnapshotClipboard(); uint ownedSequence = Native.GetClipboardSequenceNumber(); bool clipboardOwned = false;
            try
            {
                // A private marker distinguishes a successful copy from stale clipboard text.
                var marker = new DataObject(); marker.SetData("L-Switcher.CopyMarker", Guid.NewGuid().ToString());
                Clipboard.SetDataObject(marker, true); clipboardOwned = true; ownedSequence = Native.GetClipboardSequenceNumber();
                Native.Chord(0x43);
                for (int i = 0; i < 40 && ownedSequence == Native.GetClipboardSequenceNumber(); i++) await Task.Delay(20);
                if (ownedSequence == Native.GetClipboardSequenceNumber()) return;
                uint copiedSequence = Native.GetClipboardSequenceNumber();
                clipboardOwned = false;
                if (window != Native.GetForegroundWindow() || !Clipboard.ContainsText()) return;
                string copied = Clipboard.GetText();
                if (Normalize(copied) != Normalize(selected)) return;
                ownedSequence = copiedSequence; clipboardOwned = true;
                string converted = transform(copied); if (converted == copied) return;
                string rtf = Clipboard.ContainsData(DataFormats.Rtf) ? Clipboard.GetData(DataFormats.Rtf) as string : null;
                var data = new DataObject(); data.SetText(converted, TextDataFormat.UnicodeText);
                if (!string.IsNullOrEmpty(rtf))
                {
                    string rich = ConvertRtf(rtf, copied, converted); if (rich != null) data.SetData(DataFormats.Rtf, rich);
                }
                // Recheck selection and focus after awaits, before changing the document.
                var current = pattern.GetSelection();
                if (current.Length != 1 || current[0].GetText(-1) != selected || window != Native.GetForegroundWindow() || ownedSequence != Native.GetClipboardSequenceNumber()) return;
                Clipboard.SetDataObject(data, true); ownedSequence = Native.GetClipboardSequenceNumber();
                Native.Chord(0x56);
                await Task.Delay(180);
                if (window != Native.GetForegroundWindow()) return;
                // UIA uses character units for selection (including surrogate pairs and line endings).
                var ranges = pattern.GetSelection();
                if (ranges.Length == 1)
                {
                    var range = ranges[0]; range.MoveEndpointByRange(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End);
                    range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -TextUnits(converted));
                    if (Normalize(range.GetText(-1)) == Normalize(converted)) range.Select();
                }
            }
            finally
            {
                // Respect clipboard changes made by the user or another application during conversion.
                if (clipboardOwned && Native.GetClipboardSequenceNumber() == ownedSequence)
                { if (original.GetFormats(false).Length == 0) Clipboard.Clear(); else Clipboard.SetDataObject(original, true); }
            }
        }
        private static string Normalize(string value) { return value.Replace("\r\n", "\n").Replace('\r', '\n'); }
        private static int TextUnits(string value) { return System.Globalization.StringInfo.ParseCombiningCharacters(Normalize(value)).Length; }
        internal static DataObject SnapshotClipboard()
        {
            var snapshot = new DataObject(); var source = Clipboard.GetDataObject(); if (source == null) return snapshot;
            foreach (string format in source.GetFormats(false))
            {
                object value = source.GetData(format, false);
                var stream = value as System.IO.MemoryStream;
                if (stream != null) value = new System.IO.MemoryStream(stream.ToArray());
                if (value == null) throw new InvalidOperationException("Clipboard format could not be backed up: " + format);
                snapshot.SetData(format, false, value);
            }
            return snapshot;
        }
        internal static string ConvertRtf(string rtf, string original, string converted)
        {
            using (var rich = new RichTextBox())
            {
                rich.Rtf = rtf;
                string normalizedOriginal = Normalize(original), normalizedConverted = Normalize(converted);
                if (Normalize(rich.Text) != normalizedOriginal || rich.Text.Length != normalizedConverted.Length) return null;
                string source = rich.Text;
                for (int i = source.Length - 1; i >= 0; i--)
                {
                    if (source[i] == normalizedConverted[i]) continue;
                    rich.Select(i, 1);
                    using (var font = rich.SelectionFont)
                    {
                        var color = rich.SelectionColor; var background = rich.SelectionBackColor; int offset = rich.SelectionCharOffset;
                        rich.SelectedText = normalizedConverted[i].ToString(); rich.Select(i, 1);
                        if (font != null) rich.SelectionFont = font; rich.SelectionColor = color; rich.SelectionBackColor = background; rich.SelectionCharOffset = offset;
                    }
                }
                rich.Select(0, rich.TextLength);
                return rich.SelectedRtf; // Exclude the document's implicit final paragraph from pasted content.
            }
        }
    }
}

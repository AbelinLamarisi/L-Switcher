using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LSwitcher
{
    // Keep the single-executable distribution: PMv2 is declared in the manifest,
    // and this form handles WM_DPICHANGED without Framework's external app.config opt-in.
    internal class DpiForm : Form
    {
        private sealed class Metrics
        {
            internal Rectangle Bounds;
            internal Padding Margin, Padding;
            internal Size Minimum, Maximum;
        }
        private readonly Dictionary<Control, Metrics> logical = new Dictionary<Control, Metrics>();
        private int currentDpi = 96;
        private bool initialized, scaling;
        private Font dpiFont;
        private readonly Font logicalFont = CreateFont(96);
        private Size logicalMinimum;
        internal int LayoutDpi { get { return currentDpi; } }

        internal DpiForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            dpiFont = CreateFont(96); Font = dpiFont;
        }
        private static Font CreateFont(int dpi)
        {
            var system = SystemFonts.MessageBoxFont;
            return new Font(system.FontFamily, system.SizeInPoints * dpi / 72f, system.Style, GraphicsUnit.Pixel);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (initialized) return;
            logicalMinimum = MinimumSize;
            foreach (Control child in Controls) Remember(child);
            initialized = true;
            int dpi = (int)Native.GetDpiForWindow(Handle);
            ApplyDpi(dpi == 0 ? 96 : dpi, null);
        }
        // Controls constructed after the form is visible also start in 96-DPI units.
        protected void AddDpiControl(Control parent, Control control)
        {
            if (!initialized) { parent.Controls.Add(control); return; }
            control.Font = logicalFont; control.PerformLayout(); Remember(control);
            control.Font = null; parent.Controls.Add(control); ScaleTree(control);
        }
        private void Remember(Control control)
        {
            if (!logical.ContainsKey(control)) logical.Add(control, new Metrics { Bounds = control.Bounds, Margin = control.Margin, Padding = control.Padding, Minimum = control.MinimumSize, Maximum = control.MaximumSize });
            foreach (Control child in control.Controls) Remember(child);
        }
        private int Px(int value) { return (int)Math.Round(value * currentDpi / 96.0, MidpointRounding.AwayFromZero); }
        private Size Px(Size value) { return new Size(Px(value.Width), Px(value.Height)); }
        private Padding Px(Padding value) { return new Padding(Px(value.Left), Px(value.Top), Px(value.Right), Px(value.Bottom)); }
        private void ScaleTree(Control control)
        {
            Metrics value;
            if (!logical.TryGetValue(control, out value) || control.IsDisposed) return;
            control.SuspendLayout();
            control.MinimumSize = Size.Empty; control.MaximumSize = Size.Empty;
            control.Margin = Px(value.Margin); control.Padding = Px(value.Padding);
            control.Bounds = new Rectangle(Px(value.Bounds.X), Px(value.Bounds.Y), Px(value.Bounds.Width), Px(value.Bounds.Height));
            control.MinimumSize = Px(value.Minimum); control.MaximumSize = Px(value.Maximum);
            foreach (Control child in control.Controls) ScaleTree(child);
            control.ResumeLayout(true);
        }
        private void ApplyDpi(int dpi, Rectangle? suggested)
        {
            if (scaling || dpi < 48 || dpi > 960) return;
            scaling = true; SuspendLayout();
            try
            {
                float ratio = (float)dpi / currentDpi;
                var client = new Size((int)Math.Round(ClientSize.Width * ratio), (int)Math.Round(ClientSize.Height * ratio));
                // WinForms ignores an equal Font assignment. Replacing and disposing
                // it at the same DPI would leave controls using the disposed font.
                if (dpi != currentDpi)
                {
                    Font replacement = CreateFont(dpi), previous = dpiFont;
                    dpiFont = replacement; Font = replacement; previous.Dispose();
                }
                currentDpi = dpi;
                foreach (Control child in Controls) ScaleTree(child);
                var area = Screen.FromRectangle(suggested ?? Bounds).WorkingArea;
                MinimumSize = new Size(Math.Min(Px(logicalMinimum.Width), area.Width), Math.Min(Px(logicalMinimum.Height), area.Height));
                if (suggested.HasValue) Bounds = suggested.Value; else ClientSize = client;
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                if (suggested.HasValue) Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
                foreach (var dead in logical.Keys.Where(c => c.IsDisposed).ToArray()) logical.Remove(dead);
            }
            finally { ResumeLayout(true); scaling = false; }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02e0 && initialized) // WM_DPICHANGED; do not also let Framework scale this tree.
            {
                var rect = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                ApplyDpi((int)(m.WParam.ToInt64() & 0xffff), Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom));
                m.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) { dpiFont?.Dispose(); dpiFont = null; logicalFont.Dispose(); logical.Clear(); } }
    }
}

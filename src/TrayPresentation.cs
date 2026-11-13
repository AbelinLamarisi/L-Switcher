using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LSwitcher
{
    internal static class TrayPresentation
    {
        internal static string Tooltip(string layoutName, bool paused, bool excluded)
        {
            string status = (paused ? " — Hotkeys paused" : "") + (excluded ? " — Hotkeys disabled for this app" : "");
            int available = 63 - status.Length;
            if (layoutName.Length > available) layoutName = layoutName.Substring(0, Math.Max(0, available - 1)) + "…";
            return layoutName + status;
        }
        internal static Bitmap Draw(string label, Color labelColor, bool paused, bool excluded)
        {
            var bitmap = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font(SystemFonts.MessageBoxFont.FontFamily, 18, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.Clear(Color.Transparent);
                using (var background = new SolidBrush(SystemColors.WindowText)) graphics.FillRectangle(background, 0, 2, 32, 27);
                using (var foreground = new SolidBrush(labelColor)) graphics.DrawString(label, font, foreground, new RectangleF(0, 4, 32, 26), format);
                if (excluded)
                {
                    graphics.FillEllipse(Brushes.Firebrick, 0, 20, 12, 12);
                    using (var cross = new Pen(Color.White, 2)) { graphics.DrawLine(cross, 4, 24, 8, 28); graphics.DrawLine(cross, 8, 24, 4, 28); }
                }
                if (paused) { graphics.FillRectangle(Brushes.Orange, 21, 21, 4, 10); graphics.FillRectangle(Brushes.Orange, 27, 21, 4, 10); }
            }
            return bitmap;
        }
    }
}

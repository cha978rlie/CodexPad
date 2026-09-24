using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexPad
{
    internal static class PadTheme
    {
        private static readonly Dictionary<string, Bitmap> buttonGlyphs = new Dictionary<string, Bitmap>();
        public static Bitmap ButtonGlyph(string kind, Color color, int size)
        {
            string key = kind + ":" + color.ToArgb() + ":" + size;
            if (!buttonGlyphs.TryGetValue(key, out var glyph)) {
                glyph = Glyph(kind, color, size);
                buttonGlyphs.Add(key, glyph);
            }
            return glyph;
        }
        public static readonly Color Background = Color.FromArgb(14, 18, 25);
        public static readonly Color Surface = Color.FromArgb(23, 29, 39);
        public static readonly Color Raised = Color.FromArgb(31, 39, 51);
        public static readonly Color Border = Color.FromArgb(47, 59, 73);
        public static readonly Color Text = Color.FromArgb(236, 242, 247);
        public static readonly Color Muted = Color.FromArgb(149, 165, 183);
        public static readonly Color Accent = Color.FromArgb(82, 224, 192);
        public static GraphicsPath Round(RectangleF rect, float radius)
        {
            var path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        public static Bitmap Glyph(string kind, Color color, int size = 24)
        {
            var image = new Bitmap(size, size);
            using var g = Graphics.FromImage(image); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(size / 24f, size / 24f);
            using var p = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var b = new SolidBrush(color);
            void Line(float x, float y, float xx, float yy) => g.DrawLine(p, x, y, xx, yy);
            switch (kind) {
                case "mic":
                    using (var r = Round(new RectangleF(9, 3, 6, 11), 3)) g.DrawPath(p, r);
                    g.DrawArc(p, 6, 7, 12, 11, 0, 180); Line(12, 18, 12, 21); Line(9, 21, 15, 21); break;
                case "send":
                    g.DrawPolygon(p, new[] { new PointF(3, 4), new PointF(21, 12), new PointF(3, 20), new PointF(6, 12) }); Line(6, 12, 16, 12); break;
                case "up": case "down":
                    bool up = kind == "up"; Line(12, 4, 12, 20);
                    Line(6, up ? 10 : 14, 12, up ? 4 : 20); Line(18, up ? 10 : 14, 12, up ? 4 : 20); break;
                case "play": g.FillPolygon(b, new[] { new PointF(8, 4), new PointF(20, 12), new PointF(8, 20) }); break;
                case "pause": g.FillRectangle(b, 7, 5, 3, 14); g.FillRectangle(b, 14, 5, 3, 14); break;
                case "led":
                    g.DrawEllipse(p, 7, 5, 10, 10); Line(10, 18, 14, 18); Line(11, 21, 13, 21);
                    Line(12, 1, 12, 2); Line(2, 10, 4, 10); Line(20, 10, 22, 10); break;
                case "tasks":
                    using (var r = Round(new RectangleF(3, 4, 14, 12), 3)) g.DrawPath(p, r);
                    Line(6, 16, 6, 20); Line(6, 20, 11, 16); Line(20, 8, 20, 18); Line(14, 19, 20, 19); break;
                case "save":
                    g.DrawRectangle(p, 4, 3, 16, 18); g.DrawRectangle(p, 8, 3, 8, 6); g.DrawRectangle(p, 8, 14, 8, 7); break;
                case "undo":
                    g.DrawArc(p, 6, 6, 14, 14, 210, 280); Line(3, 5, 3, 12); Line(3, 12, 10, 12); break;
                case "folder":
                    g.DrawPolygon(p, new[] { new PointF(3, 5), new PointF(10, 5), new PointF(12, 8), new PointF(21, 8), new PointF(21, 20), new PointF(3, 20) }); break;
                case "key":
                    g.DrawEllipse(p, 3, 4, 9, 9); Line(10, 12, 20, 21); Line(16, 17, 19, 14); Line(13, 15, 16, 12); break;
                case "info":
                    g.DrawEllipse(p, 3, 3, 18, 18); g.FillEllipse(b, 11, 6, 2, 2); Line(12, 11, 12, 17); break;
                case "check": Line(4, 12, 10, 18); Line(10, 18, 21, 5); break;
                case "settings":
                    using (var fill = new SolidBrush(Surface))
                        for (int i = 0; i < 3; i++) { int y = 5 + i * 7; Line(3, y, 21, y); g.FillEllipse(fill, i == 1 ? 6 : 13, y - 3, 6, 6); g.DrawEllipse(p, i == 1 ? 6 : 13, y - 3, 6, 6); } break;
                case "record": g.DrawEllipse(p, 4, 4, 16, 16); g.FillEllipse(b, 8, 8, 8, 8); break;
                default:
                    using (var r = Round(new RectangleF(2, 5, 20, 14), 3)) g.DrawPath(p, r);
                    for (int i = 0; i < 3; i++) g.FillRectangle(b, 5 + i * 4, 9, 2, 6);
                    g.DrawEllipse(p, 17, 10, 2, 4); break;
            }
            return image;
        }
        public static string ActionIcon(string action) => action switch {
            "dictation" or "voice" => "mic", "enter" or "send" => "send",
            "scrollUp" or "chatScrollUp" or "effortUp" => "up", "scrollDown" or "chatScrollDown" or "effortDown" => "down",
            "cycle" or "latest" or "thread" or "attention" => "tasks", "shortcut" => "key", _ => "settings"
        };
        public static Font UiFont(float points, FontStyle style = FontStyle.Regular)
            => new Font("Segoe UI", points * 96f / 72f, style, GraphicsUnit.Pixel);

        public static void StyleCombo(ComboBox box)
        {
            box.BackColor = Raised; box.ForeColor = Text; box.FlatStyle = FlatStyle.Flat;
            box.DrawMode = DrawMode.OwnerDrawFixed; box.ItemHeight = 25;
            box.DrawItem += (s, e) => {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using var background = new SolidBrush(selected ? Color.FromArgb(47, 75, 76) : Raised);
                e.Graphics.FillRectangle(background, e.Bounds);
                TextRenderer.DrawText(e.Graphics, box.Items[e.Index].ToString(), box.Font, e.Bounds,
                    box.Enabled ? Text : Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        public static void DarkTitle(IntPtr window) { int dark = 1; try { DwmSetWindowAttribute(window, 20, ref dark, 4); } catch { } }
    }

    internal sealed class PadButton : Button
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Symbol { get; set; } = "";
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Primary { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Selected { get; set; }
        private bool hover;
        public PadButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = PadTheme.Surface; ForeColor = PadTheme.Text; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using var path = PadTheme.Round(rect, 8);
            Color fill = Primary ? PadTheme.Accent : Selected ? Color.FromArgb(29, 66, 65) : hover ? PadTheme.Raised : BackColor;
            using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, path);
            using var border = new Pen(Selected ? PadTheme.Accent : PadTheme.Border); e.Graphics.DrawPath(border, path);
            Color text = !Enabled ? PadTheme.Muted : Primary ? PadTheme.Background : Selected ? PadTheme.Accent : ForeColor;
            int size = (int)(20 * DeviceDpi / 96f), gap = (int)(10 * DeviceDpi / 96f);
            bool icon = Symbol.Length > 0;
            var textSize = TextRenderer.MeasureText(Text, Font);
            int groupWidth = textSize.Width + (icon ? size + gap : 0);
            int x = Math.Max(10, (Width - groupWidth) / 2);
            if (icon) { e.Graphics.DrawImage(PadTheme.ButtonGlyph(Symbol, text, size), x, (Height - size) / 2); x += size + gap; }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x, 0, Width - x - 6, Height), text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class PadIllustration : Control
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string[] Actions { get; set; } = { "cycle", "dictation", "enter" };
        public PadIllustration() { DoubleBuffered = true; BackColor = PadTheme.Surface; AccessibleName = "Pad mit drei Tasten und Drehregler"; AccessibleRole = AccessibleRole.Graphic; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Min(Width / 600f, Height / 195f); g.TranslateTransform((Width - 600 * scale) / 2, (Height - 195 * scale) / 2); g.ScaleTransform(scale, scale);
            using var chassis = PadTheme.Round(new RectangleF(20, 20, 560, 150), 22);
            using var dark = new SolidBrush(PadTheme.Background); g.FillPath(dark, chassis);
            using var edge = new Pen(PadTheme.Border, 2); g.DrawPath(edge, chassis);
            for (int i = 0; i < 3; i++) {
                using var key = PadTheme.Round(new RectangleF(42 + i * 123, 42, 104, 101), 12);
                using var fill = new SolidBrush(PadTheme.Raised); g.FillPath(fill, key); g.DrawPath(edge, key);
                using var glyph = PadTheme.Glyph(PadTheme.ActionIcon(Actions[i]), PadTheme.Text, 36); g.DrawImage(glyph, 76 + i * 123, 70, 36, 36);
                using var glow = new Pen(PadTheme.Accent, 3); g.DrawLine(glow, 73 + i * 123, 132, 115 + i * 123, 132);
            }
            using var knob = new SolidBrush(PadTheme.Raised); g.FillEllipse(knob, 444, 50, 86, 86); g.DrawEllipse(edge, 444, 50, 86, 86);
            using var ring = new Pen(PadTheme.Muted, 2); g.DrawEllipse(ring, 452, 58, 70, 70);
            using var marker = new Pen(PadTheme.Accent, 4); g.DrawLine(marker, 487, 61, 487, 73);
            using var font = PadTheme.UiFont(9); using var muted = new SolidBrush(PadTheme.Muted);
            g.DrawString("DREHEN + DRÜCKEN", font, muted, 433, 145);
        }
    }

    internal sealed class PadCombo : ComboBox
    {
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != 0x000F && message.Msg != 0x0318) return;
            using var g = message.Msg == 0x0318 && message.WParam != IntPtr.Zero
                ? Graphics.FromHdc(message.WParam) : Graphics.FromHwnd(Handle);
            using var fill = new SolidBrush(PadTheme.Raised); g.FillRectangle(fill, ClientRectangle);
            using var edge = new Pen(Focused ? PadTheme.Accent : PadTheme.Border);
            g.DrawRectangle(edge, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            int arrow = (int)(22 * DeviceDpi / 96f);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(6, 0, Math.Max(0, Width - arrow - 7), Height),
                Enabled ? PadTheme.Text : PadTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using var pen = new Pen(PadTheme.Muted, 1.5f);
            float x = Width - arrow / 2f, y = Height / 2f;
            g.DrawLines(pen, new[] { new PointF(x - 3, y - 2), new PointF(x, y + 1), new PointF(x + 3, y - 2) });
        }
    }
}

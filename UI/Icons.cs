using System.Drawing;
using System.Drawing.Drawing2D;

namespace SCAnimator.V261.UI {
    internal static class Icons {
        private static Bitmap setStart;
        private static Bitmap addKeyframe;
        private static Bitmap remove;
        private static Bitmap clear;
        private static Bitmap play;
        private static Bitmap pause;
        private static Bitmap cancel;
        private static Bitmap reset;
        private static Bitmap export;

        public static Bitmap SetStart { get { return setStart ?? (setStart = CreateSetStart()); } }
        public static Bitmap AddKeyframe { get { return addKeyframe ?? (addKeyframe = CreateAdd()); } }
        public static Bitmap Remove { get { return remove ?? (remove = CreateMinus()); } }
        public static Bitmap Clear { get { return clear ?? (clear = CreateClear()); } }
        public static Bitmap Play { get { return play ?? (play = CreatePlay()); } }
        public static Bitmap Pause { get { return pause ?? (pause = CreatePause()); } }
        public static Bitmap Cancel { get { return cancel ?? (cancel = CreateCancel()); } }
        public static Bitmap Reset { get { return reset ?? (reset = CreateReset()); } }
        public static Bitmap Export { get { return export ?? (export = CreateExport()); } }

        public static Bitmap Toolbar(string label) {
            string glyph;
            Color color;
            switch (label) {
                case "New": glyph = "+"; color = Color.SeaGreen; break;
                case "Rename": glyph = "✎"; color = Color.SteelBlue; break;
                case "Delete animation": glyph = "×"; color = Color.Firebrick; break;
                case "Scenario...": glyph = "≡"; color = Color.MediumPurple; break;
                case "Add track": glyph = "+"; color = Color.SeaGreen; break;
                case "Remove track": glyph = "−"; color = Color.DarkOrange; break;
                case "Add key": glyph = "◆"; color = Color.SeaGreen; break;
                case "Update key": glyph = "↻"; color = Color.SteelBlue; break;
                case "Delete key": glyph = "×"; color = Color.Firebrick; break;
                case "Undo": glyph = "↶"; color = Color.SteelBlue; break;
                case "Redo": glyph = "↷"; color = Color.SteelBlue; break;
                case "Copy": glyph = "C"; color = Color.MediumPurple; break;
                case "Paste": glyph = "P"; color = Color.MediumPurple; break;
                case "Set In": glyph = "["; color = Color.DarkGoldenrod; break;
                case "Set Out": glyph = "]"; color = Color.DarkGoldenrod; break;
                case "Full range": glyph = "↔"; color = Color.DarkGoldenrod; break;
                case "Insert pause": glyph = "Ⅱ"; color = Color.DarkGoldenrod; break;
                case "Zoom −": glyph = "−"; color = Color.SteelBlue; break;
                case "Zoom +": glyph = "+"; color = Color.SteelBlue; break;
                case "Save start": glyph = "S"; color = Color.SeaGreen; break;
                default: glyph = "•"; color = Color.SteelBlue; break;
            }
            var bitmap = new Bitmap(18, 18);
            using (Graphics graphics = Graphics.FromImage(bitmap)) {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (Brush background = new SolidBrush(color)) graphics.FillEllipse(background, 1, 1, 16, 16);
                using (Font font = new Font("Segoe UI Symbol", 10f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush foreground = new SolidBrush(Color.White))
                using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center })
                    graphics.DrawString(glyph, font, foreground, new RectangleF(1, 0, 16, 17), centered);
            }
            return bitmap;
        }

        private static Bitmap NewBitmap() {
            return new Bitmap(32, 32);
        }

        private static Bitmap CreateSetStart() {
            Bitmap b = NewBitmap();
            using (Graphics g = Graphics.FromImage(b)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(Color.RoyalBlue, 3f)) {
                    g.DrawLine(p, 7, 5, 7, 27);
                    g.DrawEllipse(p, 11, 9, 14, 14);
                }
                using (Brush br = new SolidBrush(Color.RoyalBlue))
                    g.FillEllipse(br, 15, 13, 6, 6);
            }
            return b;
        }

        private static Bitmap CreateAdd() {
            Bitmap b = NewBitmap();
            using (Graphics g = Graphics.FromImage(b)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(Color.SeaGreen, 3f)) {
                    g.DrawEllipse(p, 4, 8, 14, 14);
                    g.DrawLine(p, 24, 10, 24, 24);
                    g.DrawLine(p, 17, 17, 31, 17);
                }
            }
            return b;
        }

        private static Bitmap CreateMinus() {
            Bitmap b = NewBitmap();
            using (Graphics g = Graphics.FromImage(b)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(Color.DarkOrange, 3f)) {
                    g.DrawEllipse(p, 4, 8, 14, 14);
                    g.DrawLine(p, 18, 17, 30, 17);
                }
            }
            return b;
        }

        private static Bitmap CreateClear() {
            Bitmap b = NewBitmap();
            using (Graphics g = Graphics.FromImage(b)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(Color.DimGray, 3f)) {
                    g.DrawLine(p, 8, 9, 24, 25);
                    g.DrawLine(p, 24, 9, 8, 25);
                }
            }
            return b;
        }

        private static Bitmap CreatePlay() {
            Bitmap b = NewBitmap();
            using (Graphics g = Graphics.FromImage(b)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Brush br = new SolidBrush(Color.SeaGreen)) {
                    g.FillPolygon(br, new[] { new Point(8, 5), new Point(27, 16), new Point(8, 27) });
                }
            }
            return b;
        }

        private static Bitmap CreatePause() {
            Bitmap b = NewBitmap();
            using (Graphics g = Graphics.FromImage(b)) {
                g.Clear(Color.Transparent);
                using (Brush br = new SolidBrush(Color.DarkOrange)) {
                    g.FillRectangle(br, 8, 6, 6, 20);
                    g.FillRectangle(br, 18, 6, 6, 20);
                }
            }
            return b;
        }

        private static Bitmap CreateCancel() {
            Bitmap b = NewBitmap();
            using (Graphics g = Graphics.FromImage(b)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(Color.Firebrick, 4f)) {
                    g.DrawLine(p, 8, 8, 24, 24);
                    g.DrawLine(p, 24, 8, 8, 24);
                }
            }
            return b;
        }

        private static Bitmap CreateReset() {
            Bitmap b = NewBitmap();
            using (Graphics g = Graphics.FromImage(b)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(Color.RoyalBlue, 3f))
                    g.DrawArc(p, 5, 5, 22, 22, 35, 290);
                using (Brush br = new SolidBrush(Color.RoyalBlue))
                    g.FillPolygon(br, new[] { new Point(6, 4), new Point(15, 5), new Point(9, 13) });
            }
            return b;
        }
        private static Bitmap CreateExport() {
            Bitmap b = NewBitmap();
            using (Graphics g = Graphics.FromImage(b)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(Color.SteelBlue, 2.5f)) {
                    g.DrawRectangle(p, 3, 7, 26, 18);
                    g.DrawLine(p, 9, 7, 9, 25);
                    g.DrawLine(p, 23, 7, 23, 25);
                }
                using (Brush br = new SolidBrush(Color.SeaGreen))
                    g.FillPolygon(br, new[] { new Point(13, 11), new Point(13, 21), new Point(21, 16) });
            }
            return b;
        }
    }
}

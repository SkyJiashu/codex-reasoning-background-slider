using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrystalDragon {
    public sealed class DataScene : FrameworkElement {
        public const double Duration = 6.2;
        public const double PlaybackRate = 1.15;
        readonly BitmapSource hero;
        readonly List<Point> targets = new List<Point>();
        readonly List<Point> origins = new List<Point>();
        readonly List<Point> edges = new List<Point>();
        readonly FormattedText[] glyphs = new FormattedText[4];
        readonly FormattedText[] stages = new FormattedText[4];
        readonly DrawingGroup backdrop = new DrawingGroup();
        readonly DrawingGroup outlineDrawing = new DrawingGroup();
        readonly Pen trailPen;
        readonly FormattedText preparingCaption;
        static readonly Dictionary<string, Brush> brushes = new Dictionary<string, Brush>();
        readonly Rect portrait;
        double seconds, waitingTime;
        bool preparing;
        internal int Phase { get { return seconds<1.6?1:seconds<3.0?2:seconds<4.5?3:4; } }
        public void SetPreparing(bool value) { if (preparing==value) return; preparing=value; InvalidateVisual(); }
        public static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
        public DataScene(string heroPath) {
            Width = 1600; Height = 900;
            BitmapImage bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(heroPath)); bitmap.EndInit(); bitmap.Freeze(); hero = bitmap;
            double height = 802, width = height * bitmap.PixelWidth / bitmap.PixelHeight;
            portrait = new Rect(1135 - width / 2, 48, width, height);
            BitmapSource small = new TransformedBitmap(bitmap, new ScaleTransform(120.0 / bitmap.PixelWidth, 180.0 / bitmap.PixelHeight));
            small = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = small.PixelWidth, h = small.PixelHeight, stride = w * 4;
            byte[] pixels = new byte[stride * h]; small.CopyPixels(pixels, stride, 0);
            Random random = new Random(19);
            for (int y = 1; y < h - 1; y += 2) for (int x = 1; x < w - 1; x += 2) {
                if (pixels[y * stride + x * 4 + 3] < 64) continue;
                Point p = new Point(portrait.X + x * portrait.Width / w, portrait.Y + y * portrait.Height / h);
                if (pixels[y * stride + (x - 1) * 4 + 3] < 64 || pixels[y * stride + (x + 1) * 4 + 3] < 64 || pixels[(y - 1) * stride + x * 4 + 3] < 64 || pixels[(y + 1) * stride + x * 4 + 3] < 64) edges.Add(p);
                if (random.NextDouble() > .25 || targets.Count >= 1000) continue;
                targets.Add(p); origins.Add(new Point(70 + random.NextDouble() * 1420, -150 - random.NextDouble() * 950));
            }
            trailPen=new Pen(Brush("#54406C"),1);trailPen.Freeze();
            using(var dc=outlineDrawing.Open())
                foreach(Point p in edges)dc.DrawEllipse(Brush("#E0CDFF"),null,p,1.9,1.9);
            outlineDrawing.Freeze();
            for (int i = 0; i < glyphs.Length; i++) glyphs[i] = Text(i == 0 ? "0" : i == 1 ? "1" : i == 2 ? ":" : "/", 10, "#D2C2FD", "Consolas");
            string[] captions={"01 / 汇聚 · GATHERING","02 / 成形 · TAKING SHAPE","03 / 呈现 · REVEALING","04 / 欢迎回来 · WELCOME BACK"};
            for(int i=0;i<stages.Length;i++)stages[i]=Text(captions[i],14,"#AC99C5","Microsoft YaHei UI");
            preparingCaption=Text("正在准备工作区 · PREPARING WORKSPACE",13,"#AC99C5","Microsoft YaHei UI");
            // Cache the unchanging grid, glow and typography rather than reshape
            // text and rebuild vector resources while Codex is loading in parallel.
            using(var dc=backdrop.Open())DrawBackdrop(dc);
            backdrop.Freeze();
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }
        public void SetTime(double value, double ambientTime=0) {
            value=Math.Max(0,value);ambientTime=preparing?Math.Max(0,ambientTime):0;
            if(seconds==value&&waitingTime==ambientTime)return;
            seconds=value;waitingTime=ambientTime;InvalidateVisual();
        }
        static Brush Brush(string color) {
            Brush existing; if (brushes.TryGetValue(color, out existing)) return existing;
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(color); b.Freeze(); brushes.Add(color, b); return b;
        }
        FormattedText Text(string text, double size, string color, string font) {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font), size, Brush(color), 1);
        }
        void DrawBackdrop(DrawingContext dc) {
            dc.DrawRectangle(Brush("#100E19"), null, new Rect(0, 0, 1600, 900));
            RadialGradientBrush glow = new RadialGradientBrush(Color.FromArgb(95, 114, 72, 165), Colors.Transparent);
            dc.DrawEllipse(glow, null, new Point(1130, 440), 570, 490);
            Pen grid = new Pen(Brush("#1B172B"), 1);
            for (int x = 0; x < 1600; x += 80) dc.DrawLine(grid, new Point(x, 0), new Point(x, 900));
            for (int y = 0; y < 900; y += 80) dc.DrawLine(grid, new Point(0, y), new Point(1600, y));
            dc.DrawEllipse(Brush("#C7A9FC"), null, new Point(89, 83), 4, 4);
            dc.DrawText(Text("AT YOUR SERVICE", 17, "#C8B4E8", "Consolas"), new Point(110, 72));
            dc.DrawText(Text("Codex", 104, "#F3EDF9", "Segoe UI Light"), new Point(80, 262));
            dc.DrawText(Text("予想象以形。", 29, "#DBCBF1", "Microsoft YaHei UI"), new Point(88, 408));
            dc.DrawText(Text("GIVE IMAGINATION FORM.", 14, "#907EA8", "Consolas"), new Point(89, 467));
            dc.DrawText(Text("ESC 跳过     ·     进入后跟随当前推理档位", 13, "#77688C", "Microsoft YaHei UI"), new Point(90, 818));
        }
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc);
            dc.DrawDrawing(backdrop);
            double material = Ease((seconds - 2.55) / 1.65);
            if (material > 0) { dc.PushOpacity(material); dc.DrawImage(hero, portrait); dc.Pop(); }
            double cloud = (1 - Ease((seconds - 3.0) / 1.6)) * Ease(seconds / .45);
            if (!preparing && cloud>.001) {
              dc.PushOpacity(cloud);
              for (int i = 0; i < targets.Count; i++) {
                double travel = Ease((seconds - (i % 23) * .035) / 2.2);
                Point from = origins[i], to = targets[i];
                Point p = new Point(from.X + (to.X - from.X) * travel, from.Y + (to.Y - from.Y) * travel);
                if (p.Y < 0 || p.Y > 900) continue;
                if (travel < .94) dc.DrawLine(trailPen, new Point(p.X, p.Y - 14), p);
                dc.DrawText(glyphs[i % glyphs.Length], p);
              }
              dc.Pop();
            }
            double outline = Ease((seconds - 1.4) / 1.0) * (1 - material);
            if(outline>0) {dc.PushOpacity(outline);dc.DrawDrawing(outlineDrawing);dc.Pop();}
            double line = seconds<4.35 ? .9*Ease(seconds/4.35) : .9+.1*Ease((seconds-4.35)/(Duration-4.35));
            dc.DrawRectangle(Brush("#33263F"), null, new Rect(90, 666, 320, 2));
            dc.DrawRectangle(Brush("#C6A7F2"), null, new Rect(90, 666, 320 * line, 2));
            dc.DrawText(stages[Phase-1],new Point(90,692));
            if (preparing) {
                // A quiet, moving preparation cue, not a frozen completed frame.
                // It adds no extra finale delay: readiness immediately starts the fade.
                double pulse=.5+.5*Math.Sin(waitingTime*2.4);
                dc.PushOpacity(.35+.65*pulse);
                dc.DrawRectangle(Brush("#E3D2FF"),null,new Rect(90+240*(.5+.5*Math.Sin(waitingTime*1.8)),665,48,3));
                dc.Pop();
                dc.PushOpacity(.65+.2*pulse);
                dc.DrawText(preparingCaption,new Point(90,726));
                dc.Pop();
            }
        }
        public void RenderPng(double time, string path) {
            SetTime(time); Measure(new Size(1600, 900)); Arrange(new Rect(0, 0, 1600, 900)); UpdateLayout();
            RenderTargetBitmap image = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32); image.Render(this);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (FileStream file = File.Create(path)) encoder.Save(file);
        }
    }
}

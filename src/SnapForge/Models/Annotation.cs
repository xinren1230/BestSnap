using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Plugins;

namespace SnapForge.Models
{
    public enum AnnotationKind { Arrow, Line, Rectangle, Ellipse, Highlight, Text, Step, Pen, Spotlight, Redact, EraseStroke, EraseRect, Watermark }

    /// <summary>A vector annotation living above the bitmap. Stays editable (select/move/recolor/delete) until export.</summary>
    public class Annotation
    {
        public AnnotationKind Kind;
        public Point A, B;
        public Color Color = Colors.Red;
        public double Thickness = 3;
        public string Text = "";
        // text options
        public string FontFamilyName = "Microsoft YaHei UI";
        public double FontSizeOverride;   // 0 = derived from thickness
        public bool TextBackground;       // filled label with contrasting text; watermark: image stamp
        public bool Bold = true;
        public double Rotation;           // watermark degrees (rotate around stamp center)
        // step number
        public int Number = 1;
        // freehand pen
        public List<Point> Points = new List<Point>();

        public double FontSize => FontSizeOverride > 0 ? FontSizeOverride : 12 + Thickness * 3;
        public double StepRadius => 10 + Thickness * 2;

        public Annotation Clone() { var c = (Annotation)MemberwiseClone(); c.Points = new List<Point>(Points); return c; }
        public void CopyFrom(Annotation o)
        {
            Color = o.Color; Thickness = o.Thickness; Text = o.Text; FontFamilyName = o.FontFamilyName; FontSizeOverride = o.FontSizeOverride;
            TextBackground = o.TextBackground; Bold = o.Bold; Number = o.Number; Rotation = o.Rotation;
            _wmImage = o._wmImage;
        }
        public void Offset(Vector v)
        {
            if (Kind == AnnotationKind.Watermark)
            {
                for (int i = 0; i < Points.Count; i++) Points[i] += v;
                return;
            }
            A += v; B += v; for (int i = 0; i < Points.Count; i++) Points[i] += v;
        }
        /// <summary>Used by crop/resize: p' = (p - offset) * scale (or the inverse).</summary>
        public void Transform(Func<Point, Point> f, double thicknessScale)
        {
            if (Kind == AnnotationKind.Watermark)
            {
                for (int i = 0; i < Points.Count; i++) Points[i] = f(Points[i]);
                A = new Point(A.X * thicknessScale, A.Y * thicknessScale);
                B = A;
                Thickness *= thicknessScale;
                if (FontSizeOverride > 0) FontSizeOverride *= thicknessScale;
                return;
            }
            A = f(A); B = f(B); for (int i = 0; i < Points.Count; i++) Points[i] = f(Points[i]);
            Thickness *= thicknessScale; if (FontSizeOverride > 0) FontSizeOverride *= thicknessScale;
        }

        [NonSerialized] BitmapSource _wmImage;
        public void SetWatermarkImage(BitmapSource img) { _wmImage = img; }

        private static Color Contrast(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 150 ? Colors.Black : Colors.White;

        private FormattedText Formatted(string text = null, double? size = null, Color? fg = null)
        {
            var ft = new FormattedText(text ?? Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(FontFamilyName ?? "Segoe UI"), FontStyles.Normal, Bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                size ?? FontSize, new SolidColorBrush(fg ?? (TextBackground ? Contrast(Color) : Color)), 1.0);
            return ft;
        }
        private double Pad => TextBackground ? Math.Max(3, FontSize * 0.25) : 0;

        public Rect Bounds
        {
            get
            {
                if (Kind == AnnotationKind.Text) { var ft = Formatted(); return new Rect(A.X, A.Y, Math.Max(1, ft.WidthIncludingTrailingWhitespace) + 2 * Pad, ft.Height + 2 * Pad); }
                if (Kind == AnnotationKind.Step) { double r0 = StepRadius; return new Rect(A.X - r0, A.Y - r0, 2 * r0, 2 * r0); }
                if (Kind == AnnotationKind.Watermark) return WatermarkBounds();
                if (Kind == AnnotationKind.Pen || Kind == AnnotationKind.EraseStroke)
                {
                    if (Points.Count == 0) { var e = new Rect(A, A); e.Inflate(Thickness / 2, Thickness / 2); return e; }
                    double x0 = Points.Min(q => q.X), y0 = Points.Min(q => q.Y), x1 = Points.Max(q => q.X), y1 = Points.Max(q => q.Y);
                    var pr = new Rect(x0, y0, x1 - x0, y1 - y0); pr.Inflate(Thickness / 2, Thickness / 2); return pr;
                }
                var r = new Rect(A, B);
                if (Kind != AnnotationKind.Highlight && Kind != AnnotationKind.Redact && Kind != AnnotationKind.EraseRect) r.Inflate(Thickness / 2, Thickness / 2);
                if (Kind == AnnotationKind.Arrow) { double h = Math.Max(12, Thickness * 4) / 2; r.Inflate(h, h); }
                return r;
            }
        }

        public bool HitTest(Point p, double tol)
        {
            switch (Kind)
            {
                case AnnotationKind.Line: case AnnotationKind.Arrow: return EraserMath.DistToSegment(p.X, p.Y, A.X, A.Y, B.X, B.Y) <= tol + Thickness / 2 + (Kind == AnnotationKind.Arrow ? 2 : 0);
                case AnnotationKind.Pen: case AnnotationKind.EraseStroke:
                    if (Points.Count == 0) return (p - A).Length <= tol + Thickness / 2;
                    for (int i = 1; i < Points.Count; i++) if (EraserMath.DistToSegment(p.X, p.Y, Points[i - 1].X, Points[i - 1].Y, Points[i].X, Points[i].Y) <= tol + Thickness / 2) return true;
                    return Points.Count == 1 && (p - Points[0]).Length <= tol + Thickness / 2;
                case AnnotationKind.Watermark:
                    foreach (var stamp in WatermarkStampAabbs()) { var wr = stamp; wr.Inflate(tol, tol); if (wr.Contains(p)) return true; }
                    return false;
                case AnnotationKind.Spotlight:
                    var sr = new Rect(A, B); var outer = sr; outer.Inflate(tol, tol); var inner = sr; inner.Inflate(-tol, -tol);
                    return outer.Contains(p) && !inner.Contains(p); // grab by its edge, so clicks inside still reach other objects
                default: var r = Bounds; r.Inflate(tol, tol); return r.Contains(p);
            }
        }

        public void Render(DrawingContext dc)
        {
            var brush = new SolidColorBrush(Color); brush.Freeze();
            var pen = new Pen(brush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; pen.Freeze();
            switch (Kind)
            {
                case AnnotationKind.Line: dc.DrawLine(pen, A, B); break;
                case AnnotationKind.Arrow: dc.DrawGeometry(brush, pen, ArrowGeometry(A, B, Thickness)); break;
                case AnnotationKind.Rectangle: dc.DrawRectangle(null, pen, new Rect(A, B)); break;
                case AnnotationKind.Ellipse:
                    var r = new Rect(A, B); dc.DrawEllipse(null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2); break;
                case AnnotationKind.Highlight:
                    var hb = new SolidColorBrush(Color.FromArgb(90, Color.R, Color.G, Color.B)); hb.Freeze();
                    dc.DrawRectangle(hb, null, new Rect(A, B)); break;
                case AnnotationKind.Text:
                    if (TextBackground)
                    {
                        var bg = new SolidColorBrush(Color); bg.Freeze();
                        dc.DrawRoundedRectangle(bg, null, Bounds, Pad, Pad);
                    }
                    dc.DrawText(Formatted(), new Point(A.X + Pad, A.Y + Pad)); break;
                case AnnotationKind.Step:
                    double rr = StepRadius;
                    dc.DrawEllipse(brush, new Pen(Brushes.White, Math.Max(1.5, rr * 0.12)), A, rr, rr);
                    var nt = Formatted(Number.ToString(), rr * 1.15, Contrast(Color));
                    dc.DrawText(nt, new Point(A.X - nt.Width / 2, A.Y - nt.Height / 2)); break;
                case AnnotationKind.Pen:
                    if (Points.Count == 0) break;
                    var g = new StreamGeometry();
                    using (var c = g.Open()) { c.BeginFigure(Points[0], false, false); c.PolyLineTo(Points.Skip(1).DefaultIfEmpty(Points[0]).ToList(), true, true); }
                    g.Freeze(); dc.DrawGeometry(null, pen, g); break;
                case AnnotationKind.EraseStroke:
                    if (Color.A == 0) break;
                    RenderEraseStroke(dc, brush, pen); break;
                case AnnotationKind.EraseRect:
                    if (Color.A == 0) break;
                    dc.DrawRectangle(brush, null, new Rect(A, B)); break;
                case AnnotationKind.Spotlight: break; // drawn collectively by RenderAll
                case AnnotationKind.Redact: RenderRedact(dc); break;
                case AnnotationKind.Watermark: RenderWatermark(dc); break;
            }
        }

        Rect WatermarkBounds()
        {
            Rect u = Rect.Empty;
            foreach (var r in WatermarkStampAabbs()) u.Union(r);
            return u.IsEmpty ? new Rect(0, 0, 1, 1) : u;
        }

        System.Collections.Generic.IEnumerable<Rect> WatermarkStampAabbs()
        {
            double iw = Math.Max(1, A.X), ih = Math.Max(1, A.Y);
            var (bw, bh) = WatermarkMath.RotatedAabb(iw, ih, Rotation);
            double ox = (bw - iw) / 2, oy = (bh - ih) / 2;
            if (Points.Count == 0) { yield return new Rect(-ox, -oy, bw, bh); yield break; }
            foreach (var p in Points) yield return new Rect(p.X - ox, p.Y - oy, bw, bh);
        }

        void RenderWatermark(DrawingContext dc)
        {
            try
            {
                double iw = Math.Max(1, A.X), ih = Math.Max(1, A.Y);
                var img = _wmImage;
                FormattedText ft = null;
                if (!TextBackground)
                    ft = Formatted(string.IsNullOrEmpty(Text) ? AppIdentity.Name : Text, FontSize, Color);
                int n = Math.Min(Points.Count, WatermarkMath.MaxStamps);
                bool rot = Math.Abs(Rotation) > 0.01 && !double.IsNaN(Rotation) && !double.IsInfinity(Rotation);
                for (int i = 0; i < n; i++)
                {
                    var origin = Points[i];
                    dc.PushTransform(new TranslateTransform(origin.X + iw / 2, origin.Y + ih / 2));
                    if (rot) dc.PushTransform(new RotateTransform(Rotation));
                    dc.PushTransform(new TranslateTransform(-iw / 2, -ih / 2));
                    if (TextBackground)
                    {
                        if (img != null)
                        {
                            dc.PushOpacity(Color.A / 255.0);
                            dc.DrawImage(img, new Rect(0, 0, iw, ih));
                            dc.Pop();
                        }
                    }
                    else if (ft != null) dc.DrawText(ft, new Point(0, 0));
                    dc.Pop();
                    if (rot) dc.Pop();
                    dc.Pop();
                }
            }
            catch (Exception ex) { SnapForge.Services.Logger.Error("Watermark.Render", ex); }
        }

        public bool IsErase => Kind == AnnotationKind.EraseStroke || Kind == AnnotationKind.EraseRect;
        public bool IsTransparentErase => IsErase && Color.A == 0;

        void RenderEraseStroke(DrawingContext dc, Brush brush, Pen pen)
        {
            if (Points.Count <= 1)
            {
                var c = Points.Count == 1 ? Points[0] : A;
                double r = Math.Max(0.5, Thickness / 2);
                dc.DrawEllipse(brush, null, c, r, r);
                return;
            }
            dc.DrawGeometry(null, pen, Polyline(Points));
        }

        static StreamGeometry Polyline(List<Point> pts)
        {
            var g = new StreamGeometry();
            using (var c = g.Open()) { c.BeginFigure(pts[0], false, false); c.PolyLineTo(pts.Skip(1).ToList(), true, true); }
            g.Freeze();
            return g;
        }

        /// <summary>Filled region this erase covers (for punching transparent holes in the base image).</summary>
        public Geometry EraseGeometry()
        {
            if (Kind == AnnotationKind.EraseRect) return new RectangleGeometry(new Rect(A, B));
            if (Kind != AnnotationKind.EraseStroke) return null;
            double r = Math.Max(0.5, Thickness / 2);
            if (Points.Count <= 1)
            {
                var c = Points.Count == 1 ? Points[0] : A;
                return new EllipseGeometry(c, r, r);
            }
            var pen = new Pen(Brushes.Black, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            try { return Polyline(Points).GetWidenedPathGeometry(pen); }
            catch { return new EllipseGeometry(Points[0], r, r); }
        }

        /// <summary>Draws all annotations; spotlights dim everything outside the union of their rectangles (one layer).</summary>
        /// <summary>Base image used by Redact (live pixelate) annotations during the current RenderAll.</summary>
        [ThreadStatic] private static BitmapSource _base;
        [NonSerialized] private BitmapSource _rcBase; private Int32Rect _rcRect; private int _rcBlock; private Drawing _rcDrawing;

        private void RenderRedact(DrawingContext dc)
        {
            var b = _base;
            var r = new Rect(A, B);
            if (b == null) { dc.DrawRectangle(Brushes.Gray, null, r); return; }
            var ir = new Int32Rect((int)Math.Max(0, Math.Floor(r.X)), (int)Math.Max(0, Math.Floor(r.Y)), 0, 0);
            ir.Width = (int)Math.Min(b.PixelWidth, Math.Ceiling(r.Right)) - ir.X; ir.Height = (int)Math.Min(b.PixelHeight, Math.Ceiling(r.Bottom)) - ir.Y;
            if (ir.Width < 1 || ir.Height < 1) return;
            int block = Math.Max(6, (int)(Thickness * 3));
            if (!ReferenceEquals(_rcBase, b) || !_rcRect.Equals(ir) || _rcBlock != block || _rcDrawing == null)
            {
                var crop = new CroppedBitmap(b, ir);
                double k = 1.0 / block;
                var small = new TransformedBitmap(crop, new ScaleTransform(Math.Max(k, 1.0 / ir.Width), Math.Max(k, 1.0 / ir.Height)));
                small.Freeze();
                var grp = new DrawingGroup();
                RenderOptions.SetBitmapScalingMode(grp, BitmapScalingMode.NearestNeighbor);
                using (var gdc = grp.Open()) gdc.DrawImage(small, new Rect(ir.X, ir.Y, ir.Width, ir.Height));
                grp.Freeze();
                _rcBase = b; _rcRect = ir; _rcBlock = block; _rcDrawing = grp;
            }
            dc.DrawDrawing(_rcDrawing);
        }

        public static void RenderAll(DrawingContext dc, IEnumerable<Annotation> items, Size canvas, Annotation draft = null, BitmapSource baseImage = null, bool drawBase = false)
        {
            _base = baseImage;
            var list = items.ToList(); if (draft != null) list.Add(draft);
            var full = new Rect(canvas);
            if (drawBase && baseImage != null)
            {
                Geometry clip = new RectangleGeometry(full);
                foreach (var a in list)
                {
                    if (!a.IsTransparentErase) continue;
                    var g = a.EraseGeometry();
                    if (g != null) clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, g);
                }
                dc.PushClip(clip);
                dc.DrawImage(baseImage, full);
                dc.Pop();
            }
            var spots = list.Where(a => a.Kind == AnnotationKind.Spotlight).ToList();
            if (spots.Count > 0)
            {
                Geometry hole = Geometry.Empty;
                foreach (var sp in spots) hole = new CombinedGeometry(GeometryCombineMode.Union, hole, new RectangleGeometry(new Rect(sp.A, sp.B)));
                var dim = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), hole);
                var b = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)); b.Freeze();
                dc.DrawGeometry(b, null, dim);
            }
            foreach (var a in list) if (a.Kind != AnnotationKind.Spotlight && !a.IsTransparentErase) a.Render(dc);
        }

        public static Geometry ArrowGeometry(Point a, Point b, double t)
        {
            var g = new GeometryGroup();
            var v = b - a;
            if (v.Length < 1) { g.Children.Add(new LineGeometry(a, b)); return g; }
            v.Normalize();
            double head = Math.Max(12, t * 4);
            var n = new Vector(-v.Y, v.X);
            var baseP = b - v * head;
            g.Children.Add(new LineGeometry(a, baseP));
            var fig = new PathFigure { StartPoint = b, IsClosed = true, IsFilled = true };
            fig.Segments.Add(new LineSegment(baseP + n * head * 0.5, true));
            fig.Segments.Add(new LineSegment(baseP - n * head * 0.5, true));
            g.Children.Add(new PathGeometry(new[] { fig }));
            return g;
        }

    }
}

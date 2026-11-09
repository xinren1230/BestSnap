using System.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Models;
using SnapForge.Services;

namespace SnapForge.ViewModels
{
    /// <summary>One undoable edit. Commands that hold bitmaps report their size so history can be capped by memory.</summary>
    public interface IEditCommand
    {
        void Do(EditorDocument d);
        void Undo(EditorDocument d);
        long Bytes { get; }
    }

    /// <summary>Base bitmap + editable annotation objects + command-based undo/redo.</summary>
    public class EditorDocument : INotifyPropertyChanged
    {
        private readonly LinkedList<IEditCommand> _undo = new LinkedList<IEditCommand>();
        private readonly Stack<IEditCommand> _redo = new Stack<IEditCommand>();
        private BitmapSource _image;
        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>Raised when annotations change (add/remove/move/style).</summary>
        public event Action AnnotationsChanged;
        public bool IsDirty { get; set; }
        public static long MaxHistoryBytes = 512L * 1024 * 1024;
        public const int MaxSteps = 100;

        public List<Annotation> Annotations { get; } = new List<Annotation>();

        public EditorDocument(BitmapSource img) { _image = img; }

        public BitmapSource Image
        {
            get => _image;
            internal set { _image = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText))); }
        }

        public string SizeText => $"{_image.PixelWidth} × {_image.PixelHeight}";
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        internal void RaiseAnnotations() => AnnotationsChanged?.Invoke();

        public void Execute(IEditCommand c)
        {
            if (c == null) return;
            c.Do(this);
            _undo.AddLast(c); _redo.Clear();
            while (_undo.Count > MaxSteps || (_undo.Count > 1 && HistoryBytes() > MaxHistoryBytes)) _undo.RemoveFirst();
            IsDirty = true;
        }

        /// <summary>Replace the base bitmap (pixelate/blur) as one undoable step.</summary>
        public void Apply(BitmapSource next) { if (next != null) Execute(new RasterCommand(_image, next, new Vector(0, 0), 1)); }

        public void Undo() { if (_undo.Count == 0) return; var c = _undo.Last.Value; _undo.RemoveLast(); c.Undo(this); _redo.Push(c); IsDirty = true; }
        public void Redo() { if (_redo.Count == 0) return; var c = _redo.Pop(); c.Do(this); _undo.AddLast(c); IsDirty = true; }

        private long HistoryBytes()
        {
            long t = 0;
            foreach (var c in _undo) t += c.Bytes;
            foreach (var c in _redo) t += c.Bytes;
            return t;
        }

        /// <summary>Bitmap with all annotations burned in (for copy/save). Transparent erases punch holes (PNG alpha).</summary>
        public BitmapSource Render()
        {
            if (Annotations.Count == 0) return _image;
            int w = _image.PixelWidth, h = _image.PixelHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
                Annotation.RenderAll(dc, Annotations, new Size(w, h), null, _image, drawBase: true);
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return CaptureService.Normalize(rtb);
        }

        internal static long BytesOf(BitmapSource b) => b == null ? 0 : (long)b.PixelWidth * b.PixelHeight * 4;
    }

    public class AddAnnotationCommand : IEditCommand
    {
        private readonly Annotation _a; public AddAnnotationCommand(Annotation a) { _a = a; }
        public long Bytes => 64;
        public void Do(EditorDocument d) { d.Annotations.Add(_a); d.RaiseAnnotations(); }
        public void Undo(EditorDocument d) { d.Annotations.Remove(_a); d.RaiseAnnotations(); }
    }

    /// <summary>Several commands undone/redone as one step (e.g. auto-redact).</summary>
    public class CompositeCommand : IEditCommand
    {
        private readonly List<IEditCommand> _items; public CompositeCommand(IEnumerable<IEditCommand> items) { _items = new List<IEditCommand>(items); }
        public long Bytes => _items.Sum(i => i.Bytes);
        public void Do(EditorDocument d) { foreach (var i in _items) i.Do(d); }
        public void Undo(EditorDocument d) { for (int i = _items.Count - 1; i >= 0; i--) _items[i].Undo(d); }
    }

    public class RemoveAnnotationCommand : IEditCommand
    {
        private readonly Annotation _a; private int _index;
        public RemoveAnnotationCommand(Annotation a) { _a = a; }
        public long Bytes => 64;
        public void Do(EditorDocument d) { _index = d.Annotations.IndexOf(_a); if (_index >= 0) d.Annotations.RemoveAt(_index); d.RaiseAnnotations(); }
        public void Undo(EditorDocument d) { d.Annotations.Insert(Math.Clamp(_index, 0, d.Annotations.Count), _a); d.RaiseAnnotations(); }
    }

    public class MoveAnnotationCommand : IEditCommand
    {
        private readonly Annotation _a; private readonly Vector _v;
        public MoveAnnotationCommand(Annotation a, Vector v) { _a = a; _v = v; }
        public long Bytes => 64;
        public void Do(EditorDocument d) { _a.Offset(_v); d.RaiseAnnotations(); }
        public void Undo(EditorDocument d) { _a.Offset(-_v); d.RaiseAnnotations(); }
    }

    /// <summary>Change style properties (colour, thickness, text, font, number…) of one annotation.</summary>
    public class StyleAnnotationCommand : IEditCommand
    {
        private readonly Annotation _a, _before, _after;
        public StyleAnnotationCommand(Annotation a, Color color, double thickness, string text)
            : this(a, x => { x.Color = color; x.Thickness = thickness; x.Text = text; }) { }
        public StyleAnnotationCommand(Annotation a, Action<Annotation> change) { _a = a; _before = a.Clone(); _after = a.Clone(); change(_after); }
        public long Bytes => 64;
        public void Do(EditorDocument d) { _a.CopyFrom(_after); d.RaiseAnnotations(); }
        public void Undo(EditorDocument d) { _a.CopyFrom(_before); d.RaiseAnnotations(); }
    }

    /// <summary>Base-bitmap change. Crop (offset) and resize (scale) also transform annotations so they stay in place.</summary>
    public class RasterCommand : IEditCommand
    {
        private readonly BitmapSource _before, _after; private readonly Vector _offset; private readonly double _scale;
        public RasterCommand(BitmapSource before, BitmapSource after, Vector offset, double scale) { _before = before; _after = after; _offset = offset; _scale = scale; }
        public long Bytes => EditorDocument.BytesOf(_before) + EditorDocument.BytesOf(_after);
        public void Do(EditorDocument d)
        {
            d.Image = _after;
            if (_offset.LengthSquared > 0 || _scale != 1)
                foreach (var a in d.Annotations) a.Transform(p => (Point)(((Vector)p - _offset) * _scale), _scale);
            d.RaiseAnnotations();
        }
        public void Undo(EditorDocument d)
        {
            d.Image = _before;
            if (_offset.LengthSquared > 0 || _scale != 1)
                foreach (var a in d.Annotations) a.Transform(p => (Point)((Vector)p / _scale + _offset), 1 / _scale);
            d.RaiseAnnotations();
        }
    }
}

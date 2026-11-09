using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Fluent-style chrome shared by the floating bars and the editor toolbar.</summary>
    public static class SmartUi
    {
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly Color AccentA = Color.FromRgb(124, 58, 237), AccentB = Color.FromRgb(37, 99, 235);
        public static Brush Accent => Frozen(new LinearGradientBrush(AccentA, AccentB, 0));
        public static Brush AccentHover => Frozen(new LinearGradientBrush(Color.FromRgb(147, 92, 246), Color.FromRgb(59, 130, 246), 0));
        public static Brush BarBackground => Frozen(new LinearGradientBrush(
            Color.FromArgb(232, 28, 29, 36), Color.FromArgb(218, 42, 44, 54), 90));
        public static Brush BarBorder => Frozen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)));
        public static Brush LightFg => Frozen(new SolidColorBrush(Color.FromRgb(30, 30, 34)));
        public static Brush LightBarBackground => Frozen(new SolidColorBrush(Color.FromArgb(240, 246, 246, 249)));
        public static Brush LightBarBorder => Frozen(new SolidColorBrush(Color.FromRgb(226, 226, 232)));
        public static Brush QuietAccentDark => Frozen(new SolidColorBrush(Color.FromArgb(55, 124, 58, 237)));
        public static Brush QuietAccentLight => Frozen(new SolidColorBrush(Color.FromArgb(36, 124, 58, 237)));
        private static Brush Frozen(Brush b) { b.Freeze(); return b; }
        private static Brush Solid(byte a, byte r, byte g, byte b) => Frozen(new SolidColorBrush(Color.FromArgb(a, r, g, b)));

        public const string GOcr = "\uE8D2", GFormula = "\uE8EF", GTranslate = "\uE774", GBlur = "\uE7B3", GRedact = "\uE72E", GQr = "\uED14";
        public const string GCopy = "\uE8C8", GSave = "\uE74E", GSaveAs = "\uE792", GUndo = "\uE7A7", GRedo = "\uE7A6";
        public const string GArrow = "\uE1FD", GRect = "\uE739", GText = "\uE8D3", GCrop = "\uE7A8", GMore = "\uE712", GEraser = "\uE75C";
        public const string GPin = "\uE718", GEdit = "\uE70F", GClose = "\uE711", GUpload = "\uE898", GMin = "\uE921", GWatermark = "\uEA18";

        /// <summary>" (Ctrl+Shift+O)" for a configured hotkey action, else "".</summary>
        public static string Key(string action)
        {
            try { var g = App.Settings.Current.Hotkeys?.FirstOrDefault(h => h.Action == action)?.Gesture; return string.IsNullOrEmpty(g) ? "" : "  (" + g + ")"; } catch { return ""; }
        }

        public static Effect Shadow() => new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Direction = 270, Opacity = 0.38, Color = Colors.Black };

        /// <summary>Fluent segment tabs (underline/pill headers) plus a page host. No scroll.</summary>
        public static FrameworkElement SegmentTabs(params (string Header, UIElement Page)[] pages)
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var host = new Border { Padding = new Thickness(2, 4, 2, 2), SnapsToDevicePixels = true };
            Button[] btns = new Button[pages.Length];
            void Select(int i)
            {
                for (int k = 0; k < btns.Length; k++)
                {
                    btns[k].Background = k == i ? QuietAccentLight : Brushes.Transparent;
                    btns[k].FontWeight = k == i ? FontWeights.SemiBold : FontWeights.Normal;
                }
                host.Child = pages[i].Page;
            }
            for (int i = 0; i < pages.Length; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Content = pages[i].Header, Height = 32, Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(14, 0, 14, 0), Cursor = Cursors.Hand, Focusable = false,
                    BorderThickness = new Thickness(0), Foreground = LightFg, Background = Brushes.Transparent,
                    Style = ButtonStyle(false)
                };
                b.Click += (s, e) => Select(idx);
                btns[i] = b;
                bar.Children.Add(b);
            }
            Select(0);
            var root = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
            root.Children.Add(host);
            return root;
        }

        /// <summary>Rounded floating-bar container (acrylic-like dark fill, subtle border + shadow).</summary>
        public static Border Bar(UIElement child) => new Border
        {
            Child = child, CornerRadius = new CornerRadius(14), Padding = new Thickness(6), Margin = new Thickness(10),
            Background = BarBackground, BorderBrush = BarBorder, BorderThickness = new Thickness(1), Effect = Shadow()
        };

        public static UIElement Divider(bool dark = true) => new Border
        {
            Width = 1, Margin = new Thickness(6, 8, 6, 8),
            Background = dark ? Solid(55, 255, 255, 255) : Solid(40, 0, 0, 0)
        };

        public static object IconLabel(string glyph, string label, bool dark)
        {
            var fg = dark ? Brushes.White : LightFg;
            var icon = new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = string.IsNullOrEmpty(label) ? 16 : 14,
                Foreground = fg, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
            if (string.IsNullOrEmpty(label)) return icon;
            icon.Margin = new Thickness(0, 0, 6, 0);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(icon);
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = fg, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        /// <summary>Fluent icon(+optional label) button. <paramref name="accent"/> is a quiet tint for smart tools, not a loud pill.</summary>
        public static Button Fluent(string glyph, string label, string tip, Action a, bool dark = true, bool accent = false)
        {
            var b = new Button
            {
                Content = IconLabel(glyph, label, dark), ToolTip = tip,
                Foreground = dark ? Brushes.White : LightFg,
                Background = accent ? (dark ? QuietAccentDark : QuietAccentLight) : Brushes.Transparent,
                MinWidth = string.IsNullOrEmpty(label) ? 32 : 0, Height = 32,
                Margin = new Thickness(2), Padding = new Thickness(string.IsNullOrEmpty(label) ? 8 : 10, 0, string.IsNullOrEmpty(label) ? 8 : 10, 0),
                Cursor = Cursors.Hand, Focusable = false, BorderThickness = new Thickness(0),
                Style = ButtonStyle(dark)
            };
            b.Click += (s, e) => a();
            return b;
        }

        public static ToggleButton FluentToggle(string glyph, string label, string tip, bool dark = false)
        {
            return new ToggleButton
            {
                Content = IconLabel(glyph, label, dark), ToolTip = tip,
                Foreground = dark ? Brushes.White : LightFg, Background = Brushes.Transparent,
                Height = 32, Margin = new Thickness(2), Padding = new Thickness(10, 0, 10, 0),
                Cursor = Cursors.Hand, Focusable = false, BorderThickness = new Thickness(0),
                Style = ToggleStyle(dark)
            };
        }

        /// <summary>Accent pill kept for compatibility; prefer <see cref="Fluent"/> with accent=true.</summary>
        public static Button Pill(string glyph, string label, string tip, Action a) => Fluent(glyph, label, tip, a, dark: true, accent: true);

        /// <summary>Flat icon(+optional label) button for dark bars.</summary>
        public static Button Flat(string glyph, string label, string tip, Action a) => Fluent(glyph, label, tip, a, dark: true, accent: false);

        private static Style Make(Type target, bool dark, bool toggle)
        {
            var tpl = new ControlTemplate(target);
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetValue(Border.SnapsToDevicePixelsProperty, true);

            var grid = new FrameworkElementFactory(typeof(Grid));
            var veil = new FrameworkElementFactory(typeof(Border), "veil");
            veil.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            veil.SetValue(Border.IsHitTestVisibleProperty, false);
            veil.SetValue(UIElement.OpacityProperty, 0.0);
            veil.SetValue(Border.BackgroundProperty, dark ? Solid(40, 255, 255, 255) : Solid(18, 0, 0, 0));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            grid.AppendChild(veil);
            grid.AppendChild(cp);
            bd.AppendChild(grid);
            tpl.VisualTree = bd;

            var hv = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hv.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "veil"));
            tpl.Triggers.Add(hv);
            var pr = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pr.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "veil"));
            pr.Setters.Add(new Setter(Border.BackgroundProperty, dark ? Solid(70, 255, 255, 255) : Solid(32, 0, 0, 0), "veil"));
            tpl.Triggers.Add(pr);
            if (toggle)
            {
                var chk = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                chk.Setters.Add(new Setter(Border.BackgroundProperty, dark ? Solid(120, 124, 58, 237) : Solid(55, 124, 58, 237), "bd"));
                tpl.Triggers.Add(chk);
            }
            var dis = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            dis.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            tpl.Triggers.Add(dis);

            var st = new Style(target);
            st.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            st.Setters.Add(new Setter(Control.BorderBrushProperty, dark ? Solid(40, 255, 255, 255) : Solid(24, 0, 0, 0)));
            st.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return st;
        }

        /// <summary>Ignore mouse-wheel and track-clicks; only the thumb drags the value.</summary>
        public static Slider Lock(Slider s)
        {
            s.Focusable = false;
            s.IsMoveToPointEnabled = false;
            s.PreviewMouseWheel += (o, e) => e.Handled = true;
            s.PreviewMouseLeftButtonDown += (o, e) =>
            {
                var d = e.OriginalSource as DependencyObject;
                while (d != null && !ReferenceEquals(d, s))
                {
                    if (d is Thumb) return;
                    if (d is RepeatButton) { e.Handled = true; return; }
                    d = VisualTreeHelper.GetParent(d);
                }
            };
            return s;
        }

        private static Style _btnDark, _btnLight, _togDark, _togLight, _pill, _flat;
        public static Style ButtonStyle(bool dark) => dark ? (_btnDark ??= Make(typeof(Button), true, false)) : (_btnLight ??= Make(typeof(Button), false, false));
        public static Style ToggleStyle(bool dark) => dark ? (_togDark ??= Make(typeof(ToggleButton), true, true)) : (_togLight ??= Make(typeof(ToggleButton), false, true));
        public static Style PillStyle => _pill ??= ButtonStyle(true);
        public static Style FlatStyle => _flat ??= ButtonStyle(true);
    }
}

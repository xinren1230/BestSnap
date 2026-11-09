using System.Windows;
using System.Windows.Controls;

using SnapForge.Services;

namespace SnapForge.Views
{
    public static class InputDialog
    {
        public static string Show(Window owner, string title, string prompt, string initial)
        {
            var w = new Window { Title = title, Owner = owner, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var box = new TextBox { Text = initial, MinWidth = 280, Margin = new Thickness(0, 6, 0, 10) };
            var ok = new Button { Content = L.T("确定"), IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("取消"), IsCancel = true, Width = 80 };
            ok.Click += (s, e) => w.DialogResult = true;
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            btns.Children.Add(ok); btns.Children.Add(cancel);
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock { Text = prompt }); sp.Children.Add(box); sp.Children.Add(btns);
            w.Content = sp;
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            return w.ShowDialog() == true ? box.Text : null;
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Manage upload/share destinations. Passwords are encrypted with DPAPI (CurrentUser) before saving.</summary>
    public class UploadTargetsWindow : Window
    {
        private readonly ObservableCollection<UploadTarget> _items;
        private readonly ListBox _list = new ListBox { DisplayMemberPath = "Name", Width = 180 };
        private readonly TextBox _name = new TextBox(), _url = new TextBox(), _user = new TextBox(), _public = new TextBox();
        private readonly PasswordBox _pwd = new PasswordBox();
        private readonly ComboBox _kind = new ComboBox { DisplayMemberPath = "DisplayName", SelectedValuePath = "Id" };
        private readonly TextBlock _hint = new TextBlock { Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        private bool _pwdChanged, _loading;

        public UploadTargetsWindow()
        {
            Title = L.T("上传/分享目标") + " - " + AppIdentity.Name; Width = 640; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _items = new ObservableCollection<UploadTarget>(App.Settings.Current.UploadTargets.Select(Copy));
            _list.ItemsSource = _items;
            _kind.ItemsSource = Uploads.All();
            _kind.SelectionChanged += (s, e) => UpdateHint();

            var form = new Grid { Margin = new Thickness(10, 0, 0, 0) };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); form.ColumnDefinitions.Add(new ColumnDefinition());
            int row = 0;
            void Row(string l, FrameworkElement el)
            {
                form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var t = new TextBlock { Text = l, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(t, row); Grid.SetRow(el, row); Grid.SetColumn(el, 1); el.Margin = new Thickness(0, 3, 0, 3);
                form.Children.Add(t); form.Children.Add(el); row++;
            }
            Row(L.T("名称"), _name); Row(L.T("方式"), _kind); Row(L.T("地址"), _url); Row(L.T("用户名"), _user); Row(L.T("密码"), _pwd); Row(L.T("公开链接前缀"), _public); Row("", _hint);
            _pwd.PasswordChanged += (s, e) => { if (!_loading) _pwdChanged = true; };

            var leftBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var add = new Button { Content = L.T("新建"), Width = 60, Margin = new Thickness(0, 0, 4, 0) }; var del = new Button { Content = L.T("删除"), Width = 60 };
            leftBtns.Children.Add(add); leftBtns.Children.Add(del);
            var left = new DockPanel(); DockPanel.SetDock(leftBtns, Dock.Bottom); left.Children.Add(leftBtns); left.Children.Add(_list);
            _list.Height = 260;

            var test = new Button { Content = L.T("测试上传"), Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            var ok = new Button { Content = L.T("保存"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("取消"), Width = 90, IsCancel = true };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            btns.Children.Add(test); btns.Children.Add(ok); btns.Children.Add(cancel);

            var main = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(btns, Dock.Bottom); main.Children.Add(btns);
            DockPanel.SetDock(left, Dock.Left); main.Children.Add(left); main.Children.Add(form);
            Content = main;

            UploadTarget current = null;
            void Store()
            {
                if (current == null) return;
                current.Name = string.IsNullOrWhiteSpace(_name.Text) ? L.T("未命名") : _name.Text.Trim();
                current.Kind = _kind.SelectedValue as string ?? "folder"; current.Url = _url.Text.Trim(); current.User = _user.Text.Trim(); current.PublicUrlPrefix = _public.Text.Trim();
                if (_pwdChanged) current.ProtectedSecret = Secrets.Protect(_pwd.Password);
                _list.Items.Refresh();
            }
            void Load(UploadTarget t)
            {
                _loading = true; current = t; _pwdChanged = false;
                form.IsEnabled = t != null;
                _name.Text = t?.Name ?? ""; _kind.SelectedValue = t?.Kind ?? "folder"; _url.Text = t?.Url ?? ""; _user.Text = t?.User ?? ""; _public.Text = t?.PublicUrlPrefix ?? "";
                _pwd.Password = string.IsNullOrEmpty(t?.ProtectedSecret) ? "" : "********"; // placeholder; real secret never shown
                _loading = false; UpdateHint();
            }
            _list.SelectionChanged += (s, e) => { Store(); Load(_list.SelectedItem as UploadTarget); };
            add.Click += (s, e) => { Store(); var t = new UploadTarget { Name = L.T("新目标"), Kind = "folder", Url = App.Settings.Current.SaveFolder }; _items.Add(t); _list.SelectedItem = t; };
            del.Click += (s, e) => { if (current != null) { var t = current; current = null; _items.Remove(t); Load(null); } };
            ok.Click += (s, e) => { Store(); App.Settings.Current.UploadTargets = _items.ToList(); App.Settings.Save(); Close(); };
            test.Click += async (s, e) =>
            {
                Store(); if (current == null) return;
                var tmp = Path.Combine(Path.GetTempPath(), "bestsnap-test-" + DateTime.Now.ToString("HHmmss") + ".txt");
                File.WriteAllText(tmp, AppIdentity.Name + " upload test " + DateTime.Now);
                test.IsEnabled = false;
                try { var url = await Uploads.UploadAsync(current, tmp); MessageBox.Show(this, L.T("上传成功") + ":\n" + url, AppIdentity.Name); }
                catch (Exception ex) { MessageBox.Show(this, L.T("上传失败") + ":\n" + ex.Message, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error); }
                finally { test.IsEnabled = true; try { File.Delete(tmp); } catch { } }
            };
            Load(null);
            if (_items.Count > 0) _list.SelectedIndex = 0;
        }

        private void UpdateHint()
        {
            _hint.Text = (_kind.SelectedValue as string) switch
            {
                "ftp" => L.T("地址示例：ftp://example.com/pub/shots 或 ftps://…"),
                "webdav" => L.T("地址示例：https://dav.jianguoyun.com/dav/shots 或 https://cloud.example.com/remote.php/dav/files/me/shots"),
                _ => L.T("地址为本机或网络共享文件夹，例如 D:\\Shots 或 \\\\nas\\share\\shots"),
            } + "\n" + L.T("密码使用 Windows DPAPI 加密保存，仅当前 Windows 用户可解密。");
        }

        private static UploadTarget Copy(UploadTarget t) => new UploadTarget { Name = t.Name, Kind = t.Kind, Url = t.Url, User = t.User, ProtectedSecret = t.ProtectedSecret, PublicUrlPrefix = t.PublicUrlPrefix };

        /// <summary>Context menu listing configured targets; uploads the file produced by <paramref name="makeFile"/>.</summary>
        public static ContextMenu BuildUploadMenu(Func<string> makeFile, Action<string> status)
        {
            var m = new ContextMenu();
            foreach (var t in App.Settings.Current.UploadTargets)
            {
                var i = new MenuItem { Header = t.Name };
                var target = t;
                i.Click += async (s, e) =>
                {
                    string f = null;
                    try
                    {
                        f = makeFile(); status(L.T("正在上传…"));
                        var url = await Uploads.UploadAsync(target, f);
                        try { Clipboard.SetText(url); } catch { }
                        status(L.T("已上传，链接已复制") + ": " + url);
                    }
                    catch (Exception ex) { Logger.Error("Upload", ex); status(L.T("上传失败") + ": " + ex.Message); }
                };
                m.Items.Add(i);
            }
            if (m.Items.Count > 0) m.Items.Add(new Separator());
            var cfg = new MenuItem { Header = L.T("管理上传目标…") };
            cfg.Click += (s, e) => new UploadTargetsWindow().ShowDialog();
            m.Items.Add(cfg);
            return m;
        }
    }
}

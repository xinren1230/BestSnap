using System;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap
{
    public partial class App : Application
    {
        static App() { Services.EmbeddedRuntime.Init(); }
        private const string MutexName = AppIdentity.MutexName;
        private const string EventName = AppIdentity.ShowEventName;
        private static Mutex _mutex;
        private static bool _ownsMutex;
        private static EventWaitHandle _showEvent;

        public static SettingsService Settings { get; private set; }
        public static BitmapFrame AppIcon { get; private set; }
        public static AppController Controller { get; private set; }
        /// <summary>True when this process only executes a command line (no tray, no hotkeys).</summary>
        public static bool IsCliOnly { get; private set; }
        private static readonly CancellationTokenSource _cliCts = new CancellationTokenSource();

        private async void RunCliOnly(string[] args)
        {
            int code = ExitCodes.Error;
            try
            {
                DispatcherUnhandledException += (o, a) => { a.Handled = true; Logger.Error("CLI UI", a.Exception); };
                AppIcon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/bestsnap.ico"));
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, a) => { if (s is Window w) { if (w.Icon == null) w.Icon = AppIcon; if (!IsRecordedOverlay(w)) BestSnap.Native.CaptureExclusion.Apply(w); } }));
                Settings = new SettingsService();
                Settings.Load();
                code = await CliRunner.RunAsync(args);
            }
            catch (Exception ex) { Logger.Error("CLI", ex); }
            Shutdown(code);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException -= OnUiException;
            DispatcherUnhandledException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException -= OnDomainException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException -= OnTaskException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnTaskException;
            // Startup warmup 1: multi-core JIT profile (second and later starts JIT hot methods in the background).
            try
            {
                AppPaths.EnsureMigrated();
                var prof = AppPaths.JitProfile;
                System.IO.Directory.CreateDirectory(prof);
                System.Runtime.ProfileOptimization.SetProfileRoot(prof);
                System.Runtime.ProfileOptimization.StartProfile("startup.jitprofile");
            }
            catch { }
            bool cli = CliOptions.IsCommand(e.Args);
            _mutex = new Mutex(true, MutexName, out _ownsMutex);
            if (cli)
            {
                if (!_ownsMutex)
                {
                    // Forward to the running instance over the named pipe and return its exit code.
                    var code = CliRunner.TryForward(e.Args);
                    if (code.HasValue) { Shutdown(code.Value); return; }
                }
                else { _mutex.ReleaseMutex(); _ownsMutex = false; } // a CLI-only run must not block a normal start
                IsCliOnly = true;
                RunCliOnly(e.Args);
                return;
            }
            if (!_ownsMutex)
            {
                // Already running: ask the first instance to show its toolbar, then exit.
                try { using (var ev = EventWaitHandle.OpenExisting(EventName)) ev.Set(); } catch { }
                Shutdown();
                return;
            }
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            var t = new Thread(() =>
            {
                while (true)
                {
                    try { _showEvent.WaitOne(); } catch { return; }
                    Dispatcher.BeginInvoke(new Action(() => Controller?.ShowToolbar()));
                }
            }) { IsBackground = true, Name = "BestSnapInstanceListener" };
            t.Start();

            base.OnStartup(e);
            AppIcon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/bestsnap.ico"));
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, a) => { if (s is Window w) { if (w.Icon == null) w.Icon = AppIcon; if (ShouldExclude(w)) BestSnap.Native.CaptureExclusion.Apply(w); } }));
            Settings = new SettingsService();
            Settings.Load();
            AutoStart.MigrateLegacy();
            Controller = new AppController(Settings);
            Controller.Start();
            CliRunner.StartServer(Dispatcher, _cliCts.Token);
            Dispatcher.BeginInvoke(new Action(Warmup), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (Settings.LoadError != null)
                MessageBox.Show(Settings.LoadError, L.T("BestSnap 设置"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// Startup warmup 2: when idle, touch the code paths of the first capture (GDI copy, PNG encode, text layout,
        /// plugin discovery) so the first hotkey press shows the overlay without a JIT/first-use stall.
        /// </summary>
        private static void Warmup()
        {
            try
            {
                var img = CaptureService.CaptureRect(new Int32Rect(0, 0, 8, 8));
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img));
                using (var ms = new System.IO.MemoryStream()) enc.Save(ms);
                _ = new System.Windows.Media.FormattedText(L.T("BestSnap 截图"), System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new System.Windows.Media.Typeface("Microsoft YaHei UI"), 12, System.Windows.Media.Brushes.Black, 1.0);
                System.Threading.Tasks.Task.Run(() => { try { _ = PluginHost.Ocr; System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(HistoryService).TypeHandle); } catch { } });
            }
            catch (Exception ex) { Logger.Error("Warmup", ex); }
        }

        /// <summary>Overlay/toolbar/recording helpers are always hidden from capture; other windows follow the setting.</summary>
        /// <summary>Windows that must appear IN recordings (keystroke overlay, webcam bubble): never capture-excluded.</summary>
        internal static bool IsRecordedOverlay(Window w) => w is Views.KeystrokeOverlay || w is Views.WebcamBubble;

        private static bool ShouldExclude(Window w) => AppIdentity.GetEnv("SHOW_IN_CAPTURE") != "1" && !IsRecordedOverlay(w) && (
            w is Views.CaptureToolbar || w is Views.RecordingControlBar || w is Views.CountdownWindow ||
            w is Views.MagnifierWindow || w is Views.SelectionOverlay || w is Views.ScrollCaptureWindow || w is Views.RegionBorderWindow || w is Views.QuickActionBar ||
            w is Views.AudioRecorderWindow || w is Views.RecordingDoneWindow ||
            (Settings?.Current?.ExcludeAppWindowsFromCapture ?? true));

        static void OnUiException(object o, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs a)
        {
            a.Handled = true;
            ReportCrash("UI", a.Exception);
        }
        static void OnDomainException(object o, UnhandledExceptionEventArgs a) => ReportCrash("AppDomain", a.ExceptionObject as Exception);
        static void OnTaskException(object o, System.Threading.Tasks.UnobservedTaskExceptionEventArgs a)
        {
            a.SetObserved();
            Logger.Error("Task", a.Exception);
        }

        private static int _crashDialogs;
        /// <summary>Log to %AppData%\BestSnap\logs and keep the app running. Avoid a modal storm.</summary>
        private static void ReportCrash(string source, Exception ex)
        {
            Logger.Error(source, ex ?? new Exception("unknown"));
            if (System.Threading.Interlocked.Increment(ref _crashDialogs) > 1) { System.Threading.Interlocked.Decrement(ref _crashDialogs); return; }
            try
            {
                MessageBox.Show(AppIdentity.Name + " 出现错误：\n" + ex?.Message + "\n\n详细信息已写入日志：\n" + Logger.Folder,
                    AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            finally { System.Threading.Interlocked.Decrement(ref _crashDialogs); }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _cliCts.Cancel();
            Controller?.Dispose();
            Settings?.Save();
            if (_ownsMutex) { try { _mutex.ReleaseMutex(); } catch { } }
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}

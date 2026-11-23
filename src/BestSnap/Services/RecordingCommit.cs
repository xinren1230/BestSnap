using System;
using System.IO;
using BestSnap.Plugins;
using BestSnap.Views;

namespace BestSnap.Services
{
    /// <summary>Move a finished recording out of temp into the user's save folder and show the saved prompt.</summary>
    public static class RecordingCommit
    {
        public static string SaveNow(string source, SettingsService svc)
        {
            if (string.IsNullOrWhiteSpace(source)) return source;
            try { TempRecordingStore.MarkComplete(source, DateTime.UtcNow); } catch { }
            bool audio = TrimRangeMath.IsAudioPath(source);
            string destFolder = RecordingSavePath.ResolveDestFolder(
                audio,
                svc?.Current?.Recording?.SaveFolder,
                svc?.Current?.AudioRecording?.SaveFolder,
                svc?.Current?.SaveFolder);
            try { Directory.CreateDirectory(destFolder); } catch { }
            return RecordingSavePath.PromoteToFolder(source, destFolder);
        }

        public static void ShowSaved(string saved, SettingsService svc)
        {
            if (string.IsNullOrWhiteSpace(saved)) return;
            new RecordingSavedWindow(saved, svc).Show();
        }

        public static void OpenEditor(string file)
        {
            if (!RecordingSavePath.IsUsableMedia(file))
            {
                System.Windows.MessageBox.Show(
                    L.T("无法打开视频：文件不存在或大小为 0。"),
                    AppIdentity.Name, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }
            if (VideoStudioHost.CanHostEditor())
            {
                new VideoEditorWindow(file).Show();
                return;
            }
            if (TrimRangeMath.CanTrimPath(file))
            {
                new TrimWindow(file, onApplied: null, protectOriginal: true).Show();
                return;
            }
            System.Windows.MessageBox.Show(
                L.T("未安装 WebView2 运行时或找不到编辑器文件，无法打开视频编辑。"),
                AppIdentity.Name, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using BestSnap.Plugins;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace BestSnap.Services
{
    /// <summary>WASAPI microphone and/or loopback capture, mixed to 44.1 kHz stereo WAV, optional MP3 via Media Foundation.</summary>
    public sealed class AudioRecorderService : IDisposable
    {
        WasapiCapture _mic;
        WasapiLoopbackCapture _loop;
        WavPcmWriter _wav;
        FileStream _fs;
        Thread _mix;
        volatile bool _run, _paused;
        readonly ConcurrentQueue<short[]> _micQ = new ConcurrentQueue<short[]>();
        readonly ConcurrentQueue<short[]> _sysQ = new ConcurrentQueue<short[]>();
        readonly List<short> _micLeftover = new List<short>();
        readonly List<short> _sysLeftover = new List<short>();
        string _wavPath, _finalPath;
        AudioRecordFileFormat _format;
        double _micVol = 1, _sysVol = 1;
        bool _useMic, _useSys;

        public bool IsRecording { get; private set; }
        public bool IsPaused { get; private set; }
        public bool HasStarted { get; private set; }
        public string CurrentFile { get; private set; }
        public float MicLevel { get; private set; }
        public float SystemLevel { get; private set; }
        public event Action Started;
        public event Action<string> Completed;
        public event Action<string> Failed;

        public static List<(string Id, string Name)> GetMicrophones()
        {
            var list = new List<(string, string)>();
            try
            {
                using var e = new MMDeviceEnumerator();
                foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    list.Add((d.ID, d.FriendlyName));
            }
            catch (Exception ex) { Logger.Error("Audio.GetMicrophones", ex); }
            return list;
        }

        public static List<(string Id, string Name)> GetLoopbackDevices()
        {
            var list = new List<(string, string)>();
            try
            {
                using var e = new MMDeviceEnumerator();
                foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    list.Add((d.ID, d.FriendlyName));
            }
            catch (Exception ex) { Logger.Error("Audio.GetLoopback", ex); }
            return list;
        }

        public void Start(AudioRecordingSettings opts, string defaultFolder)
        {
            if (IsRecording) return;
            opts = AudioRecordingSettings.Normalize(opts, defaultFolder);
            _useMic = opts.Source != AudioRecordSourceKind.System;
            _useSys = opts.Source != AudioRecordSourceKind.Microphone;
            if (!_useMic && !_useSys) throw new InvalidOperationException(L.T("请选择至少一个音频来源。"));
            string folder = TempRecordingStore.EnsureFolder(AppPaths.LocalData);
            _format = opts.Format;
            _micVol = opts.MicVolume;
            _sysVol = opts.SystemVolume;
            var utc = DateTime.UtcNow;
            if (_format == AudioRecordFileFormat.Mp3)
            {
                _finalPath = TempRecordingStore.NewMediaPath(folder, "Audio", ".mp3", utc);
                _wavPath = _finalPath + ".pre.wav";
            }
            else
            {
                _wavPath = TempRecordingStore.NewMediaPath(folder, "Audio", ".wav", utc);
                _finalPath = _wavPath;
            }
            CurrentFile = _finalPath;
            HasStarted = false;
            _paused = false;
            Drain(_micQ); Drain(_sysQ);
            _micLeftover.Clear(); _sysLeftover.Clear();
            MicLevel = SystemLevel = 0;

            if (_useMic)
            {
                var dev = FindDevice(opts.MicrophoneDevice, DataFlow.Capture);
                _mic = dev != null ? new WasapiCapture(dev) : new WasapiCapture();
                _mic.DataAvailable += (s, e) => Enqueue(_micQ, _mic.WaveFormat, e.Buffer, e.BytesRecorded, isMic: true);
                _mic.RecordingStopped += (s, e) => { if (e.Exception != null) Logger.Error("MicStopped", e.Exception); };
            }
            if (_useSys)
            {
                var dev = FindDevice(opts.SystemDevice, DataFlow.Render);
                _loop = dev != null ? new WasapiLoopbackCapture(dev) : new WasapiLoopbackCapture();
                _loop.DataAvailable += (s, e) => Enqueue(_sysQ, _loop.WaveFormat, e.Buffer, e.BytesRecorded, isMic: false);
                _loop.RecordingStopped += (s, e) => { if (e.Exception != null) Logger.Error("LoopStopped", e.Exception); };
            }

            _fs = new FileStream(_wavPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            _wav = new WavPcmWriter(_fs, AudioPcm.TargetRate, AudioPcm.TargetChannels, leaveOpen: true);
            _run = true;
            _mix = new Thread(MixLoop) { IsBackground = true, Name = "AudioMix" };
            _mix.Start();
            try
            {
                _mic?.StartRecording();
                _loop?.StartRecording();
            }
            catch
            {
                StopInternal(failed: true);
                throw;
            }
            IsRecording = true;
            HasStarted = true;
            Raise(Started);
        }

        internal static MMDevice FindDevice(string id, DataFlow flow)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            try
            {
                // Do not Dispose the enumerator: NAudio MMDevice instances stay tied to it.
                var e = new MMDeviceEnumerator();
                try { return e.GetDevice(id); } catch { }
                foreach (var d in e.EnumerateAudioEndPoints(flow, DeviceState.Active))
                    if (d.ID == id || string.Equals(d.FriendlyName, id, StringComparison.OrdinalIgnoreCase))
                        return d;
            }
            catch (Exception ex) { Logger.Error("FindDevice", ex); }
            return null;
        }

        void Enqueue(ConcurrentQueue<short[]> q, WaveFormat fmt, byte[] buf, int bytes, bool isMic)
        {
            if (buf == null || bytes <= 0 || !_run) return;
            bool ieee = fmt.Encoding == WaveFormatEncoding.IeeeFloat;
            var samples = AudioPcm.ToStereo16(buf, bytes, fmt.SampleRate, fmt.Channels, fmt.BitsPerSample, ieee);
            if (samples.Length == 0) return;
            q.Enqueue(samples);
            float lv = (float)AudioPcm.Meter(AudioPcm.Rms(samples));
            if (isMic) MicLevel = lv; else SystemLevel = lv;
        }

        void MixLoop()
        {
            const int chunk = AudioPcm.TargetRate / 50 * AudioPcm.TargetChannels; // 20 ms
            try
            {
                while (_run)
                {
                    DrainTo(_micQ, _micLeftover);
                    DrainTo(_sysQ, _sysLeftover);
                    bool enoughMic = !_useMic || _micLeftover.Count >= chunk;
                    bool enoughSys = !_useSys || _sysLeftover.Count >= chunk;
                    if (!enoughMic && !enoughSys) { Thread.Sleep(10); continue; }
                    if ((_useMic && !enoughMic) || (_useSys && !enoughSys)) { Thread.Sleep(5); continue; }
                    var mic = Take(_micLeftover, chunk, _useMic);
                    var sys = Take(_sysLeftover, chunk, _useSys);
                    if (_paused) continue;
                    short[] mixed;
                    if (_useMic && _useSys) mixed = AudioPcm.Mix(mic, sys, _micVol, _sysVol);
                    else if (_useMic) mixed = Scale(mic, _micVol);
                    else mixed = Scale(sys, _sysVol);
                    lock (this) { _wav?.Write(mixed); }
                }
            }
            catch (Exception ex) { Logger.Error("AudioMix", ex); }
        }

        static short[] Scale(short[] a, double vol)
        {
            vol = AudioPcm.ClampVolume(vol);
            if (Math.Abs(vol - 1) < 1e-6) return a;
            var d = new short[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                int s = (int)Math.Round(a[i] * vol);
                if (s > short.MaxValue) s = short.MaxValue;
                else if (s < short.MinValue) s = short.MinValue;
                d[i] = (short)s;
            }
            return d;
        }

        static void DrainTo(ConcurrentQueue<short[]> q, List<short> dst)
        {
            while (q.TryDequeue(out var c)) dst.AddRange(c);
        }

        static short[] Take(List<short> buf, int n, bool used)
        {
            if (!used) return new short[n];
            var a = new short[n];
            int take = Math.Min(n, buf.Count);
            buf.CopyTo(0, a, 0, take);
            buf.RemoveRange(0, take);
            return a;
        }

        static void Drain(ConcurrentQueue<short[]> q) { while (q.TryDequeue(out _)) { } }

        public void Pause() { if (IsRecording && !IsPaused) { _paused = true; IsPaused = true; } }
        public void Resume() { if (IsRecording && IsPaused) { _paused = false; IsPaused = false; } }

        public void Stop()
        {
            if (!IsRecording && _wav == null) return;
            try { StopInternal(failed: false); }
            catch (Exception ex)
            {
                Logger.Error("AudioStop", ex);
                RaiseFailed(ex.Message);
            }
        }

        void StopInternal(bool failed)
        {
            _run = false;
            try { _mic?.StopRecording(); } catch { }
            try { _loop?.StopRecording(); } catch { }
            try { _mix?.Join(2000); } catch { }
            try { _mic?.Dispose(); } catch { }
            try { _loop?.Dispose(); } catch { }
            _mic = null; _loop = null; _mix = null;
            string wav = _wavPath, final = _finalPath;
            var fmt = _format;
            lock (this)
            {
                try { _wav?.Dispose(); } catch { }
                _wav = null;
                try { _fs?.Dispose(); } catch { }
                _fs = null;
            }
            IsRecording = false; IsPaused = false;
            if (failed)
            {
                try { if (wav != null && File.Exists(wav)) File.Delete(wav); } catch { }
                return;
            }
            try
            {
                if (fmt == AudioRecordFileFormat.Mp3 && wav != null)
                {
                    EncodeMp3(wav, final);
                    if (!string.Equals(wav, final, StringComparison.OrdinalIgnoreCase))
                        try { File.Delete(wav); } catch { }
                }
                CurrentFile = final;
                TempRecordingStore.MarkComplete(final, DateTime.UtcNow);
                RaiseCompleted(final);
            }
            catch (Exception ex)
            {
                Logger.Error("AudioFinalize", ex);
                CurrentFile = wav;
                RaiseCompleted(wav); // keep WAV if MP3 encode failed
            }
        }

        static void EncodeMp3(string wav, string mp3)
        {
            MediaFoundationApi.Startup();
            using var reader = new WaveFileReader(wav);
            MediaFoundationEncoder.EncodeToMp3(reader, mp3, 192000);
        }

        void Raise(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d != null) d.BeginInvoke(a); else a?.Invoke();
        }
        void RaiseCompleted(string f) => Raise(() => Completed?.Invoke(f));
        void RaiseFailed(string e) => Raise(() => Failed?.Invoke(e));

        public void Dispose()
        {
            if (IsRecording) Stop();
        }
    }
}

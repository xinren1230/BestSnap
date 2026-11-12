using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BestSnap.Plugins;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BestSnap.Services
{
    /// <summary>
    /// Mic and/or WASAPI loopback → 16 kHz mono in memory → rolling Whisper chunks.
    /// No audio file is written. Short-lived sample arrays are dropped after each chunk.
    /// </summary>
    public sealed class LiveTranscribeService : IDisposable
    {
        WasapiCapture _mic;
        WasapiLoopbackCapture _loop;
        Thread _mix;
        volatile bool _run, _paused;
        readonly ConcurrentQueue<short[]> _micQ = new ConcurrentQueue<short[]>();
        readonly ConcurrentQueue<short[]> _sysQ = new ConcurrentQueue<short[]>();
        readonly List<short> _micLeftover = new List<short>();
        readonly List<short> _sysLeftover = new List<short>();
        readonly List<short> _ring = new List<short>();
        readonly List<TranscriptCue> _cues = new List<TranscriptCue>();
        readonly ConcurrentQueue<(short[] Pcm, double AbsStart)> _jobs = new ConcurrentQueue<(short[], double)>();
        readonly object _cueLock = new object();
        CancellationTokenSource _cts;
        Task _worker;
        bool _useMic, _useSys;
        double _micVol = 1, _sysVol = 1;
        long _elapsedSamples;
        long _nextFire;
        double _committed;
        double _noiseFloor;
        int _chunkIndex;
        int _processed;
        int _skipped;
        int _silentStreak;
        bool _reportedFail;
        string _modelId = WhisperModels.DefaultId;

        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public bool HasStarted { get; private set; }
        public bool UsingMic => _useMic;
        public bool UsingSystem => _useSys;
        public float MicLevel { get; private set; }
        public float SystemLevel { get; private set; }
        public int ChunksProcessed => _processed;
        public int ChunksSkipped => _skipped;
        public IReadOnlyList<TranscriptCue> Cues
        {
            get { lock (_cueLock) return _cues.ToArray(); }
        }

        public event Action Started;
        public event Action Stopped;
        public event Action<string> Failed;
        public event Action<string> Caption;
        public event Action StatusChanged;
        public string Status { get; private set; }

        public void Start(AudioRecordingSettings opts, string modelId) =>
            StartAsync(opts, modelId).GetAwaiter().GetResult();

        public async Task StartAsync(AudioRecordingSettings opts, string modelId)
        {
            if (IsRunning) return;
            opts = AudioRecordingSettings.Normalize(opts, null);
            _useMic = opts.Source != AudioRecordSourceKind.System;
            _useSys = opts.Source != AudioRecordSourceKind.Microphone;
            if (!_useMic && !_useSys) throw new InvalidOperationException(L.T("请选择至少一个音频来源。"));
            _micVol = opts.MicVolume;
            _sysVol = opts.SystemVolume;
            _modelId = WhisperModels.Normalize(modelId);
            lock (_cueLock) _cues.Clear();
            _ring.Clear();
            _micLeftover.Clear(); _sysLeftover.Clear();
            Drain(_micQ); Drain(_sysQ);
            while (_jobs.TryDequeue(out _)) { }
            _elapsedSamples = 0;
            _committed = 0;
            _chunkIndex = 0;
            _processed = 0;
            _skipped = 0;
            _silentStreak = 0;
            _reportedFail = false;
            _noiseFloor = 0;
            _nextFire = (long)Math.Round(LiveChunkMath.FireAt(0) * AudioPcm.WhisperRate);
            MicLevel = SystemLevel = 0;
            _paused = false;
            IsPaused = false;
            HasStarted = false;
            _cts = new CancellationTokenSource();
            SetStatus(L.T("正在加载模型…"));
            Logger.Info($"Live start source={opts.Source} mic={_useMic} sys={_useSys} model={_modelId}");

            try
            {
                await WhisperTranscriber.WarmupAsync(_modelId, null, _cts.Token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Logger.Error("LiveWarmup", ex);
                SetStatus(L.T("转写失败：\n") + ex.Message);
                RaiseFailed(ex.Message);
                throw;
            }

            if (_useMic)
            {
                var dev = AudioRecorderService.FindDevice(opts.MicrophoneDevice, DataFlow.Capture);
                _mic = dev != null ? new WasapiCapture(dev) : new WasapiCapture();
                _mic.DataAvailable += (s, e) => Enqueue(_micQ, _mic.WaveFormat, e.Buffer, e.BytesRecorded, isMic: true);
                _mic.RecordingStopped += (s, e) => { if (e.Exception != null) Logger.Error("LiveMic", e.Exception); };
            }
            if (_useSys)
            {
                var dev = AudioRecorderService.FindDevice(opts.SystemDevice, DataFlow.Render);
                _loop = dev != null ? new WasapiLoopbackCapture(dev) : new WasapiLoopbackCapture();
                _loop.DataAvailable += (s, e) => Enqueue(_sysQ, _loop.WaveFormat, e.Buffer, e.BytesRecorded, isMic: false);
                _loop.RecordingStopped += (s, e) => { if (e.Exception != null) Logger.Error("LiveLoop", e.Exception); };
            }

            _run = true;
            _mix = new Thread(MixLoop) { IsBackground = true, Name = "LiveMix" };
            _mix.Start();
            _worker = Task.Run(() => Worker(_cts.Token));
            try
            {
                _mic?.StartRecording();
                _loop?.StartRecording();
            }
            catch (Exception ex)
            {
                Logger.Error("StartRecording", ex);
                StopInternal(failed: true);
                SetStatus(L.T("转写失败：\n") + ex.Message);
                RaiseFailed(ex.Message);
                throw;
            }
            IsRunning = true;
            HasStarted = true;
            SetStatus(L.T("正在聆听…（尚未检测到语音）"));
            Logger.Info("Live capture started");
            Raise(Started);
        }

        void Enqueue(ConcurrentQueue<short[]> q, WaveFormat fmt, byte[] buf, int bytes, bool isMic)
        {
            if (buf == null || bytes <= 0 || !_run) return;
            bool ieee = AudioRecorderService.IsIeeeFloat(fmt);
            var samples = AudioPcm.ToMono16k(buf, bytes, fmt.SampleRate, fmt.Channels, fmt.BitsPerSample, ieee);
            if (samples.Length == 0) return;
            q.Enqueue(samples);
            float lv = (float)AudioPcm.Meter(AudioPcm.Rms(samples));
            if (isMic) MicLevel = lv; else SystemLevel = lv;
        }

        void MixLoop()
        {
            const int chunk = AudioPcm.WhisperRate / 50; // 20 ms mono
            int maxRing = (int)(LiveChunkMath.WindowSeconds * AudioPcm.WhisperRate) + AudioPcm.WhisperRate;
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
                    _ring.AddRange(mixed);
                    if (_ring.Count > maxRing) _ring.RemoveRange(0, _ring.Count - maxRing);
                    _elapsedSamples += mixed.Length;
                    if (_elapsedSamples >= _nextFire)
                    {
                        int want = (int)Math.Round(LiveChunkMath.WindowForIndex(_chunkIndex) * AudioPcm.WhisperRate);
                        if (want < AudioPcm.WhisperRate / 4) want = AudioPcm.WhisperRate / 4;
                        int take = Math.Min(want, _ring.Count);
                        var snap = new short[take];
                        _ring.CopyTo(_ring.Count - take, snap, 0, take);
                        double absStart = LiveChunkMath.ChunkAbsStart(_chunkIndex);
                        _jobs.Enqueue((snap, absStart));
                        _chunkIndex++;
                        _nextFire = (long)Math.Round(LiveChunkMath.FireAt(_chunkIndex) * AudioPcm.WhisperRate);
                    }
                }
            }
            catch (Exception ex) { Logger.Error("LiveMix", ex); }
        }

        async Task Worker(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!_jobs.TryDequeue(out var job))
                    {
                        await Task.Delay(40, ct).ConfigureAwait(false);
                        continue;
                    }
                    await TranscribeChunk(job.Pcm, job.AbsStart, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Error("LiveWhisper", ex);
                SetStatus(L.T("转写失败：\n") + ex.Message);
                RaiseFailed(ex.Message);
            }
        }

        async Task TranscribeChunk(short[] pcm, double absStart, CancellationToken ct)
        {
            if (pcm == null || pcm.Length == 0) return;
            double rms = AudioPcm.Rms(pcm);
            double thresh = AudioPcm.VoiceGate.Threshold(_noiseFloor);
            bool voiced = rms >= thresh;
            _noiseFloor = AudioPcm.VoiceGate.UpdateFloor(_noiseFloor, rms, voiced);
            if (!voiced)
            {
                _skipped++;
                _silentStreak++;
                Logger.Info($"Live chunk skipped silent skipped={_skipped} processed={_processed} rms={rms:F5} thresh={thresh:F5} floor={_noiseFloor:F5} samples={pcm.Length}");
                if (_cues.Count == 0)
                {
                    string listen = L.T("正在聆听…（尚未检测到语音）");
                    if (_silentStreak >= AudioPcm.VoiceGate.SilentHintChunks)
                        listen += " " + L.T("连续多段被判定为静音。若没有字幕，请将来源改为麦克风或「麦克风 + 系统声音」。");
                    SetStatus(listen);
                }
                return;
            }
            _silentStreak = 0;
            _processed++;
            Logger.Info($"Live chunk process processed={_processed} skipped={_skipped} rms={rms:F5} thresh={thresh:F5} samples={pcm.Length} abs={absStart:F2}");
            SetStatus(L.T("正在转写…"));
            List<TranscriptCue> rel;
            try
            {
                rel = await WhisperTranscriber.TranscribeSamplesAsync(pcm, _modelId, null, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Logger.Error("LiveChunk", ex);
                SetStatus(L.T("转写失败：\n") + ex.Message);
                if (!_reportedFail)
                {
                    _reportedFail = true;
                    RaiseFailed(ex.Message);
                }
                return;
            }
            List<TranscriptCue> added;
            string caption = "";
            lock (_cueLock)
            {
                added = LiveChunkMath.Stitch(rel, absStart, _committed);
                _cues.AddRange(added);
                _committed = LiveChunkMath.ExtendCommitted(added, _committed);
                if (_cues.Count > 0) caption = _cues[_cues.Count - 1].Text;
            }
            if (added.Count > 0) Raise(() => Caption?.Invoke(caption));
            if (IsPaused) SetStatus(L.T("已暂停（不保存音频）"));
            else if (_cues.Count == 0) SetStatus(L.T("正在聆听…（尚未检测到语音）"));
            else SetStatus(L.T("仅转写中（不保存音频）"));
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

        public void Pause() { if (IsRunning && !IsPaused) { _paused = true; IsPaused = true; SetStatus(L.T("已暂停（不保存音频）")); } }
        public void Resume()
        {
            if (IsRunning && IsPaused)
            {
                _paused = false; IsPaused = false;
                SetStatus(_cues.Count == 0 ? L.T("正在聆听…（尚未检测到语音）") : L.T("仅转写中（不保存音频）"));
            }
        }

        public void Stop()
        {
            if (!IsRunning && _mix == null) return;
            try { StopInternal(failed: false); }
            catch (Exception ex)
            {
                Logger.Error("LiveStop", ex);
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
            short[] tail = null;
            double tailStart = 0;
            if (!failed && _ring.Count >= AudioPcm.WhisperRate / 2)
            {
                tail = _ring.ToArray();
                tailStart = LiveChunkMath.ChunkAbsStart(Math.Max(0, _chunkIndex));
            }
            _ring.Clear();
            try { _cts?.Cancel(); } catch { }
            try { _worker?.Wait(8000); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null; _worker = null;
            if (tail != null)
            {
                try { TranscribeChunk(tail, tailStart, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (Exception ex) { Logger.Error("LiveTail", ex); }
            }
            IsRunning = false; IsPaused = false;
            Logger.Info($"Live stop processed={_processed} skipped={_skipped} failed={failed}");
            SetStatus(failed ? L.T("转写失败") : L.T("转写已停止（未保存音频）"));
            if (failed) return;
            Raise(Stopped);
        }

        void SetStatus(string s)
        {
            Status = s;
            Raise(() => StatusChanged?.Invoke());
        }

        void Raise(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d != null) d.BeginInvoke(a); else a?.Invoke();
        }
        void RaiseFailed(string e) => Raise(() => Failed?.Invoke(e));

        public void Dispose()
        {
            if (IsRunning) Stop();
        }
    }
}

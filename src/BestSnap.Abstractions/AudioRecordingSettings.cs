using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BestSnap.Plugins
{
    public enum AudioRecordSourceKind { Microphone, System, Mix }
    public enum AudioRecordFileFormat { Wav, Mp3 }

    /// <summary>Persisted options for the audio-only recorder (devices + last folder).</summary>
    public class AudioRecordingSettings
    {
        public AudioRecordSourceKind Source { get; set; } = AudioRecordSourceKind.Microphone;
        public string MicrophoneDevice { get; set; }
        public string SystemDevice { get; set; }
        public AudioRecordFileFormat Format { get; set; } = AudioRecordFileFormat.Wav;
        public string SaveFolder { get; set; }
        public double MicVolume { get; set; } = 1;
        public double SystemVolume { get; set; } = 1;

        public static AudioRecordingSettings Normalize(AudioRecordingSettings s, string defaultFolder = null)
        {
            s ??= new AudioRecordingSettings();
            if (!Enum.IsDefined(typeof(AudioRecordSourceKind), s.Source)) s.Source = AudioRecordSourceKind.Microphone;
            if (!Enum.IsDefined(typeof(AudioRecordFileFormat), s.Format)) s.Format = AudioRecordFileFormat.Wav;
            s.MicVolume = AudioPcm.ClampVolume(s.MicVolume);
            s.SystemVolume = AudioPcm.ClampVolume(s.SystemVolume);
            s.SaveFolder = AudioPcm.CoalesceFolder(s.SaveFolder, defaultFolder);
            if (s.MicrophoneDevice != null) s.MicrophoneDevice = s.MicrophoneDevice.Trim();
            if (s.SystemDevice != null) s.SystemDevice = s.SystemDevice.Trim();
            if (s.MicrophoneDevice == "") s.MicrophoneDevice = null;
            if (s.SystemDevice == "") s.SystemDevice = null;
            return s;
        }

        public static AudioRecordingSettings FromJson(string json, string defaultFolder = null)
        {
            AudioRecordingSettings s = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try { s = JsonSerializer.Deserialize<AudioRecordingSettings>(json, JsonOpts); }
                catch (JsonException) { s = null; }
            }
            return Normalize(s, defaultFolder);
        }

        public string ToJson() => JsonSerializer.Serialize(Normalize(this), JsonOpts);

        static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}

using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class VoiceGateTests
    {
        [Fact]
        public void Threshold_UnknownFloor_UsesAbsoluteMin()
        {
            Assert.Equal(AudioPcm.VoiceGate.AbsoluteMin, AudioPcm.VoiceGate.Threshold(0), 6);
            Assert.Equal(AudioPcm.VoiceGate.AbsoluteMin, AudioPcm.VoiceGate.Threshold(double.NaN), 6);
        }

        [Fact]
        public void Threshold_QuietHeadsetFloor_IsWellBelowOldFixedBar()
        {
            double t = AudioPcm.VoiceGate.Threshold(0.0002);
            Assert.True(t < 0.010);
            Assert.True(t >= AudioPcm.VoiceGate.FloorMin);
            Assert.True(AudioPcm.VoiceGate.IsVoice(0.001, 0.0002));
            Assert.False(AudioPcm.VoiceGate.IsVoice(0.0001, 0.0002));
        }

        [Fact]
        public void Threshold_NoisyFloor_RaisesBarButCaps()
        {
            double t = AudioPcm.VoiceGate.Threshold(0.02);
            Assert.Equal(AudioPcm.VoiceGate.ThresholdCap, t, 6);
            Assert.False(AudioPcm.VoiceGate.IsVoice(0.005, 0.02));
            Assert.True(AudioPcm.VoiceGate.IsVoice(0.02, 0.02));
        }

        [Fact]
        public void UpdateFloor_TracksSilenceAndHoldsOnSpeech()
        {
            double floor = AudioPcm.VoiceGate.UpdateFloor(0, 0.0003, voiced: false);
            Assert.InRange(floor, 0.0003, 0.0004);
            double afterSpeech = AudioPcm.VoiceGate.UpdateFloor(floor, 0.05, voiced: true);
            Assert.True(afterSpeech < 0.01);
            double afterQuiet = AudioPcm.VoiceGate.UpdateFloor(floor, 0.0001, voiced: false);
            Assert.True(afterQuiet < floor);
        }

        [Fact]
        public void HasVoice_DefaultIsLenientAbsolute()
        {
            Assert.False(AudioPcm.HasVoice(new short[800]));
            var quietSpeech = new short[800];
            for (int i = 0; i < quietSpeech.Length; i++) quietSpeech[i] = (short)(i % 2 == 0 ? 200 : -200); // ~0.006 RMS
            Assert.True(AudioPcm.HasVoice(quietSpeech));
            Assert.False(AudioPcm.HasVoice(quietSpeech, 0.010));
        }

        [Fact]
        public void SilentHintChunks_IsAtLeastThree()
        {
            Assert.True(AudioPcm.VoiceGate.SilentHintChunks >= 3);
        }
    }
}

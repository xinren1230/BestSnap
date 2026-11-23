using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class CaptureSessionStateTests
    {
        [Fact]
        public void StartStopStart_AllowsThreeCycles()
        {
            var s = new CaptureSessionState();
            Assert.True(s.CanStart);
            Assert.Equal(CaptureSessionPhase.Idle, s.Phase);
            for (int i = 1; i <= 3; i++)
            {
                Assert.True(s.TryBeginStart(out _), "cycle " + i + " start");
                Assert.Equal(i, s.Cycle);
                Assert.False(s.CanStart);
                Assert.True(s.TryMarkRunning(out _));
                Assert.True(s.CanStop);
                Assert.True(s.TryPause(out _));
                Assert.True(s.TryResume(out _));
                Assert.True(s.TryBeginStop(out _));
                Assert.False(s.CanStart);
                s.MarkFinished();
                Assert.Equal(CaptureSessionPhase.Finished, s.Phase);
                Assert.True(s.CanStart, "cycle " + i + " must allow another start after stop");
            }
        }

        [Fact]
        public void FailedSession_CanStartAgain()
        {
            var s = new CaptureSessionState();
            Assert.True(s.TryBeginStart(out _));
            s.MarkFailed();
            Assert.Equal(CaptureSessionPhase.Failed, s.Phase);
            Assert.True(s.CanStart);
            Assert.True(s.TryBeginStart(out _));
            Assert.Equal(2, s.Cycle);
        }

        [Fact]
        public void Reset_UnsticksStopping()
        {
            var s = new CaptureSessionState();
            Assert.True(s.TryBeginStart(out _));
            Assert.True(s.TryMarkRunning(out _));
            Assert.True(s.TryBeginStop(out _));
            Assert.False(s.CanStart);
            s.Reset("stuck");
            Assert.Equal(CaptureSessionPhase.Idle, s.Phase);
            Assert.True(s.CanStart);
            Assert.True(s.TryBeginStart(out _));
        }

        [Fact]
        public void LogsTransitions()
        {
            var s = new CaptureSessionState();
            var log = new System.Collections.Generic.List<string>();
            s.Changed += (from, to, why) => log.Add(from + "->" + to + ":" + why);
            s.TryBeginStart(out _);
            s.TryMarkRunning(out _);
            s.TryBeginStop(out _);
            s.MarkFinished();
            Assert.Contains("Idle->Starting:start", log);
            Assert.Contains("Starting->Running:running", log);
            Assert.Contains("Running->Stopping:stop", log);
            Assert.Contains("Stopping->Finished:finished", log);
        }

        [Fact]
        public void CannotDoubleStartWhileRunning()
        {
            var s = new CaptureSessionState();
            Assert.True(s.TryBeginStart(out _));
            Assert.False(s.TryBeginStart(out string why));
            Assert.Contains("already", why);
        }
    }
}

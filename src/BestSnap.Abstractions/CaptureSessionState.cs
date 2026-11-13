using System;

namespace BestSnap.Plugins
{
    public enum CaptureSessionPhase
    {
        Idle = 0,
        Starting = 1,
        Running = 2,
        Paused = 3,
        Stopping = 4,
        Finished = 5,
        Failed = 6,
    }

    /// <summary>
    /// Start/stop/pause for live captions, audio recorder, and screen recorder.
    /// Finished/Failed always allow a new Start (second and third cycles).
    /// </summary>
    public sealed class CaptureSessionState
    {
        public CaptureSessionPhase Phase { get; private set; }
        public int Cycle { get; private set; }
        public event Action<CaptureSessionPhase, CaptureSessionPhase, string> Changed;

        public bool CanStart => Phase == CaptureSessionPhase.Idle || Phase == CaptureSessionPhase.Finished || Phase == CaptureSessionPhase.Failed;
        public bool CanStop => Phase == CaptureSessionPhase.Starting || Phase == CaptureSessionPhase.Running || Phase == CaptureSessionPhase.Paused;
        public bool CanPause => Phase == CaptureSessionPhase.Running;
        public bool CanResume => Phase == CaptureSessionPhase.Paused;
        public bool IsActive =>
            Phase == CaptureSessionPhase.Starting || Phase == CaptureSessionPhase.Running
            || Phase == CaptureSessionPhase.Paused || Phase == CaptureSessionPhase.Stopping;

        public bool TryBeginStart(out string reason)
        {
            if (Phase == CaptureSessionPhase.Stopping)
            {
                reason = "stopping";
                return false;
            }
            if (Phase == CaptureSessionPhase.Starting || Phase == CaptureSessionPhase.Running || Phase == CaptureSessionPhase.Paused)
            {
                reason = "already " + Phase;
                return false;
            }
            Cycle++;
            Transition(CaptureSessionPhase.Starting, "start");
            reason = null;
            return true;
        }

        public bool TryMarkRunning(out string reason)
        {
            if (Phase != CaptureSessionPhase.Starting && Phase != CaptureSessionPhase.Paused)
            {
                reason = "not starting";
                return false;
            }
            Transition(CaptureSessionPhase.Running, "running");
            reason = null;
            return true;
        }

        public bool TryPause(out string reason)
        {
            if (Phase != CaptureSessionPhase.Running)
            {
                reason = "not running";
                return false;
            }
            Transition(CaptureSessionPhase.Paused, "pause");
            reason = null;
            return true;
        }

        public bool TryResume(out string reason)
        {
            if (Phase != CaptureSessionPhase.Paused)
            {
                reason = "not paused";
                return false;
            }
            Transition(CaptureSessionPhase.Running, "resume");
            reason = null;
            return true;
        }

        public bool TryBeginStop(out string reason)
        {
            if (!CanStop)
            {
                reason = Phase == CaptureSessionPhase.Stopping ? "already stopping" : "not active";
                return false;
            }
            Transition(CaptureSessionPhase.Stopping, "stop");
            reason = null;
            return true;
        }

        public void MarkFinished()
        {
            if (Phase == CaptureSessionPhase.Idle || Phase == CaptureSessionPhase.Finished) return;
            Transition(CaptureSessionPhase.Finished, "finished");
        }

        public void MarkFailed()
        {
            if (Phase == CaptureSessionPhase.Idle || Phase == CaptureSessionPhase.Failed) return;
            Transition(CaptureSessionPhase.Failed, "failed");
        }

        /// <summary>Stuck-flag recovery so a later Start is never permanently blocked.</summary>
        public void Reset(string why = "reset")
        {
            if (Phase == CaptureSessionPhase.Idle) return;
            Transition(CaptureSessionPhase.Idle, why ?? "reset");
        }

        void Transition(CaptureSessionPhase next, string why)
        {
            var prev = Phase;
            if (prev == next) return;
            Phase = next;
            Changed?.Invoke(prev, next, why ?? "");
        }
    }
}

using System;

namespace BestSnap.Plugins
{
    /// <summary>Media Foundation encoder failures and start/stop races.</summary>
    public static class RecorderFailure
    {
        public const string MfUnexpected = "0xc00d36bb";

        /// <summary>Stop before the encoder has emitted Recording status causes MF_E_UNEXPECTED write errors.</summary>
        public static bool ShouldDeferStop(bool hasStarted, bool isRecording) => isRecording && !hasStarted;

        public static bool IsEncoderWriteError(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;
            return error.IndexOf(MfUnexpected, StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("c00d36bb", StringComparison.OrdinalIgnoreCase) >= 0
                || error.IndexOf("MF_E_UNEXPECTED", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

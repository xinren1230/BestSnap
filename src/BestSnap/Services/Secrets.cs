using System;
using System.Security.Cryptography;
using System.Text;

namespace BestSnap.Services
{
    /// <summary>DPAPI (CurrentUser) protection for upload passwords; only this Windows user on this machine can decrypt.</summary>
    public static class Secrets
    {
        /// <summary>
        /// LEGACY MIGRATION: DPAPI entropy from the SnapForge builds. Changing it would make
        /// existing upload passwords in settings.json undecryptable, so this string is kept.
        /// </summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SnapForge.Upload.v1");
        public static string Protect(string plain) =>
            string.IsNullOrEmpty(plain) ? null : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));
        public static string Unprotect(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(b64), Entropy, DataProtectionScope.CurrentUser)); }
            catch (Exception ex) { Logger.Error("DPAPI", ex); return null; }
        }
    }
}

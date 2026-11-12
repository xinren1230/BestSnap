using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace BestSnap.Tests
{
    public class InstallerScriptTests
    {
        static string IssPath
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    var p = Path.Combine(dir.FullName, "installer", "bestsnap.iss");
                    if (File.Exists(p)) return p;
                    dir = dir.Parent;
                }
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "installer", "bestsnap.iss"));
            }
        }

        [Fact]
        public void InnoScript_IsPerUserOfflinePayload_WithoutSideBySidePlugins()
        {
            Assert.True(File.Exists(IssPath), "missing " + IssPath);
            var text = File.ReadAllText(IssPath);
            Assert.Contains("PrivilegesRequired=lowest", text);
            Assert.Contains("AppMutex=SnapForge_SingleInstance_Mutex", text);
            Assert.Contains("CloseApplications=yes", text);
            Assert.Contains("CloseApplicationsFilter=BestSnap.exe,SnapForge.exe", text);
            Assert.Contains(@"{app}\SnapForge.exe", text);
            Assert.Contains(@"{localappdata}\SnapForge\runtime", text);
            Assert.Contains("chinesesimplified", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("BestSnap-Setup-", text);
            Assert.Contains("#define MyAppName \"BestSnap\"", text);
            Assert.Contains("AppId={{A7E3C4B1-9F2D-4E8A-B6C1-5D0F8A3E2B19}", text);
            Assert.Contains(@"DefaultDirName={autopf}\{#MyAppName}", text);
            Assert.Contains("BestSnap.exe", text);
            Assert.Contains(@"models\pix2text-mfr", text);
            Assert.Contains("THIRD-PARTY-NOTICES", text);
            Assert.DoesNotContain("plugins\\BestSnap.Ocr", text);
            Assert.DoesNotContain("plugins\\SnapForge.Ocr", text);
            Assert.DoesNotContain("Source: \"{#PayloadDir}\\plugins", text);
            Assert.False(Regex.IsMatch(text, @"Source:.*plugins", RegexOptions.IgnoreCase),
                "installer must not copy a plugins\\ folder next to the exe");
            Assert.Contains(@"runtimes\win-x64", text);
            Assert.Contains(@"DestDir: ""{app}\runtimes""", text);
            Assert.Contains("recursesubdirs", text);
            Assert.Contains("whisper.dll", File.ReadAllText(Path.Combine(Directory.GetParent(Path.GetDirectoryName(IssPath)).FullName, "build.ps1")));
        }

        [Fact]
        public void BuildScript_OffersInstallerSwitchAndSignThumbprint()
        {
            var dir = Path.GetDirectoryName(IssPath);
            var build = Path.Combine(Directory.GetParent(dir).FullName, "build.ps1");
            Assert.True(File.Exists(build), "missing " + build);
            var text = File.ReadAllText(build);
            Assert.Contains("[switch]$Installer", text);
            Assert.Contains("$SignThumbprint", text);
            Assert.Contains("$TimestampUrl", text);
            Assert.Contains("huggingface.co/breezedeus/pix2text-mfr", text);
            Assert.Contains("JRSoftware.InnoSetup", text);
            Assert.Contains("BestSnap.exe", text);
            Assert.Contains("BestSnap-Setup-", text);
            Assert.Contains("whisper.dll", text);
            Assert.Contains(@"runtimes\win-x64", text);
            Assert.Contains("single-file extract is not enough", text);
            Assert.Contains("Sign-RuntimeDlls", text);
            Assert.Contains("cuda", text);
            Assert.Contains("vulkan", text);
            Assert.Contains("Filter *.dll", text);
            Assert.DoesNotContain(".pfx", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}

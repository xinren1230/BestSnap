# Produces publish\BestSnap.exe
#   .\build.ps1                         framework-dependent (needs .NET 8 Desktop Runtime x64)
#   .\build.ps1 -SelfContained          no runtime needed (bigger)
#   .\build.ps1 -Installer              self-contained exe + Inno Setup one-click installer
#                                       (requires Inno Setup 6.5+: winget install JRSoftware.InnoSetup)
#   .\build.ps1 -Installer -SignThumbprint <40-char SHA-1 cert thumbprint>
#                                       signs BestSnap.exe then BestSnap-Setup-<version>.exe (no keys in-repo)
#   Optional: -TimestampUrl http://time.certum.pl  (Certum); default is DigiCert.
param(
    [switch]$SelfContained,
    [switch]$Installer,
    [string]$Configuration = "Release",
    [string]$SignThumbprint = "",
    [string]$Version = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if ($Installer) { $SelfContained = $true }
$sc    = if ($SelfContained) { "true" } else { "false" }
$stage = Join-Path $PSScriptRoot "stage"
$rt    = Join-Path $stage "rt"
Remove-Item $stage, publish -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $rt | Out-Null

function Get-AppVersion {
    param([string]$Fallback)
    if ($Fallback) { return $Fallback.TrimStart("v") }
    $csproj = Join-Path $PSScriptRoot "src\BestSnap\BestSnap.csproj"
    $text = Get-Content -LiteralPath $csproj -Raw
    if ($text -match "<Version>([^<]+)</Version>") { return $Matches[1].Trim() }
    return "1.1.1"
}

function Find-Iscc {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($p in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )) {
        if ($p -and (Test-Path -LiteralPath $p)) { return $p }
    }
    return $null
}

function Find-SignTool {
    $cmd = Get-Command signtool -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin"
    )
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $hit = Get-ChildItem -LiteralPath $root -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

function Invoke-AuthenticodeSign {
    param([string]$FilePath, [string]$Thumbprint, [string]$Timestamp)
    $tp = ($Thumbprint -replace "\s", "").ToUpperInvariant()
    if ($tp.Length -lt 40) { throw "SignThumbprint must be the 40-character SHA-1 certificate thumbprint (no private key in the repo or this script)." }
    $st = Find-SignTool
    if (-not $st) { throw "signtool.exe not found. Install the Windows 10/11 SDK, or put signtool on PATH." }
    & $st sign /fd SHA256 /td SHA256 /tr $Timestamp /sha1 $tp $FilePath
    if ($LASTEXITCODE) { throw "signtool failed for $FilePath (exit $LASTEXITCODE)" }
}

function Get-Sha256Lower {
    param([string]$FilePath)
    return (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Save-VerifiedFile {
    param([string]$Url, [string]$Dest, [string]$ExpectedSha256, [long]$ExpectedSize)
    if ((Test-Path -LiteralPath $Dest) -and ((Get-Item -LiteralPath $Dest).Length -eq $ExpectedSize) -and ((Get-Sha256Lower $Dest) -eq $ExpectedSha256)) {
        return
    }
    $dir = Split-Path -Parent $Dest
    New-Item -ItemType Directory -Force $dir | Out-Null
    $tmp = "$Dest.part"
    Write-Host "Downloading $Url"
    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        & curl.exe -fsSL --retry 3 --retry-delay 2 -o $tmp $Url
        if ($LASTEXITCODE) { throw "download failed: $Url" }
    } else {
        Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
    }
    $len = (Get-Item -LiteralPath $tmp).Length
    if ($len -ne $ExpectedSize) {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        throw "size mismatch for $(Split-Path -Leaf $Dest): got $len expected $ExpectedSize"
    }
    $got = Get-Sha256Lower $tmp
    if ($got -ne $ExpectedSha256) {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        throw "SHA-256 mismatch for $(Split-Path -Leaf $Dest): got $got expected $ExpectedSha256"
    }
    Move-Item -LiteralPath $tmp -Destination $Dest -Force
}

# Official Hugging Face files for breezedeus/pix2text-mfr (MIT). Hashes match FormulaService / BundledModels.
$FormulaModels = @(
    @{ Name = "encoder_model.onnx"; Sha256 = "bd8d5c322792e9ec45793af5569e9748f82a3d728a9e00213dbfc56c1486f37d"; Size = 87496990L }
    @{ Name = "decoder_model.onnx"; Sha256 = "fd0f92d7a012f3dae41e1ac79421aea0ea888b5a66cb3f9a004e424f82f3daed"; Size = 30114937L }
    @{ Name = "tokenizer.json";     Sha256 = "3e2ab757277d22639bec28c9d7972e352d3d1dba223051fa674002dc5ab64df3"; Size = 39161L }
)

# 1) OCR / face / webcam plugin (+ WinRT projection) -> rt\plugins\BestSnap.Ocr
dotnet publish src\BestSnap.Ocr -c $Configuration -r win-x64 --self-contained false -o "$rt\plugins\BestSnap.Ocr"
if ($LASTEXITCODE) { throw "plugin publish failed" }
Get-ChildItem "$rt\plugins" -Recurse -Include *.pdb,*.xml | Remove-Item
$pluginDir = Join-Path $rt "plugins\BestSnap.Ocr"
foreach ($n in @(
    "BestSnap.Ocr.dll",
    "BestSnap.Ocr.deps.json",
    "Microsoft.ML.OnnxRuntime.dll",
    "onnxruntime.dll",
    "models\ch_PP-OCRv4_det_infer.onnx",
    "models\ch_PP-OCRv4_rec_infer.onnx"
)) {
    $p = Join-Path $pluginDir $n
    if (-not (Test-Path -LiteralPath $p)) { throw "plugin publish missing $n (formula/OCR ONNX runtime will fail to load)" }
}

# 2) normal publish once to harvest the mixed-mode ScreenRecorderLib.dll (+ Ijwhost.dll if the SDK emits one)
dotnet publish src\BestSnap -c $Configuration -r win-x64 --self-contained false -o "$stage\pass1"
if ($LASTEXITCODE) { throw "pass1 publish failed" }
Copy-Item "$stage\pass1\ScreenRecorderLib.dll" $rt
if (Test-Path "$stage\pass1\Ijwhost.dll") { Copy-Item "$stage\pass1\Ijwhost.dll" $rt }

# 3) final single-file publish with everything embedded
dotnet publish src\BestSnap -c $Configuration -r win-x64 --self-contained $sc `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=$sc `
  -p:EmbedRuntimeDir="$rt" -o publish
if ($LASTEXITCODE) { throw "final publish failed" }

# Whisper.net loads {exeDir}\runtimes\win-x64\whisper.dll. Those DLLs must stay beside the
# exe (ExcludeFromSingleFile). Deleting everything except BestSnap.exe left natives only in
# %TEMP%\.net\BestSnap\<hash>\runtimes\win-x64\, which the loader does not search.
$whisperNatives = @("whisper.dll", "ggml-whisper.dll", "ggml-base-whisper.dll", "ggml-cpu-whisper.dll")
Get-ChildItem publish | ForEach-Object {
    if ($_.Name -eq "BestSnap.exe" -or $_.Name -eq "runtimes") { return }
    Remove-Item $_.FullName -Recurse -Force
}
$rtKeep = Join-Path "publish" "runtimes\win-x64"
if (Test-Path (Join-Path "publish" "runtimes")) {
    Get-ChildItem (Join-Path "publish" "runtimes") | Where-Object { $_.Name -ne "win-x64" } | Remove-Item -Recurse -Force
}
foreach ($n in $whisperNatives) {
    $p = Join-Path $rtKeep $n
    if (-not (Test-Path -LiteralPath $p)) {
        throw "publish missing Whisper native $n at runtimes\win-x64 (single-file extract is not enough; transcribe will fail)"
    }
}
Remove-Item $stage -Recurse -Force
$exe = Get-Item publish\BestSnap.exe
"{0}  {1:N1} MB" -f $exe.FullName, ($exe.Length / 1MB)
$rtKeepItem = Get-Item $rtKeep
"Whisper natives  {0}" -f $rtKeepItem.FullName

if (-not $Installer) { return }

$appVersion = Get-AppVersion $Version
$payload = Join-Path $PSScriptRoot "dist\payload"
$cache = Join-Path $PSScriptRoot "installer\cache\pix2text-mfr"
$hf = "https://huggingface.co/breezedeus/pix2text-mfr/resolve/main"
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $payload | Out-Null
Copy-Item $exe.FullName (Join-Path $payload "BestSnap.exe")
$rtDst = Join-Path $payload "runtimes\win-x64"
New-Item -ItemType Directory -Force $rtDst | Out-Null
foreach ($n in $whisperNatives) {
    Copy-Item (Join-Path $rtKeep $n) (Join-Path $rtDst $n) -Force
}
Copy-Item (Join-Path $PSScriptRoot "LICENSE") (Join-Path $payload "LICENSE")
Copy-Item (Join-Path $PSScriptRoot "NOTICE") (Join-Path $payload "NOTICE")
Copy-Item (Join-Path $PSScriptRoot "installer\licenses") (Join-Path $payload "licenses") -Recurse

foreach ($m in $FormulaModels) {
    Save-VerifiedFile -Url "$hf/$($m.Name)" -Dest (Join-Path $cache $m.Name) -ExpectedSha256 $m.Sha256 -ExpectedSize $m.Size
}
$modelDest = Join-Path $payload "models\pix2text-mfr"
New-Item -ItemType Directory -Force $modelDest | Out-Null
foreach ($m in $FormulaModels) {
    Copy-Item (Join-Path $cache $m.Name) (Join-Path $modelDest $m.Name) -Force
}

if (Test-Path (Join-Path $payload "plugins")) {
    throw "payload must not contain plugins\ (incomplete side-by-side plugins hide the embedded OCR runtime)"
}

$payloadExe = Join-Path $payload "BestSnap.exe"
if ($SignThumbprint) {
    Invoke-AuthenticodeSign -FilePath $payloadExe -Thumbprint $SignThumbprint -Timestamp $TimestampUrl
}

$iscc = Find-Iscc
if (-not $iscc) {
    throw @"
Inno Setup 6.5+ (ISCC.exe) is required to build BestSnap-Setup-$appVersion.exe.
Install on Windows:
  winget install --id JRSoftware.InnoSetup -e
Then re-run:
  .\build.ps1 -Installer$(if ($SignThumbprint) { " -SignThumbprint <thumbprint>" })
Linux/macOS cannot compile this installer (ISCC is Windows-only).
"@
}

$iss = Join-Path $PSScriptRoot "installer\bestsnap.iss"
$outDir = Join-Path $PSScriptRoot "dist"
$payloadArg = ($payload -replace "\\", "/")
$outArg = ($outDir -replace "\\", "/")
& $iscc /Q "/DMyAppVersion=$appVersion" "/DPayloadDir=$payloadArg" "/DOutputDir=$outArg" $iss
if ($LASTEXITCODE) { throw "ISCC failed (exit $LASTEXITCODE)" }

$setup = Join-Path $outDir "BestSnap-Setup-$appVersion.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "installer not produced: $setup" }
if ($SignThumbprint) {
    Invoke-AuthenticodeSign -FilePath $setup -Thumbprint $SignThumbprint -Timestamp $TimestampUrl
}
$setupItem = Get-Item $setup
"Installer  {0}  {1:N1} MB" -f $setupItem.FullName, ($setupItem.Length / 1MB)

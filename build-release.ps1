#Requires -Version 5.1
<#
  build-release.ps1 - builds the FOUR LiteBox release artifacts (2 runtimes x 2 forms).
  See BUILD-RELEASE.md for the full explanation.

  The two forms (BOTH self-contained, so both get the 'includedFrameworks' runtimeconfig):
    - standalone : self-contained SINGLE-FILE, self-installing, native payload EMBEDDED (~84 MB).
    - light      : self-contained NON-single-file, then STRIPPED of the .NET runtime DLLs so it borrows the
                   runtime already sitting in LaunchBox\Core (exactly like LaunchBox.exe). Core-only, payload
                   loose under litebox\thirdparty\. Ships LiteBox.exe + LiteBox.dll + the two .json (~a few MB
                   + payload). Runs ONLY from Core (that's where the runtime it needs lives).

  Usage:
    powershell -ExecutionPolicy Bypass -File build-release.ps1
    powershell -ExecutionPolicy Bypass -File build-release.ps1 -Lb9Root "D:\LaunchBox-net9"

  Params:
    -Lb9Root  <path>  .NET 9  LaunchBox: net9 SDK compile ref (CS1705 otherwise) AND net9 version label. Default G:\LB.
    -Lb10Root <path>  .NET 10 LaunchBox: net10 SDK compile ref AND net10 version label. Default ..\..\..\LB.
    -Rid      <rid>   runtime identifier. Default win-x64.

  Output layout (release\, git-ignored):
    <ver>_<label>\standalone\LiteBox-<ver>.exe        (+ README.txt)   e.g. 13.28_net10\standalone\LiteBox-13.28.exe
    <ver>_<label>\zip\LiteBox-<ver>.zip               (+ README.txt)   e.g. 13.28_net10\zip\LiteBox-13.28.zip
  where <ver> = Major.Minor of the referenced LaunchBox, <label> = net9 / net10.
  The exe INSIDE the zip stays "LiteBox.exe" (it lands in Core as-is; the uninstaller + ExtendDB host-detection
  key on that name). Only the distributed files (LiteBox-<ver>.exe / .zip) carry the version.
#>
[CmdletBinding()]
param(
  [string]$Lb9Root  = 'G:\LB',
  [string]$Lb10Root = "$PSScriptRoot\..\..\..\LB",
  [string]$Rid      = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$here       = $PSScriptRoot
$proj       = Join-Path $here 'LiteBox.csproj'
$thirdparty = Join-Path $here 'thirdparty'
$readme     = Join-Path $here 'release-README.txt'
$out        = Join-Path $here 'release'
$tmp        = Join-Path $env:TEMP 'litebox-pub'
$Lb10Root   = [IO.Path]::GetFullPath($Lb10Root)

# The 8 native payload files shipped LOOSE in the zip (under litebox\thirdparty\). Same list as the
# csproj EmbeddedResource block and NativeInstaller.Payload - keep the three in sync.
$payload = @(
  'Everything64.dll.api','Magick.Native-Q16-x64.dll.api','RahasherExtendDB.exe','7z.dll.api',
  'MSVCP140.dll.api','VCRUNTIME140.dll.api','VCRUNTIME140_1.dll.api','steam_api64.dll.api'
)

# The ONLY files the light build ships (everything else the publish produced is the .NET runtime, which
# LaunchBox\Core already provides). deps.json + runtimeconfig.json make it self-contained-flat.
$appFiles = @('LiteBox.exe','LiteBox.dll','LiteBox.deps.json','LiteBox.runtimeconfig.json')

# net9.0-windows -> "net9" (uses Lb9Root), net10.0-windows -> "net10" (uses Lb10Root)
$targets = @(
  @{ Tfm = 'net9.0-windows';  Label = 'net9';  LbRoot = $Lb9Root  },
  @{ Tfm = 'net10.0-windows'; Label = 'net10'; LbRoot = $Lb10Root }
)

# Major.Minor of the LaunchBox this target compiles against (from LaunchBox.exe, fallback to the SDK dll).
function LbVersion([string]$lbRoot) {
  $exe = Join-Path $lbRoot 'Core\LaunchBox.exe'
  $dll = Join-Path $lbRoot 'Core\Unbroken.LaunchBox.Plugins.dll'
  $src = if (Test-Path $exe) { $exe } elseif (Test-Path $dll) { $dll } else { throw "no LaunchBox.exe / SDK dll under $lbRoot\Core" }
  $p = (Get-Item $src).VersionInfo.FileVersion.Split('.')
  return "$($p[0]).$($p[1])"
}

# Publish (always self-contained) into $Dir with the extra props; capture stdout so it doesn't leak; verify.
function Publish([string]$Tfm, [string]$Dir, [string[]]$Extra) {
  if (Test-Path $Dir) { Remove-Item $Dir -Recurse -Force }
  $a = @('publish', $proj, '-c', 'Release', '-r', $Rid, '-f', $Tfm, '-p:SelfContained=true',
         "-p:Lb9Root=$Lb9Root", "-p:Lb10Root=$Lb10Root", '-o', $Dir, '-v', 'q') + $Extra
  $log = & dotnet @a
  if ($LASTEXITCODE -ne 0) { $log | Write-Host; throw "publish failed: $Tfm ($($Extra -join ' '))" }
  if (-not (Test-Path (Join-Path $Dir 'LiteBox.exe'))) { throw "LiteBox.exe missing after publish: $Dir" }
}

# fresh output + temp. Tolerate a locked dir (e.g. the release folder open in Explorer): we overwrite every
# file in place anyway (New-Item -Force / Copy-Item -Force / Compress-Archive -Force), so a failed delete
# only risks leaving unrelated stale files behind - not a broken build.
foreach ($d in @($out, $tmp)) {
  if (Test-Path $d) {
    try { Remove-Item $d -Recurse -Force -ErrorAction Stop }
    catch { Write-Host "  (note: could not fully clear '$d' - overwriting in place. Close any Explorer window on it for a clean wipe.)" }
  }
}
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($t in $targets) {
  $tfm = $t.Tfm; $label = $t.Label
  $ver = LbVersion $t.LbRoot           # e.g. 13.27
  $dir = "${ver}_${label}"             # e.g. 13.27_net9
  Write-Host "== building $dir ($tfm, LaunchBox $ver from $($t.LbRoot)) =="

  # ---- A) standalone: self-contained SINGLE-FILE, payload EMBEDDED, self-installing ----
  $scDir = Join-Path $tmp "$label-sc"
  Publish $tfm $scDir @('-p:PublishSingleFile=true', '-p:LiteBoxDist=standalone')
  $scRel = Join-Path $out "$dir\standalone"
  New-Item -ItemType Directory -Force $scRel | Out-Null
  Copy-Item (Join-Path $scDir 'LiteBox.exe') (Join-Path $scRel "LiteBox-$ver.exe")
  Copy-Item $readme (Join-Path $scRel 'README.txt')

  # ---- B) light "zip": self-contained NON-single-file, STRIPPED of runtime DLLs (Core provides them) ----
  $liDir = Join-Path $tmp "$label-light"
  Publish $tfm $liDir @('-p:PublishSingleFile=false', '-p:LiteBoxDist=light')
  $stage = Join-Path $tmp "$label-stage"
  if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
  $tpDir = Join-Path $stage 'litebox\thirdparty'
  New-Item -ItemType Directory -Force $tpDir | Out-Null
  foreach ($f in $appFiles) {                              # keep ONLY the app files (strip the runtime)
    $src = Join-Path $liDir $f
    if (-not (Test-Path $src)) { throw "light build missing expected file: $f" }
    Copy-Item $src (Join-Path $stage $f)
  }
  foreach ($p in $payload) {                               # loose payload
    $src = Join-Path $thirdparty $p
    if (-not (Test-Path $src)) { throw "payload file missing: $src" }
    Copy-Item $src (Join-Path $tpDir $p)
  }
  $zipRel = Join-Path $out "$dir\zip"
  New-Item -ItemType Directory -Force $zipRel | Out-Null
  Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $zipRel "LiteBox-$ver.zip") -Force
  Copy-Item $readme (Join-Path $zipRel 'README.txt')       # beside the zip, not inside
}

# ---- summary ----
Write-Host "`n== release tree ($out) =="
Get-ChildItem $out -Recurse -File | Sort-Object FullName | ForEach-Object {
  $size = '{0,9:N2} MB' -f ($_.Length / 1MB)
  $rel  = $_.FullName.Substring($out.Length + 1)
  Write-Host ("  {0}  {1}" -f $size, $rel)
}
Write-Host "`nDone."

# Installs the Android build on a Nubia Pad / RedMagic 3D tablet over USB and copies the clip data where the
# app reads it (4DV's plugin reads Android clips from the app's own storage, not from the APK):
#   /sdcard/Android/data/com.displayxrdemos.fourdvviewer/files/GaussianAssets/<clip>/
# Needs USB debugging on (Settings > About > tap Build number 7 times, then Developer options > USB debugging)
# and the tablet's "Allow USB debugging?" prompt accepted.
#
#   pwsh Tools/push-android-clips.ps1 [-Apk Builds/Android/4DV-Viewer.apk] [-Source <GaussianAssets folder>] [-Clips a,b,c]
param(
    [string]$Apk = (Join-Path (Split-Path $PSScriptRoot) 'Builds\Android\4DV-Viewer.apk'),
    # converted clip data: a Windows install's, or Assets/StreamingAssets/GaussianAssets in a project with clips
    [string]$Source = "$env:USERPROFILE\Apps\4DV Viewer\4DV-Viewer\4DV Viewer_Data\StreamingAssets\GaussianAssets",
    # FourDVSetup.kAndroidClips: the clips in the Android build
    [string[]]$Clips = @('dance_sample.c', 'cat_high', 'Kat_Miche_10s_150w', 'TimTalk_iPhone17', 'dance2'),
    [string]$Adb
)
$ErrorActionPreference = 'Stop'
$package = 'com.displayxrdemos.fourdvviewer'
if (-not $Adb) {
    $Adb = @(
        (Get-Command adb -ErrorAction SilentlyContinue)?.Source
        Get-ChildItem 'C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe' -ErrorAction SilentlyContinue |
            Select-Object -Last 1 -ExpandProperty FullName
    ) | Where-Object { $_ } | Select-Object -First 1
    if (-not $Adb) { throw 'adb not found; install Unity Android Build Support or pass -Adb' }
}

$devices = & $Adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\tdevice$' }
if (-not $devices) { throw "No tablet over USB. Check the cable, USB debugging, and the tablet's 'Allow USB debugging?' prompt (& '$Adb' devices)." }
"Tablet: $(& $Adb shell getprop ro.product.model)"

if (Test-Path $Apk) {
    "Installing $Apk"
    & $Adb install -r $Apk
    if ($LASTEXITCODE) { throw 'Install failed' }
}

$dest = "/sdcard/Android/data/$package/files/GaussianAssets"
& $Adb shell mkdir -p $dest
foreach ($clip in $Clips) {
    $dir = Join-Path $Source $clip
    if (-not (Test-Path $dir)) { Write-Warning "No data for $clip at $dir"; continue }
    $mb = [math]::Round((Get-ChildItem $dir -File | Measure-Object Length -Sum).Sum / 1MB)
    "Copying $clip ($mb MB)"
    & $Adb push $dir $dest
    if ($LASTEXITCODE) { throw "Copy of $clip failed" }
}
& $Adb shell du -sh $dest
'Done. Open "4DV Viewer" on the tablet.'

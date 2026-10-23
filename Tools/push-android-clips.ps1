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
    [string]$Adb,
    # needed when adb lists the tablet twice (USB and wireless debugging) or several devices
    [string]$Serial
)
$ErrorActionPreference = 'Stop'
if ($Serial) { $env:ANDROID_SERIAL = $Serial }
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

# Android 11+ doesn't let adb create an app's folder under Android/data; the app does on first launch.
$dest = "/sdcard/Android/data/$package/files/GaussianAssets"
& $Adb shell "ls -d /sdcard/Android/data/$package/files" 2>&1 | Out-Null
if ($LASTEXITCODE) {
    'Starting the app once so Android creates its storage folder'
    & $Adb shell monkey -p $package -c android.intent.category.LAUNCHER 1 | Out-Null
    foreach ($i in 1..20) {
        Start-Sleep 1
        & $Adb shell "ls -d /sdcard/Android/data/$package/files" 2>&1 | Out-Null
        if (-not $LASTEXITCODE) { break }
    }
    & $Adb shell am force-stop $package
}
& $Adb shell mkdir -p $dest
foreach ($clip in $Clips) {
    $dir = Join-Path $Source $clip
    if (-not (Test-Path $dir)) { Write-Warning "No data for $clip at $dir"; continue }
    $mb = [math]::Round((Get-ChildItem $dir -File | Measure-Object Length -Sum).Sum / 1MB)
    "Copying $clip ($mb MB)"
    # adb push can't create folders under Android/data (secure_mkdirs: Operation not permitted); the shell can.
    & $Adb shell mkdir -p "$dest/$clip"
    & $Adb push (Get-ChildItem $dir -File).FullName "$dest/$clip/"
    if ($LASTEXITCODE) { throw "Copy of $clip failed" }
}
# Folders adb creates belong to `shell` with no access for others, and the app is "others": the plugin then
# reports every file missing.
& $Adb shell "chmod -R a+rX $dest"
& $Adb shell du -sh $dest
'Done. Open "4DV Viewer" on the tablet.'

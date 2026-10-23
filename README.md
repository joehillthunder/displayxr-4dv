# 4D Volumetric Viewer (4DV)

Plays [4DV.ai](https://www.4dv.ai/) volumetric video (4D Gaussian splats) in glasses-free 3D on Leia displays,
through [DisplayXR](https://github.com/DisplayXR/displayxr-unity). Without a 3D display it runs in 2D in a window.

It uses 4DV.ai's own Unity plugin unchanged, so it is also the reference for judging 4DV.ai's rendering quality
against other splat players.

Demo page: https://joehillthunder.github.io/displayxr-4dv/ (one of the rooms in [DisplayXR HQ](https://joehillthunder.github.io/))

## Run it

1. Install DisplayXR with the [DisplayXR installer](https://github.com/DisplayXR/displayxr-installer/releases/latest).
2. Download `4DV-Viewer-Win64.zip` from [Releases](https://github.com/joehillthunder/displayxr-4dv/releases/latest), unzip, run `4DV Viewer.exe`.

It opens in a window (70% of the screen) and weaves 3D inside it; F11 goes fullscreen. The sidebar lists the
clips and has play/pause, previous/next, a seekable progress bar, loop mode, speed, 3D on/off, turntable, reset
view and a **3D strength** slider. Clips with a soundtrack play it in sync.

**Depth budget.** Past about 1% of the screen width, disparity on a lenticular panel shows as double edges
(crosstalk). For each clip and view, the app measures how far the content in view sits in front of and behind
the screen (5th and 95th percentile, from the clip's chunk bounds). It then scales the stereo down until that
fits, the same rule as the web player, but never below 25% of natural depth. Close-ups (Cat, Dance, Tim Talk)
keep full depth. Scenes that surround the performer (Badminton, Horse Walk, Rocket, Lang Lang) drop to 25–45%.
3D strength multiplies on top: 1 = the fitted depth, 2 = double it, 0 = flat. The sidebar shows the fit,
e.g. `1.00x · fit 25%`. Anything more than 20 cm in front of the screen is clipped. Those splats are strays
around the capture rig, at the viewer's eye position. **Fit: off** (sidebar) turns the budget off and gives
every clip its natural depth, times the strength (up to 3x).

| Input | Action |
|---|---|
| Left-drag | Orbit around the performer |
| Right- or middle-drag | Pan |
| Mouse wheel | Zoom |
| T | Turntable (auto-turn) |
| Home | Reset view |
| `[` / `]` | Less / more 3D strength |
| V | Switch the display between 2D and 3D |
| Space | Pause / play |
| ← / → | Previous / next clip |
| L | Loop mode: loop clip → play all → play once |
| ↑ / ↓ | Playback speed (¼× to 2×) |
| R | Restart clip |
| H | Hide / show the sidebar |
| F11 | Fullscreen / window |
| I | Save a screenshot |
| Esc | Quit |

Larger releases come as several zips (GitHub's 2 GB per-file limit). Unzip the extra clip packs into the same
folder as `4DV Viewer.exe`; clips whose data isn't there are simply left out of the list.

To frame a new clip without rebuilding: `"4DV Viewer.exe" -4dv-pivot 0 0.95 0 -4dv-yaw 0 -4dv-height 2`
(pivot = point to orbit, height = metres of scene shown top to bottom). To keep a framing, add the clip to
`4DV Viewer_Data/StreamingAssets/4dv-views.json`. It uses the same format as `Assets/Resources/4dv-views.json`
and wins over it.

| Command line | Effect |
|---|---|
| `-screen-fullscreen 1 -window-mode borderless` | Unity's own: open fullscreen |
| `-4dv-kiosk` | Open with the sidebar hidden (H shows it) |
| `-4dv-clip N` | Open on clip N (0-based, name order) |
| `-4dv-popout m` | Near clip, in metres in front of the screen (default 0.2) |
| `-4dv-size-cut x` | Drop splats narrower than x × their distance (default 0.00015, half a pixel; 4DV's own 0.001) |
| `-4dv-opacity-cut x` | Drop splats fainter than x (default 0.01; 4DV's own 0.04) |
| `-4dv-splat-view f` | Sharpness: where the plugin measures splat blur from, as a fraction of the viewing distance (default 0.5; 1 = 4DV's own, softer; 0.4 = crisper) |
| `-4dv-hdr 0` | Blend splats in 8 bits instead of 16 |
| `-4dv-debug` | Log the view, the content's extent and the depth fit to `Player.log` |
| `-4dv-shot path.png` | Save one frame after 8 s and quit (blank in 3D: the woven image isn't Unity's) |

The desktop shortcut on the demo PC runs `-screen-fullscreen 1 -window-mode borderless -4dv-kiosk`.

**Rendering fixes on top of 4DV's plugin.** None of these edit the plugin:

- **Content in front of the screen.** The plugin culls and sorts splats from `camera.worldToCameraMatrix`
  and drops anything behind that camera. In a display-centric DisplayXR rig, the Camera object sits on the
  screen plane, through the performer, so the near half of every clip was missing. Kat Miche showed only an
  arm. The app now points that matrix from the viewer's position. XR rendering keeps its own per-eye matrices.
- **Small splats.** 4DV's size cut (0.001 of distance) suits a wide headset. On this panel's narrow view it
  removed everything under about 3.4 px. It's now half a pixel.
- **Splat pool.** The pool held 4.19M splats a frame. Badminton, Corridor, Horse Walk, Neemo and Unitree have
  4.8–6.5M, so the pool now grows to fit the largest clip (8.4M, 512 MB of VRAM).
- **Blur floor.** The plugin blurs every splat to at least 0.0006 × its distance, about 2 px here, and the
  value is compiled into its shader. The distance is now measured from half the viewing distance, which
  roughly halves that floor. Fur, braids and fabric weave are visibly sharper, at no GPU cost. Splats blend
  in 16 bits.
- **Resolution is fixed.** DisplayXR offers one 3D mode on this panel, 2 views of 1920×1200, and ignores
  Unity's eye-texture scale. At 60 fps the heaviest clip uses about 40% of the RTX 5070 Ti.
- **Cat framing.** Cat opens side-on and close, instead of 4DV's far, high camera.

At launch the screen goes: the Unity splash, then DisplayXR's "XR for Unity" logo, then the clip. While the app's
DisplayXR session has no eye tracking, a soft ring with a rotating highlight shows over the whole screen.
It isn't drawn by this app. `Player.log` logs `[4DV] Eye tracking ON/OFF` and samples the eye positions every
5 s, so tracking can be checked from a normal launch.

## Android (Nubia Pad 3D II, RedMagic 3D Explorer)

The same app runs on Leia's Android tablets through **DisplayXR's Android runtime**, which is an OpenXR
runtime there (`org.freedesktop.monado.openxr_runtime.out_of_process`, installed by DisplayXR's
`install-android.sh`). The app reaches it through Unity's OpenXR plug-in and the Khronos loader. The runtime
does the face tracking and the weave, as on Windows. Touch controls: one finger orbits, pinch zooms, two
fingers pan, and a double tap resets the view. The toolbar (top right) stays on screen:

| Button | Action |
|---|---|
| Depth − / + | 3D strength in steps of 0.25 (0 = flat, 1 = full; DisplayXR caps the eye spread at 1 on Android) |
| Detail | Splat density: Auto (half below 20 fps, back to full above 40 fps at half), Full, ½, ¼ |
| Fit | On: depth is capped to what the panel shows cleanly (the depth budget). Off: natural depth × strength |
| AR | Tabletop AR: the performer, cropped out of the capture stage, stands on a table or the floor seen through the rear camera (details below) |
| Menu / Hide | The sidebar |

**AR.** A crop pass (4DV's per-clip FX hook, `Assets/Shaders/FourDVCrop.compute`) keeps a 0.7 m disc around the
performer. The figure stands upright on a horizontal surface below the rear camera, facing the tablet, and is
rendered through DisplayXR's camera rig at the rear camera's real field of view (from Camera2), so it lines
up with the camera image. Until a tap it follows the centre of the view; then: tap or drag to place it, pinch
to scale it (figurine to life size), two-finger drag up/down to raise or lower the surface until the feet sit
on it, double tap to pick it up again. Tracking is turning only (3 degrees of freedom, from the gyroscope;
the accelerometer if there's no gyroscope), not walking around, and surfaces aren't detected: full 6DoF would
need ARCore, which doesn't list these tablets, and Unity's ARCore plug-in only renders with Vulkan (which
4DV needs) from AR Foundation 6.1 on Unity 6. Face tracking keeps the front camera while AR uses the rear one.

It ships five lighter clips: Dance, Cat, Kat Miche, Tim Talk and Dance 2, about 1 GB. Each eye renders at
1920×1200 (the panel's 3D mode). If a clip can't hold 20 fps, the app draws every other splat.

**Setting up a tablet:**

1. Turn on USB debugging (Settings > About, tap Build number 7 times, then Developer options). Connect it
   and accept "Allow USB debugging?".
2. Install DisplayXR's runtime with its `scripts/install-android.sh` (displayxr-runtime). Use the script, not
   a bare `adb install`: the runtime app has to be **opened once** after every install, or its OpenXR broker
   stays unreachable and every OpenXR app fails with `XR_ERROR_RUNTIME_UNAVAILABLE`.
3. `pwsh Tools/push-android-clips.ps1 [-Serial <adb serial>]` installs `Builds/Android/4DV-Viewer.apk`, starts
   it once so Android creates its storage folder, and copies the clips there (4DV's plugin reads Android clip
   data from `persistentDataPath/GaussianAssets/<clip>/`, not from the APK). Pass `-Serial` when adb lists
   the tablet twice (USB and wireless debugging).

**Building it** needs Unity 2022.3 with Android Build Support. Build from a checkout whose path is short (like
`C:\dev\4dv-android`: package files deep in `Library/PackageCache` fail to import past Windows' 260-character
limit) and without `Assets/StreamingAssets/GaussianAssets` (it would go into the APK):

```bat
Unity.exe -batchmode -quit -buildTarget Android -projectPath . -executeMethod FourDVSetup.PrepareAndroid -logFile Logs\prepare.log
Unity.exe -batchmode -quit -buildTarget Android -projectPath . -executeMethod FourDVSetup.BuildAndroid -logFile Logs\android.log
```

`BuildAndroid` makes `Builds/Android/4DV-Viewer.apk` (`com.displayxrdemos.fourdvviewer`); `-4dv-clips a,b,c`
picks other clips. `adb shell am start -n com.displayxrdemos.fourdvviewer/com.unity3d.player.UnityPlayerActivity
-e unity "-4dv-debug"` passes command-line flags. What differs from Windows:

- **Vulkan only.** 4DV's sort needs wave intrinsics, which Unity only compiles for Vulkan on Android; the sort
  uses 4DV's `_pico` variant, their build for Snapdragon GPUs.
- **`DisplayXRAndroidFeature`** (an OpenXR feature) declares the display rig with `XR_DXR_view_rig`
  (virtual display height = zoom, eye spread = 3D strength × depth fit), so the runtime returns views in
  scene units, and works around three things Unity and the runtime (v2.51–2.52) don't agree on: Unity's
  second `xrCreateVulkanDeviceKHR` passes a queue create info with a garbage `pNext` that the runtime walks
  and crashes on; the runtime's `xrSetAndroidApplicationThreadKHR` calls a null hook; and without
  `XR_DXR_display_info` the runtime renders a "legacy" 0.75 view scale.
- **The XR space** sits on the orbit pivot, in Unity's device (LOCAL) tracking origin, which is where the
  runtime anchors the rig. The XR camera is the tracked head (Unity adds each eye's offset to it); 4DV culls
  and sorts from `worldToCameraMatrix`, which `FourDVOrbit` points from half the nominal viewing distance, as
  on Windows.
- **Touch.** The runtime shows the woven image in its own in-process window, titled "DisplayXR", over the
  app's, and it takes every touch; the app marks it not-touchable. With the XR runtime active, Unity's UI
  canvas is 1920×1200 while touches arrive at 2560×1600, so UGUI's raycaster misses; `FourDVPanel`
  hit-tests touches against its controls itself.

Leia's own Unity plugin (LeiaUnity 3.3.2) was tried first and dropped: on this tablet its Vulkan path crashed
in `vkCreateFramebuffer` and, once patched, produced no image, Leia's own sample included.

## Build it

Needs Unity **2022.3 LTS** (4DV.ai's plugin targets URP 14) and a signed-in Unity Hub.

```bat
"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod FourDVSetup.Build -logFile Logs\build.log
```

`FourDVSetup.Build` sets up URP with 4DV's `GaussianSplat4DFeature`, enables the DisplayXR display provider,
generates `Assets/Scenes/4DVViewer.unity` with one entry per clip in `Assets/GaussianSplatAssets`, and builds
to `Builds/Win64/4DV-Viewer/`. The same steps are under the **4DV** menu in the editor.

Things the setup handles that are easy to get wrong by hand:

- **Direct3D 12 only.** 4DV's GPU radix sort uses Shader Model 6 wave intrinsics; on D3D11 its kernels fail to
  load and nothing draws.
- **Renderer feature shader slots.** A `GaussianSplat4DFeature` added from script has empty shader slots and throws
  in `Create()`; setup points them at the plugin's shaders (the non-Pico `DeviceRadixSort`).
- **Camera layout.** The Camera and `DisplayXRDisplay` are on one GameObject (DisplayXR's supported layout; its
  2D fallback and per-camera hooks look for the Camera on the display's own object), and that object is a child
  of the orbit pivot, never a root object: in XR, Unity overwrites the camera's local pose with the tracked eye pose.
- **DisplayXR subsystem manifest.** The build step copies `UnitySubsystemsManifest.json` into
  `<Data>/UnitySubsystems/DisplayXR/`; without it the provider never loads and the app runs as 2D.

## Adding clips

4DV.ai's sample library (`.4dv` files) is linked from their assets portal. A `.4dv` has to be converted by
4DV's importer before the plugin can play it. `FourDVImport` runs that importer over a whole folder:

```bat
"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod FourDVImport.ImportAll -4dv-dir C:\path\to\clips -logFile Logs\import.log
```

(or **4DV > Import .4dv Folder...** in the editor). For each clip it writes the frame data to
`Assets/StreamingAssets/GaussianAssets/<clip>/`, puts `<clip>.asset` in `Assets/GaussianSplatAssets/`, and saves
the clip's soundtrack (`audio.mp3`) and 4DV's suggested camera (`view.json`) beside the frame data. Clips already
imported are skipped. Then run the build; every clip in `Assets/GaussianSplatAssets` is in the playlist, in name
order, and opens framed by its own suggested camera.

Converted frame data is large, so `Assets/StreamingAssets/GaussianAssets/` is not in git; it ships inside the
release zip.

## What's in here

| Path | What |
|---|---|
| `Packages/com.4dv.4d-gaussian-splatting/` | 4DV.ai's Unity plugin v1.0.1 (0506 build), unchanged |
| `Packages/manifest.json` | Pins `com.displayxr.unity` to `upm/v2.26.0` and URP 14.0.12 |
| `Assets/Scripts/FourDVPlayer.cs` | Playlist, loop modes, seeking, speed, synced audio, per-clip framing |
| `Assets/Scripts/FourDVOrbit.cs` | Display-centric DisplayXR rig: orbit, pan, zoom, 3D strength, 2D/3D, windowed sizing |
| `Assets/Scripts/FourDVPanel.cs` | Sidebar UI; a DisplayXR window-space layer in 3D, a screen overlay in 2D; tablet toolbar |
| `Assets/Scripts/FourDVAR.cs` | Android tabletop AR: crop, camera rig, placement on a surface, touch |
| `Assets/Shaders/FourDVCrop.compute` | 4DV FX setup pass that keeps a cylinder around the performer (AR) |
| `Assets/Editor/FourDVImport.cs` | Batch `.4dv` import through 4DV's own importer, plus audio and view sidecars |
| `Assets/Editor/FourDVSetup.cs` | Headless project setup, Windows build, Android prepare and build |
| `Tools/push-android-clips.ps1` | Installs the APK on a tablet and copies clip data to it over USB |
| `Assets/Scripts/DisplayXRAndroidFeature.cs` | Android OpenXR feature: DisplayXR display rig, runtime workarounds |
| `Assets/Plugins/Android/DisplayXRClient.androidlib/` | Manifest entries a DisplayXR Android client needs |

## Credits

The 4D Gaussian Splatting plugin and the volumetric clips are © 4DV.ai, shared for this demo. Stereo rendering by
DisplayXR for Unity (Apache-2.0).

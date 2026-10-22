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
around the capture rig, at the viewer's eye position.

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

The same app runs on Leia's Android tablets through Leia's own Unity plugin. The tablets' face tracking and
weaving are built in, so there's nothing to install on them except the app. Touch controls: one finger orbits,
pinch zooms, two fingers pan, and a double tap resets the view. **Menu** (top right) shows and hides the sidebar.
It ships five lighter clips: Dance, Cat, Kat Miche, Tim Talk and Dance 2, about 1 GB. If a clip can't hold
27 fps, the app draws every other splat, and then every fourth if needed.

Clip data isn't in the APK. It goes on the tablet's app storage over USB:

1. On the tablet, turn on USB debugging (Settings > About, tap Build number 7 times, then Developer options).
   Connect it and accept "Allow USB debugging?".
2. `pwsh Tools/push-android-clips.ps1` installs `Builds/Android/4DV-Viewer.apk` and copies the clips from a
   Windows install's `StreamingAssets/GaussianAssets` (or pass `-Source`).

Building it needs Unity 2022.3 with Android Build Support, and Leia's Unity plugin (**LeiaUnity 3.3.2**,
"Unity (Windows + Android)" at [support.immersity.ai/sdk](https://support.immersity.ai/sdk), sign-in needed).
The plugin is proprietary, so it isn't in git. Its Windows runtime also starts Leia SR weaving on its own,
which fights DisplayXR, so build Android from a **separate checkout** (e.g. `git worktree add ../4dv-android`),
never the one used for Windows builds:

```bat
Unity.exe -batchmode -quit -buildTarget Android -projectPath . -executeMethod FourDVSetup.PrepareAndroid -leia-package C:\path\LeiaUnity-3.3.2+454.74e0328.unitypackage -logFile Logs\prepare.log
Unity.exe -batchmode -quit -buildTarget Android -projectPath . -executeMethod FourDVSetup.BuildAndroid -logFile Logs\android.log
```

`PrepareAndroid` imports the plugin to `Assets/Leia` and defines `FOURDV_LEIA`, `LEIA_URP_DETECTED` and
`DISPLAYXR_NO_SPLASH` for Android. `BuildAndroid` makes `Builds/Android/4DV-Viewer.apk`
(`com.displayxrdemos.fourdvviewer`); `-4dv-clips a,b,c` picks other clips. What differs from Windows:

- **Leia rig.** A `LeiaDisplay` sits on the orbit pivot in place of `DisplayXRDisplay`. Leia renders two real eye
  cameras at the tracked eye positions, so 4DV's plugin culls from each eye and the Windows viewpoint fix isn't
  needed. 3D strength and the depth budget drive `LeiaDisplay.DepthFactor`.
- **Vulkan only.** Leia recommends OpenGL ES 3, but 4DV's sort needs wave intrinsics, and Unity only compiles
  those for Vulkan on Android. Leia's interlacer has a Vulkan path. The sort uses 4DV's `_pico` variant,
  their build for Snapdragon GPUs.
- **Renderer 1** (`URP-Renderer-Plain`, no splat feature) is for Leia's head and UI-event cameras. Their images
  are thrown away, and every camera on renderer 0 runs a full splat sort.
- 4DV's plugin reads Android clip data from `persistentDataPath/GaussianAssets/<clip>/`, not from StreamingAssets.

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
| `Assets/Scripts/FourDVPanel.cs` | Sidebar UI; a DisplayXR window-space layer in 3D, a screen overlay in 2D |
| `Assets/Editor/FourDVImport.cs` | Batch `.4dv` import through 4DV's own importer, plus audio and view sidecars |
| `Assets/Editor/FourDVSetup.cs` | Headless project setup, Windows build, Android prepare and build |
| `Tools/push-android-clips.ps1` | Installs the APK on a tablet and copies clip data to it over USB |

## Credits

The 4D Gaussian Splatting plugin and the volumetric clips are © 4DV.ai, shared for this demo. Stereo rendering by
DisplayXR for Unity (Apache-2.0).

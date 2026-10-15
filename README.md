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
view and a **3D strength** slider (0 = flat, 1 = natural, 2 = exaggerated). Clips with a soundtrack play it in sync.

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
(pivot = point to orbit, height = metres of scene shown top to bottom).

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
| `Assets/Editor/FourDVSetup.cs` | Headless project setup and Windows build |

## Credits

The 4D Gaussian Splatting plugin and the volumetric clips are © 4DV.ai, shared for this demo. Stereo rendering by
DisplayXR for Unity (Apache-2.0).

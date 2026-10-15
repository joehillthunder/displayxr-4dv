# 4D Volumetric Viewer (4DV)

Plays [4DV.ai](https://www.4dv.ai/) volumetric video (4D Gaussian splats) in glasses-free 3D on Leia displays,
through [DisplayXR](https://github.com/DisplayXR/displayxr-unity). Without a 3D display it runs in 2D in a window.

It uses 4DV.ai's own Unity plugin unchanged, so it is also the reference for judging 4DV.ai's rendering quality
against other splat players.

Demo page: https://joehillthunder.github.io/displayxr-demos/apps/4dv/

## Run it

1. Install DisplayXR with the [DisplayXR installer](https://github.com/DisplayXR/displayxr-installer/releases/latest).
2. Download `4DV-Viewer-Win64.zip` from [Releases](https://github.com/joehillthunder/displayxr-4dv/releases/latest), unzip, run `4DV Viewer.exe`.

| Key | Action |
|---|---|
| Space | Pause / play |
| ← / → | Previous / next clip |
| ↑ / ↓ | Playback speed (⅛× to 4×) |
| R | Restart clip |
| Esc | Quit |

## Build it

Needs Unity **2022.3 LTS** (4DV.ai's plugin targets URP 14) and a signed-in Unity Hub.

```bat
"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod FourDVSetup.Build -logFile Logs\build.log
```

`FourDVSetup.Build` sets up URP with 4DV's `GaussianSplat4DFeature`, enables the DisplayXR display provider,
generates `Assets/Scenes/4DVViewer.unity` with one entry per clip in `Assets/GaussianSplatAssets`, and builds
to `Builds/Win64/4DV-Viewer/`. The same steps are under the **4DV** menu in the editor.

## Adding clips

4DV.ai's sample library (`.4dv` files) is linked from their assets portal. A `.4dv` has to be converted by the
4DV importer before the plugin can play it:

1. Open the project in the Unity editor.
2. Open 4DV's importer window (its menu item, under **Tools**), pick the `.4dv` file and import.
   It writes `Assets/GaussianSplatAssets/<clip>.asset` and the frame data under
   `Assets/StreamingAssets/GaussianAssets/<clip>/`.
3. Run **4DV > Set Up Project** (or the build command above). Every clip in `Assets/GaussianSplatAssets` is added
   to the playlist, in name order.

Converted frame data is large, so `Assets/StreamingAssets/GaussianAssets/` is not in git; it ships inside the
release zip.

## What's in here

| Path | What |
|---|---|
| `Packages/com.4dv.4d-gaussian-splatting/` | 4DV.ai's Unity plugin v1.0.1 (0506 build), unchanged |
| `Packages/manifest.json` | Pins `com.displayxr.unity` to `upm/v2.26.0` and URP 14.0.12 |
| `Assets/Scripts/FourDVPlayer.cs` | Playlist, looping playback and keyboard controls |
| `Assets/Editor/FourDVSetup.cs` | Headless project setup and Windows build |

## Credits

The 4D Gaussian Splatting plugin and the volumetric clips are © 4DV.ai, shared for this demo. Stereo rendering by
DisplayXR for Unity (Apache-2.0).

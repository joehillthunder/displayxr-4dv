# CLAUDE.md: 4D Volumetric Viewer (Unity)

A Windows Unity app that plays 4DV.ai volumetric video (4D Gaussian splats) in glasses-free 3D on Leia displays
through DisplayXR. It uses 4DV.ai's own Unity plugin, unchanged. `README.md` covers running, building and adding
clips; this file is what an agent needs on top of it.

## Related projects

- **Web version:** `joehillthunder.github.io` → `displayxr-demos/apps/4dv-player/` (three.js, decodes `.4dv`
  directly, no Unity). Room "4DV Player" in DisplayXR HQ's Spatial UI/UX wing. Its `CLAUDE.md` documents the
  `.4dv` (4dv-sog) format.
- **This app** is the reference for 4DV.ai's own rendering quality; the web player is the one that runs anywhere.

## Layout

| Path | Role |
|---|---|
| `Assets/Scripts/FourDVPlayer.cs` | Playlist (every `.asset` in `Assets/GaussianSplatAssets`, name order), loop modes, seek, speed, synced audio, per-clip framing (`view.json`, then `Assets/Resources/4dv-views.json`, then `StreamingAssets/4dv-views.json`, then `-4dv-*` command-line args), chunk-centre depth samples. |
| `Assets/Scripts/FourDVOrbit.cs` | Display-centric DisplayXR rig: orbit, pan, zoom, 3D strength × per-view depth fit (stereo depth budget), near clip, 2D/3D, windowed sizing. |
| `Assets/Scripts/FourDVPanel.cs` | Sidebar UI: a DisplayXR window-space layer in 3D, a screen overlay in 2D. |
| `Assets/Editor/FourDVImport.cs` | Batch `.4dv` → plugin format via 4DV's obfuscated importer (`ImporterWindow.A`), plus `audio.mp3` and `view.json` sidecars. |
| `Assets/Editor/FourDVSetup.cs` | Headless setup + Win64 build (`FourDVSetup.Build`). |
| `Tools/patch-installed.ps1` | Rebuilds just `Assembly-CSharp.dll` with PowerShell 7's Roslyn against an installed player's `Managed/` and swaps it in (`-Restore` undoes). For script-only fixes on a PC without Unity. |
| `Packages/com.4dv.4d-gaussian-splatting/` | 4DV.ai plugin v1.0.1, vendored. Don't edit it. |

## Rules

- **4DV's plugin views splats from `camera.worldToCameraMatrix`, not the XR eye matrices.** In this rig that
  camera is on the display plane, so `FourDVOrbit.ApplySplatViewpoint` overrides the matrix with one from the
  nominal viewer. Without it, everything in front of the screen is culled. Its size and opacity cuts are
  retuned in `FourDVPlayer.ConfigureRenderer`, and its 4.19M splat pool is grown there by reflection (the
  feature class is internal).
- **Don't edit 4DV's plugin.** Work around it from `Assets/` (as `FourDVSetup` does for the renderer feature's
  shader slots).
- **Direct3D 12 only**, URP 14, Unity 2022.3 LTS. D3D11 draws nothing (SM6 wave intrinsics in the radix sort).
- Camera and `DisplayXRDisplay` share one GameObject, which must be a child of the orbit pivot, never a root.
- The build must copy `UnitySubsystemsManifest.json` into `<Data>/UnitySubsystems/DisplayXR/`, or the app
  silently runs 2D.
- Converted clip data (`Assets/StreamingAssets/GaussianAssets/`, about 5.5 GB for 14 clips) is **never** committed.
  It ships in release zips (GitHub caps each file at 2 GB, hence `4DV-Viewer-Win64.zip` + `Clips-Pack2/3`).
- The clips and plugin are © 4DV.ai, shared for this demo. Keep the credit in README and release notes.
- **Android** (README "Android") runs through DisplayXR's Android OpenXR runtime via Unity's OpenXR plug-in;
  `DisplayXRAndroidFeature` declares the display rig and works around runtime crashes. Vulkan only. Build from
  a short-path checkout without clip data. On the tablet, the DisplayXR runtime app must be opened once after
  each runtime install, or every OpenXR app gets `XR_ERROR_RUNTIME_UNAVAILABLE`.
- Testing on the tablet: `adb shell input tap` is too short for the app (it polls touches per frame); use
  `input swipe x y x y 150`. Screenshots show one unwoven view; ask the user how the 3D looks.
- Work on a branch and merge through a PR; end commit messages with the Co-Authored-By trailer.

## Releases and installing on the demo PC

- v1.0.0 = `4DV-Viewer-Win64.zip` (app + 8 clips) + `4DV-Viewer-Clips-Pack2.zip` / `Pack3.zip` (6 more). Unzip
  the packs into the same folder as `4DV Viewer.exe`; missing clips are just left out of the list.
- On the Leia demo PC (`4kDemo`) it's installed at `C:\Users\4kDemo\Apps\4DV Viewer\4DV-Viewer\` with all 14
  clips, and the desktop shortcut is **4DV Player (Unity)**.
- That PC is short on disk. Stream packs straight in instead of saving the zip:
  `curl -sL <asset-url> | C:\Windows\System32\tar.exe -xf -`.
- Unity is **not** installed on that PC, so builds and imports happen elsewhere. Script-only changes can be
  tried there with `pwsh Tools/patch-installed.ps1` (keeps the shipped DLL as `Assembly-CSharp.dll.orig`).
  It only works while no MonoBehaviour or serialized field is added or renamed, since the scene comes from
  the release build. `Tools/MonoScriptTypes.cs` stands in for Unity's generated MonoScript table.
- Check an install against the release before debugging: each zip lists its files, and a pack streamed in
  while the disk filled once dropped `badminton_10s_0_chunk.bytes` and `Horse_Walk_0_chunk.bytes`. A missing
  `_0_chunk` makes the plugin hang the GPU (`Device removed 887a0006`) and the app crash on that clip.

## Checking 3D on hardware

- `"C:\Program Files\DisplayXR\Runtime\displayxr-cli.exe" status` should show `leia-sr READY` and the panel
  `VERIFIED` / `TRACKING`.
- The app writes `displayxr.log` next to the exe (look for `Created overlay HWND` and steady `FRAMELOG`) and Unity's
  `%USERPROFILE%\AppData\LocalLow\DisplayXR Demos\4DV Viewer\Player.log`. Look for
  `DisplayXR successfully registered Provider` and one `[4DGSMgr] Splat:<clip> registered` line per clip.
- Screenshots of the woven panel come out blank; ask the user to look at the display. A desktop capture
  (`CopyFromScreen` from a DPI-aware process) does show the unwoven picture, which is enough to check framing.
- Injected key presses (`keybd_event`, `PostMessage`) don't reach the app, so test clips with `-4dv-clip N` and
  read `-4dv-debug` output in `Player.log` (it reports the content extent and depth fit per clip).
- While the app's own session isn't eye-tracked, a soft ring with a rotating highlight covers the screen. It
  isn't the app's. `status` can say `TRACKING` (the browser's session) while the app isn't. Check
  `[4DV] Eye tracking ON` / `[4DV] Eyes tracked=...` in `Player.log`.
- The 3D panel weaves one 3D window at a time. Close the DisplayXR Browser (it keeps an inline-3D lease) before
  judging the app's 3D.

## Clip sources

The original `.4dv` files come from 4DV.ai's assets portal (a Google Apps Script page that needs a 4dv.ai
sign-in). They're not in this repo or on the demo PC. Converted data can't go back to `.4dv`, so getting a clip
into the web player needs the original file.

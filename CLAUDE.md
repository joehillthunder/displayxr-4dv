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
| `Assets/Scripts/FourDVPlayer.cs` | Playlist (every `.asset` in `Assets/GaussianSplatAssets`, name order), loop modes, seek, speed, synced audio, per-clip framing (`view.json`, then `Assets/Resources/4dv-views.json` overrides, then `-4dv-*` command-line args). |
| `Assets/Scripts/FourDVOrbit.cs` | Display-centric DisplayXR rig: orbit, pan, zoom, 3D strength, 2D/3D, windowed sizing. |
| `Assets/Scripts/FourDVPanel.cs` | Sidebar UI: a DisplayXR window-space layer in 3D, a screen overlay in 2D. |
| `Assets/Editor/FourDVImport.cs` | Batch `.4dv` → plugin format via 4DV's obfuscated importer (`ImporterWindow.A`), plus `audio.mp3` and `view.json` sidecars. |
| `Assets/Editor/FourDVSetup.cs` | Headless setup + Win64 build (`FourDVSetup.Build`). |
| `Packages/com.4dv.4d-gaussian-splatting/` | 4DV.ai plugin v1.0.1, vendored. Don't edit it. |

## Rules

- **Don't edit 4DV's plugin.** Work around it from `Assets/` (as `FourDVSetup` does for the renderer feature's
  shader slots).
- **Direct3D 12 only**, URP 14, Unity 2022.3 LTS. D3D11 draws nothing (SM6 wave intrinsics in the radix sort).
- Camera and `DisplayXRDisplay` share one GameObject, which must be a child of the orbit pivot, never a root.
- The build must copy `UnitySubsystemsManifest.json` into `<Data>/UnitySubsystems/DisplayXR/`, or the app
  silently runs 2D.
- Converted clip data (`Assets/StreamingAssets/GaussianAssets/`, about 5.5 GB for 14 clips) is **never** committed.
  It ships in release zips (GitHub caps each file at 2 GB, hence `4DV-Viewer-Win64.zip` + `Clips-Pack2/3`).
- The clips and plugin are © 4DV.ai, shared for this demo. Keep the credit in README and release notes.
- Work on a branch and merge through a PR; end commit messages with the Co-Authored-By trailer.

## Releases and installing on the demo PC

- v1.0.0 = `4DV-Viewer-Win64.zip` (app + 8 clips) + `4DV-Viewer-Clips-Pack2.zip` / `Pack3.zip` (6 more). Unzip
  the packs into the same folder as `4DV Viewer.exe`; missing clips are just left out of the list.
- On the Leia demo PC (`4kDemo`) it's installed at `C:\Users\4kDemo\Apps\4DV Viewer\4DV-Viewer\` with all 14
  clips, and the desktop shortcut is **4DV Player (Unity)**.
- That PC is short on disk. Stream packs straight in instead of saving the zip:
  `curl -sL <asset-url> | C:\Windows\System32\tar.exe -xf -`.
- Unity is **not** installed on that PC, so builds and imports happen elsewhere.

## Checking 3D on hardware

- `"C:\Program Files\DisplayXR\Runtime\displayxr-cli.exe" status` should show `leia-sr READY` and the panel
  `VERIFIED` / `TRACKING`.
- The app writes `displayxr.log` next to the exe (look for `Created overlay HWND` and steady `FRAMELOG`) and Unity's
  `%USERPROFILE%\AppData\LocalLow\DisplayXR Demos\4DV Viewer\Player.log`. Look for
  `DisplayXR successfully registered Provider` and one `[4DGSMgr] Splat:<clip> registered` line per clip.
- Screenshots of the woven panel come out blank; ask the user to look at the display.

## Clip sources

The original `.4dv` files come from 4DV.ai's assets portal (a Google Apps Script page that needs a 4dv.ai
sign-in). They're not in this repo or on the demo PC. Converted data can't go back to `.4dv`, so getting a clip
into the web player needs the original file.

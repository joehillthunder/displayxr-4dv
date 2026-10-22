using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;

// Headless project setup and build. Idempotent; safe to re-run after importing new clips.
//   Unity.exe -batchmode -quit -projectPath . -executeMethod FourDVSetup.Setup
//   Unity.exe -batchmode -quit -projectPath . -executeMethod FourDVSetup.Build
//
// Android (Nubia Pad / RedMagic 3D), in a separate checkout (Leia's plugin must never reach the Windows
// build), two runs because the first one changes what compiles:
//   Unity.exe -batchmode -quit -buildTarget Android -projectPath . -executeMethod FourDVSetup.PrepareAndroid [-leia-package path]
//   Unity.exe -batchmode -quit -buildTarget Android -projectPath . -executeMethod FourDVSetup.BuildAndroid [-4dv-clips a,b,c]
public static class FourDVSetup
{
    const string kScenePath = "Assets/Scenes/4DVViewer.unity";
    const string kClipFolder = "Assets/GaussianSplatAssets";
    const string kPipelinePath = "Assets/Settings/URP-Pipeline.asset";
    const string kRendererPath = "Assets/Settings/URP-Renderer.asset";
    const string kPlainRendererPath = "Assets/Settings/URP-Renderer-Plain.asset";
    const string kProductName = "4DV Viewer";
    const string kLeiaFolder = "Assets/Leia";
    public const string kAndroidId = "com.displayxrdemos.fourdvviewer";
    // The lighter clips for a tablet (88-353 MB each, about 1 GB together); `-4dv-clips a,b,c` overrides.
    static readonly string[] kAndroidClips = { "dance_sample.c", "cat_high", "Kat_Miche_10s_150w", "TimTalk_iPhone17", "dance2" };

    [MenuItem("4DV/Set Up Project")]
    public static void Setup() => Setup(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android);

    static void Setup(bool android)
    {
        SetupRenderPipeline(android);
        SetupXR();
        SetupPlayer();
        if (android) SetupAndroidPlayer();
        BuildScene(android);
        AssetDatabase.SaveAssets();
        Debug.Log($"[4DV] Setup complete ({(android ? "Android" : "Windows")})");
    }

    [MenuItem("4DV/Build Windows Player")]
    public static void Build()
    {
        // Leia's plugin starts its own Windows SR weaving at load (WindowedWeavingInitializer, SRSystemHandler),
        // which fights DisplayXR for the panel.
        if (Directory.Exists(kLeiaFolder))
            Fail($"{kLeiaFolder} (Leia's Unity plugin, for Android) is in this project. Build Windows from a checkout without it.");
        Setup(false);
        var outDir = Path.GetFullPath("Builds/Win64/4DV-Viewer");
        Directory.CreateDirectory(outDir);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { kScenePath },
            locationPathName = Path.Combine(outDir, kProductName + ".exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            DeployDisplayXRManifest(report.summary.outputPath);
        Debug.Log($"[4DV] Build {report.summary.result}: {report.summary.outputPath}");
        if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }

    // ---- Android ----

    // Imports Leia's Unity plugin (LeiaUnity-3.3.2, from support.immersity.ai/sdk; it's proprietary, so it
    // isn't in git) and turns on the Android defines. Scripts recompile after this returns, so the build is a
    // second run.
    [MenuItem("4DV/Android/1. Prepare (import Leia plugin)")]
    public static void PrepareAndroid()
    {
        if (!Directory.Exists(kLeiaFolder))
        {
            var package = ArgValue("-leia-package") ?? FindLeiaPackage();
            if (package == null || !File.Exists(package))
                Fail("Leia's Unity plugin not found. Download LeiaUnity (Unity, Windows + Android) from support.immersity.ai/sdk " +
                     "and pass -leia-package <path to .unitypackage>.");
            Debug.Log($"[4DV] Importing {package}");
            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh();
        }
        // FOURDV_LEIA: the app's Leia rig. LEIA_URP_DETECTED: Leia's URP paths (its editor adds it on reload,
        // set here too so a headless run doesn't depend on that). DISPLAYXR_NO_SPLASH: DisplayXR's boot splash
        // spawns its own camera, which would cover Leia's.
        var target = UnityEditor.Build.NamedBuildTarget.Android;
        var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(d => d.Length > 0).ToList();
        foreach (var d in new[] { "FOURDV_LEIA", "LEIA_URP_DETECTED", "DISPLAYXR_NO_SPLASH" })
            if (!defines.Contains(d)) defines.Add(d);
        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        AssetDatabase.SaveAssets();
        Debug.Log("[4DV] Android prepared; now run FourDVSetup.BuildAndroid");
    }

    static string FindLeiaPackage()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ImmersitySDK");
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "LeiaUnity-*.unitypackage", SearchOption.AllDirectories).OrderByDescending(p => p).FirstOrDefault()
            : null;
    }

    [MenuItem("4DV/Android/2. Build APK")]
    public static void BuildAndroid()
    {
#if !FOURDV_LEIA
        Fail("FOURDV_LEIA isn't defined for this editor session: run FourDVSetup.PrepareAndroid first, and pass -buildTarget Android.");
#else
        // Clip data goes to the tablet over USB (Tools/push-android-clips.ps1), never into the APK.
        if (Directory.Exists("Assets/StreamingAssets/GaussianAssets"))
            Fail("Assets/StreamingAssets/GaussianAssets would be packed into the APK (gigabytes). Build Android from a checkout without it.");
        Setup(true);
        var outDir = Path.GetFullPath("Builds/Android");
        Directory.CreateDirectory(outDir);
        EditorUserBuildSettings.buildAppBundle = false;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { kScenePath },
            locationPathName = Path.Combine(outDir, "4DV-Viewer.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });
        Debug.Log($"[4DV] Build {report.summary.result}: {report.summary.outputPath}");
        if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
#endif
    }

    // Leia's recommended Android settings (ARM64, IL2CPP, API 29+, no minify), except the graphics API: Leia
    // suggests OpenGL ES 3, but 4DV's GPU sort needs wave intrinsics, which Unity only compiles for Vulkan
    // there. Leia's Android interlacer has a Vulkan path.
    static void SetupAndroidPlayer()
    {
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, kAndroidId);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.minifyRelease = false;
        PlayerSettings.Android.minifyDebug = false;
        PlayerSettings.Android.minifyWithR8 = false;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
    }

    static string ArgValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    static void Fail(string message)
    {
        Debug.LogError("[4DV] " + message);
        if (Application.isBatchMode) EditorApplication.Exit(1);
        throw new Exception(message);
    }

    // Without <Data>/UnitySubsystems/DisplayXR/UnitySubsystemsManifest.json the DisplayXR display provider never
    // loads and the app runs as plain 2D. The package's own post-build deploy didn't produce it in this project
    // (git-URL install), so copy it from wherever the package actually resolved.
    static void DeployDisplayXRManifest(string exePath)
    {
        var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.displayxr.unity");
        if (pkg == null) throw new Exception("com.displayxr.unity not found");
        var dataDir = Path.Combine(Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath) + "_Data");
        var copies = new[]
        {
            (Path.Combine(pkg.resolvedPath, "Runtime", "UnitySubsystemsManifest.json"),
             Path.Combine(dataDir, "UnitySubsystems", "DisplayXR", "UnitySubsystemsManifest.json")),
            (Path.Combine(pkg.resolvedPath, "Runtime", "Plugins", "Windows", "x64", "displayxr_unity.dll"),
             Path.Combine(dataDir, "Plugins", "x86_64", "displayxr_unity.dll")),
        };
        foreach (var (src, dst) in copies)
        {
            if (File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(src).Length) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
            Debug.Log($"[4DV] Deployed {Path.GetFileName(dst)}");
        }
    }

    static void SetupRenderPipeline(bool android)
    {
        Directory.CreateDirectory("Assets/Settings");
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(kRendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, kRendererPath);
        }

        // Add 4DV's splat renderer feature the same way URP's "Add Renderer Feature" button does.
        var featureType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("GaussianSplat4D.Runtime.GaussianSplat4DFeature"))
            .FirstOrDefault(t => t != null);
        if (featureType == null)
            throw new Exception("GaussianSplat4DFeature not found; is com.4dv.4d-gaussian-splatting imported?");
        if (!renderer.rendererFeatures.Any(f => f != null && f.GetType() == featureType))
        {
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
            feature.name = "GaussianSplat4D";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            var so = new SerializedObject(renderer);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(renderer);
        }

        // A feature made with CreateInstance has empty shader slots (4DV's sample sets them by hand in the
        // inspector), and Create() then throws in the GPU sort. Point each slot at the plugin's shader.
        const string kShaderDir = "Packages/com.4dv.4d-gaussian-splatting/Runtime/Shader/";
        foreach (var f in renderer.rendererFeatures.Where(f => f != null && f.GetType() == featureType))
        {
            var fso = new SerializedObject(f);
            void Slot(string field, string file)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(kShaderDir + file);
                if (asset == null) throw new Exception($"4DV shader {file} not found");
                fso.FindProperty(field).objectReferenceValue = asset;
            }
            Slot("GS4DShader", "GaussianSplat4D.shader");
            Slot("CompositeShader", "DepthAwareComposite.shader");
            Slot("splatComputeShader", "GaussianSplat4D.compute");
            // The _pico variant is 4DV's build for Snapdragon (Adreno) headsets; the Nubia and RedMagic tablets are Snapdragon too.
            Slot("dvr", android ? "DeviceRadixSort_pico.compute" : "DeviceRadixSort.compute");
            Slot("GSDepthStencil", "GSDepthStencil.shader");
            fso.ApplyModifiedProperties();
            EditorUtility.SetDirty(f);
        }

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(kPipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, kPipelinePath);
        }
        // Renderer 1 has no splat feature, for cameras whose image is thrown away (Leia's head and UI-event
        // cameras on Android); every camera on renderer 0 runs 4DV's sort and draw.
        var plain = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(kPlainRendererPath);
        if (plain == null)
        {
            plain = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(plain, kPlainRendererPath);
        }
        var pso = new SerializedObject(pipeline);
        var renderers = pso.FindProperty("m_RendererDataList");
        if (renderers.arraySize < 2) renderers.arraySize = 2;
        renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
        renderers.GetArrayElementAtIndex(1).objectReferenceValue = plain;
        pso.FindProperty("m_DefaultRendererIndex").intValue = 0;
        pso.ApplyModifiedPropertiesWithoutUndo();

        // MSAA off and no upscaler: the OpenXR validator rejects FSR/STP, and splats don't need MSAA.
        pipeline.msaaSampleCount = 1;
        pipeline.upscalingFilter = UpscalingFilterSelection.Auto;
        pipeline.supportsHDR = false;
        EditorUtility.SetDirty(pipeline);

        GraphicsSettings.renderPipelineAsset = pipeline;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
    }

    static void SetupXR()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
        {
            Directory.CreateDirectory("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }
        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Standalone))
            perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);

        var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        general.InitManagerOnStart = true;
        var manager = general.Manager;
        // DisplayXR drives the runtime itself; OpenXR on the same tab conflicts.
        XRPackageMetadataStore.RemoveLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone);
        XRPackageMetadataStore.AssignLoader(manager, "DisplayXR.DisplayXRDisplayLoader", BuildTargetGroup.Standalone);
        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(manager);
    }

    static void SetupPlayer()
    {
        PlayerSettings.productName = kProductName;
        PlayerSettings.companyName = "DisplayXR Demos";
        // Windowed weaving: DisplayXR weaves into the app's window wherever it sits; F11 toggles fullscreen.
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

        // 4DV's GPU radix sort needs SM 6.0 wave intrinsics (DXC), which D3D11 lacks. DisplayXR supports URP on D3D12.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });

        // Input handling: Both (the DisplayXR plugin uses the Input System package).
        var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (ps != null)
        {
            var so = new SerializedObject(ps);
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null && prop.intValue != 2) { prop.intValue = 2; so.ApplyModifiedProperties(); }
        }
    }

    static void BuildScene(bool android)
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Display-centric rig. The camera must be a child: in XR, Unity overwrites the camera's local pose
        // with the tracked eye pose every frame, so a root-level camera ends up at the origin, inside the clip.
        var pivot = new GameObject("Orbit Pivot");
        // 4DV captures put the performer at the origin facing -Z, inside a ~3.5 m ring of camera stands;
        // this default view sits inside the ring so no stands block it.
        pivot.transform.position = new Vector3(0f, 0.95f, 0f);
        var orbit = pivot.AddComponent<FourDVOrbit>();
        if (android)
        {
#if FOURDV_LEIA
            // Leia's display on the pivot. Its Update builds the Head and eye cameras the first time it runs
            // (in the editor, as when the component is added by hand); they must be saved in the scene, since
            // LeiaDisplay.OnEnable reads the head camera at startup.
            var leiaGo = new GameObject("LeiaDisplay");
            leiaGo.transform.SetParent(pivot.transform, false);
            var leia = leiaGo.AddComponent<LeiaUnity.LeiaDisplay>();
            leia.Update();
            if (leia.ViewersHead == null) Fail("LeiaDisplay didn't build its Head and eye cameras");
            var head = leia.HeadCamera;
            head.tag = "MainCamera";
            head.clearFlags = CameraClearFlags.SolidColor;
            head.backgroundColor = FourDVOrbit.Background;
            head.nearClipPlane = 0.05f;
            head.farClipPlane = 1000f;
            orbit.leia = leia;
#endif
        }
        else
        {
            // Camera and DisplayXRDisplay on one object, as in DisplayXR's samples: its 2D fallback and
            // per-camera hooks look for the Camera on the display's own GameObject.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(pivot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FourDVOrbit.Background;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            orbit.display = camGo.AddComponent<DisplayXR.DisplayXRDisplay>();
            orbit.fallbackCamera = cam;
        }

        var managerGo = new GameObject("GSManager");
        AddComponentByName(managerGo, "GaussianSplat4D.Runtime.GaussianSplat4DManager");

        var playerGo = new GameObject("4DV Clips");
        var player = playerGo.AddComponent<FourDVPlayer>();
        player.orbit = orbit;

        var panel = new GameObject("4DV Panel Host").AddComponent<FourDVPanel>();
        panel.player = player;
        panel.orbit = orbit;

        var sourceType = FindType("GaussianSplat4D.Runtime.GaussianSplat4DSource");
        var assetType = FindType("GaussianSplat4D.Runtime.GaussianSplat4DAsset");
        var clips = AssetDatabase.FindAssets("t:" + assetType.Name, new[] { kClipFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => AssetDatabase.LoadAssetAtPath(p, assetType))
            .Where(a => a != null)
            .ToArray();
        if (android)
        {
            var keep = ArgValue("-4dv-clips")?.Split(',') ?? kAndroidClips;
            clips = clips.Where(c => keep.Contains(c.name)).ToArray();
        }
        if (clips.Length == 0)
            Debug.LogWarning($"[4DV] No clips in {kClipFolder}. Import a .4dv with the 4DV importer, then re-run setup.");

        var sources = new System.Collections.Generic.List<Component>();
        foreach (var clip in clips)
        {
            var go = new GameObject(clip.name);
            go.transform.SetParent(playerGo.transform, false);
            var src = go.AddComponent(sourceType);
            var so = new SerializedObject(src);
            so.FindProperty("GSplat4DAsset").objectReferenceValue = clip;
            so.ApplyModifiedPropertiesWithoutUndo();
            sources.Add(src);
        }
        var pso = new SerializedObject(player);
        var arr = pso.FindProperty("clips");
        arr.arraySize = sources.Count;
        for (int i = 0; i < sources.Count; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue = sources[i];
        pso.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, kScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(kScenePath, true) };
        Debug.Log($"[4DV] Scene saved with {clips.Length} clip(s): {string.Join(", ", clips.Select(c => c.name))}");
    }

    static Type FindType(string fullName)
    {
        var t = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(fullName))
            .FirstOrDefault(x => x != null);
        if (t == null) throw new Exception($"Type {fullName} not found");
        return t;
    }

    static Component AddComponentByName(GameObject go, string fullName) => go.AddComponent(FindType(fullName));
}

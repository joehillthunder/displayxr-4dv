using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Batch-converts .4dv files with 4DV.ai's own importer, so clips don't have to be picked one at a time in its
// Tools > Import GS Assets window. 4DV's editor assembly is obfuscated; its import entry point is
// ImporterWindow.A(string sourceFile, string outputFolder), which writes <name>.asset plus the frame data
// (<name>_N_chunk/sh/vertex.bytes) into outputFolder and records that folder as the asset's byteFolderPath.
//
// For each clip this writes the frame data to Assets/StreamingAssets/GaussianAssets/<name>/ (loaded at runtime),
// makes sure the small .asset is in Assets/GaussianSplatAssets/ (the playlist), and saves two sidecars next to the
// frame data: view.json (4DV's suggested camera from the clip's settings.json) and audio.mp3 when the clip has one.
//
//   Unity.exe -batchmode -quit -projectPath . -executeMethod FourDVImport.ImportAll -4dv-dir C:\path\to\clips
public static class FourDVImport
{
    const string kClipFolder = "Assets/GaussianSplatAssets";
    const string kDataRoot = "Assets/StreamingAssets/GaussianAssets";

    [MenuItem("4DV/Import .4dv Folder...")]
    static void ImportFolderMenu()
    {
        var dir = EditorUtility.OpenFolderPanel("Folder of .4dv clips", "", "");
        if (!string.IsNullOrEmpty(dir)) ImportFolder(dir);
    }

    public static void ImportAll()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-4dv-dir");
        ImportFolder(i >= 0 && i + 1 < args.Length ? args[i + 1] : @"C:\Users\Demo\Downloads\4dv\clips");
    }

    public static void ImportFolder(string dir)
    {
        var importer = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "GaussianSplat4DEditor")
            .GetType("GaussianSplat4D.ImporterWindow");
        var import = importer.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(m => m.Name == "A" && m.GetParameters().Length == 2 &&
                        m.GetParameters().All(p => p.ParameterType == typeof(string)));
        var window = ScriptableObject.CreateInstance(importer);
        Directory.CreateDirectory(kClipFolder);

        foreach (var src in Directory.GetFiles(dir, "*.4dv").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = ClipName(src);
            var assetPath = $"{kClipFolder}/{name}.asset";
            var outDir = $"{kDataRoot}/{name}";
            if (File.Exists(assetPath))
            {
                if (!File.Exists(Path.Combine(outDir, "view.json"))) WriteSidecars(src, outDir);
                Debug.Log($"[4DV] {name}: already imported");
                continue;
            }

            // The importer names its output after the source file, so give it a clean name.
            var tmp = Path.Combine(Path.GetTempPath(), name + ".4dv");
            if (!string.Equals(Path.GetFileName(src), name + ".4dv", StringComparison.Ordinal)) File.Copy(src, tmp, true);
            else tmp = src;

            Directory.CreateDirectory(outDir);
            var t0 = DateTime.Now;
            try { import.Invoke(window, new object[] { tmp, outDir }); }
            catch (TargetInvocationException e)
            {
                Debug.LogError($"[4DV] {name}: import failed: {e.InnerException}");
                continue;
            }
            finally { if (tmp != src) File.Delete(tmp); }

            AssetDatabase.Refresh();
            // 4DV's importer writes the .asset to Assets/GaussianSplatAssets itself when given a
            // StreamingAssets output folder; move it there only if it landed beside the frame data.
            var made = $"{outDir}/{name}.asset";
            if (File.Exists(made))
            {
                var err = AssetDatabase.MoveAsset(made, assetPath);
                if (!string.IsNullOrEmpty(err)) { Debug.LogError($"[4DV] {name}: {err}"); continue; }
            }
            if (!File.Exists(assetPath)) { Debug.LogError($"[4DV] {name}: importer produced no {assetPath}"); continue; }

            WriteSidecars(src, outDir);
            Debug.Log($"[4DV] {name}: imported in {(DateTime.Now - t0).TotalSeconds:0}s");
        }
        UnityEngine.Object.DestroyImmediate(window);
        AssetDatabase.Refresh();
    }

    // Lower-case-free, filesystem- and URL-safe: "Kat & Miche_10s.4dv" -> "Kat_Miche_10s".
    static string ClipName(string path)
    {
        var n = Path.GetFileNameWithoutExtension(path);
        n = new string(n.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
        while (n.Contains("__")) n = n.Replace("__", "_");
        return n.Trim('_');
    }

    static void WriteSidecars(string src, string outDir)
    {
        using var zip = new ZipArchive(File.OpenRead(src), ZipArchiveMode.Read);
        string metaJson = Read(zip, "meta.json");
        string settingsPath = Field(metaJson, "settingPath") ?? "settings.json";
        string settings = Read(zip, settingsPath);
        if (settings != null) File.WriteAllText(Path.Combine(outDir, "view.json"), settings);

        string audio = Field(metaJson, "audioPath");
        var entry = string.IsNullOrEmpty(audio) ? null : zip.GetEntry(audio);
        if (entry == null) return;
        using var from = entry.Open();
        using var to = File.Create(Path.Combine(outDir, "audio" + Path.GetExtension(audio).ToLowerInvariant()));
        from.CopyTo(to);
    }

    static string Read(ZipArchive zip, string entryName)
    {
        var e = zip.GetEntry(entryName);
        if (e == null) return null;
        using var r = new StreamReader(e.Open());
        return r.ReadToEnd();
    }

    // meta.json is flat string fields; avoid pulling in a JSON package for one lookup.
    static string Field(string json, string key)
    {
        if (json == null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(json, $"\"{key}\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }
}

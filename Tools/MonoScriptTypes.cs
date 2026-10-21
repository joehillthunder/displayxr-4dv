// Recreates the class Unity's source generator adds to Assembly-CSharp (MonoScript lookup for the 3 script
// files / 7 types in Assets/Scripts, copied from the v1.0.0 build). Used only by Tools/patch-installed.ps1;
// regenerate it from a Unity build if a script file or serialized type is added or renamed.
[global::System.Runtime.CompilerServices.CompilerGenerated]
class UnitySourceGeneratedAssemblyMonoScriptTypes_v1
{
    private struct MonoScriptData
    {
        public byte[] FilePathsData;
        public byte[] TypesData;
        public int TotalTypes;
        public int TotalFiles;
        public bool IsEditorOnly;
    }

    [global::UnityEngine.Scripting.Preserve]
    private static MonoScriptData Get()
    {
        MonoScriptData result = new MonoScriptData();
        result.FilePathsData = new byte[] { 0, 0, 0, 1, 0, 0, 0, 30, 92, 65, 115, 115, 101, 116, 115, 92, 83, 99, 114, 105, 112, 116, 115, 92, 70, 111, 117, 114, 68, 86, 79, 114, 98, 105, 116, 46, 99, 115, 0, 0, 0, 1, 0, 0, 0, 30, 92, 65, 115, 115, 101, 116, 115, 92, 83, 99, 114, 105, 112, 116, 115, 92, 70, 111, 117, 114, 68, 86, 80, 97, 110, 101, 108, 46, 99, 115, 0, 0, 0, 5, 0, 0, 0, 31, 92, 65, 115, 115, 101, 116, 115, 92, 83, 99, 114, 105, 112, 116, 115, 92, 70, 111, 117, 114, 68, 86, 80, 108, 97, 121, 101, 114, 46, 99, 115 };
        result.TypesData = new byte[] { 0, 0, 0, 0, 12, 124, 70, 111, 117, 114, 68, 86, 79, 114, 98, 105, 116, 0, 0, 0, 0, 12, 124, 70, 111, 117, 114, 68, 86, 80, 97, 110, 101, 108, 0, 0, 0, 0, 13, 124, 70, 111, 117, 114, 68, 86, 80, 108, 97, 121, 101, 114, 0, 0, 0, 0, 23, 70, 111, 117, 114, 68, 86, 80, 108, 97, 121, 101, 114, 124, 86, 105, 101, 119, 67, 97, 109, 101, 114, 97, 0, 0, 0, 0, 25, 70, 111, 117, 114, 68, 86, 80, 108, 97, 121, 101, 114, 124, 86, 105, 101, 119, 83, 101, 116, 116, 105, 110, 103, 115, 0, 0, 0, 0, 25, 70, 111, 117, 114, 68, 86, 80, 108, 97, 121, 101, 114, 124, 86, 105, 101, 119, 79, 118, 101, 114, 114, 105, 100, 101, 0, 0, 0, 0, 26, 70, 111, 117, 114, 68, 86, 80, 108, 97, 121, 101, 114, 124, 86, 105, 101, 119, 79, 118, 101, 114, 114, 105, 100, 101, 115 };
        result.TotalFiles = 3;
        result.TotalTypes = 7;
        result.IsEditorOnly = false;
        return result;
    }
}

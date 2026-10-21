# Rebuilds Assembly-CSharp.dll (Assets/Scripts) against an installed 4DV Viewer and swaps it in, with no Unity.
# For machines like the Leia demo PC that have the player but not the editor. Needs PowerShell 7 (pwsh), whose
# bundled Roslyn compiles the scripts against the player's own Managed/*.dll. Only works while the scripts'
# MonoBehaviours and serialized fields match the installed build's scene; a real build is still the release path.
#
#   pwsh Tools/patch-installed.ps1 [-App "C:\Users\4kDemo\Apps\4DV Viewer\4DV-Viewer"] [-Restore]
param(
    [string]$App = "$env:USERPROFILE\Apps\4DV Viewer\4DV-Viewer",
    [switch]$Restore   # put the shipped Assembly-CSharp.dll back
)
$ErrorActionPreference = 'Stop'
$managed = Join-Path $App '4DV Viewer_Data\Managed'
$target = Join-Path $managed 'Assembly-CSharp.dll'
$orig = "$target.orig"
if (-not (Test-Path $target)) { throw "No player at $App" }
if (-not (Test-Path $orig)) { Copy-Item $target $orig }   # keep the shipped build once
if ($Restore) { Copy-Item $orig $target -Force; "Restored the shipped Assembly-CSharp.dll"; return }

$pwshDir = Split-Path ([System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName)
Add-Type -Path (Join-Path $pwshDir 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $pwshDir 'Microsoft.CodeAnalysis.CSharp.dll')

$refs = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
Get-ChildItem $managed -Filter *.dll | Where-Object Name -ne 'Assembly-CSharp.dll' |
    ForEach-Object { $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_.FullName)) }

$repo = Split-Path $PSScriptRoot
$sources = @(Get-ChildItem (Join-Path $repo 'Assets\Scripts') -Filter *.cs) + (Get-Item (Join-Path $PSScriptRoot 'MonoScriptTypes.cs'))
$parse = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp9)
$trees = $sources | ForEach-Object {
    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_.FullName), $parse, $_.FullName)
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).
    WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('Assembly-CSharp', [Microsoft.CodeAnalysis.SyntaxTree[]]$trees, $refs, $options)

$built = "$target.new"
$fs = [IO.File]::Create($built)
$result = $compilation.Emit($fs)
$fs.Dispose()
$result.Diagnostics | Where-Object Severity -eq ([Microsoft.CodeAnalysis.DiagnosticSeverity]::Error) | ForEach-Object { $_.ToString() }
if (-not $result.Success) { Remove-Item $built; throw 'Build failed; the installed app is unchanged.' }
Move-Item $built $target -Force
"Installed $target ($((Get-Item $target).Length) bytes); -Restore puts the shipped one back."

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$sourcePath = Join-Path $repoRoot 'scripts/unity/run-ci-tests.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($sourcePath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'The shipping runner must parse.' }
$generator = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'New-StandaloneBuildModifierSource' }, $true)
if (-not $generator) { throw 'The shipping modifier generator is missing.' }
Invoke-Expression $generator.Extent.Text
$source = New-StandaloneBuildModifierSource
$stubs = @'
namespace UnityEngine { public static class Debug { public static void Log(string value) { } } }
namespace UnityEngine.TestTools {
    [System.AttributeUsage(System.AttributeTargets.Assembly)] public sealed class TestPlayerBuildModifierAttribute : System.Attribute { public TestPlayerBuildModifierAttribute(System.Type value) { } }
    [System.AttributeUsage(System.AttributeTargets.Assembly)] public sealed class PostBuildCleanupAttribute : System.Attribute { public PostBuildCleanupAttribute(System.Type value) { } }
    public interface IPostBuildCleanup { void Cleanup(); }
}
namespace UnityEditor.TestTools { public interface ITestPlayerBuildModifier { UnityEditor.BuildPlayerOptions ModifyOptions(UnityEditor.BuildPlayerOptions options); } }
namespace UnityEditor.Build { public enum NamedBuildTarget { Standalone } }
namespace UnityEditor {
    public sealed class SceneAsset { }
    public static class AssetDatabase { public static bool Present = true; public static T LoadAssetAtPath<T>(string path) where T : class, new() { return Present ? new T() : null; } }
    [System.Flags] public enum BuildOptions { None = 0, AutoRunPlayer = 1, ConnectToHost = 2, ConnectWithProfiler = 4, IncludeTestAssemblies = 8, Development = 16 }
    public struct BuildPlayerOptions { public string[] scenes; public BuildOptions options; public string locationPathName; }
    public static class EditorApplication { public delegate void CallbackFunction(); public static CallbackFunction update; public static void Exit(int value) { } }
    public static class PlayerSettings { public static string GetScriptingBackend(Build.NamedBuildTarget target) { return "stub"; } public static string GetManagedStrippingLevel(Build.NamedBuildTarget target) { return "stub"; } }
}
'@
Add-Type -TypeDefinition ($source + [Environment]::NewLine + $stubs)
$scene = 'Packages/com.wallstop-studios.unity-helpers/Tests/Runtime/Scenes/Test1.unity'
$modifier = [UhCiStandaloneBuildModifier]::new()
$inputs = @(
    @{ Name = 'bootstrap'; Scenes = @('Assets/Runner.unity', 'Assets/Other.unity'); Expected = @('Assets/Runner.unity', 'Assets/Other.unity', $scene) },
    @{ Name = 'already provisioned'; Scenes = @('Assets/Runner.unity', $scene); Expected = @('Assets/Runner.unity', $scene) },
    @{ Name = 'empty'; Scenes = @(); Expected = @($scene) },
    @{ Name = 'null'; Scenes = $null; Expected = @($scene) }
)
$savedOutput = $env:UH_PLAYER_BUILD_PATH
try {
    $env:UH_PLAYER_BUILD_PATH = ''
    foreach ($case in $inputs) {
        $options = [UnityEditor.BuildPlayerOptions]::new()
        $options.scenes = $case.Scenes
        $options.options = [UnityEditor.BuildOptions]::Development -bor [UnityEditor.BuildOptions]::AutoRunPlayer
        $actual = $modifier.ModifyOptions($options)
        if (($actual.scenes -join '|') -cne ($case.Expected -join '|')) { throw "Scene order/content changed in $($case.Name)." }
        if (($options.scenes -join '|') -cne ($case.Scenes -join '|')) { throw 'The incoming scene array was mutated.' }
        if ($actual.options -band [UnityEditor.BuildOptions]::Development) { throw 'Release development flag was not cleared.' }
        if ($actual.options -band [UnityEditor.BuildOptions]::AutoRunPlayer) { throw 'Split-run auto launch was not cleared.' }
        if (-not ($actual.options -band [UnityEditor.BuildOptions]::IncludeTestAssemblies)) { throw 'Test assemblies were removed.' }
    }
    [UnityEditor.AssetDatabase]::Present = $false
    $rejected = $false
    try { $null = $modifier.ModifyOptions([UnityEditor.BuildPlayerOptions]::new()) }
    catch { $rejected = $_.Exception.ToString().Contains('Required standalone test scene is missing:') }
    if (-not $rejected) { throw 'A missing required scene must reject the build.' }
    Write-Host 'Standalone scene provision: 5 shipping-generated C# controls passed (Unity APIs stubbed; no player proof).'
}
finally { $env:UH_PLAYER_BUILD_PATH = $savedOutput }

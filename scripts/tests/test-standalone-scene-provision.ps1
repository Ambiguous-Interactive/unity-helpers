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
$preparationGenerator = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'New-WProtoBuildPreparationSource' }, $true)
if (-not $preparationGenerator) { throw 'The shipping preparation generator is missing.' }
Invoke-Expression $preparationGenerator.Extent.Text
$configuratorGenerator = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'New-ConfiguratorSource' }, $true)
if (-not $configuratorGenerator) { throw 'The shipping configurator generator is missing.' }
Invoke-Expression $configuratorGenerator.Extent.Text
$source = New-StandaloneBuildModifierSource
$stubs = @'
namespace UnityEngine { public static class Debug { public static readonly System.Collections.Generic.List<string> Logs = new System.Collections.Generic.List<string>(); public static void Log(string value) { Logs.Add(value); } } public static class Application { public static string dataPath; } }
namespace UnityEngine.TestTools {
    [System.AttributeUsage(System.AttributeTargets.Assembly)] public sealed class TestPlayerBuildModifierAttribute : System.Attribute { public TestPlayerBuildModifierAttribute(System.Type value) { } }
    [System.AttributeUsage(System.AttributeTargets.Assembly)] public sealed class PostBuildCleanupAttribute : System.Attribute { public PostBuildCleanupAttribute(System.Type value) { } }
    public interface IPostBuildCleanup { void Cleanup(); }
}
namespace UnityEditor.TestTools { public interface ITestPlayerBuildModifier { UnityEditor.BuildPlayerOptions ModifyOptions(UnityEditor.BuildPlayerOptions options); } }
namespace UnityEditor.Build { public struct NamedBuildTarget { public static readonly NamedBuildTarget Standalone = new NamedBuildTarget(); public string TargetName { get { return "Standalone"; } } } }
namespace UnityEditor.Compilation { public enum CodeOptimization { Debug, Release } public static class CompilationPipeline { public static CodeOptimization codeOptimization; } }
namespace UnityEditor {
    public sealed class SceneAsset { }
    public static class AssetDatabase {
        public static bool Present = true;
        public static int SaveCount;
        public static int VariantSetCountAtSave;
        public static ManagedCodeVariant VariantAtSave;
        public static T LoadAssetAtPath<T>(string path) where T : class, new() { return Present ? new T() : null; }
        public static void SaveAssets() {
            string settings = System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, "ProjectSettings");
            if (!System.IO.File.Exists(System.IO.Path.Combine(settings, "UnityHelpersSettings.asset"))) { throw new System.InvalidOperationException("Settings must be saved before assets."); }
            System.IO.File.WriteAllText(System.IO.Path.Combine(settings, "SavedAssets.txt"), "assets saved");
            VariantSetCountAtSave = PlayerSettings.VariantSetCount;
            VariantAtSave = PlayerSettings.Variant;
            SaveCount++;
        }
    }
    [System.Flags] public enum BuildOptions { None = 0, AutoRunPlayer = 1, ConnectToHost = 2, ConnectWithProfiler = 4, IncludeTestAssemblies = 8, Development = 16 }
    public struct BuildPlayerOptions { public string[] scenes; public BuildOptions options; public string locationPathName; }
    public static class EditorApplication { public delegate void CallbackFunction(); public static CallbackFunction update; public static void Exit(int value) { } }
    public enum BuildTargetGroup { Standalone }
    public enum BuildTarget { StandaloneWindows64 }
    public enum ScriptingImplementation { Mono2x, IL2CPP }
    public enum ApiCompatibilityLevel { NET_Standard }
    public enum ManagedStrippingLevel { Disabled, Low, Medium, High }
    public enum Il2CppCompilerConfiguration { Release, Debug }
    public enum ManagedCodeVariant { Default = 0, Release = 3 }
    public static class EditorUserBuildSettings { public static bool SwitchActiveBuildTarget(BuildTargetGroup group, BuildTarget target) { return true; } }
    public static class PlayerSettings {
        public static int VariantSetCount;
        public static ManagedCodeVariant Variant;
        private static ScriptingImplementation backend;
        private static ApiCompatibilityLevel api;
        private static ManagedStrippingLevel stripping;
        private static Il2CppCompilerConfiguration compiler;
        private static string[] defines = new string[0];
        public static void SetScriptingBackend(Build.NamedBuildTarget target, ScriptingImplementation value) { backend = value; }
        public static ScriptingImplementation GetScriptingBackend(Build.NamedBuildTarget target) { return backend; }
        public static void SetManagedCodeVariant(Build.NamedBuildTarget target, ManagedCodeVariant value) { Variant = value; VariantSetCount++; }
        public static ManagedCodeVariant GetManagedCodeVariant(Build.NamedBuildTarget target) { return Variant; }
        public static void SetApiCompatibilityLevel(Build.NamedBuildTarget target, ApiCompatibilityLevel value) { api = value; }
        public static ApiCompatibilityLevel GetApiCompatibilityLevel(Build.NamedBuildTarget target) { return api; }
        public static void SetManagedStrippingLevel(Build.NamedBuildTarget target, ManagedStrippingLevel value) { stripping = value; }
        public static ManagedStrippingLevel GetManagedStrippingLevel(Build.NamedBuildTarget target) { return stripping; }
        public static void SetIl2CppCompilerConfiguration(Build.NamedBuildTarget target, Il2CppCompilerConfiguration value) { compiler = value; }
        public static Il2CppCompilerConfiguration GetIl2CppCompilerConfiguration(Build.NamedBuildTarget target) { return compiler; }
        public static void GetScriptingDefineSymbols(Build.NamedBuildTarget target, out string[] values) { values = defines; }
        public static void SetScriptingDefineSymbols(Build.NamedBuildTarget target, string[] values) { defines = values; }
    }
}
namespace WallstopStudios.UnityHelpers.Editor.Settings {
    public sealed class UnityHelpersSettings {
        public static readonly UnityHelpersSettings instance = new UnityHelpersSettings();
        public static int SaveCount;
        public void SaveSettings() {
            string project = System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            System.IO.File.WriteAllText(System.IO.Path.Combine(project, "ProjectSettings", "UnityHelpersSettings.asset"), "settings saved");
            SaveCount++;
        }
    }
}
public sealed partial class WProtoPreparationControl {
    static partial void WriteFrozenBuildPreparation(UnityEditor.BuildPlayerOptions playerOptions);
    public static void Invoke(UnityEditor.BuildPlayerOptions options) { WriteFrozenBuildPreparation(options); }
}
'@
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('wproto-preparation-control-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $temporary 'project'
$compileDirectory = Join-Path $temporary 'sources'
$receiptDirectory = Join-Path $temporary 'receipts'
$savedOutput = $env:UH_PLAYER_BUILD_PATH
$savedDefines = $env:UH_ADDITIONAL_SCRIPTING_DEFINES
$savedMarker = $env:UH_CONFIGURE_MARKER_PATH
try {
    foreach ($directory in @('Assets/Nested/Empty', 'Packages', 'ProjectSettings')) {
        $null = [IO.Directory]::CreateDirectory((Join-Path $project $directory))
    }
    $null = [IO.Directory]::CreateDirectory($compileDirectory)
    $null = [IO.Directory]::CreateDirectory($receiptDirectory)
    [IO.File]::WriteAllBytes((Join-Path $project 'Assets/Nested/Input.bytes'), [byte[]]@(0, 1, 127, 128, 255))
    [IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), '{"dependencies":{}}')
    [IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/Existing.asset'), 'existing settings')
    $context = [pscustomobject]@{
        Declaration = [pscustomobject]@{
            Value = [pscustomobject]@{ RunId = 'preparation-control'; SourceCommit = ('a' * 40) }
            Sha256 = ('b' * 64)
            ReportDirectory = $receiptDirectory
        }
    }
    # Rename only the hook class to compile both the erased ordinary call and the full producer in one assembly.
    $preparation = (New-WProtoBuildPreparationSource -Context $context).Replace('UhCiStandaloneBuildModifier', 'WProtoPreparationControl')
    $compileSources = @(
        @{ Name = 'Modifier.cs'; Text = $source },
        @{ Name = 'Preparation.cs'; Text = $preparation },
        @{ Name = 'Stubs.cs'; Text = $stubs },
        @{ Name = 'LatestMono.cs'; Text = "#define UNITY_6000_6_OR_NEWER`n" + (New-ConfiguratorSource -Backend Mono2x).Replace('UhCiTestConfigurator', 'LatestMonoConfiguratorControl') },
        @{ Name = 'LatestIl2Cpp.cs'; Text = "#define UNITY_6000_6_OR_NEWER`n" + (New-ConfiguratorSource -Backend IL2CPP).Replace('UhCiTestConfigurator', 'LatestIl2CppConfiguratorControl') },
        @{ Name = 'OlderMono.cs'; Text = (New-ConfiguratorSource -Backend Mono2x).Replace('UhCiTestConfigurator', 'OlderMonoConfiguratorControl') }
    )
    $compilePaths = foreach ($item in $compileSources) {
        $path = Join-Path $compileDirectory $item.Name
        [IO.File]::WriteAllText($path, $item.Text)
        $path
    }
    Add-Type -Path $compilePaths
    $scene = 'Packages/com.wallstop-studios.unity-helpers/Tests/Runtime/Scenes/Test1.unity'
    $modifier = [UhCiStandaloneBuildModifier]::new()
    $inputs = @(
        @{ Name = 'bootstrap'; Scenes = @('Assets/Runner.unity', 'Assets/Other.unity'); Expected = @('Assets/Runner.unity', 'Assets/Other.unity', $scene) },
        @{ Name = 'already provisioned'; Scenes = @('Assets/Runner.unity', $scene); Expected = @('Assets/Runner.unity', $scene) },
        @{ Name = 'empty'; Scenes = @(); Expected = @($scene) },
        @{ Name = 'null'; Scenes = $null; Expected = @($scene) }
    )
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
    if ([UnityEditor.AssetDatabase]::SaveCount -ne 0 -or [WallstopStudios.UnityHelpers.Editor.Settings.UnityHelpersSettings]::SaveCount -ne 0) {
        throw 'The ordinary modifier must erase the unimplemented preparation call.'
    }
    Write-Host 'Standalone scene provision: 5 shipping-generated C# controls passed (Unity APIs stubbed; no player proof).'

    [UnityEngine.Application]::dataPath = Join-Path $project 'Assets'
    $preparationOptions = [UnityEditor.BuildPlayerOptions]::new()
    $preparationOptions.scenes = @('Assets\Runner.unity', $scene)
    [WProtoPreparationControl]::Invoke($preparationOptions)
    $receiptPath = Join-Path $receiptDirectory 'build-preparation.json'
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.SchemaVersion -ne 1 -or $receipt.RunId -cne $context.Declaration.Value.RunId -or $receipt.DeclarationSha256 -cne $context.Declaration.Sha256 -or $receipt.SourceCommit -cne $context.Declaration.Value.SourceCommit) {
        throw 'The emitted preparation receipt lost its declaration-bound identity.'
    }
    if (($receipt.Scenes -join '|') -cne (@('Assets/Runner.unity', $scene) -join '|')) { throw 'Observed scene order or path normalization changed.' }

    $expectedEntries = @(foreach ($directory in @('Assets', 'Packages', 'ProjectSettings')) {
        Get-ChildItem -LiteralPath (Join-Path $project $directory) -Recurse -Force
    })
    if ($receipt.ProjectInputs.Count -ne $expectedEntries.Count -or $expectedEntries.Count -eq 0) { throw 'The preparation census must cover every project entry.' }
    foreach ($entry in $expectedEntries) {
        $relative = [IO.Path]::GetRelativePath($project, $entry.FullName).Replace('\', '/')
        $rows = @($receipt.ProjectInputs | Where-Object { $_.Path -ceq $relative })
        if ($rows.Count -ne 1) { throw "Missing or duplicate preparation row: $relative" }
        $row = $rows[0]
        if ($entry.PSIsContainer) {
            if ($row.Kind -cne 'directory' -or $null -ne $row.Length -or $null -ne $row.Sha256 -or @($row.PSObject.Properties.Name | Where-Object { $_ -ceq 'Length' -or $_ -ceq 'Sha256' }).Count -ne 2) {
                throw "Directory metadata must contain explicit null length/hash: $relative"
            }
        }
        else {
            $expectedHash = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($row.Kind -cne 'file' -or $row.Length -ne $entry.Length -or $row.Sha256 -cne $expectedHash) { throw "File metadata differs from actual bytes: $relative" }
        }
    }
    if ([UnityEditor.AssetDatabase]::SaveCount -ne 1 -or [WallstopStudios.UnityHelpers.Editor.Settings.UnityHelpersSettings]::SaveCount -ne 1) { throw 'Both settings and assets must be saved before capture.' }
    foreach ($savedPath in @('ProjectSettings/UnityHelpersSettings.asset', 'ProjectSettings/SavedAssets.txt')) {
        if (@($receipt.ProjectInputs | Where-Object { $_.Path -ceq $savedPath }).Count -ne 1) { throw "Capture occurred before saving: $savedPath" }
    }

    $originalReceiptHash = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
    $rejected = $false
    try { [WProtoPreparationControl]::Invoke($preparationOptions) }
    catch {
        $exception = $_.Exception
        while ($null -ne $exception.InnerException) { $exception = $exception.InnerException }
        $rejected = $exception -is [IO.IOException]
    }
    if (-not $rejected -or (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -cne $originalReceiptHash) { throw 'A second capture must refuse to overwrite the existing receipt.' }
    Write-Host 'WProto preparation: 4 shipping-generated C# controls passed (identity/scenes, complete census, save ordering, exclusive creation; Unity APIs stubbed; no player proof).'

    $env:UH_ADDITIONAL_SCRIPTING_DEFINES = ''
    $env:UH_CONFIGURE_MARKER_PATH = ''
    $configuratorCases = @(
        @{ Name = 'latest Mono'; Apply = { [LatestMonoConfiguratorControl]::Apply() }; Backend = [UnityEditor.ScriptingImplementation]::Mono2x; ExpectedPins = 1; ExpectedVariant = [UnityEditor.ManagedCodeVariant]::Release },
        @{ Name = 'latest IL2CPP'; Apply = { [LatestIl2CppConfiguratorControl]::Apply() }; Backend = [UnityEditor.ScriptingImplementation]::IL2CPP; ExpectedPins = 0; ExpectedVariant = [UnityEditor.ManagedCodeVariant]::Default },
        @{ Name = 'older Mono'; Apply = { [OlderMonoConfiguratorControl]::Apply() }; Backend = [UnityEditor.ScriptingImplementation]::Mono2x; ExpectedPins = 0; ExpectedVariant = [UnityEditor.ManagedCodeVariant]::Default }
    )
    foreach ($case in $configuratorCases) {
        [UnityEditor.PlayerSettings]::VariantSetCount = 0
        [UnityEditor.PlayerSettings]::Variant = [UnityEditor.ManagedCodeVariant]::Default
        [UnityEditor.AssetDatabase]::SaveCount = 0
        [UnityEditor.AssetDatabase]::VariantSetCountAtSave = -1
        [UnityEditor.AssetDatabase]::VariantAtSave = [UnityEditor.ManagedCodeVariant]::Default
        [UnityEngine.Debug]::Logs.Clear()
        & $case.Apply
        if ([UnityEditor.PlayerSettings]::GetScriptingBackend([UnityEditor.Build.NamedBuildTarget]::Standalone) -ne $case.Backend) { throw "Wrong effective backend in $($case.Name)." }
        if ([UnityEditor.PlayerSettings]::VariantSetCount -ne $case.ExpectedPins -or [UnityEditor.PlayerSettings]::Variant -ne $case.ExpectedVariant) { throw "Wrong managed variant pin in $($case.Name)." }
        if ([UnityEditor.AssetDatabase]::SaveCount -ne 1 -or [UnityEditor.AssetDatabase]::VariantSetCountAtSave -ne $case.ExpectedPins -or [UnityEditor.AssetDatabase]::VariantAtSave -ne $case.ExpectedVariant) { throw "Managed variant must be effective before SaveAssets in $($case.Name)." }
        $variantLogs = @([UnityEngine.Debug]::Logs | Where-Object { $_.StartsWith('UH perf managed code variant: ') })
        if ($variantLogs.Count -ne $case.ExpectedPins -or ($case.ExpectedPins -eq 1 -and $variantLogs[0] -cne ('UH perf managed code variant: ' + $case.ExpectedVariant))) { throw "Missing or incorrect effective variant log in $($case.Name)." }
        $configLogs = @([UnityEngine.Debug]::Logs | Where-Object { $_.StartsWith('UH perf config: backend=' + $case.Backend + ',') })
        if ($configLogs.Count -ne 1) { throw "Missing effective backend log in $($case.Name)." }
    }
    Write-Host 'Configurator: 3 shipping-generated C# branch controls passed (latest Mono, latest IL2CPP, older Mono; Unity APIs stubbed; no player proof).'
}
finally {
    $env:UH_PLAYER_BUILD_PATH = $savedOutput
    $env:UH_ADDITIONAL_SCRIPTING_DEFINES = $savedDefines
    $env:UH_CONFIGURE_MARKER_PATH = $savedMarker
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}

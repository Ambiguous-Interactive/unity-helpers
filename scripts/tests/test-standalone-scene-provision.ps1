$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repoRoot 'scripts/unity/lib/wproto-player-provenance.ps1')
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
            string playerSettings = System.IO.Path.Combine(settings, "ProjectSettings.asset");
            if (System.IO.File.Exists(playerSettings) && PlayerSettings.Variant == ManagedCodeVariant.Release)
            {
                System.IO.File.WriteAllText(playerSettings, System.IO.File.ReadAllText(playerSettings).Replace("Standalone: 1", "Standalone: 3"));
            }
            if (System.IO.File.Exists(playerSettings) && PlayerSettings.VariantSetCount > 0)
            {
                string text = System.IO.File.ReadAllText(playerSettings);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^  managedCodeVariant: \{\}\r?$", "  managedCodeVariant:\n    Standalone: " + (int)PlayerSettings.Variant);
                text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^    Standalone: [0-3]\r?$", "    Standalone: " + (int)PlayerSettings.Variant);
                System.IO.File.WriteAllText(playerSettings, text);
            }
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
    public enum ManagedCodeVariant { Default = 0, Checked = 1, Release = 3 }
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
$evidenceRoot = $env:WPROTO_PRODUCER_EVIDENCE_ROOT
$keepEvidence = -not [string]::IsNullOrWhiteSpace($evidenceRoot)
$parent = if ($keepEvidence) { $evidenceRoot } else { [IO.Path]::GetTempPath() }
$temporary = Join-Path $parent ('wproto-preparation-control-' + [guid]::NewGuid().ToString('N'))
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
    $playerSettingsPath = Join-Path $project 'ProjectSettings/ProjectSettings.asset'
    [IO.File]::WriteAllText($playerSettingsPath, "actual prepared bytes`nStandalone: 3")
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
        @{ Name = 'LatestFrozenIl2Cpp.cs'; Text = "#define UNITY_6000_6_OR_NEWER`n" + (New-ConfiguratorSource -Backend IL2CPP -RequireReleaseManagedCodeVariant).Replace('UhCiTestConfigurator', 'LatestFrozenIl2CppConfiguratorControl') },
        @{ Name = 'OlderFrozenIl2Cpp.cs'; Text = (New-ConfiguratorSource -Backend IL2CPP -RequireReleaseManagedCodeVariant).Replace('UhCiTestConfigurator', 'OlderFrozenIl2CppConfiguratorControl') },
        @{ Name = 'OlderMono.cs'; Text = (New-ConfiguratorSource -Backend Mono2x).Replace('UhCiTestConfigurator', 'OlderMonoConfiguratorControl') }
    )
    $producerCases = @(
        @{ Name = 'Latest'; Invoke = { [LatestWProtoPreparationControl]::Invoke($preparationOptions) }; Reject = $false },
        @{ Name = 'LatestIl2Cpp'; Invoke = { [LatestIl2CppWProtoPreparationControl]::Invoke($preparationOptions) }; Reject = $false },
        @{ Name = 'MissingCopy'; Invoke = { [MissingCopyWProtoPreparationControl]::Invoke($preparationOptions) }; Reject = $true },
        @{ Name = 'MissingRelease'; Invoke = { [MissingReleaseWProtoPreparationControl]::Invoke($preparationOptions) }; Reject = $true },
        @{ Name = 'MissingLimit'; Invoke = { [MissingLimitWProtoPreparationControl]::Invoke($preparationOptions) }; Reject = $true },
        @{ Name = 'Overwrite'; Invoke = { [OverwriteWProtoPreparationControl]::Invoke($preparationOptions) }; Reject = $true }
    )
    foreach ($case in $producerCases) {
        $class = $case.Name + 'WProtoPreparationControl'
        $directory = Join-Path $receiptDirectory $case.Name
        $null = [IO.Directory]::CreateDirectory($directory)
        $caseContext = @{ Declaration = @{ Value = $context.Declaration.Value; Sha256 = $context.Declaration.Sha256; ReportDirectory = $directory } }
        $text = (New-WProtoBuildPreparationSource -Context $caseContext).Replace('UhCiStandaloneBuildModifier', $class)
        $before = $text
        switch ($case.Name) {
            'MissingCopy' { $text = [regex]::Replace($text, '(?m)^        File.Copy\(playerSettingsPath,[^\r\n]+\r?\n', '') }
            'MissingRelease' { $text = $text.Replace('PlayerSettings.SetManagedCodeVariant(UnityEditor.Build.NamedBuildTarget.Standalone, ManagedCodeVariant.Release);', '') }
            'MissingLimit' { $text = $text.Replace('1048576 < playerSettings.Length', 'long.MaxValue < playerSettings.Length') }
            'Overwrite' { $text = $text.Replace(', "build-player-settings.asset"), false);', ', "build-player-settings.asset"), true);') }
        }
        if ($case.Reject -and $before -ceq $text) { throw 'A producer mutation did not change its actual source.' }
        $wrapper = "public sealed partial class $class { static partial void WriteFrozenBuildPreparation(UnityEditor.BuildPlayerOptions options); public static void Invoke(UnityEditor.BuildPlayerOptions options) { WriteFrozenBuildPreparation(options); } }"
        $compileSources += @{ Name = ($class + '.cs'); Text = "#define UNITY_6000_6_OR_NEWER`n" + $text + "`n" + $wrapper }
    }
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
    if ($receipt.SchemaVersion -ne 2 -or $receipt.RunId -cne $context.Declaration.Value.RunId -or $receipt.DeclarationSha256 -cne $context.Declaration.Sha256 -or $receipt.SourceCommit -cne $context.Declaration.Value.SourceCommit) {
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
    if ([UnityEditor.PlayerSettings]::VariantSetCount -ne 0) { throw 'The older callback must not call the newer managed-variant API.' }
    foreach ($savedPath in @('ProjectSettings/UnityHelpersSettings.asset', 'ProjectSettings/SavedAssets.txt')) {
        if (@($receipt.ProjectInputs | Where-Object { $_.Path -ceq $savedPath }).Count -ne 1) { throw "Capture occurred before saving: $savedPath" }
    }

    $preparedPath = Join-Path $receiptDirectory 'build-player-settings.asset'
    $preparedBytes = [IO.File]::ReadAllBytes($preparedPath)
    $preparedRow = @($receipt.ProjectInputs | Where-Object { $_.Path -ceq 'ProjectSettings/ProjectSettings.asset' })
    if ($preparedRow.Count -ne 1 -or $preparedBytes.Length -ne $preparedRow[0].Length -or
        (Get-FileHash -LiteralPath $preparedPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $preparedRow[0].Sha256) { throw 'Actual prepared settings are not bound to the producer inventory.' }
    [IO.File]::WriteAllText($playerSettingsPath, 'restored settings')
    if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($preparedPath)) -cne [Convert]::ToBase64String($preparedBytes)) { throw 'Restoration changed retained prepared bytes.' }
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

    Remove-Item -LiteralPath $preparedPath, $receiptPath
    [IO.File]::WriteAllText($playerSettingsPath, ('x' * 1048577))
    $rejected = $false
    try { [WProtoPreparationControl]::Invoke($preparationOptions) }
    catch { $rejected = $_.Exception.ToString().Contains('Invalid WProto prepared player settings.') }
    if (-not $rejected -or (Test-Path -LiteralPath $preparedPath)) { throw 'Oversized settings must reject before copying.' }
    foreach ($case in $producerCases) {
        $directory = Join-Path $receiptDirectory $case.Name
        $snapshot = Join-Path $directory 'build-player-settings.asset'
        $backend = if ($case.Name -ceq 'LatestIl2Cpp') { [UnityEditor.ScriptingImplementation]::IL2CPP } else { [UnityEditor.ScriptingImplementation]::Mono2x }
        [UnityEditor.PlayerSettings]::SetScriptingBackend([UnityEditor.Build.NamedBuildTarget]::Standalone, $backend)
        [UnityEditor.PlayerSettings]::Variant = [UnityEditor.ManagedCodeVariant]::Checked
        [UnityEditor.PlayerSettings]::VariantSetCount = 0
        [IO.File]::WriteAllText($playerSettingsPath, $(if ($case.Name -ceq 'MissingLimit') { 'x' * 1048577 } else { "actual prepared bytes`nStandalone: 1" }))
        & $case.Invoke
        $expectedPins = if ($case.Name -ceq 'LatestIl2Cpp') { 0 } else { 1 }
        $expectedVariant = if ($case.Name -ceq 'LatestIl2Cpp') { [UnityEditor.ManagedCodeVariant]::Checked } else { [UnityEditor.ManagedCodeVariant]::Release }
        $valid = (Test-Path -LiteralPath $snapshot) -and (Get-Item -LiteralPath $snapshot).Length -le 1048576 -and
            [UnityEditor.PlayerSettings]::VariantSetCount -eq $expectedPins -and [UnityEditor.AssetDatabase]::VariantAtSave -eq $expectedVariant
        if ($valid) { $valid = ([IO.File]::ReadAllText($snapshot) -ceq "actual prepared bytes`nStandalone: $([int]$expectedVariant)") }
        if ($case.Name -ceq 'Overwrite') {
            if (-not $valid) { throw 'Overwrite control must first capture valid prepared bytes.' }
            $original = (Get-FileHash -LiteralPath $snapshot).Hash
            [IO.File]::WriteAllText($playerSettingsPath, 'later restored bytes')
            $collision = $false
            try { & $case.Invoke } catch {
                $exception = $_.Exception
                while ($null -ne $exception.InnerException) { $exception = $exception.InnerException }
                $collision = $exception -is [IO.IOException]
            }
            if (-not $collision) { throw 'Overwrite control did not reach exclusive receipt creation.' }
            $valid = (Get-FileHash -LiteralPath $snapshot).Hash -ceq $original
        }
        if ($valid -eq $case.Reject) { throw "Producer control $($case.Name) did not distinguish the shipping callback from its mutation." }
        Write-Host "Producer control $($case.Name): expected acceptance $(-not $case.Reject) observed."
    }
    [IO.File]::WriteAllText($playerSettingsPath, "actual prepared bytes`nStandalone: 3")
    $env:UH_ADDITIONAL_SCRIPTING_DEFINES = ''
    $env:UH_CONFIGURE_MARKER_PATH = ''
    $configuratorCases = @(
        @{ Name = 'latest Mono'; Apply = { [LatestMonoConfiguratorControl]::Apply() }; Backend = [UnityEditor.ScriptingImplementation]::Mono2x; ExpectedPins = 1; ExpectedVariant = [UnityEditor.ManagedCodeVariant]::Release },
        @{ Name = 'latest IL2CPP'; Apply = { [LatestIl2CppConfiguratorControl]::Apply() }; Backend = [UnityEditor.ScriptingImplementation]::IL2CPP; ExpectedPins = 0; ExpectedVariant = [UnityEditor.ManagedCodeVariant]::Default },
        @{ Name = 'latest frozen IL2CPP'; Apply = { [LatestFrozenIl2CppConfiguratorControl]::Apply() }; Backend = [UnityEditor.ScriptingImplementation]::IL2CPP; ExpectedPins = 1; ExpectedVariant = [UnityEditor.ManagedCodeVariant]::Release },
        @{ Name = 'older frozen IL2CPP'; Apply = { [OlderFrozenIl2CppConfiguratorControl]::Apply() }; Backend = [UnityEditor.ScriptingImplementation]::IL2CPP; ExpectedPins = 0; ExpectedVariant = [UnityEditor.ManagedCodeVariant]::Default },
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
    Write-Host "Configurator: $($configuratorCases.Count) shipping-generated C# branch controls passed (ordinary/frozen backends, older API guards; Unity APIs stubbed; no player proof)."

    function Get-ControlSettingsEntry {
        param([string]$Path)
        return [pscustomobject]@{
            Path = 'ProjectSettings/ProjectSettings.asset'; Kind = 'file'
            Length = (Get-Item -LiteralPath $Path).Length
            Sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $transitionCases = @(
        @{ Name = 'Mono'; Backend = 'Mono2x'; PreparedVariant = 3; Configure = { [LatestMonoConfiguratorControl]::Apply() }; Prepare = { [LatestWProtoPreparationControl]::Invoke($preparationOptions) }; Directory = 'Latest' },
        @{ Name = 'IL2CPP'; Backend = 'IL2CPP'; PreparedVariant = 1; Configure = { [LatestFrozenIl2CppConfiguratorControl]::Apply() }; Prepare = { [LatestIl2CppWProtoPreparationControl]::Invoke($preparationOptions) }; Directory = 'LatestIl2Cpp' }
    )
    $transitionChecks = 0
    foreach ($case in $transitionCases) {
        foreach ($initialMap in @('empty', 'Checked', 'Release')) {
            $map = switch ($initialMap) {
                'empty' { '  managedCodeVariant: {}' }
                'Checked' { "  managedCodeVariant:`n    Standalone: 1" }
                'Release' { "  managedCodeVariant:`n    Standalone: 3" }
            }
            $configuredText = "PlayerSettings:`n  companyName: preserved`n  runInBackground: 0`n  fullscreenMode: 1`n  resizableWindow: 0`n  m_ShowUnitySplashScreen: 1`n  m_ShowUnitySplashLogo: 1`n$map`n"
            [IO.File]::WriteAllText($playerSettingsPath, $configuredText)
            [UnityEditor.PlayerSettings]::Variant = if ($initialMap -ceq 'Checked') { [UnityEditor.ManagedCodeVariant]::Checked } else { [UnityEditor.ManagedCodeVariant]::Release }
            [UnityEditor.PlayerSettings]::VariantSetCount = 0
            & $case.Configure
            $configuredText = [IO.File]::ReadAllText($playerSettingsPath)
            if (-not $configuredText.Contains("  managedCodeVariant:`n    Standalone: 3`n")) { throw "Missing explicit configured Release map for $($case.Name)/$initialMap." }
            $configuredEntry = Get-ControlSettingsEntry $playerSettingsPath
            $oldVariant = [UnityEditor.PlayerSettings]::GetManagedCodeVariant([UnityEditor.Build.NamedBuildTarget]::Standalone)
            [UnityEditor.PlayerSettings]::SetManagedCodeVariant([UnityEditor.Build.NamedBuildTarget]::Standalone, [UnityEditor.ManagedCodeVariant]::Checked)
            $preparedText = $configuredText.Replace('runInBackground: 0', 'runInBackground: 1').Replace('fullscreenMode: 1', 'fullscreenMode: 3').Replace('resizableWindow: 0', 'resizableWindow: 1').Replace('m_ShowUnitySplashScreen: 1', 'm_ShowUnitySplashScreen: 0').Replace('m_ShowUnitySplashLogo: 1', 'm_ShowUnitySplashLogo: 0')
            [IO.File]::WriteAllText($playerSettingsPath, $preparedText)
            [UnityEditor.AssetDatabase]::SaveAssets()
            if (-not [IO.File]::ReadAllText($playerSettingsPath).Contains('    Standalone: 1')) { throw 'Framework control did not select Checked.' }
            $directory = Join-Path $receiptDirectory $case.Directory
            Remove-Item -LiteralPath (Join-Path $directory 'build-player-settings.asset'), (Join-Path $directory 'build-preparation.json')
            & $case.Prepare
            $snapshot = Join-Path $directory 'build-player-settings.asset'
            $preparedEntry = Get-ControlSettingsEntry $snapshot
            if (-not [IO.File]::ReadAllText($snapshot).Contains("    Standalone: $($case.PreparedVariant)")) { throw "Wrong prepared variant for $($case.Name)/$initialMap." }
            [IO.File]::WriteAllText($playerSettingsPath, $configuredText.Replace('    Standalone: 3', '    Standalone: 1'))
            [UnityEditor.PlayerSettings]::SetManagedCodeVariant([UnityEditor.Build.NamedBuildTarget]::Standalone, $oldVariant)
            [UnityEditor.AssetDatabase]::SaveAssets()
            $restoredEntry = Get-ControlSettingsEntry $playerSettingsPath
            Assert-WProtoPreparedPlayerSettings -Configured $configuredEntry -Prepared $preparedEntry -Built $restoredEntry `
                -RestoredPath $playerSettingsPath -PreparedPath $snapshot -UnityVersion '6000.6.0f1' -Backend $case.Backend
            if ([IO.File]::ReadAllText($playerSettingsPath) -cne $configuredText) { throw 'Restoration did not preserve the configured bytes.' }
            $transitionChecks++
        }
    }
    Write-Host "WProto settings transitions: $transitionChecks configure/Checked/callback/restore controls passed through the shipping strict verifier (Unity APIs stubbed; no native proof)."
}
finally {
    $env:UH_PLAYER_BUILD_PATH = $savedOutput
    $env:UH_ADDITIONAL_SCRIPTING_DEFINES = $savedDefines
    $env:UH_CONFIGURE_MARKER_PATH = $savedMarker
    if (-not $keepEvidence -and (Test-Path -LiteralPath $temporary)) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}

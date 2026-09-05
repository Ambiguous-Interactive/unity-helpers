#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('sentinel', 'intmap', 'serialization', 'all')]
    [string]$Acceptance,
    [Parameter(Mandatory = $true)]
    [string]$UnityVersion,
    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,
    [Parameter(Mandatory = $true)]
    [string]$TemporaryRoot,
    [string]$Repository = (Join-Path $PSScriptRoot '../..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Initialize-SerializationAcceptanceProject {
    param([string]$Project, [string]$Repository, [switch]$ConflictingSibling)

    $commit = (& git -C $Repository rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot bind serialization acceptance to its source commit.' }
    $upstreamNamespace = 'WallstopStudios.UnityHelpers.Acceptance.Upstream'
    $consumerNamespace = 'WallstopStudios.UnityHelpers.Acceptance.Consumer'
    $upstream = [IO.File]::ReadAllText((Join-Path $Repository 'Tests/Core/WProtoExtensionContracts.cs')).Replace(
        'WallstopStudios.UnityHelpers.Tests.Core', $upstreamNamespace)
    $consumer = [IO.File]::ReadAllText((Join-Path $Repository 'Tests/Runtime/Serialization/WProtoCrossAssemblyTests.cs')).Replace(
        'WallstopStudios.UnityHelpers.Tests.Core', $upstreamNamespace).Replace(
        'WallstopStudios.UnityHelpers.Tests.Serialization', $consumerNamespace)
    $consumer = $consumer -replace '(?m)^\s*(?:using NUnit\.Framework;|\[(?:TestFixture|Test|Category\("[^"]+"\))\])\s*\r?\n', ''
    foreach ($assembly in @('Upstream', 'Consumer')) {
        $directory = Join-Path $Project "Assets/SerializationAcceptance/$assembly"
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        $references = @('WallstopStudios.UnityHelpers')
        if ($assembly -eq 'Consumer') { $references += $upstreamNamespace }
        @{ name = "WallstopStudios.UnityHelpers.Acceptance.$assembly"; references = $references; autoReferenced = $true } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory "$assembly.asmdef")
        $source = if ($assembly -eq 'Upstream') { $upstream } else { $consumer }
        if ($assembly -eq 'Upstream') {
            $source += @'
namespace WallstopStudios.UnityHelpers.Acceptance.Upstream
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    public sealed class ForwardOwnerOrder { }
    public sealed class ReverseOwnerOrder { }

    public class UpstreamReplacement<T> : IWProtoReplacementFormatter<T>
    {
        public int Measure(in T value) => 0;
        public bool Write(ref WProtoWriter writer, in T value) => true;
        public bool CanWrite(Type runtimeType) => runtimeType == typeof(T);
        public bool TryRead(ref WProtoReader reader, out T value)
        {
            value = default;
            return true;
        }
    }
}
'@
        }
        [IO.File]::WriteAllText((Join-Path $directory 'Contracts.cs'), $source)
    }
    $driver = @'
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Acceptance.Consumer
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Acceptance.Upstream;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    internal static class SerializationAcceptance
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
#if !ENABLE_IL2CPP || UNITY_EDITOR
            throw new InvalidOperationException("Serialization acceptance requires an IL2CPP player.");
#else
            Assert.IsTrue(!Debug.isDebugBuild);
            WProtoCrossAssemblyTests cases = new WProtoCrossAssemblyTests();
            Action[] scenarios =
            {
                cases.AnExtendingAssemblyRoundTripsThroughPrecompiledMembersAndCollections,
                cases.ExtendingAPreviouslyMergeableBasePreservesItsConstructorSeed,
                cases.SeededBasePromotionKeepsInheritedMembersAndConsumerFields,
                cases.AConcreteSubtypeEntryPointUsesTheReplacementRootChain,
            };
            int completed = 0;
            foreach (Action scenario in scenarios)
            {
                scenario();
                ++completed;
            }
            Action[] ownerOrders =
            {
                () => VerifyConflict<ForwardOwnerOrder>(new UpstreamReplacement<ForwardOwnerOrder>(), new ConsumerReplacement<ForwardOwnerOrder>()),
                () => VerifyConflict<ReverseOwnerOrder>(new ConsumerReplacement<ReverseOwnerOrder>(), new UpstreamReplacement<ReverseOwnerOrder>()),
            };
            int conflicts = 0;
            foreach (Action order in ownerOrders)
            {
                order();
                ++conflicts;
            }
            Debug.Log("UH_SERIALIZATION_ACCEPTANCE commit=__COMMIT__ unity=" + Application.unityVersion
                + " backend=IL2CPP development=False cases=" + completed + " conflicts=" + conflicts);
#endif
        }

        private static void VerifyConflict<T>(IWProtoReplacementFormatter<T> first, IWProtoReplacementFormatter<T> second)
        {
            WProtoFormatterProvider.RegisterReplacement(first);
            WProtoFormatterProvider.RegisterReplacement(first);
            Assert.IsTrue(WProtoFormatterProvider.TryGetReplacement(out IWProtoReplacementFormatter<T> registered));
            Assert.IsTrue(ReferenceEquals(first, registered));
            string conflict = Refusal(() => WProtoFormatterProvider.RegisterReplacement(second));
            int consumer = conflict.IndexOf(typeof(ConsumerReplacement<T>).FullName, StringComparison.Ordinal);
            int upstream = conflict.IndexOf(typeof(UpstreamReplacement<T>).FullName, StringComparison.Ordinal);
            Assert.IsTrue(0 <= consumer && consumer < upstream);
            Assert.AreEqual(conflict, Refusal(() => WProtoFormatterProvider.TryGetReplacement(out IWProtoReplacementFormatter<T> _)));
            Assert.AreEqual(conflict, Refusal(() => WProtoFormatterProvider.RegisterReplacement(first)));
        }

        private static string Refusal(Action action)
        {
            try { action(); }
            catch (InvalidOperationException exception) { return exception.Message; }
            throw new InvalidOperationException("Conflicting owners were not refused.");
        }
    }

    internal sealed class ConsumerReplacement<T> : UpstreamReplacement<T> { }

    internal static class Assert
    {
        public static void IsTrue(bool value)
        {
            if (!value) { throw new InvalidOperationException("Serialization acceptance assertion failed."); }
        }

        public static void AreEqual<T>(T expected, T actual)
        {
            IsTrue(EqualityComparer<T>.Default.Equals(expected, actual));
        }

        public static void AreNotEqual<T>(T expected, T actual)
        {
            IsTrue(!EqualityComparer<T>.Default.Equals(expected, actual));
        }

        public static void IsInstanceOf<T>(object value)
        {
            IsTrue(value is T);
        }
    }
}
'@
    [IO.File]::WriteAllText((Join-Path $Project 'Assets/SerializationAcceptance/Consumer/SerializationAcceptance.cs'),
        $driver.Replace('__COMMIT__', $commit))
    if ($ConflictingSibling) {
        $sibling = Join-Path $Project 'Assets/SerializationAcceptance/Sibling'
        New-Item -ItemType Directory -Path $sibling | Out-Null
        @{ name = 'WallstopStudios.UnityHelpers.Acceptance.Sibling'; references = @('WallstopStudios.UnityHelpers', $upstreamNamespace); autoReferenced = $true } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $sibling 'Sibling.asmdef')
        @'
namespace WallstopStudios.UnityHelpers.Acceptance.Sibling
{
    using WallstopStudios.UnityHelpers.Acceptance.Upstream;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    [WProtoSubtype(typeof(WProtoExtensionBase), 201)]
    public sealed partial class SiblingWeapon : WProtoExtensionBase { }
}
'@ | Set-Content -LiteralPath (Join-Path $sibling 'Contracts.cs')
    }
}

$failures = [System.Collections.Generic.List[string]]::new()
$selected = if ($Acceptance -eq 'all') { @('sentinel', 'intmap', 'serialization', 'owners') } elseif ($Acceptance -eq 'serialization') { @('serialization', 'owners') } else { @($Acceptance) }
foreach ($kind in $selected) {
    $ownedProject = $null
    try {
        & (Join-Path $PSScriptRoot 'assert-no-active-unity-editor.ps1')
        $parameters = @{
            UnityVersion = $UnityVersion
            RepoRoot = $Repository
            ArtifactsPath = Join-Path $ArtifactsPath $kind
            ReleaseCodeOptimization = $true
            ReleasePlayerBuild = $true
            Il2CppCompilerConfiguration = 'Release'
        }
        if ($kind -eq 'sentinel') {
            $token = [Guid]::NewGuid().ToString('N')
            $project = [IO.Path]::GetFullPath((Join-Path $TemporaryRoot "sentinel-interaction-$token"))
            New-Item -ItemType Directory -Path $project | Out-Null
            $ownedProject = $project
            [IO.File]::WriteAllText((Join-Path $project '.sentinel-interaction-disposable'), $token)
            $oldToken = $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN
            $oldProject = $env:WALLSTOP_SENTINEL_INTERACTION_PROJECT
            try {
                $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN = $token
                $env:WALLSTOP_SENTINEL_INTERACTION_PROJECT = $project
                $parameters.ProjectPath = $project
                $parameters.TestMode = 'editmode'
                $parameters.AssemblyNames = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation'
                $parameters.TestFilter = 'WallstopStudios.UnityHelpers.Tests.Editor.Validation.ValidationWorkspaceInteractionTests.NativePanelCallbacksRetainDraftAndPersistSettings'
                & (Join-Path $PSScriptRoot 'run-ci-tests.ps1') @parameters
            } finally {
                $env:WALLSTOP_SENTINEL_INTERACTION_TOKEN = $oldToken
                $env:WALLSTOP_SENTINEL_INTERACTION_PROJECT = $oldProject
            }
        } elseif ($kind -eq 'owners') {
            $token = [Guid]::NewGuid().ToString('N')
            $project = [IO.Path]::GetFullPath((Join-Path $TemporaryRoot "proto-owner-acceptance-$token"))
            New-Item -ItemType Directory -Path $project | Out-Null
            $ownedProject = $project
            [IO.File]::WriteAllText((Join-Path $project '.proto-owner-acceptance'), $token)
            Initialize-SerializationAcceptanceProject -Project $project -Repository $Repository -ConflictingSibling
            $oldToken = $env:WALLSTOP_PROTO_OWNER_CONFLICT_TOKEN
            $oldProject = $env:WALLSTOP_PROTO_OWNER_CONFLICT_PROJECT
            try {
                $env:WALLSTOP_PROTO_OWNER_CONFLICT_TOKEN = $token
                $env:WALLSTOP_PROTO_OWNER_CONFLICT_PROJECT = $project
                $parameters.ProjectPath = $project
                $parameters.TestMode = 'editmode'
                $parameters.AssemblyNames = 'WallstopStudios.UnityHelpers.Tests.Editor'
                $parameters.TestFilter = 'WallstopStudios.UnityHelpers.Tests.Editor.Tools.WProtoSubtypeTagAssignerClassificationTests.NativeSiblingOwnersRefuseAssignmentAndThePlayerBuildGate'
                & (Join-Path $PSScriptRoot 'run-ci-tests.ps1') @parameters
            } finally {
                $env:WALLSTOP_PROTO_OWNER_CONFLICT_TOKEN = $oldToken
                $env:WALLSTOP_PROTO_OWNER_CONFLICT_PROJECT = $oldProject
            }
        } else {
            $parameters.ProjectPath = Join-Path $TemporaryRoot "$kind-acceptance-$([Guid]::NewGuid().ToString('N'))"
            New-Item -ItemType Directory -Path $parameters.ProjectPath | Out-Null
            $ownedProject = $parameters.ProjectPath
            $parameters.TestMode = 'standalone'
            $parameters.StandaloneScriptingBackend = 'IL2CPP'
            if ($kind -eq 'serialization') {
                $parameters.ManagedStrippingLevel = 'High'
                $parameters.AssemblyNames = 'WallstopStudios.UnityHelpers.Tests.Runtime'
                $parameters.TestFilter = 'WallstopStudios.UnityHelpers.Tests.Serialization.WProtoCrossAssemblyTests'
                Initialize-SerializationAcceptanceProject -Project $ownedProject -Repository $Repository
            } else {
                $parameters.AssemblyNames = 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance'
                $parameters.TestFilter = 'WallstopStudios.UnityHelpers.Tests.Runtime.Performance.IntMapPerformanceTests.IntMapLookupsComparedAgainstDictionary'
            }
            & (Join-Path $PSScriptRoot 'run-ci-tests.ps1') @parameters
        }
    } catch {
        $failures.Add("${kind}: $($_.Exception.Message)")
        Write-Warning "Acceptance run failed: $kind. Preserving artifacts and continuing other selected work."
    } finally {
        if ($null -ne $ownedProject -and (Test-Path -LiteralPath $ownedProject)) {
            try {
                & (Join-Path $PSScriptRoot 'assert-no-active-unity-editor.ps1')
                Remove-Item -LiteralPath $ownedProject -Recurse -Force
            } catch {
                $failures.Add("${kind} cleanup: $($_.Exception.Message)")
                Write-Warning "Could not remove the owned acceptance project: $ownedProject"
            }
        }
    }
}
if ($failures.Count -ne 0) {
    throw ($failures -join [Environment]::NewLine)
}

// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;

    [TestFixture]
    public sealed class SpriteLabelProcessorTests
    {
        [SetUp]
        public void SetUp()
        {
            // First in SetUp, so leaked handler statics are attributed to the prior fixture.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            SpriteLabelProcessor.ResetForTesting();
        }

        [TearDown]
        public void TearDown()
        {
            AssetPostprocessorDeferral.FlushForTesting();
            SpriteLabelProcessor.ResetForTesting();
        }

        [Test]
        [TestCaseSource(nameof(EnqueuePathFilteringCases))]
        public void EnqueueImportedPathsForTestingFiltersAndDeduplicatesPaths(
            string[] importedPaths,
            string[] expectedNormalizedPaths
        )
        {
            SpriteLabelProcessor.EnqueueImportedPathsForTesting(importedPaths);

            string[] normalizedPaths = SnapshotNormalizedPendingPaths();
            string details = DescribePendingState();

            Assert.AreEqual(
                expectedNormalizedPaths.Length,
                SpriteLabelProcessor.PendingImportedPathCountForTesting,
                "Only unique sprite candidate assets under Assets/ should remain queued. " + details
            );
            CollectionAssert.AreEquivalent(
                expectedNormalizedPaths,
                normalizedPaths,
                "The pending set should keep the expected unique candidate paths only. " + details
            );
        }

        [Test]
        public void EnqueueImportedPathsForTestingNullOrEmptyInputDoesNotMutateState()
        {
            Assert.AreEqual(
                0,
                SpriteLabelProcessor.PendingImportedPathCountForTesting,
                "Expected clean state at test start. " + DescribePendingState()
            );

            SpriteLabelProcessor.EnqueueImportedPathsForTesting(
                new[] { "Assets/Sprites/Hero.png" }
            );
            Assert.AreEqual(
                1,
                SpriteLabelProcessor.PendingImportedPathCountForTesting,
                "Expected a seeded pending path before null/empty no-op checks. "
                    + DescribePendingState()
            );

            SpriteLabelProcessor.EnqueueImportedPathsForTesting(null);
            SpriteLabelProcessor.EnqueueImportedPathsForTesting(Array.Empty<string>());

            Assert.AreEqual(
                1,
                SpriteLabelProcessor.PendingImportedPathCountForTesting,
                "Null/empty inputs should leave pending state unchanged. " + DescribePendingState()
            );
            CollectionAssert.AreEquivalent(
                new[] { "assets/sprites/hero.png" },
                SnapshotNormalizedPendingPaths(),
                "Null/empty enqueue calls should not alter pending paths. " + DescribePendingState()
            );
        }

        [Test]
        public void EnqueueImportedPathsForTestingDeduplicatesAcrossMultipleCalls()
        {
            SpriteLabelProcessor.EnqueueImportedPathsForTesting(
                new[] { "Assets/Sprites/Hero.png", "Assets/Sprites/Villain.jpg" }
            );
            SpriteLabelProcessor.EnqueueImportedPathsForTesting(
                new[] { "assets/sprites/HERO.PNG", "Assets/Sprites/Sidekick.jpeg" }
            );

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "assets/sprites/hero.png",
                    "assets/sprites/villain.jpg",
                    "assets/sprites/sidekick.jpeg",
                },
                SnapshotNormalizedPendingPaths(),
                "Paths should deduplicate case-insensitively across enqueue batches. "
                    + DescribePendingState()
            );
            Assert.AreEqual(
                3,
                SpriteLabelProcessor.PendingImportedPathCountForTesting,
                "Expected exactly three unique candidate paths after two enqueue batches. "
                    + DescribePendingState()
            );
        }

        private static IEnumerable<TestCaseData> EnqueuePathFilteringCases()
        {
            yield return new TestCaseData(
                new[]
                {
                    "Assets/Sprites/Hero.png",
                    "Assets/Sprites/Hero.png",
                    "Assets/Sprites/hero.PNG",
                    "Assets/Sprites/Villain.jpg",
                    "Assets/Sprites/Notes.txt",
                    "Packages/com.wallstop-studios.unity-helpers/Icons/Icon.png",
                },
                new[] { "assets/sprites/hero.png", "assets/sprites/villain.jpg" }
            ).SetName("FiltersByAssetsPrefixAndSpriteExtensions");

            yield return new TestCaseData(
                new[]
                {
                    "assets/characters/NPC.JPEG",
                    "ASSETS/characters/npc.jpeg",
                    "Assets/Characters/Boss.JPG",
                    "Assets/Characters/Boss.jpg",
                    "Assets/Characters/Notes.md",
                },
                new[] { "assets/characters/npc.jpeg", "assets/characters/boss.jpg" }
            ).SetName("DeduplicatesCaseInsensitivelyAcrossJpgAndJpegCandidates");

            yield return new TestCaseData(
                new[]
                {
                    null,
                    string.Empty,
                    "Packages/com.wallstop-studios.unity-helpers/Icons/Icon.jpg",
                    "ProjectSettings/Icon.png",
                    "Assets/Readme.txt",
                },
                Array.Empty<string>()
            ).SetName("RejectsNullEmptyAndNonAssetsCandidateInputs");
        }

        private static string[] SnapshotNormalizedPendingPaths()
        {
            return SpriteLabelProcessor
                .SnapshotPendingImportedPathsForTesting()
                .Select(path => path.ToLowerInvariant())
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static string DescribePendingState()
        {
            string[] normalized = SnapshotNormalizedPendingPaths();
            return $"PendingImportedPaths.Count={SpriteLabelProcessor.PendingImportedPathCountForTesting}; "
                + $"Normalized=[{string.Join(", ", normalized)}]";
        }
    }
}

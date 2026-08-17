// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Provides multiple algorithms for automatic sprite grid detection.
    /// Each algorithm analyzes texture pixel data to determine optimal cell dimensions.
    /// </summary>
    public static class SpriteSheetAlgorithms
    {
        /// <summary>
        /// Minimum confidence threshold for AutoBest algorithm to accept a result early.
        /// Set high (90%) to ensure multiple algorithms are tried before accepting a result,
        /// since BoundaryScoring can produce high-confidence wrong results on mostly-transparent sheets.
        /// </summary>
        public const float AutoBestEarlyStopConfidence = 0.90f;

        /// <summary>
        /// Minimum cell size in pixels for valid grid detection.
        /// </summary>
        public const int MinimumCellSize = 4;

        /// <summary>
        /// Transparency ratio threshold for remainder pixel handling.
        /// If remainder column/row has transparency below this value (mostly opaque), adjust cell size to include it.
        /// </summary>
        private const float RemainderTransparencyThreshold = 0.9f;

        /// <summary>
        /// Maximum score for a fully transparent grid line in the non-linear scoring system.
        /// </summary>
        private const float MaxTransparencyLineScore = 10f;

        /// <summary>
        /// Hard constraint threshold for sprite-fit validation in FindBestTransparencyAlignedDivisor.
        /// Divisors with sprite-fit scores below this are rejected outright.
        /// Set very low (0.3) because the core zone concept already handles edge clipping -
        /// we only want to reject divisors that split sprites through their centers.
        /// </summary>
        private const float SpriteFitHardThresholdStrict = 0.3f;

        /// <summary>
        /// Hard constraint threshold for sprite-fit validation in BoundaryScoring.
        /// Set very low (0.3) to allow candidates that may touch sprite edges.
        /// </summary>
        private const float SpriteFitHardThresholdRelaxed = 0.3f;

        /// <summary>
        /// Maximum ratio by which a candidate cell count can differ from the expected count.
        /// Candidates with cell counts outside the range [expected/ratio, expected*ratio] are rejected.
        /// </summary>
        private const float MaxCellCountRatio = 1.5f;

        /// <summary>
        /// Threshold for sprite-fit fallback logic in ClusterCentroid and RegionGrowing.
        /// When sprite-fit score is below this, alternative cell sizes are tried.
        /// </summary>
        private const float SpriteFitFallbackThreshold = 0.5f;

        /// <summary>
        /// Multiplier used in CalculateGapBasedTolerance to determine grouping tolerance.
        /// Positions within this fraction of the minimum significant gap are considered the same group.
        /// Uses a smaller multiplier (0.25) with minimum gap (not median) for tighter grouping.
        /// </summary>
        private const float GapToleranceMultiplier = 0.25f;

        /// <summary>
        /// Fraction of sprite width/height that defines the "core zone" for split detection.
        /// Only grid lines passing through the middle (this fraction) of a sprite are penalized.
        /// Set to 0.8 (80%) - only ignore outer 10% on each side for anti-aliased edge handling.
        /// </summary>
        private const float SpriteCoreZoneFraction = 0.8f;

        /// <summary>
        /// Minimum gap size (in pixels) between consecutive positions to be considered a significant gap.
        /// Gaps smaller than this are treated as noise (positions in the same column/row with slight variations).
        /// </summary>
        private const float MinSignificantGapSize = 5f;

        /// <summary>
        /// Minimum gap tolerance multiplier for DistanceTransform algorithm.
        /// Uses MinimumCellSize * 2 instead of MinimumCellSize because distance transform
        /// peaks can be more spread out due to chamfer distance approximation errors.
        /// </summary>
        private const int DistanceTransformMinGapToleranceMultiplier = 2;

        /// <summary>
        /// Common sprite cell sizes for grid detection candidate generation.
        /// </summary>
        private static readonly int[] CommonCellSizes =
        {
            8,
            16,
            24,
            32,
            48,
            64,
            96,
            128,
            256,
            512,
        };

        /// <summary>
        /// Result of a grid detection algorithm.
        /// </summary>
        public readonly struct AlgorithmResult
        {
            /// <summary>
            /// The detected cell width in pixels.
            /// </summary>
            public readonly int CellWidth;

            /// <summary>
            /// The detected cell height in pixels.
            /// </summary>
            public readonly int CellHeight;

            /// <summary>
            /// Confidence score in the range [0, 1] where 1 is highest confidence.
            /// </summary>
            public readonly float Confidence;

            /// <summary>
            /// The algorithm that produced this result.
            /// </summary>
            public readonly AutoDetectionAlgorithm Algorithm;

            /// <summary>
            /// Whether this result represents a valid grid detection.
            /// </summary>
            public bool IsValid => CellWidth >= MinimumCellSize && CellHeight >= MinimumCellSize;

            public AlgorithmResult(
                int cellWidth,
                int cellHeight,
                float confidence,
                AutoDetectionAlgorithm algorithm
            )
            {
                CellWidth = cellWidth;
                CellHeight = cellHeight;
                Confidence = Mathf.Clamp01(confidence);
                Algorithm = algorithm;
            }

            public static AlgorithmResult Invalid(AutoDetectionAlgorithm algorithm)
            {
                return new AlgorithmResult(0, 0, 0f, algorithm);
            }
        }

        /// <summary>
        /// Detects optimal grid dimensions using the specified algorithm.
        /// </summary>
        /// <param name="pixels">The texture pixel data in Color32 format.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="alphaThreshold">Alpha value (0-1) below which a pixel is considered transparent.</param>
        /// <param name="algorithm">The detection algorithm to use.</param>
        /// <param name="expectedSpriteCount">Optional expected sprite count (required for UniformGrid).</param>
        /// <param name="snapToTextureDivisor">When true, adjusts cell sizes to be exact divisors of texture dimensions using transparency-aware analysis.</param>
        /// <param name="cancellationToken">Optional cancellation token for long-running operations.</param>
        /// <returns>The algorithm result containing detected cell dimensions and confidence.</returns>
        public static AlgorithmResult DetectGrid(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            float alphaThreshold,
            AutoDetectionAlgorithm algorithm,
            int expectedSpriteCount = -1,
            bool snapToTextureDivisor = true,
            CancellationToken cancellationToken = default
        )
        {
            if (pixels == null || pixels.Length == 0)
            {
                return AlgorithmResult.Invalid(algorithm);
            }

            if (textureWidth < MinimumCellSize || textureHeight < MinimumCellSize)
            {
                return AlgorithmResult.Invalid(algorithm);
            }

            if (pixels.Length != textureWidth * textureHeight)
            {
                return AlgorithmResult.Invalid(algorithm);
            }

            if (alphaThreshold < 0f || alphaThreshold >= 1f)
            {
                return AlgorithmResult.Invalid(algorithm);
            }

            switch (algorithm)
            {
                case AutoDetectionAlgorithm.AutoBest:
                    return DetectGridAutoBest(
                        pixels,
                        textureWidth,
                        textureHeight,
                        alphaThreshold,
                        expectedSpriteCount,
                        snapToTextureDivisor,
                        cancellationToken
                    );
                case AutoDetectionAlgorithm.UniformGrid:
                    return DetectGridUniformGrid(textureWidth, textureHeight, expectedSpriteCount);
                case AutoDetectionAlgorithm.BoundaryScoring:
                    return DetectGridBoundaryScoring(
                        pixels,
                        textureWidth,
                        textureHeight,
                        alphaThreshold,
                        expectedSpriteCount,
                        snapToTextureDivisor
                    );
                case AutoDetectionAlgorithm.ClusterCentroid:
                    return DetectGridClusterCentroid(
                        pixels,
                        textureWidth,
                        textureHeight,
                        alphaThreshold,
                        expectedSpriteCount,
                        snapToTextureDivisor,
                        cancellationToken
                    );
                case AutoDetectionAlgorithm.DistanceTransform:
                    return DetectGridDistanceTransform(
                        pixels,
                        textureWidth,
                        textureHeight,
                        alphaThreshold,
                        expectedSpriteCount,
                        snapToTextureDivisor,
                        cancellationToken
                    );
                case AutoDetectionAlgorithm.RegionGrowing:
                    return DetectGridRegionGrowing(
                        pixels,
                        textureWidth,
                        textureHeight,
                        alphaThreshold,
                        expectedSpriteCount,
                        snapToTextureDivisor,
                        cancellationToken
                    );
                default:
                    return AlgorithmResult.Invalid(algorithm);
            }
        }

        /// <summary>
        /// AutoBest algorithm: runs algorithms in order of speed until one exceeds 70% confidence.
        /// Order: BoundaryScoring (fastest) -> ClusterCentroid -> DistanceTransform -> RegionGrowing
        /// </summary>
        private static AlgorithmResult DetectGridAutoBest(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            float alphaThreshold,
            int expectedSpriteCount,
            bool snapToTextureDivisor,
            CancellationToken cancellationToken
        )
        {
            AlgorithmResult bestResult = AlgorithmResult.Invalid(AutoDetectionAlgorithm.AutoBest);

            // Try BoundaryScoring first (fastest)
            if (cancellationToken.IsCancellationRequested)
            {
                return bestResult;
            }

            AlgorithmResult boundaryResult = DetectGridBoundaryScoring(
                pixels,
                textureWidth,
                textureHeight,
                alphaThreshold,
                expectedSpriteCount,
                snapToTextureDivisor
            );

            if (boundaryResult.IsValid && boundaryResult.Confidence >= AutoBestEarlyStopConfidence)
            {
                return new AlgorithmResult(
                    boundaryResult.CellWidth,
                    boundaryResult.CellHeight,
                    boundaryResult.Confidence,
                    AutoDetectionAlgorithm.AutoBest
                );
            }

            if (boundaryResult.Confidence > bestResult.Confidence)
            {
                bestResult = boundaryResult;
            }

            // Try ClusterCentroid
            if (cancellationToken.IsCancellationRequested)
            {
                return ConvertToAutoBest(bestResult);
            }

            AlgorithmResult clusterResult = DetectGridClusterCentroid(
                pixels,
                textureWidth,
                textureHeight,
                alphaThreshold,
                expectedSpriteCount,
                snapToTextureDivisor,
                cancellationToken
            );

            if (clusterResult.IsValid && clusterResult.Confidence >= AutoBestEarlyStopConfidence)
            {
                return new AlgorithmResult(
                    clusterResult.CellWidth,
                    clusterResult.CellHeight,
                    clusterResult.Confidence,
                    AutoDetectionAlgorithm.AutoBest
                );
            }

            if (clusterResult.Confidence > bestResult.Confidence)
            {
                bestResult = clusterResult;
            }

            // Try DistanceTransform
            if (cancellationToken.IsCancellationRequested)
            {
                return ConvertToAutoBest(bestResult);
            }

            AlgorithmResult distanceResult = DetectGridDistanceTransform(
                pixels,
                textureWidth,
                textureHeight,
                alphaThreshold,
                expectedSpriteCount,
                snapToTextureDivisor,
                cancellationToken
            );

            if (distanceResult.IsValid && distanceResult.Confidence >= AutoBestEarlyStopConfidence)
            {
                return new AlgorithmResult(
                    distanceResult.CellWidth,
                    distanceResult.CellHeight,
                    distanceResult.Confidence,
                    AutoDetectionAlgorithm.AutoBest
                );
            }

            if (distanceResult.Confidence > bestResult.Confidence)
            {
                bestResult = distanceResult;
            }

            // Try RegionGrowing
            if (cancellationToken.IsCancellationRequested)
            {
                return ConvertToAutoBest(bestResult);
            }

            AlgorithmResult regionResult = DetectGridRegionGrowing(
                pixels,
                textureWidth,
                textureHeight,
                alphaThreshold,
                expectedSpriteCount,
                snapToTextureDivisor,
                cancellationToken
            );

            if (regionResult.Confidence > bestResult.Confidence)
            {
                bestResult = regionResult;
            }

            // Try UniformGrid as last resort if expected count is provided
            if (expectedSpriteCount > 0)
            {
                AlgorithmResult uniformResult = DetectGridUniformGrid(
                    textureWidth,
                    textureHeight,
                    expectedSpriteCount
                );

                if (uniformResult.Confidence > bestResult.Confidence)
                {
                    bestResult = uniformResult;
                }
            }

            return ConvertToAutoBest(bestResult);
        }

        private static AlgorithmResult ConvertToAutoBest(AlgorithmResult result)
        {
            if (!result.IsValid)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.AutoBest);
            }

            return new AlgorithmResult(
                result.CellWidth,
                result.CellHeight,
                result.Confidence,
                AutoDetectionAlgorithm.AutoBest
            );
        }

        /// <summary>
        /// UniformGrid algorithm: simple division based on expected sprite count.
        /// Tries to find optimal rows/columns that evenly divide the texture.
        /// </summary>
        private static AlgorithmResult DetectGridUniformGrid(
            int textureWidth,
            int textureHeight,
            int expectedSpriteCount
        )
        {
            if (expectedSpriteCount <= 0)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.UniformGrid);
            }

            // Find factor pairs of expectedSpriteCount
            int bestColumns = 1;
            int bestRows = expectedSpriteCount;
            float bestAspectRatio = float.MaxValue;
            float textureAspect = (float)textureWidth / textureHeight;

            for (int cols = 1; cols <= expectedSpriteCount; ++cols)
            {
                if (expectedSpriteCount % cols != 0)
                {
                    continue;
                }

                int rows = expectedSpriteCount / cols;
                int cellWidth = textureWidth / cols;
                int cellHeight = textureHeight / rows;

                // Skip if cells don't divide evenly
                if (textureWidth % cols != 0 || textureHeight % rows != 0)
                {
                    continue;
                }

                // Skip if cells are too small
                if (cellWidth < MinimumCellSize || cellHeight < MinimumCellSize)
                {
                    continue;
                }

                float cellAspect = (float)cellWidth / cellHeight;
                float aspectDiff = Mathf.Abs(cellAspect - 1f);

                // Prefer square cells, but also consider texture aspect ratio
                float gridAspect = (float)cols / rows;
                float textureAspectDiff = Mathf.Abs(gridAspect - textureAspect);
                float combinedScore = aspectDiff + textureAspectDiff * 0.5f;

                if (combinedScore < bestAspectRatio)
                {
                    bestAspectRatio = combinedScore;
                    bestColumns = cols;
                    bestRows = rows;
                }
            }

            int finalCellWidth = textureWidth / bestColumns;
            int finalCellHeight = textureHeight / bestRows;

            // Confidence is high only if cells divide evenly
            bool perfectDivision = textureWidth % bestColumns == 0 && textureHeight % bestRows == 0;
            float confidence = perfectDivision ? 1.0f : 0.5f;

            // Reduce confidence for extreme aspect ratios
            float cellAspectRatio = (float)finalCellWidth / finalCellHeight;
            if (cellAspectRatio < 0.25f || cellAspectRatio > 4.0f)
            {
                confidence *= 0.5f;
            }

            return new AlgorithmResult(
                finalCellWidth,
                finalCellHeight,
                confidence,
                AutoDetectionAlgorithm.UniformGrid
            );
        }

        /// <summary>
        /// BoundaryScoring algorithm: scores grid lines by transparent pixel percentage.
        /// Also performs sprite-fit validation to penalize grids that would split sprites.
        /// </summary>
        private static AlgorithmResult DetectGridBoundaryScoring(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            float alphaThreshold,
            int expectedSpriteCount,
            bool snapToTextureDivisor
        )
        {
            byte alphaThresholdByte = ColorQuantization.ToThresholdByte(alphaThreshold);

            using PooledArray<int> columnTransparencyLease = SystemArrayPool<int>.Get(
                textureWidth,
                out int[] columnTransparencyCount
            );
            using PooledArray<int> rowTransparencyLease = SystemArrayPool<int>.Get(
                textureHeight,
                out int[] rowTransparencyCount
            );

            Array.Clear(columnTransparencyCount, 0, textureWidth);
            Array.Clear(rowTransparencyCount, 0, textureHeight);

            for (int y = 0; y < textureHeight; ++y)
            {
                int rowOffset = y * textureWidth;
                for (int x = 0; x < textureWidth; ++x)
                {
                    if (pixels[rowOffset + x].a <= alphaThresholdByte)
                    {
                        ++columnTransparencyCount[x];
                        ++rowTransparencyCount[y];
                    }
                }
            }

            // Detect sprite bounds for sprite-fit validation
            using PooledResource<List<Rect>> spriteBoundsLease = Buffers<Rect>.List.Get(
                out List<Rect> spriteBounds
            );
            DetectSpriteBoundsByAlpha(
                pixels,
                textureWidth,
                textureHeight,
                alphaThresholdByte,
                spriteBounds,
                default
            );

            // PRIMARY METHOD: If user provided expectedSpriteCount, use it directly without fallback
            if (expectedSpriteCount > 0)
            {
                int cellWidth;
                int cellHeight;
                if (
                    InferGridFromSpriteCount(
                        expectedSpriteCount,
                        textureWidth,
                        textureHeight,
                        out cellWidth,
                        out cellHeight
                    )
                )
                {
                    Debug.Log(
                        $"[BoundaryScoring] Using InferGridFromSpriteCount: expectedCount={expectedSpriteCount}, cellSize={cellWidth}x{cellHeight}"
                    );
                    // User explicitly set sprite count - validate confidence based on cell aspect ratio
                    float confidence = CalculateUserSpecifiedCountConfidence(
                        cellWidth,
                        cellHeight,
                        textureWidth,
                        textureHeight
                    );
                    return new AlgorithmResult(
                        cellWidth,
                        cellHeight,
                        confidence,
                        AutoDetectionAlgorithm.BoundaryScoring
                    );
                }
                // Fallback to approximate grid when exact division fails
                if (
                    TryInferApproximateGrid(
                        expectedSpriteCount,
                        textureWidth,
                        textureHeight,
                        out cellWidth,
                        out cellHeight
                    )
                )
                {
                    Debug.Log(
                        $"[BoundaryScoring] Using TryInferApproximateGrid: expectedCount={expectedSpriteCount}, cellSize={cellWidth}x{cellHeight}"
                    );
                    return new AlgorithmResult(
                        cellWidth,
                        cellHeight,
                        0.95f, // Slightly lower confidence since not exact
                        AutoDetectionAlgorithm.BoundaryScoring
                    );
                }
                Debug.LogWarning(
                    $"[BoundaryScoring] Both InferGridFromSpriteCount and TryInferApproximateGrid failed for expectedCount={expectedSpriteCount}, texture={textureWidth}x{textureHeight}"
                );
            }

            // Auto-detection path: use detected sprite count
            int spriteCount = spriteBounds.Count;
            Debug.Log(
                $"[BoundaryScoring] Auto-detection path: detectedSpriteCount={spriteCount}, expectedSpriteCount={expectedSpriteCount}"
            );

            if (spriteCount >= 2)
            {
                int cellWidth;
                int cellHeight;
                if (
                    InferGridFromSpriteCount(
                        spriteCount,
                        textureWidth,
                        textureHeight,
                        out cellWidth,
                        out cellHeight
                    )
                )
                {
                    // Validate that sprites fit within the inferred cells
                    float spriteFitScore = CalculateSpriteFitScore(
                        spriteBounds,
                        cellWidth,
                        cellHeight,
                        textureWidth,
                        textureHeight
                    );

                    // If sprites fit reasonably well, use this result directly
                    if (spriteFitScore >= 0.5f)
                    {
                        return new AlgorithmResult(
                            cellWidth,
                            cellHeight,
                            spriteFitScore,
                            AutoDetectionAlgorithm.BoundaryScoring
                        );
                    }
                }
            }

            // Fallback: use the original candidate-based approach
            using PooledResource<List<int>> widthCandidatesLease = Buffers<int>.List.Get(
                out List<int> widthCandidates
            );
            using PooledResource<List<int>> heightCandidatesLease = Buffers<int>.List.Get(
                out List<int> heightCandidates
            );

            GenerateCandidateCellSizes(textureWidth, widthCandidates);
            GenerateCandidateCellSizes(textureHeight, heightCandidates);

            int bestWidth = 0;
            int bestHeight = 0;
            float bestScore = -1f;
            float bestTransparencyScore = 0f; // Track transparency-only score for confidence

            for (int wi = 0; wi < widthCandidates.Count; ++wi)
            {
                int candidateWidth = widthCandidates[wi];
                float widthScore = ScoreCellSizeForDimension(
                    columnTransparencyCount,
                    textureWidth,
                    textureHeight,
                    candidateWidth
                );

                if (widthScore < 0.15f)
                {
                    continue;
                }

                for (int hi = 0; hi < heightCandidates.Count; ++hi)
                {
                    int candidateHeight = heightCandidates[hi];
                    float heightScore = ScoreCellSizeForDimension(
                        rowTransparencyCount,
                        textureHeight,
                        textureWidth,
                        candidateHeight
                    );

                    if (heightScore < 0.15f)
                    {
                        continue;
                    }

                    // Base transparency score (used for confidence)
                    float transparencyScore = (widthScore + heightScore) * 0.5f;

                    // Selection score includes bonuses (used for picking between candidates)
                    float selectionScore = transparencyScore;

                    int columns = textureWidth / candidateWidth;
                    int rows = textureHeight / candidateHeight;
                    int cellCount = columns * rows;

                    // HARD CONSTRAINT: Skip candidates that split sprites
                    if (spriteBounds.Count > 0)
                    {
                        float spriteFitScore = CalculateSpriteFitScore(
                            spriteBounds,
                            candidateWidth,
                            candidateHeight,
                            textureWidth,
                            textureHeight
                        );

                        // HARD CONSTRAINT: Skip candidates that split sprites
                        if (spriteFitScore < SpriteFitHardThresholdRelaxed)
                        {
                            continue;
                        }

                        // Bonus for excellent fit
                        if (spriteFitScore >= 0.95f)
                        {
                            selectionScore += 0.1f;
                        }
                    }

                    // Use DETECTED SPRITE COUNT as primary guide for cell count validation
                    // This is the most reliable signal - the grid should have about as many cells as sprites
                    int detectedSpriteCount = spriteBounds.Count;
                    float cellCountRatio =
                        detectedSpriteCount > 0 ? (float)cellCount / detectedSpriteCount : 1f;

                    // HARD CONSTRAINT: Cell count should not exceed 2x detected sprites
                    // (allows some margin for empty cells but prevents over-splitting)
                    if (detectedSpriteCount > 0 && cellCount > detectedSpriteCount * 2)
                    {
                        continue; // Skip this candidate entirely
                    }

                    // HARD CONSTRAINT: Cell count should not be less than half of detected sprites
                    // (prevents under-splitting / grouping multiple sprites per cell)
                    if (detectedSpriteCount > 0 && cellCount < detectedSpriteCount / 2)
                    {
                        continue; // Skip this candidate entirely
                    }

                    // Strong bonus for cell count CLOSE to detected sprite count
                    // This is the primary selection criterion
                    if (detectedSpriteCount > 0)
                    {
                        if (cellCountRatio >= 0.5f && cellCountRatio <= 2f)
                        {
                            // Cell count is within 0.5x to 2x of sprite count - strong bonus
                            float closenessBonus = 1f - Math.Abs(1f - cellCountRatio);
                            selectionScore += closenessBonus * 0.4f; // Up to +0.4 bonus
                        }
                        else
                        {
                            // Cell count is far from sprite count - penalty
                            selectionScore *= 0.5f;
                        }
                    }

                    // Bonus for producing multiple cells
                    if (columns >= 2 && rows >= 2)
                    {
                        selectionScore += 0.1f;
                    }
                    else if (columns >= 2 || rows >= 2)
                    {
                        selectionScore += 0.05f;
                    }

                    // Bonus for power of two sizes
                    if (IsPowerOfTwo(candidateWidth) && IsPowerOfTwo(candidateHeight))
                    {
                        selectionScore += 0.03f;
                    }

                    // Bonus for square cells
                    if (candidateWidth == candidateHeight)
                    {
                        selectionScore += 0.01f;
                    }

                    // Much stronger penalty for very high cell counts
                    if (cellCount > 100)
                    {
                        selectionScore *= 0.3f; // Was 0.5
                    }
                    else if (cellCount > 64)
                    {
                        selectionScore *= 0.5f; // Was 0.7
                    }
                    else if (cellCount > 32)
                    {
                        selectionScore *= 0.7f;
                    }

                    // Size bonus - prefer larger cells
                    float sizeRatio =
                        (float)(candidateWidth + candidateHeight) / (textureWidth + textureHeight);
                    float sizeBonus = sizeRatio * 0.3f; // Was 0.1f
                    selectionScore += sizeBonus;

                    if (selectionScore > bestScore)
                    {
                        bestScore = selectionScore;
                        bestTransparencyScore = transparencyScore;
                        bestWidth = candidateWidth;
                        bestHeight = candidateHeight;
                    }
                }
            }

            if (bestScore >= 0.15f && bestWidth > 0 && bestHeight > 0)
            {
                int finalWidth = bestWidth;
                int finalHeight = bestHeight;

                if (snapToTextureDivisor)
                {
                    Vector2Int adjusted = FindBestTransparencyAlignedDivisor(
                        pixels,
                        textureWidth,
                        textureHeight,
                        bestWidth,
                        bestHeight,
                        alphaThreshold,
                        spriteBounds
                    );
                    finalWidth = adjusted.x;
                    finalHeight = adjusted.y;

                    // Validate the adjusted result also fits sprites well
                    if (spriteBounds.Count > 0)
                    {
                        float adjustedFitScore = CalculateSpriteFitScore(
                            spriteBounds,
                            finalWidth,
                            finalHeight,
                            textureWidth,
                            textureHeight
                        );
                        float originalFitScore = CalculateSpriteFitScore(
                            spriteBounds,
                            bestWidth,
                            bestHeight,
                            textureWidth,
                            textureHeight
                        );

                        // If adjustment made sprite-fit worse AT ALL, keep original
                        if (adjustedFitScore < originalFitScore)
                        {
                            finalWidth = bestWidth;
                            finalHeight = bestHeight;
                        }
                    }
                }

                // Use transparency-only score for confidence (not inflated by bonuses)
                float confidence = Mathf.Clamp01(bestTransparencyScore);
                return new AlgorithmResult(
                    finalWidth,
                    finalHeight,
                    confidence,
                    AutoDetectionAlgorithm.BoundaryScoring
                );
            }

            return AlgorithmResult.Invalid(AutoDetectionAlgorithm.BoundaryScoring);
        }

        /// <summary>
        /// ClusterCentroid algorithm: detects sprites via flood-fill, computes centroids,
        /// and infers grid from unique centroid positions (grouping by tolerance).
        /// </summary>
        private static AlgorithmResult DetectGridClusterCentroid(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            float alphaThreshold,
            int expectedSpriteCount,
            bool snapToTextureDivisor,
            CancellationToken cancellationToken
        )
        {
            byte alphaThresholdByte = ColorQuantization.ToThresholdByte(alphaThreshold);

            using PooledResource<List<Rect>> spriteBoundsLease = Buffers<Rect>.List.Get(
                out List<Rect> spriteBounds
            );

            DetectSpriteBoundsByAlpha(
                pixels,
                textureWidth,
                textureHeight,
                alphaThresholdByte,
                spriteBounds,
                cancellationToken
            );

            if (spriteBounds.Count < 2)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.ClusterCentroid);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.ClusterCentroid);
            }

            // Compute centroids
            using PooledResource<List<Vector2>> centroidsLease = Buffers<Vector2>.List.Get(
                out List<Vector2> centroids
            );

            for (int i = 0; i < spriteBounds.Count; ++i)
            {
                Rect bounds = spriteBounds[i];
                centroids.Add(bounds.center);
            }

            // Collect X and Y positions
            using PooledResource<List<float>> xPositionsLease = Buffers<float>.List.Get(
                out List<float> xPositions
            );
            using PooledResource<List<float>> yPositionsLease = Buffers<float>.List.Get(
                out List<float> yPositions
            );

            for (int i = 0; i < centroids.Count; ++i)
            {
                xPositions.Add(centroids[i].x);
                yPositions.Add(centroids[i].y);
            }

            xPositions.Sort();
            yPositions.Sort();

            // Calculate average sprite size for tolerance computation
            float avgWidth = 0f;
            float avgHeight = 0f;
            float maxWidth = 0f;
            float maxHeight = 0f;
            for (int i = 0; i < spriteBounds.Count; ++i)
            {
                avgWidth += spriteBounds[i].width;
                avgHeight += spriteBounds[i].height;
                if (spriteBounds[i].width > maxWidth)
                {
                    maxWidth = spriteBounds[i].width;
                }
                if (spriteBounds[i].height > maxHeight)
                {
                    maxHeight = spriteBounds[i].height;
                }
            }
            avgWidth /= spriteBounds.Count;
            avgHeight /= spriteBounds.Count;

            // PRIMARY METHOD: If user provided expectedSpriteCount, use it directly without fallback
            if (expectedSpriteCount > 0)
            {
                int cellWidth;
                int cellHeight;
                if (
                    InferGridFromSpriteCount(
                        expectedSpriteCount,
                        textureWidth,
                        textureHeight,
                        out cellWidth,
                        out cellHeight
                    )
                )
                {
                    // User explicitly set sprite count - validate confidence based on cell aspect ratio
                    float userCountConfidence = CalculateUserSpecifiedCountConfidence(
                        cellWidth,
                        cellHeight,
                        textureWidth,
                        textureHeight
                    );
                    return new AlgorithmResult(
                        cellWidth,
                        cellHeight,
                        userCountConfidence,
                        AutoDetectionAlgorithm.ClusterCentroid
                    );
                }
                // Fallback to approximate grid when exact division fails
                if (
                    TryInferApproximateGrid(
                        expectedSpriteCount,
                        textureWidth,
                        textureHeight,
                        out cellWidth,
                        out cellHeight
                    )
                )
                {
                    return new AlgorithmResult(
                        cellWidth,
                        cellHeight,
                        0.95f, // Slightly lower confidence since not exact
                        AutoDetectionAlgorithm.ClusterCentroid
                    );
                }
            }

            // Auto-detection path: use detected sprite count
            int spriteCount = spriteBounds.Count;
            int inferredCellWidth;
            int inferredCellHeight;

            if (
                !InferGridFromSpriteCount(
                    spriteCount,
                    textureWidth,
                    textureHeight,
                    out inferredCellWidth,
                    out inferredCellHeight
                )
            )
            {
                // Fallback: use tolerance-based grouping if sprite count doesn't produce valid grid
                xPositions.Sort();
                yPositions.Sort();

                float xTolerance = CalculateGapBasedTolerance(xPositions, MinimumCellSize);
                float yTolerance = CalculateGapBasedTolerance(yPositions, MinimumCellSize);

                int numColumns = Math.Max(1, CountUniquePositionGroups(xPositions, xTolerance));
                int numRows = Math.Max(1, CountUniquePositionGroups(yPositions, yTolerance));

                inferredCellWidth = FindNearestDivisor(textureWidth, textureWidth / numColumns);
                inferredCellHeight = FindNearestDivisor(textureHeight, textureHeight / numRows);
            }

            int cellWidth2 = inferredCellWidth;
            int cellHeight2 = inferredCellHeight;

            // Validate that detected sprites fit within cells
            float spriteFitScore = CalculateSpriteFitScore(
                spriteBounds,
                cellWidth2,
                cellHeight2,
                textureWidth,
                textureHeight
            );

            // If sprites don't fit well, try alternative cell sizes based on max sprite dimensions
            if (spriteFitScore < SpriteFitFallbackThreshold)
            {
                int altCellWidth = FindNearestDivisor(
                    textureWidth,
                    Mathf.CeilToInt(maxWidth * 1.1f)
                );
                int altCellHeight = FindNearestDivisor(
                    textureHeight,
                    Mathf.CeilToInt(maxHeight * 1.1f)
                );

                float altFitScore = CalculateSpriteFitScore(
                    spriteBounds,
                    altCellWidth,
                    altCellHeight,
                    textureWidth,
                    textureHeight
                );

                if (altFitScore > spriteFitScore)
                {
                    cellWidth2 = altCellWidth;
                    cellHeight2 = altCellHeight;
                    spriteFitScore = altFitScore;
                }
            }

            if (cellWidth2 < MinimumCellSize || cellHeight2 < MinimumCellSize)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.ClusterCentroid);
            }

            // Calculate confidence based on multiple factors
            float gridConsistency = CalculateGridConsistency(
                xPositions,
                yPositions,
                cellWidth2,
                cellHeight2
            );

            // Cell count ratio
            var finalCellCount = (textureWidth / cellWidth2) * (textureHeight / cellHeight2);
            float cellCountRatio =
                (float)Math.Min(finalCellCount, spriteCount)
                / Math.Max(finalCellCount, spriteCount);

            // Combine factors for confidence
            float confidence = (
                gridConsistency * 0.4f + cellCountRatio * 0.3f + spriteFitScore * 0.3f
            );

            return new AlgorithmResult(
                cellWidth2,
                cellHeight2,
                confidence,
                AutoDetectionAlgorithm.ClusterCentroid
            );
        }

        /// <summary>
        /// Infers grid dimensions directly from detected sprite count and texture dimensions.
        /// Finds the factor pair of spriteCount that best matches the texture aspect ratio.
        /// This is the PRIMARY method for grid detection - simple and reliable for clean sprite sheets.
        /// </summary>
        /// <param name="spriteCount">Number of detected sprites.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="cellWidth">Output: calculated cell width.</param>
        /// <param name="cellHeight">Output: calculated cell height.</param>
        /// <returns>True if a valid grid was found, false otherwise.</returns>
        private static bool InferGridFromSpriteCount(
            int spriteCount,
            int textureWidth,
            int textureHeight,
            out int cellWidth,
            out int cellHeight
        )
        {
            if (spriteCount < 1)
            {
                cellWidth = 0;
                cellHeight = 0;
                return false;
            }

            float textureAspect = (float)textureWidth / textureHeight;

            // SPECIAL CASE: For strip textures (horizontal or vertical), prefer single-row/column layouts
            // even if they don't evenly divide the texture. This handles cases like 256x21 with 12 sprites
            // where the ideal layout (12x1) doesn't evenly divide (256/12 = 21.33).
            if (textureAspect > 4f && spriteCount > 1)
            {
                // Horizontal strip - prefer single row layout
                // Use texture height as the target cell height (sprites are likely square-ish)
                int targetCellHeight = textureHeight;
                // Round to nearest instead of truncating to handle imprecise divisions
                int targetCellWidth = (textureWidth + spriteCount / 2) / spriteCount;

                // Check if this produces reasonable cells
                if (
                    targetCellWidth >= MinimumCellSize
                    && targetCellHeight >= MinimumCellSize
                    && targetCellWidth <= textureWidth
                )
                {
                    // Even if width doesn't divide evenly, prefer this for strip textures
                    // as long as cells are roughly square (within 3:1 aspect ratio)
                    float stripCellAspect = (float)targetCellWidth / targetCellHeight;
                    if (stripCellAspect > 0.33f && stripCellAspect < 3f)
                    {
                        cellWidth = targetCellWidth;
                        cellHeight = targetCellHeight;
                        return true;
                    }
                }
            }
            else if (textureAspect < 0.25f && spriteCount > 1)
            {
                // Vertical strip - prefer single column layout
                int targetCellWidth = textureWidth;
                // Round to nearest instead of truncating to handle imprecise divisions
                int targetCellHeight = (textureHeight + spriteCount / 2) / spriteCount;

                if (
                    targetCellWidth >= MinimumCellSize
                    && targetCellHeight >= MinimumCellSize
                    && targetCellHeight <= textureHeight
                )
                {
                    float stripCellAspect = (float)targetCellWidth / targetCellHeight;
                    if (stripCellAspect > 0.33f && stripCellAspect < 3f)
                    {
                        cellWidth = targetCellWidth;
                        cellHeight = targetCellHeight;
                        return true;
                    }
                }
            }

            int bestCols = 1;
            int bestRows = spriteCount;
            float bestAspectError = float.MaxValue;

            // Find all factor pairs of spriteCount
            for (int cols = 1; cols <= spriteCount; ++cols)
            {
                if (spriteCount % cols != 0)
                {
                    continue;
                }

                int rows = spriteCount / cols;

                // Calculate what cell size this would produce
                int candidateCellWidth = textureWidth / cols;
                int candidateCellHeight = textureHeight / rows;

                // Skip if cells would be too small
                if (candidateCellWidth < MinimumCellSize || candidateCellHeight < MinimumCellSize)
                {
                    continue;
                }

                // Skip if cells don't evenly divide the texture
                if (textureWidth % cols != 0 || textureHeight % rows != 0)
                {
                    continue;
                }

                // Calculate aspect ratio of the resulting cells
                float cellAspect = (float)candidateCellWidth / candidateCellHeight;

                // We want cells that are roughly square, or match the texture aspect
                // For most sprite sheets, cells should be square (aspect ~1)
                // However, for extreme aspect ratio textures (horizontal/vertical strips),
                // we should prefer layouts that match the strip orientation
                float aspectError;
                if (textureAspect > 4f)
                {
                    // Wide horizontal strip (e.g., 256x21) - prefer fewer rows
                    // Penalize configurations with more rows
                    float rowPenalty = rows > 1 ? (rows - 1) * 2f : 0f;
                    aspectError = Math.Abs(cellAspect - 1f) + rowPenalty;
                }
                else if (textureAspect < 0.25f)
                {
                    // Tall vertical strip - prefer fewer columns
                    float colPenalty = cols > 1 ? (cols - 1) * 2f : 0f;
                    aspectError = Math.Abs(cellAspect - 1f) + colPenalty;
                }
                else
                {
                    // Normal sprite sheet - prefer square cells
                    aspectError = Math.Abs(cellAspect - 1f);
                }

                // Prefer this if it's a better match
                if (aspectError < bestAspectError)
                {
                    bestAspectError = aspectError;
                    bestCols = cols;
                    bestRows = rows;
                }
            }

            int inferredCellWidth = textureWidth / bestCols;
            int inferredCellHeight = textureHeight / bestRows;

            cellWidth = inferredCellWidth;
            cellHeight = inferredCellHeight;
            return inferredCellWidth >= MinimumCellSize && inferredCellHeight >= MinimumCellSize;
        }

        /// <summary>
        /// Attempts to infer grid dimensions from sprite count even when exact division isn't possible.
        /// Finds the closest factor pair that produces at least the expected number of cells.
        /// </summary>
        /// <param name="expectedSpriteCount">The expected number of sprites.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="cellWidth">Output: calculated cell width.</param>
        /// <param name="cellHeight">Output: calculated cell height.</param>
        /// <returns>True if a valid approximate grid was found, false otherwise.</returns>
        private static bool TryInferApproximateGrid(
            int expectedSpriteCount,
            int textureWidth,
            int textureHeight,
            out int cellWidth,
            out int cellHeight
        )
        {
            if (expectedSpriteCount < 1)
            {
                cellWidth = 0;
                cellHeight = 0;
                return false;
            }

            int bestCols = 1;
            int bestRows = expectedSpriteCount;
            float bestScore = float.MaxValue;

            for (int cols = 1; cols <= expectedSpriteCount; ++cols)
            {
                int rows = (expectedSpriteCount + cols - 1) / cols; // Ceiling division

                int candidateCellWidth = textureWidth / cols;
                int candidateCellHeight = textureHeight / rows;

                if (candidateCellWidth < MinimumCellSize || candidateCellHeight < MinimumCellSize)
                {
                    continue;
                }

                int actualCells = cols * rows;
                // Penalize overcounting (more cells than expected) more heavily than undercounting
                // Overcounting creates empty cells, which is usually worse
                float cellCountError = Math.Abs(actualCells - expectedSpriteCount);
                float overcountPenalty =
                    actualCells > expectedSpriteCount
                        ? (actualCells - expectedSpriteCount) * 15f
                        : 0f;
                float cellAspect = (float)candidateCellWidth / candidateCellHeight;
                float aspectError = Math.Abs(cellAspect - 1f);
                float score = cellCountError * 10f + overcountPenalty + aspectError;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestCols = cols;
                    bestRows = rows;
                }
            }

            int inferredCellWidth = textureWidth / bestCols;
            int inferredCellHeight = textureHeight / bestRows;

            cellWidth = inferredCellWidth;
            cellHeight = inferredCellHeight;
            return inferredCellWidth >= MinimumCellSize && inferredCellHeight >= MinimumCellSize;
        }

        /// <summary>
        /// Counts the number of unique position groups by grouping positions within tolerance.
        /// Compares each position to the PREVIOUS position rather than the group start,
        /// preventing groups from stretching across large distances.
        /// </summary>
        /// <param name="sortedPositions">Sorted list of position values to group.</param>
        /// <param name="tolerance">Maximum distance between adjacent positions to be considered the same group.</param>
        /// <returns>Number of distinct position groups found.</returns>
        private static int CountUniquePositionGroups(List<float> sortedPositions, float tolerance)
        {
            if (sortedPositions.Count == 0)
            {
                return 0;
            }

            int groupCount = 1;

            for (int i = 1; i < sortedPositions.Count; ++i)
            {
                // Compare to PREVIOUS position, not group start
                if (sortedPositions[i] - sortedPositions[i - 1] > tolerance)
                {
                    ++groupCount;
                }
            }

            return groupCount;
        }

        /// <summary>
        /// Calculates an appropriate tolerance for grouping positions based on the minimum significant gap.
        /// Uses minimum gap (not median) to prevent over-grouping when sprites have varying spacing.
        /// </summary>
        /// <param name="sortedPositions">Sorted list of position values to analyze.</param>
        /// <param name="minTolerance">Minimum tolerance value to return.</param>
        /// <returns>A tolerance value based on minimum significant gap, or minTolerance if not enough data.</returns>
        private static float CalculateGapBasedTolerance(
            List<float> sortedPositions,
            float minTolerance
        )
        {
            if (sortedPositions.Count < 2)
            {
                return minTolerance;
            }

            // Collect all significant gaps between consecutive positions
            // A "significant" gap is one that's larger than a small threshold (likely between different columns/rows)
            using PooledResource<List<float>> gapsLease = Buffers<float>.List.Get(
                out List<float> gaps
            );
            for (int i = 1; i < sortedPositions.Count; ++i)
            {
                float gap = sortedPositions[i] - sortedPositions[i - 1];
                // Gaps must be larger than MinSignificantGapSize to be considered "between columns/rows"
                // This filters out small variations in sprite positions within the same column
                if (gap > MinSignificantGapSize)
                {
                    gaps.Add(gap);
                }
            }

            if (gaps.Count == 0)
            {
                return minTolerance;
            }

            // Use MINIMUM significant gap (not median) to prevent over-grouping
            // This ensures tight tolerance that only groups positions that are truly close
            gaps.Sort();
            float minGap = gaps[0];

            // Tolerance is a fraction of the minimum gap
            // Using a smaller multiplier (0.25) ensures we don't accidentally merge distinct columns
            return Math.Max(minTolerance, minGap * GapToleranceMultiplier);
        }

        /// <summary>
        /// Calculates how well sprites fit within the proposed grid cells.
        /// Uses a "core zone" concept: only grid lines passing through the middle portion
        /// of a sprite are penalized. Edge clipping is ignored to handle anti-aliased bounding boxes.
        /// </summary>
        /// <param name="spriteBounds">List of detected sprite bounding rectangles.</param>
        /// <param name="cellWidth">Proposed grid cell width in pixels.</param>
        /// <param name="cellHeight">Proposed grid cell height in pixels.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <returns>Score in range [0, 1] where 1 means all sprites fit within single cells without core zone splits.</returns>
        private static float CalculateSpriteFitScore(
            List<Rect> spriteBounds,
            int cellWidth,
            int cellHeight,
            int textureWidth,
            int textureHeight
        )
        {
            if (spriteBounds.Count == 0 || cellWidth <= 0 || cellHeight <= 0)
            {
                return 0f;
            }

            float totalScore = 0f;

            for (int i = 0; i < spriteBounds.Count; ++i)
            {
                Rect bounds = spriteBounds[i];
                float spriteScore = 1f;

                // Calculate the "core zone" - the middle portion of the sprite
                // Edge pixels (outside core zone) are ignored as they may be anti-aliased
                float coreMarginX = bounds.width * (1f - SpriteCoreZoneFraction) * 0.5f;
                float coreMarginY = bounds.height * (1f - SpriteCoreZoneFraction) * 0.5f;
                float coreXMin = bounds.xMin + coreMarginX;
                float coreXMax = bounds.xMax - coreMarginX;
                float coreYMin = bounds.yMin + coreMarginY;
                float coreYMax = bounds.yMax - coreMarginY;

                // Calculate worst vertical split severity (only for core zone)
                float worstVerticalSeverity = 0f;
                for (int x = cellWidth; x < textureWidth; x += cellWidth)
                {
                    // Only penalize if the grid line passes through the CORE zone
                    if (coreXMin < x && coreXMax > x)
                    {
                        // Calculate severity based on how centered the split is in the core zone
                        float coreWidth = coreXMax - coreXMin;
                        float distFromCoreLeft = x - coreXMin;
                        float distFromCoreRight = coreXMax - x;
                        float minDist = Math.Min(distFromCoreLeft, distFromCoreRight);
                        float halfCoreWidth = coreWidth * 0.5f;
                        float splitRatio = halfCoreWidth > 0f ? minDist / halfCoreWidth : 0f;
                        float severity = Mathf.Clamp01(splitRatio);

                        if (severity > worstVerticalSeverity)
                        {
                            worstVerticalSeverity = severity;
                        }
                    }
                }

                // Calculate worst horizontal split severity (only for core zone)
                float worstHorizontalSeverity = 0f;
                for (int y = cellHeight; y < textureHeight; y += cellHeight)
                {
                    // Only penalize if the grid line passes through the CORE zone
                    if (coreYMin < y && coreYMax > y)
                    {
                        float coreHeight = coreYMax - coreYMin;
                        float distFromCoreBottom = y - coreYMin;
                        float distFromCoreTop = coreYMax - y;
                        float minDist = Math.Min(distFromCoreBottom, distFromCoreTop);
                        float halfCoreHeight = coreHeight * 0.5f;
                        float splitRatio = halfCoreHeight > 0f ? minDist / halfCoreHeight : 0f;
                        float severity = Mathf.Clamp01(splitRatio);

                        if (severity > worstHorizontalSeverity)
                        {
                            worstHorizontalSeverity = severity;
                        }
                    }
                }

                // Combine severities: worst case of either direction, but compound if both split
                float combinedSeverity = Math.Max(worstVerticalSeverity, worstHorizontalSeverity);
                if (worstVerticalSeverity > 0f && worstHorizontalSeverity > 0f)
                {
                    // Both directions split - extra penalty
                    combinedSeverity = Math.Min(
                        1f,
                        combinedSeverity + worstVerticalSeverity * worstHorizontalSeverity * 0.5f
                    );
                }

                // Apply penalty based on severity
                // Severity of 1.0 (middle split) = 0.0 score for this sprite
                // Severity of 0.0 (no core zone split) = 1.0 score
                spriteScore = 1f - combinedSeverity;

                totalScore += spriteScore;
            }

            return totalScore / spriteBounds.Count;
        }

        /// <summary>
        /// Calculates confidence for user-specified sprite count based on resulting cell aspect ratio.
        /// Penalizes extremely non-square cells which are unlikely to be correct for most sprite sheets.
        /// Returns a confidence in range [0.5, 0.95] since user explicitly specified the count.
        /// </summary>
        /// <param name="cellWidth">The proposed cell width.</param>
        /// <param name="cellHeight">The proposed cell height.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <returns>Confidence score in range [0.5, 0.95].</returns>
        private static float CalculateUserSpecifiedCountConfidence(
            int cellWidth,
            int cellHeight,
            int textureWidth,
            int textureHeight
        )
        {
            if (cellWidth <= 0 || cellHeight <= 0)
            {
                return 0.5f;
            }

            float cellAspect = (float)cellWidth / cellHeight;
            float textureAspect = (float)textureWidth / textureHeight;

            // Calculate how far the cell aspect ratio is from square (1:1)
            // Use log scale so 2:1 and 1:2 have equal deviation
            float cellAspectDeviation = Math.Abs((float)Math.Log(cellAspect));

            // Base confidence starts high since user specified the count
            float confidence = 0.9f;

            // Penalize non-square cells more heavily for extreme deviations
            if (cellAspectDeviation > 2f) // > 7.4:1 or < 1:7.4
            {
                // Extreme deviation - significantly reduce confidence
                confidence = 0.5f;
            }
            else if (cellAspectDeviation > 1.4f) // > 4:1 or < 1:4
            {
                // Large deviation - reduce confidence
                confidence = 0.6f;
            }
            else if (cellAspectDeviation > 0.7f) // > 2:1 or < 1:2
            {
                // Moderate deviation - slight reduction
                confidence = 0.75f;
            }
            else
            {
                // Close to square - high confidence
                // But still cap at 0.9 since we haven't validated sprite boundaries
                confidence = 0.9f;
            }

            // For texture strips (extreme aspect ratios), matching cell orientation gets bonus
            // E.g., 256x21 texture (12:1) should prefer horizontal layout with square-ish cells
            if (textureAspect > 4f || textureAspect < 0.25f)
            {
                // Check if cells match texture orientation
                bool textureIsHorizontal = textureAspect > 1f;
                bool cellsAreHorizontal = cellAspect > 1f;

                if (textureIsHorizontal == cellsAreHorizontal && cellAspectDeviation < 0.7f)
                {
                    // Cells roughly match texture strip orientation and are reasonably square
                    confidence = Math.Min(0.95f, confidence + 0.1f);
                }
            }

            return confidence;
        }

        /// <summary>
        /// Calculates grid consistency: how well centroid positions align to a regular grid.
        /// Combines horizontal and vertical alignment scores for an overall consistency measure.
        /// </summary>
        /// <param name="xPositions">List of X centroid positions.</param>
        /// <param name="yPositions">List of Y centroid positions.</param>
        /// <param name="cellWidth">Expected grid cell width in pixels.</param>
        /// <param name="cellHeight">Expected grid cell height in pixels.</param>
        /// <returns>Consistency score in range [0, 1] where 1 means perfect grid alignment.</returns>
        private static float CalculateGridConsistency(
            List<float> xPositions,
            List<float> yPositions,
            int cellWidth,
            int cellHeight
        )
        {
            float xConsistency = CalculatePositionGridAlignment(xPositions, cellWidth);
            float yConsistency = CalculatePositionGridAlignment(yPositions, cellHeight);
            return (xConsistency + yConsistency) * 0.5f;
        }

        /// <summary>
        /// Calculates how well positions align to cell centers in a grid.
        /// Measures the average deviation of each position from its expected cell center.
        /// </summary>
        /// <param name="positions">List of positions to check for grid alignment.</param>
        /// <param name="cellSize">Expected cell size in pixels.</param>
        /// <returns>Alignment score in range [0, 1] where 1 means all positions are at cell centers.</returns>
        private static float CalculatePositionGridAlignment(List<float> positions, int cellSize)
        {
            if (positions.Count == 0 || cellSize <= 0)
            {
                return 0f;
            }

            float halfCell = cellSize * 0.5f;
            float totalAlignment = 0f;

            for (int i = 0; i < positions.Count; ++i)
            {
                // Find expected cell center
                int cellIndex = Mathf.FloorToInt(positions[i] / cellSize);
                float expectedCenter = cellIndex * cellSize + halfCell;
                float deviation = Mathf.Abs(positions[i] - expectedCenter);

                // Normalize deviation to [0, 1] where 0 = perfect alignment
                float normalizedDeviation = Mathf.Clamp01(deviation / halfCell);
                totalAlignment += 1f - normalizedDeviation;
            }

            return totalAlignment / positions.Count;
        }

        /// <summary>
        /// DistanceTransform algorithm: computes chamfer distance from alpha boundaries
        /// and finds local maxima to identify sprite centers.
        /// </summary>
        private static AlgorithmResult DetectGridDistanceTransform(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            float alphaThreshold,
            int expectedSpriteCount,
            bool snapToTextureDivisor,
            CancellationToken cancellationToken
        )
        {
            byte alphaThresholdByte = ColorQuantization.ToThresholdByte(alphaThreshold);

            // Detect sprite bounds for sprite-fit validation in divisor selection
            using PooledResource<List<Rect>> spriteBoundsLease = Buffers<Rect>.List.Get(
                out List<Rect> spriteBounds
            );
            DetectSpriteBoundsByAlpha(
                pixels,
                textureWidth,
                textureHeight,
                alphaThresholdByte,
                spriteBounds,
                cancellationToken
            );

            using PooledArray<int> distanceLease = SystemArrayPool<int>.Get(
                pixels.Length,
                out int[] distance
            );

            // Initialize: 0 for transparent, large value for opaque
            const int maxDistance = int.MaxValue / 2;
            for (int i = 0; i < pixels.Length; ++i)
            {
                distance[i] = pixels[i].a <= alphaThresholdByte ? 0 : maxDistance;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.DistanceTransform);
            }

            // Forward pass (3-4 chamfer distance)
            for (int y = 1; y < textureHeight; ++y)
            {
                for (int x = 0; x < textureWidth; ++x)
                {
                    int idx = y * textureWidth + x;
                    if (distance[idx] == 0)
                    {
                        continue;
                    }

                    int top = distance[idx - textureWidth];
                    int current = distance[idx];

                    if (x > 0)
                    {
                        int topLeft = distance[idx - textureWidth - 1];
                        int left = distance[idx - 1];
                        current = Math.Min(current, Math.Min(topLeft + 4, left + 3));
                    }

                    current = Math.Min(current, top + 3);

                    if (x < textureWidth - 1)
                    {
                        int topRight = distance[idx - textureWidth + 1];
                        current = Math.Min(current, topRight + 4);
                    }

                    distance[idx] = current;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.DistanceTransform);
            }

            // Backward pass
            for (int y = textureHeight - 2; y >= 0; --y)
            {
                for (int x = textureWidth - 1; x >= 0; --x)
                {
                    int idx = y * textureWidth + x;
                    if (distance[idx] == 0)
                    {
                        continue;
                    }

                    int bottom = distance[idx + textureWidth];
                    int current = distance[idx];

                    if (x < textureWidth - 1)
                    {
                        int bottomRight = distance[idx + textureWidth + 1];
                        int right = distance[idx + 1];
                        current = Math.Min(current, Math.Min(bottomRight + 4, right + 3));
                    }

                    current = Math.Min(current, bottom + 3);

                    if (x > 0)
                    {
                        int bottomLeft = distance[idx + textureWidth - 1];
                        current = Math.Min(current, bottomLeft + 4);
                    }

                    distance[idx] = current;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.DistanceTransform);
            }

            // Find local maxima (raw candidates)
            using PooledResource<List<Vector2Int>> rawMaximaLease = Buffers<Vector2Int>.List.Get(
                out List<Vector2Int> rawMaxima
            );
            using PooledResource<List<int>> rawMaximaValuesLease = Buffers<int>.List.Get(
                out List<int> rawMaximaValues
            );

            int minPeakDistance = MinimumCellSize / 2;
            for (int y = minPeakDistance; y < textureHeight - minPeakDistance; ++y)
            {
                for (int x = minPeakDistance; x < textureWidth - minPeakDistance; ++x)
                {
                    int idx = y * textureWidth + x;
                    int val = distance[idx];

                    if (val < minPeakDistance)
                    {
                        continue;
                    }

                    bool isMaximum = true;
                    for (int dy = -1; dy <= 1 && isMaximum; ++dy)
                    {
                        for (int dx = -1; dx <= 1 && isMaximum; ++dx)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }
                            int neighborIdx = (y + dy) * textureWidth + (x + dx);
                            if (distance[neighborIdx] > val)
                            {
                                isMaximum = false;
                            }
                        }
                    }

                    if (isMaximum)
                    {
                        rawMaxima.Add(new Vector2Int(x, y));
                        rawMaximaValues.Add(val);
                    }
                }
            }

            if (rawMaxima.Count < 2)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.DistanceTransform);
            }

            // Non-maximum suppression: filter out peaks that are too close to stronger peaks
            // Estimate minimum separation based on texture dimensions and peak count
            float estimatedCellSize =
                (float)Math.Min(textureWidth, textureHeight)
                / Math.Max(1, (int)Math.Sqrt(rawMaxima.Count));
            float minSeparation = Math.Max(MinimumCellSize, estimatedCellSize * 0.4f);

            using PooledResource<List<Vector2Int>> maximaLease = Buffers<Vector2Int>.List.Get(
                out List<Vector2Int> localMaxima
            );

            // Sort by peak strength (descending) for non-maximum suppression
            using PooledResource<List<int>> sortedIndicesLease = Buffers<int>.List.Get(
                out List<int> sortedIndices
            );
            for (int i = 0; i < rawMaxima.Count; ++i)
            {
                sortedIndices.Add(i);
            }
            sortedIndices.Sort((a, b) => rawMaximaValues[b].CompareTo(rawMaximaValues[a]));

            // Keep only peaks that are not suppressed by a stronger nearby peak
            using PooledArray<bool> suppressedLease = SystemArrayPool<bool>.Get(
                rawMaxima.Count,
                out bool[] suppressed
            );
            Array.Clear(suppressed, 0, suppressed.Length);

            for (int i = 0; i < sortedIndices.Count; ++i)
            {
                int idx = sortedIndices[i];
                if (suppressed[idx])
                {
                    continue;
                }

                Vector2Int peak = rawMaxima[idx];
                localMaxima.Add(peak);

                // Suppress weaker peaks within minimum separation distance
                for (int j = i + 1; j < sortedIndices.Count; ++j)
                {
                    int otherIdx = sortedIndices[j];
                    if (suppressed[otherIdx])
                    {
                        continue;
                    }

                    Vector2Int other = rawMaxima[otherIdx];
                    float dx = peak.x - other.x;
                    float dy = peak.y - other.y;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist < minSeparation)
                    {
                        suppressed[otherIdx] = true;
                    }
                }
            }

            if (localMaxima.Count < 2)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.DistanceTransform);
            }

            // Analyze peak spacing
            using PooledResource<List<float>> xPositionsLease = Buffers<float>.List.Get(
                out List<float> xPositions
            );
            using PooledResource<List<float>> yPositionsLease = Buffers<float>.List.Get(
                out List<float> yPositions
            );

            for (int i = 0; i < localMaxima.Count; ++i)
            {
                xPositions.Add(localMaxima[i].x);
                yPositions.Add(localMaxima[i].y);
            }

            // PRIMARY METHOD: If user provided expectedSpriteCount, use it directly without fallback
            if (expectedSpriteCount > 0)
            {
                int cellWidth;
                int cellHeight;
                if (
                    InferGridFromSpriteCount(
                        expectedSpriteCount,
                        textureWidth,
                        textureHeight,
                        out cellWidth,
                        out cellHeight
                    )
                )
                {
                    // User explicitly set sprite count - validate confidence based on cell aspect ratio
                    float confidence = CalculateUserSpecifiedCountConfidence(
                        cellWidth,
                        cellHeight,
                        textureWidth,
                        textureHeight
                    );
                    return new AlgorithmResult(
                        cellWidth,
                        cellHeight,
                        confidence,
                        AutoDetectionAlgorithm.DistanceTransform
                    );
                }
                // Fallback to approximate grid when exact division fails
                if (
                    TryInferApproximateGrid(
                        expectedSpriteCount,
                        textureWidth,
                        textureHeight,
                        out cellWidth,
                        out cellHeight
                    )
                )
                {
                    return new AlgorithmResult(
                        cellWidth,
                        cellHeight,
                        0.95f, // Slightly lower confidence since not exact
                        AutoDetectionAlgorithm.DistanceTransform
                    );
                }
            }

            // Auto-detection path: use detected sprite count
            int spriteCount = spriteBounds.Count;
            int inferredCellWidth;
            int inferredCellHeight;

            if (
                spriteCount > 0
                && InferGridFromSpriteCount(
                    spriteCount,
                    textureWidth,
                    textureHeight,
                    out inferredCellWidth,
                    out inferredCellHeight
                )
            )
            {
                // Successfully inferred grid from sprite count - use it directly
            }
            else
            {
                // Fallback: use peak-based grouping
                xPositions.Sort();
                yPositions.Sort();

                int minGapTolerance = MinimumCellSize * DistanceTransformMinGapToleranceMultiplier;
                float xTolerance = CalculateGapBasedTolerance(xPositions, minGapTolerance);
                float yTolerance = CalculateGapBasedTolerance(yPositions, minGapTolerance);

                int numColumns = Math.Max(1, CountUniquePositionGroups(xPositions, xTolerance));
                int numRows = Math.Max(1, CountUniquePositionGroups(yPositions, yTolerance));

                inferredCellWidth = FindNearestDivisor(textureWidth, textureWidth / numColumns);
                inferredCellHeight = FindNearestDivisor(textureHeight, textureHeight / numRows);
            }

            if (inferredCellWidth < MinimumCellSize || inferredCellHeight < MinimumCellSize)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.DistanceTransform);
            }

            xPositions.Sort();
            yPositions.Sort();

            float gridConsistency = CalculateGridConsistency(
                xPositions,
                yPositions,
                inferredCellWidth,
                inferredCellHeight
            );

            return new AlgorithmResult(
                inferredCellWidth,
                inferredCellHeight,
                gridConsistency,
                AutoDetectionAlgorithm.DistanceTransform
            );
        }

        /// <summary>
        /// RegionGrowing algorithm: seeds from local intensity maxima and grows regions
        /// via 4-connected flood-fill to alpha boundaries.
        /// </summary>
        private static AlgorithmResult DetectGridRegionGrowing(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            float alphaThreshold,
            int expectedSpriteCount,
            bool snapToTextureDivisor,
            CancellationToken cancellationToken
        )
        {
            byte alphaThresholdByte = ColorQuantization.ToThresholdByte(alphaThreshold);

            // Find seed points based on intensity (sum of RGB)
            using PooledArray<int> intensityLease = SystemArrayPool<int>.Get(
                pixels.Length,
                out int[] intensity
            );

            for (int i = 0; i < pixels.Length; ++i)
            {
                Color32 c = pixels[i];
                intensity[i] = c.a > alphaThresholdByte ? c.r + c.g + c.b : 0;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.RegionGrowing);
            }

            // Find local intensity maxima as seeds
            using PooledResource<List<Vector2Int>> seedsLease = Buffers<Vector2Int>.List.Get(
                out List<Vector2Int> seeds
            );

            int windowSize = Math.Max(8, Math.Min(textureWidth, textureHeight) / 16);
            for (int y = windowSize; y < textureHeight - windowSize; y += windowSize / 2)
            {
                for (int x = windowSize; x < textureWidth - windowSize; x += windowSize / 2)
                {
                    int idx = y * textureWidth + x;
                    int val = intensity[idx];

                    if (val <= 0)
                    {
                        continue;
                    }

                    bool isMaximum = true;
                    for (int dy = -windowSize / 4; dy <= windowSize / 4 && isMaximum; dy += 2)
                    {
                        for (int dx = -windowSize / 4; dx <= windowSize / 4 && isMaximum; dx += 2)
                        {
                            if (dx == 0 && dy == 0)
                            {
                                continue;
                            }
                            int neighborIdx = (y + dy) * textureWidth + (x + dx);
                            if (
                                neighborIdx >= 0
                                && neighborIdx < pixels.Length
                                && intensity[neighborIdx] > val
                            )
                            {
                                isMaximum = false;
                            }
                        }
                    }

                    if (isMaximum)
                    {
                        seeds.Add(new Vector2Int(x, y));
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.RegionGrowing);
            }

            if (seeds.Count < 2)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.RegionGrowing);
            }

            // Grow regions from seeds and measure sizes
            using PooledArray<int> regionLease = SystemArrayPool<int>.Get(
                pixels.Length,
                out int[] regionId
            );
            Array.Clear(regionId, 0, regionId.Length);

            using PooledResource<List<int>> regionSizesLease = Buffers<int>.List.Get(
                out List<int> regionSizes
            );
            using PooledResource<List<Rect>> regionBoundsLease = Buffers<Rect>.List.Get(
                out List<Rect> regionBounds
            );

            int currentRegion = 0;
            using PooledResource<List<int>> stackLease = Buffers<int>.List.Get(out List<int> stack);

            for (int seedIdx = 0; seedIdx < seeds.Count && currentRegion < 256; ++seedIdx)
            {
                Vector2Int seed = seeds[seedIdx];
                int startIdx = seed.y * textureWidth + seed.x;

                if (regionId[startIdx] != 0 || pixels[startIdx].a <= alphaThresholdByte)
                {
                    continue;
                }

                ++currentRegion;
                stack.Clear();
                stack.Add(startIdx);
                regionId[startIdx] = currentRegion;

                int size = 0;
                int minX = seed.x;
                int maxX = seed.x;
                int minY = seed.y;
                int maxY = seed.y;

                while (stack.Count > 0)
                {
                    int lastIndex = stack.Count - 1;
                    int idx = stack[lastIndex];
                    stack.RemoveAt(lastIndex);

                    ++size;
                    int px = idx % textureWidth;
                    int py = idx / textureWidth;

                    if (px < minX)
                    {
                        minX = px;
                    }
                    if (px > maxX)
                    {
                        maxX = px;
                    }
                    if (py < minY)
                    {
                        minY = py;
                    }
                    if (py > maxY)
                    {
                        maxY = py;
                    }

                    // 4-connected neighbors
                    if (px > 0)
                    {
                        int left = idx - 1;
                        if (regionId[left] == 0 && pixels[left].a > alphaThresholdByte)
                        {
                            regionId[left] = currentRegion;
                            stack.Add(left);
                        }
                    }
                    if (px < textureWidth - 1)
                    {
                        int right = idx + 1;
                        if (regionId[right] == 0 && pixels[right].a > alphaThresholdByte)
                        {
                            regionId[right] = currentRegion;
                            stack.Add(right);
                        }
                    }
                    if (py > 0)
                    {
                        int bottom = idx - textureWidth;
                        if (regionId[bottom] == 0 && pixels[bottom].a > alphaThresholdByte)
                        {
                            regionId[bottom] = currentRegion;
                            stack.Add(bottom);
                        }
                    }
                    if (py < textureHeight - 1)
                    {
                        int top = idx + textureWidth;
                        if (regionId[top] == 0 && pixels[top].a > alphaThresholdByte)
                        {
                            regionId[top] = currentRegion;
                            stack.Add(top);
                        }
                    }
                }

                if (size > MinimumCellSize * MinimumCellSize / 4)
                {
                    regionSizes.Add(size);
                    regionBounds.Add(new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.RegionGrowing);
            }

            if (regionBounds.Count < 2)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.RegionGrowing);
            }

            // Use unique positions approach based on region centroids
            using PooledResource<List<float>> xPositionsLease = Buffers<float>.List.Get(
                out List<float> xPositions
            );
            using PooledResource<List<float>> yPositionsLease = Buffers<float>.List.Get(
                out List<float> yPositions
            );

            // Compute average region size and collect centroid positions
            float avgWidth = 0f;
            float avgHeight = 0f;
            float maxWidth = 0f;
            float maxHeight = 0f;

            for (int i = 0; i < regionBounds.Count; ++i)
            {
                Rect bounds = regionBounds[i];
                avgWidth += bounds.width;
                avgHeight += bounds.height;
                xPositions.Add(bounds.center.x);
                yPositions.Add(bounds.center.y);

                if (bounds.width > maxWidth)
                {
                    maxWidth = bounds.width;
                }
                if (bounds.height > maxHeight)
                {
                    maxHeight = bounds.height;
                }
            }
            avgWidth /= regionBounds.Count;
            avgHeight /= regionBounds.Count;

            // PRIMARY METHOD: If user provided expectedSpriteCount, use it directly without fallback
            if (expectedSpriteCount > 0)
            {
                int cellWidth;
                int cellHeight;
                if (
                    InferGridFromSpriteCount(
                        expectedSpriteCount,
                        textureWidth,
                        textureHeight,
                        out cellWidth,
                        out cellHeight
                    )
                )
                {
                    // User explicitly set sprite count - validate confidence based on cell aspect ratio
                    float userCountConfidence = CalculateUserSpecifiedCountConfidence(
                        cellWidth,
                        cellHeight,
                        textureWidth,
                        textureHeight
                    );
                    return new AlgorithmResult(
                        cellWidth,
                        cellHeight,
                        userCountConfidence,
                        AutoDetectionAlgorithm.RegionGrowing
                    );
                }
                // Fallback to approximate grid when exact division fails
                if (
                    TryInferApproximateGrid(
                        expectedSpriteCount,
                        textureWidth,
                        textureHeight,
                        out cellWidth,
                        out cellHeight
                    )
                )
                {
                    return new AlgorithmResult(
                        cellWidth,
                        cellHeight,
                        0.95f, // Slightly lower confidence since not exact
                        AutoDetectionAlgorithm.RegionGrowing
                    );
                }
            }

            // Auto-detection path: use detected sprite count
            int spriteCount = regionBounds.Count;
            int inferredCellWidth;
            int inferredCellHeight;

            if (
                !InferGridFromSpriteCount(
                    spriteCount,
                    textureWidth,
                    textureHeight,
                    out inferredCellWidth,
                    out inferredCellHeight
                )
            )
            {
                // Fallback: use tolerance-based grouping
                xPositions.Sort();
                yPositions.Sort();

                float xTolerance = CalculateGapBasedTolerance(xPositions, MinimumCellSize);
                float yTolerance = CalculateGapBasedTolerance(yPositions, MinimumCellSize);

                int numColumns = Math.Max(1, CountUniquePositionGroups(xPositions, xTolerance));
                int numRows = Math.Max(1, CountUniquePositionGroups(yPositions, yTolerance));

                inferredCellWidth = FindNearestDivisor(textureWidth, textureWidth / numColumns);
                inferredCellHeight = FindNearestDivisor(textureHeight, textureHeight / numRows);
            }

            int cellWidth2 = inferredCellWidth;
            int cellHeight2 = inferredCellHeight;

            // Ensure cells are at least as large as the max region
            if (cellWidth2 < maxWidth)
            {
                cellWidth2 = FindNearestDivisor(textureWidth, Mathf.CeilToInt(maxWidth));
            }
            if (cellHeight2 < maxHeight)
            {
                cellHeight2 = FindNearestDivisor(textureHeight, Mathf.CeilToInt(maxHeight));
            }

            // Sprite-fit validation
            float spriteFitScore = CalculateSpriteFitScore(
                regionBounds,
                cellWidth2,
                cellHeight2,
                textureWidth,
                textureHeight
            );

            // If sprites don't fit well, try larger cell sizes
            if (spriteFitScore < SpriteFitFallbackThreshold)
            {
                int altCellWidth = FindNearestDivisor(
                    textureWidth,
                    Mathf.CeilToInt(maxWidth * 1.1f)
                );
                int altCellHeight = FindNearestDivisor(
                    textureHeight,
                    Mathf.CeilToInt(maxHeight * 1.1f)
                );

                float altFitScore = CalculateSpriteFitScore(
                    regionBounds,
                    altCellWidth,
                    altCellHeight,
                    textureWidth,
                    textureHeight
                );

                if (altFitScore > spriteFitScore)
                {
                    cellWidth2 = altCellWidth;
                    cellHeight2 = altCellHeight;
                    spriteFitScore = altFitScore;
                }
            }

            if (cellWidth2 < MinimumCellSize || cellHeight2 < MinimumCellSize)
            {
                return AlgorithmResult.Invalid(AutoDetectionAlgorithm.RegionGrowing);
            }

            // Calculate confidence based on region size uniformity
            float widthVariance = 0f;
            float heightVariance = 0f;
            for (int i = 0; i < regionBounds.Count; ++i)
            {
                float wDiff = regionBounds[i].width - avgWidth;
                float hDiff = regionBounds[i].height - avgHeight;
                widthVariance += wDiff * wDiff;
                heightVariance += hDiff * hDiff;
            }
            widthVariance /= regionBounds.Count;
            heightVariance /= regionBounds.Count;

            float widthStdDev = Mathf.Sqrt(widthVariance);
            float heightStdDev = Mathf.Sqrt(heightVariance);

            float widthCoeffVar = avgWidth > 0 ? widthStdDev / avgWidth : 1f;
            float heightCoeffVar = avgHeight > 0 ? heightStdDev / avgHeight : 1f;

            float uniformityScore = Mathf.Clamp01(1f - (widthCoeffVar + heightCoeffVar) * 0.5f);

            // Combine uniformity and sprite-fit for confidence
            float confidence = (uniformityScore * 0.5f + spriteFitScore * 0.5f);

            return new AlgorithmResult(
                cellWidth2,
                cellHeight2,
                confidence,
                AutoDetectionAlgorithm.RegionGrowing
            );
        }

        /// <summary>
        /// Detects individual sprite bounds by flood-filling opaque regions.
        /// </summary>
        private static void DetectSpriteBoundsByAlpha(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            byte alphaThresholdByte,
            List<Rect> result,
            CancellationToken cancellationToken
        )
        {
            result.Clear();

            using PooledArray<bool> visitedLease = SystemArrayPool<bool>.Get(
                pixels.Length,
                out bool[] visited
            );
            Array.Clear(visited, 0, visited.Length);

            using PooledResource<List<int>> stackLease = Buffers<int>.List.Get(out List<int> stack);

            for (int y = 0; y < textureHeight; ++y)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                for (int x = 0; x < textureWidth; ++x)
                {
                    int index = y * textureWidth + x;
                    if (visited[index])
                    {
                        continue;
                    }

                    if (pixels[index].a <= alphaThresholdByte)
                    {
                        visited[index] = true;
                        continue;
                    }

                    int minX = x;
                    int maxX = x;
                    int minY = y;
                    int maxY = y;

                    stack.Clear();
                    stack.Add(index);
                    visited[index] = true;

                    while (stack.Count > 0)
                    {
                        int lastIndex = stack.Count - 1;
                        int current = stack[lastIndex];
                        stack.RemoveAt(lastIndex);

                        int currentX = current % textureWidth;
                        int currentY = current / textureWidth;

                        if (currentX < minX)
                        {
                            minX = currentX;
                        }
                        if (currentX > maxX)
                        {
                            maxX = currentX;
                        }
                        if (currentY < minY)
                        {
                            minY = currentY;
                        }
                        if (currentY > maxY)
                        {
                            maxY = currentY;
                        }

                        // 4-connected neighbors (cardinal directions)
                        if (currentX > 0)
                        {
                            int leftIndex = current - 1;
                            if (!visited[leftIndex] && pixels[leftIndex].a > alphaThresholdByte)
                            {
                                visited[leftIndex] = true;
                                stack.Add(leftIndex);
                            }
                        }
                        if (currentX < textureWidth - 1)
                        {
                            int rightIndex = current + 1;
                            if (!visited[rightIndex] && pixels[rightIndex].a > alphaThresholdByte)
                            {
                                visited[rightIndex] = true;
                                stack.Add(rightIndex);
                            }
                        }
                        if (currentY > 0)
                        {
                            int bottomIndex = current - textureWidth;
                            if (!visited[bottomIndex] && pixels[bottomIndex].a > alphaThresholdByte)
                            {
                                visited[bottomIndex] = true;
                                stack.Add(bottomIndex);
                            }
                        }
                        if (currentY < textureHeight - 1)
                        {
                            int topIndex = current + textureWidth;
                            if (!visited[topIndex] && pixels[topIndex].a > alphaThresholdByte)
                            {
                                visited[topIndex] = true;
                                stack.Add(topIndex);
                            }
                        }

                        // Add diagonal neighbors for 8-connectivity
                        // Top-left
                        if (currentX > 0 && currentY < textureHeight - 1)
                        {
                            int neighborIndex = (currentY + 1) * textureWidth + (currentX - 1);
                            if (
                                !visited[neighborIndex]
                                && pixels[neighborIndex].a > alphaThresholdByte
                            )
                            {
                                visited[neighborIndex] = true;
                                stack.Add(neighborIndex);
                            }
                        }
                        // Top-right
                        if (currentX < textureWidth - 1 && currentY < textureHeight - 1)
                        {
                            int neighborIndex = (currentY + 1) * textureWidth + (currentX + 1);
                            if (
                                !visited[neighborIndex]
                                && pixels[neighborIndex].a > alphaThresholdByte
                            )
                            {
                                visited[neighborIndex] = true;
                                stack.Add(neighborIndex);
                            }
                        }
                        // Bottom-left
                        if (currentX > 0 && currentY > 0)
                        {
                            int neighborIndex = (currentY - 1) * textureWidth + (currentX - 1);
                            if (
                                !visited[neighborIndex]
                                && pixels[neighborIndex].a > alphaThresholdByte
                            )
                            {
                                visited[neighborIndex] = true;
                                stack.Add(neighborIndex);
                            }
                        }
                        // Bottom-right
                        if (currentX < textureWidth - 1 && currentY > 0)
                        {
                            int neighborIndex = (currentY - 1) * textureWidth + (currentX + 1);
                            if (
                                !visited[neighborIndex]
                                && pixels[neighborIndex].a > alphaThresholdByte
                            )
                            {
                                visited[neighborIndex] = true;
                                stack.Add(neighborIndex);
                            }
                        }
                    }

                    int width = maxX - minX + 1;
                    int height = maxY - minY + 1;

                    // Filter out anti-aliasing artifacts while preserving small sprites
                    // Cap minArea at 256 to ensure 16x16 sprites are always preserved regardless of texture size
                    // Cap minDimension at 16 to ensure small sprites aren't filtered on large textures
                    int minArea = Math.Max(4, Math.Min(256, (textureWidth * textureHeight) / 1000));
                    int minDimension = Math.Max(
                        2,
                        Math.Min(16, Math.Min(textureWidth, textureHeight) / 64)
                    );

                    if (
                        width >= minDimension
                        && height >= minDimension
                        && width * height >= minArea
                    )
                    {
                        result.Add(new Rect(minX, minY, width, height));
                    }
                }
            }
        }

        /// <summary>
        /// Generates candidate cell sizes for a given dimension.
        /// Includes common sprite sizes (8, 16, 32, etc.) and all divisors of the dimension.
        /// </summary>
        /// <param name="dimension">The texture dimension (width or height) to generate candidates for.</param>
        /// <param name="candidates">Output list to populate with candidate cell sizes.</param>
        private static void GenerateCandidateCellSizes(int dimension, List<int> candidates)
        {
            candidates.Clear();

            // Add common sizes that divide evenly
            for (int i = 0; i < CommonCellSizes.Length; ++i)
            {
                int size = CommonCellSizes[i];
                if (size >= MinimumCellSize && size <= dimension && dimension % size == 0)
                {
                    candidates.Add(size);
                }
            }

            // Add all divisors >= MinimumCellSize using sqrt optimization
            // Iterate only to sqrt(dimension) and add both divisor pairs
            for (int div = MinimumCellSize; div * div <= dimension; ++div)
            {
                if (dimension % div == 0)
                {
                    if (!candidates.Contains(div))
                    {
                        candidates.Add(div);
                    }
                    int complement = dimension / div;
                    if (
                        complement != div
                        && complement >= MinimumCellSize
                        && !candidates.Contains(complement)
                    )
                    {
                        candidates.Add(complement);
                    }
                }
            }

            // Add the full dimension itself
            if (!candidates.Contains(dimension))
            {
                candidates.Add(dimension);
            }
        }

        /// <summary>
        /// Scores a cell size by measuring boundary transparency.
        /// Uses EXACT grid line positions only (no offset checking) to prevent
        /// smaller cell sizes from getting artificially high scores by finding
        /// nearby transparent pixels.
        /// </summary>
        /// <param name="transparencyCount">Array of transparent pixel counts per column or row.</param>
        /// <param name="dimension">The texture dimension being scored (width or height).</param>
        /// <param name="orthogonalDimension">The perpendicular dimension (height for width scoring, width for height).</param>
        /// <param name="cellSize">Candidate cell size to evaluate.</param>
        /// <returns>Normalized score in range [0, 1] where 1 means all boundaries are fully transparent.</returns>
        private static float ScoreCellSizeForDimension(
            int[] transparencyCount,
            int dimension,
            int orthogonalDimension,
            int cellSize
        )
        {
            if (cellSize <= 0 || dimension % cellSize != 0)
            {
                return 0f;
            }

            int numBoundaries = dimension / cellSize - 1;
            if (numBoundaries <= 0)
            {
                return 0f;
            }

            float totalScore = 0f;
            for (int i = 1; i <= numBoundaries; ++i)
            {
                int boundaryPos = i * cellSize;
                if (boundaryPos < dimension)
                {
                    // Check EXACT position only - no offset checking
                    // This prevents smaller cell sizes from gaming the scoring
                    // by finding adjacent transparent pixels
                    float transparency =
                        (float)transparencyCount[boundaryPos] / orthogonalDimension;

                    float lineScore = ComputeTransparencyLineScore(transparency);
                    totalScore += lineScore;
                }
            }

            float avgScore = totalScore / numBoundaries;
            return avgScore / MaxTransparencyLineScore;
        }

        /// <summary>
        /// Finds the nearest divisor of dimension to the target value.
        /// Returns the target if it already evenly divides the dimension.
        /// </summary>
        /// <param name="dimension">The texture dimension that must be evenly divisible.</param>
        /// <param name="target">The desired cell size to match as closely as possible.</param>
        /// <returns>The divisor of dimension closest to target, or target if it divides evenly.</returns>
        private static int FindNearestDivisor(int dimension, int target)
        {
            if (dimension % target == 0)
            {
                return target;
            }

            int bestDivisor = target;
            int bestDiff = int.MaxValue;

            for (int div = MinimumCellSize; div <= dimension; ++div)
            {
                if (dimension % div == 0)
                {
                    int diff = Mathf.Abs(div - target);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestDivisor = div;
                    }
                }
            }

            return bestDivisor;
        }

        /// <summary>
        /// Checks if a value is a power of two.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if the value is a positive power of two (1, 2, 4, 8, ...).</returns>
        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        /// <summary>
        /// Finds the best cell divisor by scoring how well grid lines align with transparent regions.
        /// Searches ALL valid divisors (not just those near base size), but prefers divisors close
        /// to the base size when scores are similar.
        /// When texture dimensions aren't cleanly divisible, analyzes remainder pixels:
        /// - If greater than 90% transparent, discards remainder
        /// - Otherwise adjusts to include content
        /// </summary>
        /// <param name="pixels">Source texture pixel data.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="baseCellWidth">Initial computed cell width.</param>
        /// <param name="baseCellHeight">Initial computed cell height.</param>
        /// <param name="transparencyThreshold">Alpha threshold (0-1) below which pixels are considered transparent.</param>
        /// <param name="spriteBounds">Optional list of detected sprite bounds to penalize divisors that would split sprites.</param>
        /// <returns>Adjusted cell size that best aligns with transparency boundaries.</returns>
        internal static Vector2Int FindBestTransparencyAlignedDivisor(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            int baseCellWidth,
            int baseCellHeight,
            float transparencyThreshold = 0.1f,
            List<Rect> spriteBounds = null
        )
        {
            int fallbackWidth =
                textureWidth % baseCellWidth == 0
                    ? baseCellWidth
                    : FindNearestDivisor(textureWidth, baseCellWidth);
            int fallbackHeight =
                textureHeight % baseCellHeight == 0
                    ? baseCellHeight
                    : FindNearestDivisor(textureHeight, baseCellHeight);

            int bestWidth = fallbackWidth;
            int bestHeight = fallbackHeight;

            // Collect ALL valid divisors for width and height
            using PooledResource<List<int>> widthDivisorsLease = Buffers<int>.List.Get(
                out List<int> widthDivisors
            );
            using PooledResource<List<int>> heightDivisorsLease = Buffers<int>.List.Get(
                out List<int> heightDivisors
            );

            // Find all divisors >= MinimumCellSize using sqrt optimization
            // Iterate only to sqrt(dimension) and add both divisor pairs
            for (int div = MinimumCellSize; div * div <= textureWidth; ++div)
            {
                if (textureWidth % div == 0)
                {
                    widthDivisors.Add(div);
                    int complement = textureWidth / div;
                    if (complement != div && complement >= MinimumCellSize)
                    {
                        widthDivisors.Add(complement);
                    }
                }
            }

            for (int div = MinimumCellSize; div * div <= textureHeight; ++div)
            {
                if (textureHeight % div == 0)
                {
                    heightDivisors.Add(div);
                    int complement = textureHeight / div;
                    if (complement != div && complement >= MinimumCellSize)
                    {
                        heightDivisors.Add(complement);
                    }
                }
            }

            float bestScore = float.MinValue;
            float bestCombinedBonus = float.MinValue;
            bool foundCandidate = false;

            // Calculate expected cell count from base dimensions
            // This is the PRIMARY constraint - we should not deviate significantly from this
            int baseCols = textureWidth / baseCellWidth;
            int baseRows = textureHeight / baseCellHeight;
            int expectedCellCount = baseCols * baseRows;
            int minCellCount = Math.Max(1, (int)(expectedCellCount / MaxCellCountRatio));
            int maxCellCount = (int)(expectedCellCount * MaxCellCountRatio);

            // Score the fallback first to establish baseline
            float fallbackScore = ScoreDivisorByTransparency(
                pixels,
                textureWidth,
                textureHeight,
                fallbackWidth,
                fallbackHeight,
                transparencyThreshold
            );
            bestScore = fallbackScore;
            bestCombinedBonus = 1.0f; // Fallback has maximum proximity bonus

            for (int wi = 0; wi < widthDivisors.Count; ++wi)
            {
                int candidateWidth = widthDivisors[wi];

                for (int hi = 0; hi < heightDivisors.Count; ++hi)
                {
                    int candidateHeight = heightDivisors[hi];

                    // HARD CONSTRAINT: Cell count must be close to expected count
                    // This prevents the function from choosing a completely different grid
                    int candidateCols = textureWidth / candidateWidth;
                    int candidateRows = textureHeight / candidateHeight;
                    int candidateCellCount = candidateCols * candidateRows;

                    if (candidateCellCount < minCellCount || candidateCellCount > maxCellCount)
                    {
                        continue; // Skip divisors that produce wrong cell counts
                    }

                    float score = ScoreDivisorByTransparency(
                        pixels,
                        textureWidth,
                        textureHeight,
                        candidateWidth,
                        candidateHeight,
                        transparencyThreshold
                    );

                    // HARD CONSTRAINT: Reject any divisor that would split sprites through their center
                    if (spriteBounds != null && spriteBounds.Count > 0)
                    {
                        float spriteFitScore = CalculateSpriteFitScore(
                            spriteBounds,
                            candidateWidth,
                            candidateHeight,
                            textureWidth,
                            textureHeight
                        );

                        // HARD CONSTRAINT: Reject divisors that split sprites
                        if (spriteFitScore < SpriteFitHardThresholdStrict)
                        {
                            continue; // Skip this divisor entirely
                        }

                        // Mild bonus for excellent sprite fit
                        if (spriteFitScore >= 0.95f)
                        {
                            score *= 1.1f;
                        }
                    }

                    // Proximity bonus: how close is this to the base cell size
                    float widthProximity =
                        1f
                        - Mathf.Clamp01(
                            (float)Math.Abs(candidateWidth - baseCellWidth) / baseCellWidth
                        );
                    float heightProximity =
                        1f
                        - Mathf.Clamp01(
                            (float)Math.Abs(candidateHeight - baseCellHeight) / baseCellHeight
                        );
                    float proximityBonus = (widthProximity + heightProximity) * 0.5f;

                    // Size bonus: prefer larger cell sizes (fewer cells = less likely to split sprites)
                    float sizeBonus =
                        (float)(candidateWidth + candidateHeight) / (textureWidth + textureHeight);

                    // Combined bonus: proximity matters most, but also prefer larger sizes
                    float combinedBonus = proximityBonus * 0.6f + sizeBonus * 0.4f;

                    // A candidate is better if:
                    // 1. It has significantly better transparency score (> 15% better), OR
                    // 2. It has similar transparency but better combined bonus (proximity + size)
                    const float scoreDifferenceThreshold = 0.15f;
                    bool isMuchBetterScore = score > bestScore + scoreDifferenceThreshold;
                    bool isSimilarScoreButBetter =
                        Math.Abs(score - bestScore) <= scoreDifferenceThreshold
                        && combinedBonus > bestCombinedBonus;

                    if (isMuchBetterScore || isSimilarScoreButBetter)
                    {
                        bestScore = score;
                        bestCombinedBonus = combinedBonus;
                        bestWidth = candidateWidth;
                        bestHeight = candidateHeight;
                        foundCandidate = true;
                    }
                }
            }

            if (!foundCandidate)
            {
                bestWidth = fallbackWidth;
                bestHeight = fallbackHeight;
            }

            int remainderX = textureWidth % bestWidth;
            int remainderY = textureHeight % bestHeight;

            if (remainderX > 0)
            {
                float remainderTransparency = CalculateColumnTransparency(
                    pixels,
                    textureWidth,
                    textureHeight,
                    textureWidth - remainderX,
                    remainderX,
                    transparencyThreshold
                );

                if (remainderTransparency < RemainderTransparencyThreshold)
                {
                    int cols = textureWidth / bestWidth;
                    if (cols > 0)
                    {
                        bestWidth = textureWidth / cols;
                    }
                }
            }

            if (remainderY > 0)
            {
                float remainderTransparency = CalculateRowTransparency(
                    pixels,
                    textureWidth,
                    textureHeight,
                    textureHeight - remainderY,
                    remainderY,
                    transparencyThreshold
                );

                if (remainderTransparency < RemainderTransparencyThreshold)
                {
                    int rows = textureHeight / bestHeight;
                    if (rows > 0)
                    {
                        bestHeight = textureHeight / rows;
                    }
                }
            }

            return new Vector2Int(bestWidth, bestHeight);
        }

        /// <summary>
        /// Scores a candidate divisor by how well vertical/horizontal grid lines align with transparent regions.
        /// Uses contrast scoring: compares boundary transparency to interior opacity.
        /// A good grid has HIGH boundary transparency and LOW interior transparency.
        /// Also checks grid line continuity by sampling multiple positions around each grid line.
        /// </summary>
        /// <param name="pixels">Source texture pixel data.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="cellWidth">Candidate cell width to score.</param>
        /// <param name="cellHeight">Candidate cell height to score.</param>
        /// <param name="transparencyThreshold">Alpha threshold (0-1) below which pixels are considered transparent.</param>
        /// <returns>Score in range [0, 1] where 1 means all grid lines pass through fully transparent pixels with good contrast.</returns>
        internal static float ScoreDivisorByTransparency(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            int cellWidth,
            int cellHeight,
            float transparencyThreshold
        )
        {
            byte alphaThreshold = ColorQuantization.ToThresholdByte(transparencyThreshold);

            int numVerticalLines = (textureWidth / cellWidth) - 1;
            int numHorizontalLines = (textureHeight / cellHeight) - 1;

            if (numVerticalLines <= 0 && numHorizontalLines <= 0)
            {
                return 0f;
            }

            // Calculate boundary transparency at EXACT grid line positions only
            // (no adjacent pixel checking to avoid false positives)
            float totalBoundaryTransparency = 0f;
            int boundaryLineCount = 0;

            // Check vertical grid lines at exact position only
            for (int baseX = cellWidth; baseX < textureWidth; baseX += cellWidth)
            {
                int transparentCount = 0;
                for (int y = 0; y < textureHeight; ++y)
                {
                    int index = y * textureWidth + baseX;
                    if (pixels[index].a <= alphaThreshold)
                    {
                        ++transparentCount;
                    }
                }
                float lineTransparency = (float)transparentCount / textureHeight;
                totalBoundaryTransparency += lineTransparency;
                ++boundaryLineCount;
            }

            // Check horizontal grid lines at exact position only
            for (int baseY = cellHeight; baseY < textureHeight; baseY += cellHeight)
            {
                int transparentCount = 0;
                for (int x = 0; x < textureWidth; ++x)
                {
                    int index = baseY * textureWidth + x;
                    if (pixels[index].a <= alphaThreshold)
                    {
                        ++transparentCount;
                    }
                }
                float lineTransparency = (float)transparentCount / textureWidth;
                totalBoundaryTransparency += lineTransparency;
                ++boundaryLineCount;
            }

            if (boundaryLineCount <= 0)
            {
                return 0f;
            }

            float avgBoundaryTransparency = totalBoundaryTransparency / boundaryLineCount;

            // Calculate interior opacity (center of cells, away from boundaries)
            float totalInteriorOpacity = 0f;
            int interiorSampleCount = 0;

            int numColumns = textureWidth / cellWidth;
            int numRows = textureHeight / cellHeight;

            // Sample center of each cell
            for (int col = 0; col < numColumns; ++col)
            {
                for (int row = 0; row < numRows; ++row)
                {
                    int centerX = col * cellWidth + cellWidth / 2;
                    int centerY = row * cellHeight + cellHeight / 2;

                    if (centerX >= textureWidth || centerY >= textureHeight)
                    {
                        continue;
                    }

                    // Sample a small region around the center
                    int sampleRadius = Math.Min(cellWidth, cellHeight) / 4;
                    sampleRadius = Math.Max(1, sampleRadius);

                    int opaqueCount = 0;
                    int sampleCount = 0;

                    for (int dy = -sampleRadius; dy <= sampleRadius; ++dy)
                    {
                        int y = centerY + dy;
                        if (y < 0 || y >= textureHeight)
                        {
                            continue;
                        }

                        for (int dx = -sampleRadius; dx <= sampleRadius; ++dx)
                        {
                            int x = centerX + dx;
                            if (x < 0 || x >= textureWidth)
                            {
                                continue;
                            }

                            int index = y * textureWidth + x;
                            if (pixels[index].a > alphaThreshold)
                            {
                                ++opaqueCount;
                            }
                            ++sampleCount;
                        }
                    }

                    if (sampleCount > 0)
                    {
                        totalInteriorOpacity += (float)opaqueCount / sampleCount;
                        ++interiorSampleCount;
                    }
                }
            }

            float avgInteriorOpacity =
                interiorSampleCount > 0 ? totalInteriorOpacity / interiorSampleCount : 0f;

            // Contrast score: good grids have transparent boundaries and opaque interiors
            // Score = avgBoundaryTransparency * (1 + avgInteriorOpacity * 0.5)
            // This rewards both high boundary transparency AND high contrast with interiors
            float contrastBonus = avgInteriorOpacity * 0.5f;
            float score = avgBoundaryTransparency * (1f + contrastBonus);

            // Normalize to [0, 1]
            return Mathf.Clamp01(score);
        }

        /// <summary>
        /// Computes a linear score for a transparency ratio.
        /// Uses linear scaling to ensure proportional transparency differences are preserved,
        /// which is important for the proximity-based tiebreaking logic.
        /// </summary>
        /// <param name="transparency">Transparency ratio in range [0, 1].</param>
        /// <returns>Score scaled to MaxTransparencyLineScore.</returns>
        private static float ComputeTransparencyLineScore(float transparency)
        {
            return transparency * MaxTransparencyLineScore;
        }

        /// <summary>
        /// Calculates percentage of transparent pixels in a column region.
        /// </summary>
        /// <param name="pixels">Source texture pixel data.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="startX">Starting X position of the column region.</param>
        /// <param name="width">Width of the column region to analyze.</param>
        /// <param name="transparencyThreshold">Alpha threshold (0-1) below which pixels are considered transparent.</param>
        /// <returns>Percentage of transparent pixels in the region (0-1).</returns>
        private static float CalculateColumnTransparency(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            int startX,
            int width,
            float transparencyThreshold
        )
        {
            int transparentCount = 0;
            int totalCount = 0;
            byte alphaThreshold = ColorQuantization.ToThresholdByte(transparencyThreshold);

            for (int x = startX; x < startX + width && x < textureWidth; ++x)
            {
                for (int y = 0; y < textureHeight; ++y)
                {
                    int index = y * textureWidth + x;
                    if (pixels[index].a <= alphaThreshold)
                    {
                        ++transparentCount;
                    }
                    ++totalCount;
                }
            }

            return totalCount > 0 ? (float)transparentCount / totalCount : 1f;
        }

        /// <summary>
        /// Calculates percentage of transparent pixels in a row region.
        /// </summary>
        /// <param name="pixels">Source texture pixel data.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="startY">Starting Y position of the row region.</param>
        /// <param name="height">Height of the row region to analyze.</param>
        /// <param name="transparencyThreshold">Alpha threshold (0-1) below which pixels are considered transparent.</param>
        /// <returns>Percentage of transparent pixels in the region (0-1).</returns>
        private static float CalculateRowTransparency(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            int startY,
            int height,
            float transparencyThreshold
        )
        {
            int transparentCount = 0;
            int totalCount = 0;
            byte alphaThreshold = ColorQuantization.ToThresholdByte(transparencyThreshold);

            for (int y = startY; y < startY + height && y < textureHeight; ++y)
            {
                for (int x = 0; x < textureWidth; ++x)
                {
                    int index = y * textureWidth + x;
                    if (pixels[index].a <= alphaThreshold)
                    {
                        ++transparentCount;
                    }
                    ++totalCount;
                }
            }

            return totalCount > 0 ? (float)transparentCount / totalCount : 1f;
        }
    }
#endif
}

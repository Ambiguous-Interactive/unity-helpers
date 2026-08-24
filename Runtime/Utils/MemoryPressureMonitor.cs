// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    /// <summary>
    /// Monitors system memory pressure and provides proactive memory management for pool purging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class tracks memory usage via <see cref="GC.GetTotalMemory"/> and GC collection frequency
    /// to detect memory pressure before the system triggers <see cref="UnityEngine.Application.lowMemory"/>.
    /// </para>
    /// <para>
    /// Memory pressure detection is based on three factors:
    /// <list type="number">
    ///   <item><description>Absolute memory usage compared to <see cref="MemoryPressureThresholdBytes"/></description></item>
    ///   <item><description>GC collection rate (rapid Gen0 collections indicate memory stress)</description></item>
    ///   <item><description>Memory growth rate between checks</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The monitor is designed to be lightweight and can be called frequently. It throttles actual
    /// pressure calculations based on <see cref="CheckIntervalSeconds"/> to avoid overhead.
    /// </para>
    /// <example>
    /// <code><![CDATA[
    /// // Configure thresholds
    /// MemoryPressureMonitor.MemoryPressureThresholdBytes = 256 * 1024 * 1024; // 256MB
    /// MemoryPressureMonitor.CheckIntervalSeconds = 2f; // Check every 2 seconds
    ///
    /// // Enable monitoring (integrated with pool purging)
    /// MemoryPressureMonitor.Enabled = true;
    ///
    /// // Check current pressure level
    /// MemoryPressureLevel level = MemoryPressureMonitor.CurrentPressure;
    /// if (level >= MemoryPressureLevel.High)
    /// {
    ///     // Take action
    /// }
    /// ]]></code>
    /// </example>
    /// </remarks>
    public static class MemoryPressureMonitor
    {
        /// <summary>
        /// Default memory threshold in bytes (512MB).
        /// </summary>
        public const long DefaultMemoryPressureThresholdBytes = 512L * 1024 * 1024;

        /// <summary>
        /// Default check interval in seconds (5 seconds).
        /// </summary>
        public const float DefaultCheckIntervalSeconds = 5f;

        /// <summary>
        /// Default GC collection rate threshold (collections per second) that indicates stress.
        /// </summary>
        public const float DefaultGCCollectionRateThreshold = 2f;

        /// <summary>
        /// Default memory growth rate threshold (bytes per second) that indicates stress.
        /// </summary>
        public const long DefaultMemoryGrowthRateThreshold = 50L * 1024 * 1024; // 50MB/s

        private static int _enabled = 1;
        private static long _memoryPressureThresholdBytes = DefaultMemoryPressureThresholdBytes;
        private static float _checkIntervalSeconds = DefaultCheckIntervalSeconds;
        private static float _gcCollectionRateThreshold = DefaultGCCollectionRateThreshold;
        private static long _memoryGrowthRateThreshold = DefaultMemoryGrowthRateThreshold;

        private static long _lastTotalMemory;
        private static int _lastGCCount;
        private static float _lastCheckTime;
        private static int _currentPressure;

        private static readonly Stopwatch MonitorStopwatch = Stopwatch.StartNew();
        private static readonly object UpdateLock = new();

        // Memory ratio thresholds for pressure calculation
        private const float CriticalMemoryRatio = 1.25f;
        private const float HighMemoryRatio = 1.0f;
        private const float MediumMemoryRatio = 0.9f;
        private const float LowMemoryRatio = 0.75f;

        // Pressure score thresholds for level determination
        private const int CriticalScoreThreshold = 6;
        private const int HighScoreThreshold = 4;
        private const int MediumScoreThreshold = 2;
        private const int LowScoreThreshold = 1;

        // Score contributions for memory ratio
        private const int CriticalMemoryScoreContribution = 4;
        private const int HighMemoryScoreContribution = 3;
        private const int MediumMemoryScoreContribution = 2;
        private const int LowMemoryScoreContribution = 1;

        // Score contributions for GC and growth rates
        private const int HighGCRateScoreContribution = 2;
        private const int MediumGCRateScoreContribution = 1;
        private const int HighGrowthRateScoreContribution = 2;
        private const int MediumGrowthRateScoreContribution = 1;

        // Multipliers for rate thresholds
        private const float HighGCRateMultiplier = 3f;
        private const float HighGrowthRateMultiplier = 2f;

        /// <summary>
        /// Calculates the pressure level from provided metrics without querying the GC.
        /// Used for testing pressure calculation logic with controlled inputs.
        /// </summary>
        /// <param name="memoryRatio">Current memory as ratio of threshold (e.g., 0.9 = 90% of threshold).</param>
        /// <param name="gcRateMultiplier">GC rate as multiple of threshold (e.g., 2.0 = 2x the threshold rate).</param>
        /// <param name="growthRateMultiplier">Growth rate as multiple of threshold (e.g., 1.5 = 1.5x the threshold rate).</param>
        /// <returns>The calculated pressure level.</returns>
        internal static MemoryPressureLevel CalculatePressureFromMetrics(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier
        )
        {
            int pressureScore = 0;

            if (CriticalMemoryRatio <= memoryRatio)
            {
                pressureScore += CriticalMemoryScoreContribution;
            }
            else if (HighMemoryRatio <= memoryRatio)
            {
                pressureScore += HighMemoryScoreContribution;
            }
            else if (MediumMemoryRatio <= memoryRatio)
            {
                pressureScore += MediumMemoryScoreContribution;
            }
            else if (LowMemoryRatio <= memoryRatio)
            {
                pressureScore += LowMemoryScoreContribution;
            }

            if (HighGCRateMultiplier <= gcRateMultiplier)
            {
                pressureScore += HighGCRateScoreContribution;
            }
            else if (1f <= gcRateMultiplier)
            {
                pressureScore += MediumGCRateScoreContribution;
            }

            if (HighGrowthRateMultiplier <= growthRateMultiplier)
            {
                pressureScore += HighGrowthRateScoreContribution;
            }
            else if (1f <= growthRateMultiplier)
            {
                pressureScore += MediumGrowthRateScoreContribution;
            }

            if (CriticalScoreThreshold <= pressureScore)
            {
                return MemoryPressureLevel.Critical;
            }

            if (HighScoreThreshold <= pressureScore)
            {
                return MemoryPressureLevel.High;
            }

            if (MediumScoreThreshold <= pressureScore)
            {
                return MemoryPressureLevel.Medium;
            }

            if (LowScoreThreshold <= pressureScore)
            {
                return MemoryPressureLevel.Low;
            }

            return MemoryPressureLevel.None;
        }

        /// <summary>
        /// Gets or sets whether memory pressure monitoring is enabled.
        /// When disabled, <see cref="CurrentPressure"/> always returns <see cref="MemoryPressureLevel.None"/>.
        /// Default is <c>true</c>.
        /// </summary>
        public static bool Enabled
        {
            get => Volatile.Read(ref _enabled) != 0;
            set => Volatile.Write(ref _enabled, value ? 1 : 0);
        }

        /// <summary>
        /// Gets or sets the memory threshold in bytes above which pressure increases.
        /// Default is 512MB.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Memory pressure levels are calculated as percentages of this threshold:
        /// <list type="bullet">
        ///   <item><description>None: Below 75% of threshold</description></item>
        ///   <item><description>Low: 75-90% of threshold</description></item>
        ///   <item><description>Medium: 90-100% of threshold</description></item>
        ///   <item><description>High: 100-125% of threshold</description></item>
        ///   <item><description>Critical: Above 125% of threshold</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// Set this value based on your target platform's available memory. For mobile platforms,
        /// consider using lower thresholds (128-256MB). For desktop, higher thresholds may be appropriate.
        /// </para>
        /// </remarks>
        public static long MemoryPressureThresholdBytes
        {
            get => Volatile.Read(ref _memoryPressureThresholdBytes);
            set =>
                Volatile.Write(
                    ref _memoryPressureThresholdBytes,
                    0 < value ? value : DefaultMemoryPressureThresholdBytes
                );
        }

        /// <summary>
        /// Gets or sets the minimum interval in seconds between pressure calculations.
        /// Default is 5 seconds.
        /// </summary>
        /// <remarks>
        /// Lower values provide more responsive pressure detection but increase CPU overhead.
        /// Higher values reduce overhead but may miss rapid memory changes.
        /// </remarks>
        public static float CheckIntervalSeconds
        {
            get => Volatile.Read(ref _checkIntervalSeconds);
            set =>
                Volatile.Write(
                    ref _checkIntervalSeconds,
                    0f < value ? value : DefaultCheckIntervalSeconds
                );
        }

        /// <summary>
        /// Gets or sets the GC collection rate threshold (collections per second) that contributes to pressure.
        /// Default is 2.0 (two Gen0 collections per second).
        /// </summary>
        /// <remarks>
        /// Frequent GC collections indicate memory churn and stress even if absolute usage is low.
        /// This threshold helps detect rapid allocation patterns that benefit from pool purging.
        /// </remarks>
        public static float GCCollectionRateThreshold
        {
            get => Volatile.Read(ref _gcCollectionRateThreshold);
            set =>
                Volatile.Write(
                    ref _gcCollectionRateThreshold,
                    0f < value ? value : DefaultGCCollectionRateThreshold
                );
        }

        /// <summary>
        /// Gets or sets the memory growth rate threshold (bytes per second) that contributes to pressure.
        /// Default is 50MB per second.
        /// </summary>
        /// <remarks>
        /// Rapid memory growth indicates potential memory issues even if absolute usage is below threshold.
        /// This helps detect runaway allocation patterns before they become critical.
        /// </remarks>
        public static long MemoryGrowthRateThreshold
        {
            get => Volatile.Read(ref _memoryGrowthRateThreshold);
            set =>
                Volatile.Write(
                    ref _memoryGrowthRateThreshold,
                    0 < value ? value : DefaultMemoryGrowthRateThreshold
                );
        }

        /// <summary>
        /// Gets the current memory pressure level.
        /// </summary>
        /// <remarks>
        /// This property returns the most recently calculated pressure level.
        /// Call <see cref="Update"/> to refresh the calculation if the check interval has elapsed.
        /// When <see cref="Enabled"/> is false, always returns <see cref="MemoryPressureLevel.None"/>.
        /// </remarks>
        public static MemoryPressureLevel CurrentPressure
        {
            get
            {
                if (!Enabled)
                {
                    return MemoryPressureLevel.None;
                }
                return (MemoryPressureLevel)Volatile.Read(ref _currentPressure);
            }
        }

        /// <summary>
        /// Gets the total memory currently in use, as reported by the last check.
        /// </summary>
        public static long LastTotalMemory => Volatile.Read(ref _lastTotalMemory);

        /// <summary>
        /// Gets the Gen0 GC collection count at the time of the last check.
        /// </summary>
        public static int LastGCCount => Volatile.Read(ref _lastGCCount);

        /// <summary>
        /// Updates the memory pressure calculation if the check interval has elapsed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is designed to be called frequently (e.g., on every pool operation).
        /// It internally throttles actual pressure calculations based on <see cref="CheckIntervalSeconds"/>.
        /// </para>
        /// <para>
        /// The calculation considers:
        /// <list type="bullet">
        ///   <item><description>Absolute memory usage vs threshold</description></item>
        ///   <item><description>GC collection rate (rapid collections = higher pressure)</description></item>
        ///   <item><description>Memory growth rate (rapid growth = higher pressure)</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public static void Update()
        {
            if (!Enabled)
            {
                return;
            }

            float currentTime = (float)MonitorStopwatch.Elapsed.TotalSeconds;
            float checkInterval = CheckIntervalSeconds;

            if (currentTime - Volatile.Read(ref _lastCheckTime) < checkInterval)
            {
                return;
            }

            lock (UpdateLock)
            {
                float lastCheck = _lastCheckTime;
                if (currentTime - lastCheck < checkInterval)
                {
                    return;
                }

                CalculatePressure(currentTime, lastCheck);
                _lastCheckTime = currentTime;
            }
        }

        /// <summary>
        /// Forces an immediate recalculation of memory pressure, bypassing the check interval.
        /// </summary>
        /// <returns>The newly calculated pressure level.</returns>
        /// <remarks>
        /// Use this method sparingly as it involves GC queries that have some overhead.
        /// Prefer calling <see cref="Update"/> which respects the throttling interval.
        /// </remarks>
        public static MemoryPressureLevel ForceUpdate()
        {
            if (!Enabled)
            {
                return MemoryPressureLevel.None;
            }

            float currentTime = (float)MonitorStopwatch.Elapsed.TotalSeconds;

            lock (UpdateLock)
            {
                CalculatePressure(currentTime, _lastCheckTime);
                _lastCheckTime = currentTime;
            }

            return CurrentPressure;
        }

        /// <summary>
        /// Resets all monitoring state and settings to defaults.
        /// </summary>
        /// <remarks>
        /// Primarily used for testing. Clears tracked memory values and resets all configuration.
        /// </remarks>
        public static void Reset()
        {
            lock (UpdateLock)
            {
                _enabled = 1;
                _memoryPressureThresholdBytes = DefaultMemoryPressureThresholdBytes;
                _checkIntervalSeconds = DefaultCheckIntervalSeconds;
                _gcCollectionRateThreshold = DefaultGCCollectionRateThreshold;
                _memoryGrowthRateThreshold = DefaultMemoryGrowthRateThreshold;
                _lastTotalMemory = 0;
                _lastGCCount = 0;
                _lastCheckTime = 0f;
                _currentPressure = 0;
            }
        }

        private static void CalculatePressure(float currentTime, float lastCheckTime)
        {
            long totalMemory = GC.GetTotalMemory(false);
            int gcCount = GC.CollectionCount(0);

            long previousMemory = _lastTotalMemory;
            int previousGCCount = _lastGCCount;

            _lastTotalMemory = totalMemory;
            _lastGCCount = gcCount;

            float elapsed = currentTime - lastCheckTime;
            if (elapsed <= 0f)
            {
                elapsed = CheckIntervalSeconds;
            }

            int pressureScore = 0;

            long threshold = MemoryPressureThresholdBytes;
            if (0 < threshold)
            {
                float memoryRatio = (float)totalMemory / threshold;

                if (CriticalMemoryRatio <= memoryRatio)
                {
                    pressureScore += CriticalMemoryScoreContribution;
                }
                else if (HighMemoryRatio <= memoryRatio)
                {
                    pressureScore += HighMemoryScoreContribution;
                }
                else if (MediumMemoryRatio <= memoryRatio)
                {
                    pressureScore += MediumMemoryScoreContribution;
                }
                else if (LowMemoryRatio <= memoryRatio)
                {
                    pressureScore += LowMemoryScoreContribution;
                }
            }

            if (0 < previousGCCount)
            {
                int gcDelta = gcCount - previousGCCount;
                if (0 < gcDelta)
                {
                    float gcRate = gcDelta / elapsed;
                    float gcRateThreshold = GCCollectionRateThreshold;

                    if (gcRateThreshold * HighGCRateMultiplier <= gcRate)
                    {
                        pressureScore += HighGCRateScoreContribution;
                    }
                    else if (gcRateThreshold <= gcRate)
                    {
                        pressureScore += MediumGCRateScoreContribution;
                    }
                }
            }

            if (0 < previousMemory)
            {
                long memoryDelta = totalMemory - previousMemory;
                if (0 < memoryDelta)
                {
                    float growthRate = memoryDelta / elapsed;
                    long growthThreshold = MemoryGrowthRateThreshold;

                    if (growthThreshold * HighGrowthRateMultiplier <= growthRate)
                    {
                        pressureScore += HighGrowthRateScoreContribution;
                    }
                    else if (growthThreshold <= growthRate)
                    {
                        pressureScore += MediumGrowthRateScoreContribution;
                    }
                }
            }

            MemoryPressureLevel level;
            if (CriticalScoreThreshold <= pressureScore)
            {
                level = MemoryPressureLevel.Critical;
            }
            else if (HighScoreThreshold <= pressureScore)
            {
                level = MemoryPressureLevel.High;
            }
            else if (MediumScoreThreshold <= pressureScore)
            {
                level = MemoryPressureLevel.Medium;
            }
            else if (LowScoreThreshold <= pressureScore)
            {
                level = MemoryPressureLevel.Low;
            }
            else
            {
                level = MemoryPressureLevel.None;
            }

            Volatile.Write(ref _currentPressure, (int)level);
        }
    }
}

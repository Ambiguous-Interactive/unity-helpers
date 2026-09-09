// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using Random;
    using UnityEngine;

    /// <summary>
    /// A lightweight time-based cache that recomputes a value after a time-to-live interval expires.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// TimedCache<int> enemyCount = new TimedCache<int>(TimeSpan.FromSeconds(1f), () => FindEnemies().Count);
    /// int cachedValue = enemyCount.GetValue(Time.time);
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">Value type produced by the cache factory.</typeparam>
    /// <remarks>
    /// Use for expensive computations that can be reused for a short period (e.g., path costs, counts, queries).
    /// Optionally introduces a one-time jitter to spread refreshes across frames when many caches exist.
    /// </remarks>
    public sealed class TimedCache<T>
    {
        /// <summary>
        /// Gets the cached value, recomputing if the TTL (plus optional jitter) has elapsed.
        /// </summary>
        public T Value
        {
            get
            {
                if (!_lastRead.HasValue)
                {
                    ResetInternal(consumeJitter: false);
                }
                else
                {
                    float expiration =
                        _cacheTtl + (_shouldUseJitter && !_usedJitter ? _jitterAmount : 0f);
                    if (_lastRead.Value + expiration < CurrentTime)
                    {
                        if (_shouldUseJitter)
                        {
                            _usedJitter = true;
                        }
                        ResetInternal(consumeJitter: false);
                    }
                }

                return _value;
            }
        }

        private float CurrentTime => _timeProvider();

        private readonly Func<T> _valueProducer;
        private readonly float _cacheTtl;

        private float? _lastRead;
        private T _value;

        private bool _usedJitter;
        private readonly bool _shouldUseJitter;
        private readonly float _jitterAmount;
        private readonly Func<float> _timeProvider;

        /// <summary>
        /// Creates a time-based cache.
        /// </summary>
        /// <param name="valueProducer">Factory invoked to recompute the value.</param>
        /// <param name="cacheTtl">Finite nonnegative time to live, in seconds.</param>
        /// <param name="useJitter">If true, applies a single randomized offset up to <paramref name="cacheTtl"/> to the first refresh.</param>
        /// <param name="jitterOverride">Optional initial jitter; negative or nonfinite values are treated as zero.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="valueProducer"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="cacheTtl"/> is negative or nonfinite.</exception>
        public TimedCache(
            Func<T> valueProducer,
            float cacheTtl,
            bool useJitter = false,
            Func<float> timeProvider = null,
            float? jitterOverride = null
        )
        {
            _valueProducer =
                valueProducer ?? throw new ArgumentNullException(nameof(valueProducer));
            if (!(0f <= cacheTtl && cacheTtl <= float.MaxValue))
            {
                throw new ArgumentException(nameof(cacheTtl));
            }

            _cacheTtl = cacheTtl;
            _shouldUseJitter = useJitter;
            float jitter = useJitter
                ? jitterOverride ?? (0f < cacheTtl ? PRNG.Instance.NextFloat(0f, cacheTtl) : 0f)
                : 0f;
            _jitterAmount = float.IsFinite(jitter) ? Mathf.Max(0f, jitter) : 0f;
            _timeProvider = timeProvider ?? (() => Time.time);
        }

        /// <summary>
        /// Forces the cache to recompute the value and resets the TTL timer.
        /// </summary>
        public void Reset()
        {
            ResetInternal(consumeJitter: true);
        }

        private void ResetInternal(bool consumeJitter)
        {
            _value = _valueProducer();
            _lastRead = CurrentTime;
            if (consumeJitter && _shouldUseJitter)
            {
                _usedJitter = true;
            }
        }
    }
}

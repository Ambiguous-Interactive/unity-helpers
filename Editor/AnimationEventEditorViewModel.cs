// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Holds the mutable state for <see cref="AnimationEventEditor"/> so we can
    /// load, edit, and diff animation events without relying on IMGUI.
    /// </summary>
    internal sealed class AnimationEventEditorViewModel
    {
        private const float SwapTimeThreshold = 0.001f;

        private readonly List<AnimationEventItem> _events = new();
        private readonly List<AnimationEvent> _baseline = new();
        private readonly List<ObjectReferenceKeyframe> _referenceCurve = new();
        private readonly List<AnimationClip> _clipFilterBuffer = new();

        public AnimationClip CurrentClip { get; private set; }

        public IReadOnlyList<AnimationEventItem> Events => _events;

        public IReadOnlyList<ObjectReferenceKeyframe> ReferenceCurve => _referenceCurve;

        public float FrameRate { get; private set; }

        public bool FrameRateChanged { get; private set; }

        public int Count => _events.Count;

        public void LoadClip(AnimationClip clip)
        {
            CurrentClip = clip;
            FrameRateChanged = false;
            _events.Clear();
            _baseline.Clear();
            _referenceCurve.Clear();

            if (clip == null)
            {
                FrameRate = 0f;
                return;
            }

            FrameRate = clip.frameRate;
            AnimationEvent[] events = clip.events ?? Array.Empty<AnimationEvent>();
            for (int i = 0; i < events.Length; i++)
            {
                AnimationEvent existing = events[i];
                _events.Add(new AnimationEventItem(existing) { originalIndex = i });
                _baseline.Add(AnimationEventEqualityComparer.Instance.Copy(existing));
            }

            ObjectReferenceKeyframe[] curve = AnimationUtility.GetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve(
                    string.Empty,
                    typeof(SpriteRenderer),
                    UnityExtensions.SpriteBindingProperty
                )
            );
            if (curve != null)
            {
                _referenceCurve.AddRange(curve);
            }

            _referenceCurve.Sort(
                static (lhs, rhs) =>
                {
                    int comparison = lhs.time.CompareTo(rhs.time);
                    if (comparison != 0)
                    {
                        return comparison;
                    }

                    string lhsName =
                        lhs.value == null ? string.Empty : lhs.value.name ?? string.Empty;
                    string rhsName =
                        rhs.value == null ? string.Empty : rhs.value.name ?? string.Empty;
                    return string.Compare(lhsName, rhsName, StringComparison.OrdinalIgnoreCase);
                }
            );
        }

        public AnimationEventItem GetEvent(int index)
        {
            return _events[index];
        }

        public IReadOnlyList<AnimationClip> FilterClips(
            AnimationClip[] clips,
            string searchTerm,
            AnimationClip currentSelection
        )
        {
            _clipFilterBuffer.Clear();

            if (clips == null || clips.Length == 0)
            {
                return _clipFilterBuffer;
            }

            string normalizedSearch = string.IsNullOrWhiteSpace(searchTerm)
                ? string.Empty
                : searchTerm.Trim();

            if (normalizedSearch.Length == 0 || normalizedSearch == "*")
            {
                _clipFilterBuffer.AddRange(clips);
                return _clipFilterBuffer;
            }

            string[] tokens = normalizedSearch.Split(' ');
            using PooledResource<List<string>> searchTermsResource = Buffers<string>.List.Get(
                out List<string> searchTerms
            );
            {
                for (int i = 0; i < tokens.Length; i++)
                {
                    string token = tokens[i];
                    if (string.IsNullOrEmpty(token))
                    {
                        continue;
                    }
                    token = token.Trim();
                    if (token.Length == 0 || token == "*")
                    {
                        continue;
                    }

                    searchTerms.Add(token.ToLowerInvariant());
                }

                if (searchTerms.Count == 0)
                {
                    _clipFilterBuffer.AddRange(clips);
                    return _clipFilterBuffer;
                }

                for (int ci = 0; ci < clips.Length; ci++)
                {
                    AnimationClip clip = clips[ci];
                    if (clip == null)
                    {
                        continue;
                    }

                    bool matches = true;
                    string lowerName =
                        clip.name != null ? clip.name.ToLowerInvariant() : string.Empty;
                    for (int ti = 0; ti < searchTerms.Count; ti++)
                    {
                        if (lowerName.IndexOf(searchTerms[ti], StringComparison.Ordinal) < 0)
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches || clip == currentSelection)
                    {
                        _clipFilterBuffer.Add(clip);
                    }
                }
            }

            return _clipFilterBuffer;
        }

        public AnimationEventItem AddEvent(float time)
        {
            AnimationEventItem item = new(new AnimationEvent { time = time });
            _events.Add(item);
            return item;
        }

        public AnimationEventItem DuplicateEvent(int index)
        {
            AnimationEventItem source = _events[index];
            AnimationEvent duplicate = AnimationEventEqualityComparer.Instance.Copy(
                source.animationEvent
            );
            AnimationEventItem item = new(duplicate);
            _events.Insert(index + 1, item);
            return item;
        }

        public void InsertEvent(int index, AnimationEventItem item)
        {
            _events.Insert(index, item);
        }

        public void RemoveEventAt(int index)
        {
            _events.RemoveAt(index);
        }

        public bool RemoveEvent(AnimationEventItem item)
        {
            return _events.Remove(item);
        }

        public void SortEvents(Comparison<AnimationEventItem> comparison)
        {
            _events.Sort(comparison);
        }

        public bool CanSwapWithPrevious(int index)
        {
            if (index <= 0 || index >= _events.Count)
            {
                return false;
            }

            return AreTimesEquivalent(_events[index - 1], _events[index]);
        }

        public bool CanSwapWithNext(int index)
        {
            if (index < 0 || index >= _events.Count - 1)
            {
                return false;
            }

            return AreTimesEquivalent(_events[index], _events[index + 1]);
        }

        public void MoveEvent(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _events.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(fromIndex));
            }

            int clampedTarget = Mathf.Clamp(toIndex, 0, _events.Count);

            if (fromIndex == clampedTarget)
            {
                return;
            }

            AnimationEventItem item = _events[fromIndex];
            _events.RemoveAt(fromIndex);
            if (clampedTarget > fromIndex)
            {
                clampedTarget--;
            }

            clampedTarget = Mathf.Clamp(clampedTarget, 0, _events.Count);
            _events.Insert(clampedTarget, item);
        }

        public bool TrySwapWithPrevious(int index)
        {
            if (!CanSwapWithPrevious(index))
            {
                return false;
            }

            Swap(index, index - 1);
            return true;
        }

        public bool TrySwapWithNext(int index)
        {
            if (!CanSwapWithNext(index))
            {
                return false;
            }

            Swap(index, index + 1);
            return true;
        }

        public bool TryResetToBaseline(AnimationEventItem item)
        {
            if (item?.originalIndex is not int originalIndex)
            {
                return false;
            }

            if (!TryGetBaseline(originalIndex, out AnimationEvent baseline))
            {
                return false;
            }

            AnimationEventEqualityComparer.Instance.CopyInto(item.animationEvent, baseline);
            return true;
        }

        public bool TryGetBaseline(int originalIndex, out AnimationEvent animationEvent)
        {
            if (originalIndex < 0 || originalIndex >= _baseline.Count)
            {
                animationEvent = null;
                return false;
            }

            animationEvent = _baseline[originalIndex];
            return animationEvent != null;
        }

        public bool HasPendingChanges()
        {
            if (FrameRateChanged)
            {
                return true;
            }

            if (_events.Count != _baseline.Count)
            {
                return true;
            }

            for (int i = 0; i < _events.Count; i++)
            {
                if (
                    !AnimationEventEqualityComparer.Instance.Equals(
                        _baseline[i],
                        _events[i].animationEvent
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        public bool NeedsReordering()
        {
            for (int i = 1; i < _events.Count; i++)
            {
                AnimationEvent previous = _events[i - 1].animationEvent;
                AnimationEvent current = _events[i].animationEvent;
                if (AnimationEventEqualityComparer.Instance.Compare(previous, current) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public AnimationEvent[] BuildEventArray()
        {
            AnimationEvent[] arr = new AnimationEvent[_events.Count];
            for (int i = 0; i < _events.Count; i++)
            {
                arr[i] = _events[i].animationEvent;
            }

            return arr;
        }

        public void SnapshotBaseline()
        {
            _baseline.Clear();
            for (int i = 0; i < _events.Count; i++)
            {
                _baseline.Add(
                    AnimationEventEqualityComparer.Instance.Copy(_events[i].animationEvent)
                );
            }
        }

        public void SetFrameRate(float newFrameRate)
        {
            if (Mathf.Approximately(FrameRate, newFrameRate))
            {
                return;
            }

            FrameRate = newFrameRate;
            FrameRateChanged = true;
        }

        public void ResetFrameRateChanged()
        {
            FrameRateChanged = false;
        }

        private static bool AreTimesEquivalent(AnimationEventItem lhs, AnimationEventItem rhs)
        {
            return Mathf.Abs(lhs.animationEvent.time - rhs.animationEvent.time) < SwapTimeThreshold;
        }

        private void Swap(int lhsIndex, int rhsIndex)
        {
            AnimationEventItem tmp = _events[lhsIndex];
            _events[lhsIndex] = _events[rhsIndex];
            _events[rhsIndex] = tmp;
        }
    }
#endif
}

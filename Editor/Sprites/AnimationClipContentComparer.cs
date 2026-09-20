// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    internal static class AnimationClipContentComparer
    {
        private const float ContentEqualityTolerance = 0f;

        /// <summary>
        /// Compares two loaded clips, including their curves, events, settings, and referenced assets,
        /// without changing either clip.
        /// </summary>
        /// <param name="sourceClip">The source animation clip (may be null).</param>
        /// <param name="destClip">The destination animation clip (may be null).</param>
        /// <returns>True if the animation clips have identical content, false otherwise.</returns>
        internal static bool AreAnimationClipsContentEqual(
            AnimationClip sourceClip,
            AnimationClip destClip
        )
        {
            if (sourceClip == null || destClip == null)
            {
                return false;
            }

            if (!sourceClip.frameRate.Approximately(destClip.frameRate, ContentEqualityTolerance))
            {
                return false;
            }
            if (!sourceClip.length.Approximately(destClip.length, ContentEqualityTolerance))
            {
                return false;
            }
            if (sourceClip.wrapMode != destClip.wrapMode)
            {
                return false;
            }
            if (sourceClip.isLooping != destClip.isLooping)
            {
                return false;
            }
            if (sourceClip.legacy != destClip.legacy)
            {
                return false;
            }

            AnimationClipSettings sourceSettings = AnimationUtility.GetAnimationClipSettings(
                sourceClip
            );
            AnimationClipSettings destSettings = AnimationUtility.GetAnimationClipSettings(
                destClip
            );
            if (!AreAnimationClipSettingsEqual(sourceSettings, destSettings))
            {
                return false;
            }

            AnimationEvent[] sourceEvents = AnimationUtility.GetAnimationEvents(sourceClip);
            AnimationEvent[] destEvents = AnimationUtility.GetAnimationEvents(destClip);
            if (!AreAnimationEventsEqual(sourceEvents, destEvents))
            {
                return false;
            }

            EditorCurveBinding[] sourceFloatBindings = AnimationUtility.GetCurveBindings(
                sourceClip
            );
            EditorCurveBinding[] destFloatBindings = AnimationUtility.GetCurveBindings(destClip);
            if (
                !AreCurveBindingsEqual(sourceClip, destClip, sourceFloatBindings, destFloatBindings)
            )
            {
                return false;
            }

            EditorCurveBinding[] sourceObjBindings =
                AnimationUtility.GetObjectReferenceCurveBindings(sourceClip);
            EditorCurveBinding[] destObjBindings = AnimationUtility.GetObjectReferenceCurveBindings(
                destClip
            );
            if (
                !AreObjectReferenceCurveBindingsEqual(
                    sourceClip,
                    destClip,
                    sourceObjBindings,
                    destObjBindings
                )
            )
            {
                return false;
            }

            return true;
        }

        private static bool AreAnimationClipSettingsEqual(
            AnimationClipSettings a,
            AnimationClipSettings b
        )
        {
            if (a.loopTime != b.loopTime)
            {
                return false;
            }
            if (a.loopBlend != b.loopBlend)
            {
                return false;
            }
            if (!a.cycleOffset.Approximately(b.cycleOffset, ContentEqualityTolerance))
            {
                return false;
            }
            if (a.keepOriginalOrientation != b.keepOriginalOrientation)
            {
                return false;
            }
            if (a.keepOriginalPositionXZ != b.keepOriginalPositionXZ)
            {
                return false;
            }
            if (a.keepOriginalPositionY != b.keepOriginalPositionY)
            {
                return false;
            }
            if (a.heightFromFeet != b.heightFromFeet)
            {
                return false;
            }
            if (a.mirror != b.mirror)
            {
                return false;
            }
            if (!a.startTime.Approximately(b.startTime, ContentEqualityTolerance))
            {
                return false;
            }
            if (!a.stopTime.Approximately(b.stopTime, ContentEqualityTolerance))
            {
                return false;
            }
            return true;
        }

        private static bool AreAnimationEventsEqual(AnimationEvent[] a, AnimationEvent[] b)
        {
            if (a == null && b == null)
            {
                return true;
            }
            if (a == null || b == null)
            {
                return false;
            }
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                AnimationEvent evtA = a[i];
                AnimationEvent evtB = b[i];
                if (!evtA.time.Approximately(evtB.time, ContentEqualityTolerance))
                {
                    return false;
                }
                if (!string.Equals(evtA.functionName, evtB.functionName, StringComparison.Ordinal))
                {
                    return false;
                }
                if (
                    !evtA.floatParameter.Approximately(
                        evtB.floatParameter,
                        ContentEqualityTolerance
                    )
                )
                {
                    return false;
                }
                if (evtA.intParameter != evtB.intParameter)
                {
                    return false;
                }
                if (
                    !string.Equals(
                        evtA.stringParameter,
                        evtB.stringParameter,
                        StringComparison.Ordinal
                    )
                )
                {
                    return false;
                }
                if (
                    !AreObjectReferencesEqual(
                        evtA.objectReferenceParameter,
                        evtB.objectReferenceParameter
                    )
                )
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AreObjectReferencesEqual(UnityEngine.Object a, UnityEngine.Object b)
        {
            if (a == null && b == null)
            {
                return true;
            }
            if (a == null || b == null)
            {
                return false;
            }
            string pathA = AssetDatabase.GetAssetPath(a);
            string pathB = AssetDatabase.GetAssetPath(b);
            if (!string.IsNullOrEmpty(pathA) && !string.IsNullOrEmpty(pathB))
            {
                string nameA = Path.GetFileName(pathA);
                string nameB = Path.GetFileName(pathB);
                return string.Equals(nameA, nameB, StringComparison.Ordinal);
            }
            return ReferenceEquals(a, b);
        }

        private static bool AreCurveBindingsEqual(
            AnimationClip sourceClip,
            AnimationClip destClip,
            EditorCurveBinding[] sourceBindings,
            EditorCurveBinding[] destBindings
        )
        {
            if (sourceBindings == null && destBindings == null)
            {
                return true;
            }
            if (sourceBindings == null || destBindings == null)
            {
                return false;
            }
            if (sourceBindings.Length != destBindings.Length)
            {
                return false;
            }

            Array.Sort(sourceBindings, CompareEditorCurveBinding);
            Array.Sort(destBindings, CompareEditorCurveBinding);

            for (int i = 0; i < sourceBindings.Length; i++)
            {
                EditorCurveBinding srcBinding = sourceBindings[i];
                EditorCurveBinding dstBinding = destBindings[i];

                if (!AreBindingsEqual(srcBinding, dstBinding))
                {
                    return false;
                }

                AnimationCurve srcCurve = AnimationUtility.GetEditorCurve(sourceClip, srcBinding);
                AnimationCurve dstCurve = AnimationUtility.GetEditorCurve(destClip, dstBinding);

                if (!AreAnimationCurvesEqual(srcCurve, dstCurve))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AreObjectReferenceCurveBindingsEqual(
            AnimationClip sourceClip,
            AnimationClip destClip,
            EditorCurveBinding[] sourceBindings,
            EditorCurveBinding[] destBindings
        )
        {
            if (sourceBindings == null && destBindings == null)
            {
                return true;
            }
            if (sourceBindings == null || destBindings == null)
            {
                return false;
            }
            if (sourceBindings.Length != destBindings.Length)
            {
                return false;
            }

            Array.Sort(sourceBindings, CompareEditorCurveBinding);
            Array.Sort(destBindings, CompareEditorCurveBinding);

            for (int i = 0; i < sourceBindings.Length; i++)
            {
                EditorCurveBinding srcBinding = sourceBindings[i];
                EditorCurveBinding dstBinding = destBindings[i];

                if (!AreBindingsEqual(srcBinding, dstBinding))
                {
                    return false;
                }

                ObjectReferenceKeyframe[] srcKeyframes = AnimationUtility.GetObjectReferenceCurve(
                    sourceClip,
                    srcBinding
                );
                ObjectReferenceKeyframe[] dstKeyframes = AnimationUtility.GetObjectReferenceCurve(
                    destClip,
                    dstBinding
                );

                if (!AreObjectReferenceKeyframesEqual(srcKeyframes, dstKeyframes))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AreBindingsEqual(EditorCurveBinding a, EditorCurveBinding b)
        {
            if (!string.Equals(a.path, b.path, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.Equals(a.propertyName, b.propertyName, StringComparison.Ordinal))
            {
                return false;
            }
            if (a.type != b.type)
            {
                return false;
            }
            return true;
        }

        private static int CompareEditorCurveBinding(EditorCurveBinding a, EditorCurveBinding b)
        {
            int pathCompare = string.Compare(a.path, b.path, StringComparison.Ordinal);
            if (pathCompare != 0)
            {
                return pathCompare;
            }
            int propCompare = string.Compare(
                a.propertyName,
                b.propertyName,
                StringComparison.Ordinal
            );
            if (propCompare != 0)
            {
                return propCompare;
            }
            return string.Compare(
                a.type?.FullName ?? "",
                b.type?.FullName ?? "",
                StringComparison.Ordinal
            );
        }

        private static bool AreAnimationCurvesEqual(AnimationCurve a, AnimationCurve b)
        {
            if (a == null && b == null)
            {
                return true;
            }
            if (a == null || b == null)
            {
                return false;
            }
            if (a.preWrapMode != b.preWrapMode)
            {
                return false;
            }
            if (a.postWrapMode != b.postWrapMode)
            {
                return false;
            }
            if (a.length != b.length)
            {
                return false;
            }

            Keyframe[] keysA = a.keys;
            Keyframe[] keysB = b.keys;
            if (keysA.Length != keysB.Length)
            {
                return false;
            }

            for (int i = 0; i < keysA.Length; i++)
            {
                Keyframe kA = keysA[i];
                Keyframe kB = keysB[i];
                if (!kA.time.Approximately(kB.time, ContentEqualityTolerance))
                {
                    return false;
                }
                if (!kA.value.Approximately(kB.value, ContentEqualityTolerance))
                {
                    return false;
                }
                if (!kA.inTangent.Approximately(kB.inTangent, ContentEqualityTolerance))
                {
                    return false;
                }
                if (!kA.outTangent.Approximately(kB.outTangent, ContentEqualityTolerance))
                {
                    return false;
                }
                if (!kA.inWeight.Approximately(kB.inWeight, ContentEqualityTolerance))
                {
                    return false;
                }
                if (!kA.outWeight.Approximately(kB.outWeight, ContentEqualityTolerance))
                {
                    return false;
                }
                if (kA.weightedMode != kB.weightedMode)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AreObjectReferenceKeyframesEqual(
            ObjectReferenceKeyframe[] a,
            ObjectReferenceKeyframe[] b
        )
        {
            if (a == null && b == null)
            {
                return true;
            }
            if (a == null || b == null)
            {
                return false;
            }
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                ObjectReferenceKeyframe kA = a[i];
                ObjectReferenceKeyframe kB = b[i];
                if (!kA.time.Approximately(kB.time, ContentEqualityTolerance))
                {
                    return false;
                }
                if (!AreObjectReferencesEqual(kA.value, kB.value))
                {
                    return false;
                }
            }
            return true;
        }
    }
#endif
}

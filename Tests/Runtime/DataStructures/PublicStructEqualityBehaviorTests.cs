// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    public sealed class PublicStructEqualityBehaviorTests
    {
        private static int _hashSink;
        private static bool _equalitySink;

        private static IEnumerable<TestCaseData> SurrogateFieldCases()
        {
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate
                            {
                                weight0 = 1,
                            }
                        )
                )
            ).SetName("BoneWeightSurrogateWeight0ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate
                            {
                                weight1 = 1,
                            }
                        )
                )
            ).SetName("BoneWeightSurrogateWeight1ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate
                            {
                                weight2 = 1,
                            }
                        )
                )
            ).SetName("BoneWeightSurrogateWeight2ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate
                            {
                                weight3 = 1,
                            }
                        )
                )
            ).SetName("BoneWeightSurrogateWeight3ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate
                            {
                                boneIndex0 = 1,
                            }
                        )
                )
            ).SetName("BoneWeightSurrogateBoneIndex0ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate
                            {
                                boneIndex1 = 1,
                            }
                        )
                )
            ).SetName("BoneWeightSurrogateBoneIndex1ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate
                            {
                                boneIndex2 = 1,
                            }
                        )
                )
            ).SetName("BoneWeightSurrogateBoneIndex2ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoneWeightSurrogate
                            {
                                boneIndex3 = 1,
                            }
                        )
                )
            ).SetName("BoneWeightSurrogateBoneIndex3ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate
                            {
                                time = 1,
                            }
                        )
                )
            ).SetName("KeyframeSurrogateTimeParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate
                            {
                                value = 1,
                            }
                        )
                )
            ).SetName("KeyframeSurrogateValueParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate
                            {
                                inTangent = 1,
                            }
                        )
                )
            ).SetName("KeyframeSurrogateInTangentParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate
                            {
                                outTangent = 1,
                            }
                        )
                )
            ).SetName("KeyframeSurrogateOutTangentParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate
                            {
                                inWeight = 1,
                            }
                        )
                )
            ).SetName("KeyframeSurrogateInWeightParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate
                            {
                                outWeight = 1,
                            }
                        )
                )
            ).SetName("KeyframeSurrogateOutWeightParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.KeyframeSurrogate
                            {
                                weightedMode = 1,
                            }
                        )
                )
            ).SetName("KeyframeSurrogateWeightedModeParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m00 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM00ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m10 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM10ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m20 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM20ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m30 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM30ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m01 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM01ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m11 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM11ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m21 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM21ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m31 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM31ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m02 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM02ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m12 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM12ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m22 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM22ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m32 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM32ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m03 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM03ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m13 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM13ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m23 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM23ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Matrix4x4Surrogate
                            {
                                m33 = 1,
                            }
                        )
                )
            ).SetName("Matrix4x4SurrogateM33ParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2Surrogate
                            {
                                x = 1,
                            }
                        )
                )
            ).SetName("Vector2SurrogateXParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2Surrogate
                            {
                                y = 1,
                            }
                        )
                )
            ).SetName("Vector2SurrogateYParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3Surrogate
                            {
                                x = 1,
                            }
                        )
                )
            ).SetName("Vector3SurrogateXParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3Surrogate
                            {
                                y = 1,
                            }
                        )
                )
            ).SetName("Vector3SurrogateYParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3Surrogate
                            {
                                z = 1,
                            }
                        )
                )
            ).SetName("Vector3SurrogateZParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate
                            {
                                x = 1,
                            }
                        )
                )
            ).SetName("QuaternionSurrogateXParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate
                            {
                                y = 1,
                            }
                        )
                )
            ).SetName("QuaternionSurrogateYParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate
                            {
                                z = 1,
                            }
                        )
                )
            ).SetName("QuaternionSurrogateZParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.QuaternionSurrogate
                            {
                                w = 1,
                            }
                        )
                )
            ).SetName("QuaternionSurrogateWParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate
                            {
                                r = 1,
                            }
                        )
                )
            ).SetName("ColorSurrogateRParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate
                            {
                                g = 1,
                            }
                        )
                )
            ).SetName("ColorSurrogateGParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate
                            {
                                b = 1,
                            }
                        )
                )
            ).SetName("ColorSurrogateBParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ColorSurrogate
                            {
                                a = 1,
                            }
                        )
                )
            ).SetName("ColorSurrogateAParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate
                            {
                                r = 1,
                            }
                        )
                )
            ).SetName("Color32SurrogateRParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate
                            {
                                g = 1,
                            }
                        )
                )
            ).SetName("Color32SurrogateGParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate
                            {
                                b = 1,
                            }
                        )
                )
            ).SetName("Color32SurrogateBParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Color32Surrogate
                            {
                                a = 1,
                            }
                        )
                )
            ).SetName("Color32SurrogateAParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate
                            {
                                x = 1,
                            }
                        )
                )
            ).SetName("RectSurrogateXParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate
                            {
                                y = 1,
                            }
                        )
                )
            ).SetName("RectSurrogateYParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate
                            {
                                width = 1,
                            }
                        )
                )
            ).SetName("RectSurrogateWidthParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.RectSurrogate
                            {
                                height = 1,
                            }
                        )
                )
            ).SetName("RectSurrogateHeightParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate
                            {
                                x = 1,
                            }
                        )
                )
            ).SetName("RectIntSurrogateXParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate
                            {
                                y = 1,
                            }
                        )
                )
            ).SetName("RectIntSurrogateYParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate
                            {
                                width = 1,
                            }
                        )
                )
            ).SetName("RectIntSurrogateWidthParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.RectIntSurrogate
                            {
                                height = 1,
                            }
                        )
                )
            ).SetName("RectIntSurrogateHeightParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate
                            {
                                cx = 1,
                            }
                        )
                )
            ).SetName("BoundsSurrogateCxParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate
                            {
                                cy = 1,
                            }
                        )
                )
            ).SetName("BoundsSurrogateCyParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate
                            {
                                cz = 1,
                            }
                        )
                )
            ).SetName("BoundsSurrogateCzParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate
                            {
                                sx = 1,
                            }
                        )
                )
            ).SetName("BoundsSurrogateSxParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate
                            {
                                sy = 1,
                            }
                        )
                )
            ).SetName("BoundsSurrogateSyParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsSurrogate
                            {
                                sz = 1,
                            }
                        )
                )
            ).SetName("BoundsSurrogateSzParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate
                            {
                                px = 1,
                            }
                        )
                )
            ).SetName("BoundsIntSurrogatePxParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate
                            {
                                py = 1,
                            }
                        )
                )
            ).SetName("BoundsIntSurrogatePyParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate
                            {
                                pz = 1,
                            }
                        )
                )
            ).SetName("BoundsIntSurrogatePzParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate
                            {
                                sx = 1,
                            }
                        )
                )
            ).SetName("BoundsIntSurrogateSxParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate
                            {
                                sy = 1,
                            }
                        )
                )
            ).SetName("BoundsIntSurrogateSyParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.BoundsIntSurrogate
                            {
                                sz = 1,
                            }
                        )
                )
            ).SetName("BoundsIntSurrogateSzParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2IntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2IntSurrogate
                            {
                                x = 1,
                            }
                        )
                )
            ).SetName("Vector2IntSurrogateXParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2IntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector2IntSurrogate
                            {
                                y = 1,
                            }
                        )
                )
            ).SetName("Vector2IntSurrogateYParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3IntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3IntSurrogate
                            {
                                x = 1,
                            }
                        )
                )
            ).SetName("Vector3IntSurrogateXParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3IntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3IntSurrogate
                            {
                                y = 1,
                            }
                        )
                )
            ).SetName("Vector3IntSurrogateYParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3IntSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector3IntSurrogate
                            {
                                z = 1,
                            }
                        )
                )
            ).SetName("Vector3IntSurrogateZParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ResolutionSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ResolutionSurrogate
                            {
                                width = 1,
                            }
                        )
                )
            ).SetName("ResolutionSurrogateWidthParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ResolutionSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ResolutionSurrogate
                            {
                                height = 1,
                            }
                        )
                )
            ).SetName("ResolutionSurrogateHeightParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ResolutionSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ResolutionSurrogate
                            {
                                refreshRate = 1,
                            }
                        )
                )
            ).SetName("ResolutionSurrogateRefreshRateParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate
                            {
                                length = 1,
                            }
                        )
                )
            ).SetName("ParabolaSurrogateLengthParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate
                            {
                                a = 1,
                            }
                        )
                )
            ).SetName("ParabolaSurrogateAParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate
                            {
                                b = 1,
                            }
                        )
                )
            ).SetName("ParabolaSurrogateBParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ParabolaSurrogate
                            {
                                maxHeight = 1,
                            }
                        )
                )
            ).SetName("ParabolaSurrogateMaxHeightParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ImmutableBitSetSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ImmutableBitSetSurrogate
                            {
                                bits = new ulong[] { 1 },
                            }
                        )
                )
            ).SetName("ImmutableBitSetSurrogateBitsParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.ImmutableBitSetSurrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.ImmutableBitSetSurrogate
                            {
                                capacity = 1,
                            }
                        )
                )
            ).SetName("ImmutableBitSetSurrogateCapacityParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate
                            {
                                x = 1,
                            }
                        )
                )
            ).SetName("Vector4SurrogateXParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate
                            {
                                y = 1,
                            }
                        )
                )
            ).SetName("Vector4SurrogateYParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate
                            {
                                z = 1,
                            }
                        )
                )
            ).SetName("Vector4SurrogateZParticipatesInEquality");
            yield return new TestCaseData(
                (Action)(
                    () =>
                        AssertDifferent(
                            default(global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate),
                            new global::WallstopStudios.UnityHelpers.Core.Serialization.Vector4Surrogate
                            {
                                w = 1,
                            }
                        )
                )
            ).SetName("Vector4SurrogateWParticipatesInEquality");
        }

        private static void AssertDifferent<T>(T first, T second)
            where T : struct, IEquatable<T>
        {
            Assert.That(first.Equals(second), Is.False);
            Assert.That(second.Equals(first), Is.False);
            Assert.That(EqualityComparer<T>.Default.Equals(first, second), Is.False);
            Assert.That(first.Equals((object)second), Is.False);
            HashSet<T> values = new() { first, second };
            Assert.That(values.Count, Is.EqualTo(2));
        }

        private static void AssertEqual<T>(T first, T second)
            where T : struct, IEquatable<T>
        {
            Assert.That(first.Equals(second), Is.True);
            Assert.That(second.Equals(first), Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first.Equals((object)second), Is.True);
            Assert.That(EqualityComparer<T>.Default.Equals(first, second), Is.True);
            HashSet<T> values = new() { first, second };
            Assert.That(values.Count, Is.EqualTo(1));
        }

        [TestCaseSource(nameof(SurrogateFieldCases))]
        public void EverySurrogateFieldParticipates(Action assertion)
        {
            assertion();
        }

        [Test]
        public void FloatingPointValuesPreserveNaNAndSignedZeroLaws()
        {
            float alternateNaN = BitConverter.ToSingle(BitConverter.GetBytes(0x7fc00001), 0);
            Vector4Surrogate first = new()
            {
                x = float.NaN,
                y = -0f,
                z = float.PositiveInfinity,
                w = float.NegativeInfinity,
            };
            Vector4Surrogate second = new()
            {
                x = alternateNaN,
                y = 0f,
                z = float.PositiveInfinity,
                w = float.NegativeInfinity,
            };
            AssertEqual(first, second);
            AssertEqual(second, first);
            AssertEqual(first, first);
            Vector4Surrogate third = second;
            Assert.That(
                first.Equals(second) && second.Equals(third) && first.Equals(third),
                Is.True
            );
            second.z = float.NegativeInfinity;
            AssertDifferent(first, second);
        }

        [Test]
        public void ArraysKeepReferenceEqualityInSurrogatesAndMasks()
        {
            ulong[] bits = { 1, 2 };
            ImmutableBitSetSurrogate first = new() { bits = bits, capacity = 2 };
            ImmutableBitSetSurrogate copy = new() { bits = bits, capacity = 2 };
            AssertEqual(first, copy);
            AssertDifferent(
                first,
                new ImmutableBitSetSurrogate { bits = new ulong[] { 1, 2 }, capacity = 2 }
            );
            bool[] pixels = { true, false };
            SpriteAlphaMask mask = new(pixels, 2, 1, Vector2.zero, Vector2.one);
            AssertEqual(mask, new SpriteAlphaMask(pixels, 2, 1, Vector2.zero, Vector2.one));
            AssertDifferent(
                mask,
                new SpriteAlphaMask(new[] { true, false }, 2, 1, Vector2.zero, Vector2.one)
            );
            AssertDifferent(mask, new SpriteAlphaMask(pixels, 1, 1, Vector2.zero, Vector2.one));
            AssertDifferent(mask, new SpriteAlphaMask(pixels, 2, 2, Vector2.zero, Vector2.one));
            AssertDifferent(mask, new SpriteAlphaMask(pixels, 2, 1, Vector2.one, Vector2.one));
            AssertDifferent(mask, new SpriteAlphaMask(pixels, 2, 1, Vector2.zero, Vector2.zero));
        }

        [Test]
        public void CacheOptionsCompareConfigurationAndDelegateValues()
        {
            Func<int, int> loader = value => value + 1;
            CacheOptions<int, int> first = new() { MaximumSize = 12, Loader = loader };
            CacheOptions<int, int> second = first;
            AssertEqual(first, second);
            second.MaximumSize = 13;
            AssertDifferent(first, second);
            second = first;
            second.Loader = value => value + 2;
            AssertDifferent(first, second);
            second = first;
            second.KeyComparer = EqualityComparer<int>.Default;
            AssertDifferent(first, second);
        }

        [Test]
        public void RandomEqualityIncludesBufferedStateAndDoesNotDraw()
        {
            NativePcgRandom first = new(123);
            NativePcgRandom second = first;
            AssertEqual(first, second);
            Assert.That(first.NextUint(), Is.EqualTo(second.NextUint()));
            NativePcgRandom before = first;
            first.NextBool();
            AssertDifferent(before, first);
            second = first;
            AssertEqual(first, second);
            Assert.That(first.NextBool(), Is.EqualTo(second.NextBool()));
        }

        [Test]
        public void WarmTypedEqualityAndHashesAvoidOuterBoxing()
        {
            AllocationProbe.IgnoreWhenUnmeasurable();
            Vector4Surrogate value = new()
            {
                x = 1,
                y = 2,
                z = 3,
                w = 4,
            };
            Vector4Surrogate copy = value;
            EqualityComparer<Vector4Surrogate> comparer =
                EqualityComparer<Vector4Surrogate>.Default;
            _hashSink = comparer.GetHashCode(value);
            _equalitySink = comparer.Equals(value, copy);
            using DisposableScope scope = DisposableScope.Create(() => { });
            DisposableScope scopeCopy = scope;
            EqualityComparer<DisposableScope> scopes = EqualityComparer<DisposableScope>.Default;
            _hashSink ^= scopes.GetHashCode(scope);
            _equalitySink = scopes.Equals(scope, scopeCopy);
            GCAssert.DoesNotAllocate(() =>
            {
                for (int index = 0; index < 128; ++index)
                {
                    _hashSink ^= comparer.GetHashCode(value);
                    _equalitySink = comparer.Equals(value, copy);
                    _hashSink ^= scopes.GetHashCode(scope);
                    _equalitySink = scopes.Equals(scope, scopeCopy);
                }
            });
            Assert.That(_equalitySink, Is.True);
        }

        [Test]
        public void ScopeCopiesKeepIdentityBeforeAndAfterDisposal()
        {
            int calls = 0;
            DisposableScope first = DisposableScope.Create(() => ++calls);
            DisposableScope copy = first;
            DisposableScope another = DisposableScope.Create(() => ++calls);
            try
            {
                int originalHash = first.GetHashCode();
                AssertEqual(first, copy);
                AssertDifferent(first, another);
                Assert.That(calls, Is.Zero);
                Assert.That(first.IsHeld, Is.True);
                copy.Dispose();
                AssertEqual(first, copy);
                Assert.That(first.GetHashCode(), Is.EqualTo(originalHash));
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(first.IsHeld, Is.False);
                first.Dispose();
                Assert.That(calls, Is.EqualTo(1));
            }
            finally
            {
                first.Dispose();
                another.Dispose();
            }
        }

        [Test]
        public void ValueEnumeratorEqualityDoesNotAdvanceOrDispose()
        {
            int[] source = { 3, 5 };
            ValueEnumerable<int> view = ValueEnumerable<int>.RequireSource(source);
            ValueEnumerator<int> first = view.GetEnumerator();
            ValueEnumerator<int> copy = first;
            AssertEqual(first, copy);
            Assert.That(first.MoveNext(), Is.True);
            AssertDifferent(first, copy);
            Assert.That(copy.MoveNext(), Is.True);
            AssertEqual(first, copy);
            Assert.That(first.Current, Is.EqualTo(3));
            Assert.That(copy.Current, Is.EqualTo(3));
            first.Dispose();
            copy.Dispose();
        }
    }
}

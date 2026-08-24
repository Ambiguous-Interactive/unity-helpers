// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// A struct that caches a derived value in a field has two ways to come into existence, and only
    /// one of them runs a constructor. <c>default(T)</c>, an element of a freshly allocated array, a
    /// dictionary's miss value and a deserializer's uninitialized instance all arrive with every
    /// field zeroed -- including the cache. If the type then answers <see cref="object.Equals(object)"/>
    /// or <see cref="object.GetHashCode"/> from that cache, a zero-initialized instance disagrees with
    /// the instance its own components describe, and a set holds the same logical value twice.
    /// </summary>
    /// <remarks>
    /// This is a sweep rather than a per-type test on purpose: the failure mode is a property of the
    /// shape, so any future struct that caches a hash has to satisfy it without anyone remembering to
    /// add a case here.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ZeroInitializedStructIdentityTests
    {
        [Test]
        public void EveryStructAgreesWithItsOwnZeroConstruction()
        {
            List<string> failures = new();
            List<string> checkedTypes = new();
            List<string> rejectedZero = new();

            foreach (Type type in CandidateStructs())
            {
                ConstructorInfo allZero = ZeroableConstructor(type);
                if (allZero == null)
                {
                    continue;
                }

                object zeroInitialized = Activator.CreateInstance(type);
                object constructed;
                try
                {
                    constructed = allZero.Invoke(ZeroArguments(allZero));
                }
                catch (TargetInvocationException invocation)
                    when (invocation.InnerException is ArgumentException)
                {
                    /*
                        The all-zero arguments are not a value this type admits -- Parabola rejects a
                        zero height and a zero length, for instance. default(T) then has nothing to be
                        consistent with, so there is no obligation to check. Recorded rather than
                        swallowed, so a type that starts rejecting its own zero is visible here
                        instead of quietly leaving the sweep.
                    */
                    rejectedZero.Add(type.Name);
                    continue;
                }
                catch (TargetInvocationException invocation)
                {
                    failures.Add(
                        $"{type.Name}: the all-zero construction threw {invocation.InnerException?.GetType().Name}"
                    );
                    continue;
                }

                checkedTypes.Add(type.Name);

                if (!zeroInitialized.Equals(constructed))
                {
                    failures.Add(
                        $"{type.Name}: default(T).Equals(new T(0, ...)) is false, so a zero-initialized "
                            + "instance is not the value its own components describe"
                    );
                }

                if (!constructed.Equals(zeroInitialized))
                {
                    failures.Add($"{type.Name}: equality is not symmetric across the two origins");
                }

                if (zeroInitialized.GetHashCode() != constructed.GetHashCode())
                {
                    failures.Add(
                        $"{type.Name}: default(T) hashes {zeroInitialized.GetHashCode()} but "
                            + $"new T(0, ...) hashes {constructed.GetHashCode()}, so equal values land "
                            + "in different buckets"
                    );
                }
            }

            /*
                A sweep that discovers nothing reads exactly like a clean run, so what it matched is
                asserted before what it found.
            */
            Assert.That(
                checkedTypes,
                Has.Count.GreaterThanOrEqualTo(5),
                $"Discovered only {checkedTypes.Count} zero-constructible structs; the sweep is "
                    + "matching less than it did."
            );
            string[] known =
            {
                nameof(FastVector2Int),
                nameof(FastVector3Int),
                nameof(CacheStatistics),
                nameof(PoolStatistics),
            };
            foreach (string expected in known)
            {
                CollectionAssert.Contains(checkedTypes, expected);
            }

            CollectionAssert.AreEquivalent(
                new[] { nameof(Parabola) },
                rejectedZero,
                "The set of types that reject their own all-zero construction changed; a new one "
                    + "needs a deliberate decision about whether default(T) is a value it admits."
            );

            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        }

        /// <summary>
        /// A set must not hold the same logical value twice. This is the consequence a caller
        /// actually meets, asserted separately from the contract that causes it.
        /// </summary>
        [Test]
        public void ASetHoldsTheOriginOnce()
        {
            FastVector2Int[] cellStorage = new FastVector2Int[1];
            HashSet<FastVector2Int> cells = new()
            {
                default,
                new FastVector2Int(0, 0),
                FastVector2Int.zero,
                cellStorage[0],
            };
            Assert.AreEqual(1, cells.Count, "The origin cell occupies more than one slot.");

            FastVector3Int[] voxelStorage = new FastVector3Int[1];
            HashSet<FastVector3Int> voxels = new()
            {
                default,
                new FastVector3Int(0, 0, 0),
                FastVector3Int.zero,
                voxelStorage[0],
            };
            Assert.AreEqual(1, voxels.Count, "The origin voxel occupies more than one slot.");
        }

        /// <summary>
        /// A dictionary keyed by a zero-initialized origin must find the value stored under the
        /// constructed one, in both directions.
        /// </summary>
        [Test]
        public void ADictionaryFindsTheOriginFromEitherOrigin()
        {
            Dictionary<FastVector2Int, int> byConstructed = new()
            {
                [new FastVector2Int(0, 0)] = 7,
            };
            Assert.IsTrue(byConstructed.TryGetValue(default, out int fromDefault));
            Assert.AreEqual(7, fromDefault);

            Dictionary<FastVector2Int, int> byDefault = new() { [default] = 9 };
            Assert.IsTrue(byDefault.TryGetValue(new FastVector2Int(0, 0), out int fromConstructed));
            Assert.AreEqual(9, fromConstructed);
        }

        /// <summary>
        /// A zero-initialized vector writes no bytes at all -- every member is at its default, so
        /// the encoder omits all three fields -- while the value it reads back is built through the
        /// constructor. The round trip therefore crosses exactly the boundary this fixture is about,
        /// and before the origin had one identity it produced a value unequal to what went in.
        /// </summary>
        [Test]
        public void AZeroInitializedVectorSurvivesItsOwnRoundTrip()
        {
            byte[] encoded = Serializer.ProtoSerialize(default(FastVector2Int));
            Assert.AreEqual(
                0,
                encoded.Length,
                "The fixture no longer exercises the empty-payload path."
            );
            Assert.AreEqual(
                default(FastVector2Int),
                Serializer.ProtoDeserialize<FastVector2Int>(encoded)
            );

            byte[] encodedVoxel = Serializer.ProtoSerialize(default(FastVector3Int));
            Assert.AreEqual(0, encodedVoxel.Length);
            Assert.AreEqual(
                default(FastVector3Int),
                Serializer.ProtoDeserialize<FastVector3Int>(encodedVoxel)
            );
        }

        /// <summary>
        /// Equality that tolerates a float difference must not hash that float, or two values that
        /// compare equal hash differently and a set holds both.
        /// </summary>
        [Test]
        public void ToleranceEqualStatisticsShareAHash()
        {
            PoolStatistics exact = new(
                currentSize: 4,
                peakSize: 9,
                rentCount: 100,
                returnCount: 98,
                purgeCount: 2,
                idleTimeoutPurges: 1,
                capacityPurges: 1,
                rentalsPerMinute: 1.0f
            );
            PoolStatistics withinTolerance = new(
                currentSize: 4,
                peakSize: 9,
                rentCount: 100,
                returnCount: 98,
                purgeCount: 2,
                idleTimeoutPurges: 1,
                capacityPurges: 1,
                rentalsPerMinute: 1.0005f
            );

            Assert.IsTrue(
                exact.Equals(withinTolerance),
                "The fixture no longer exercises tolerance equality."
            );
            Assert.AreEqual(
                exact.GetHashCode(),
                withinTolerance.GetHashCode(),
                "Two snapshots that compare equal hash differently, so a set holds both."
            );
            Assert.AreEqual(1, new HashSet<PoolStatistics> { exact, withinTolerance }.Count);
        }

        private static IEnumerable<Type> CandidateStructs()
        {
            foreach (Type type in RuntimeTypes())
            {
                if (
                    !type.IsValueType
                    || type.IsEnum
                    || type.IsPrimitive
                    || type.IsGenericTypeDefinition
                    || type.ContainsGenericParameters
                )
                {
                    continue;
                }

                /*
                    A struct that inherits ValueType's structural equality cannot disagree with
                    itself; only a hand-written GetHashCode can.
                */
                if (
                    type.GetMethod(
                        nameof(GetHashCode),
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        Type.EmptyTypes,
                        modifiers: null
                    )?.DeclaringType != type
                )
                {
                    continue;
                }

                yield return type;
            }
        }

        private static IEnumerable<Type> RuntimeTypes()
        {
            Assembly runtime = typeof(IRandom).Assembly;
            try
            {
                return runtime.GetTypes();
            }
            catch (ReflectionTypeLoadException partial)
            {
                return partial.Types.Where(type => type != null);
            }
        }

        /// <summary>
        /// The widest constructor whose every parameter has a meaningful zero, so that passing zero
        /// for all of them describes exactly the value <c>default(T)</c> already holds.
        /// </summary>
        private static ConstructorInfo ZeroableConstructor(Type type)
        {
            ConstructorInfo widest = null;
            foreach (
                ConstructorInfo candidate in type.GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public
                )
            )
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length == 0 || !parameters.All(p => IsZeroable(p.ParameterType)))
                {
                    continue;
                }

                if (widest == null || widest.GetParameters().Length < parameters.Length)
                {
                    widest = candidate;
                }
            }

            return widest;
        }

        private static bool IsZeroable(Type type)
        {
            return type.IsEnum
                || (type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr))
                || type == typeof(decimal);
        }

        private static object[] ZeroArguments(ConstructorInfo constructor)
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            object[] arguments = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                arguments[i] = parameterType.IsEnum
                    ? Enum.ToObject(parameterType, 0)
                    : Convert.ChangeType(0, parameterType);
            }

            return arguments;
        }
    }
}

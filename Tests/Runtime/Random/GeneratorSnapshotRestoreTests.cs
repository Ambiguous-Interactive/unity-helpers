// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// Every generator must resume its exact stream from its own <see cref="RandomState"/> snapshot,
    /// because that snapshot is what a save file stores. A generator that answers
    /// <see cref="IRandom.InternalState"/> with less than its live state restores something that looks
    /// plausible and diverges immediately, which is the worst shape a determinism bug can take: the load
    /// succeeds and the run is different. There are no exceptions to this: <see cref="UnityRandom"/>
    /// was one until its snapshot learned to carry <c>UnityEngine.Random</c>'s position.
    /// </summary>
    /// <remarks>
    /// The snapshot is taken mid-stream rather than at construction. Restoring a freshly seeded generator
    /// only proves the seed round-trips; restoring one that has already advanced is what a save actually
    /// does, and it is the case a seed-only snapshot fails.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class GeneratorSnapshotRestoreTests
    {
        private const int DrawsBeforeSnapshot = 37;
        private const int ComparedDraws = 64;

        [Test]
        public void EveryGeneratorResumesItsStreamFromItsOwnSnapshot()
        {
            List<string> diverged = new();
            List<string> resumed = new();

            foreach (Type type in GeneratorTypes())
            {
                ConstructorInfo parameterless = type.GetConstructor(Type.EmptyTypes);
                ConstructorInfo fromSnapshot = type.GetConstructor(new[] { typeof(RandomState) });
                Assert.IsTrue(
                    fromSnapshot != null,
                    $"{type.Name} has no {nameof(RandomState)} constructor, so its state cannot be restored at all."
                );
                if (parameterless == null)
                {
                    continue;
                }

                IRandom generator = (IRandom)parameterless.Invoke(null);
                for (int i = 0; i < DrawsBeforeSnapshot; i++)
                {
                    generator.NextUint();
                }

                RandomState snapshot = generator.InternalState;
                uint[] expected = new uint[ComparedDraws];
                for (int i = 0; i < ComparedDraws; i++)
                {
                    expected[i] = generator.NextUint();
                }

                IRandom restored = (IRandom)fromSnapshot.Invoke(new object[] { snapshot });
                int firstMismatch = -1;
                for (int i = 0; i < ComparedDraws; i++)
                {
                    if (restored.NextUint() != expected[i])
                    {
                        firstMismatch = i;
                        break;
                    }
                }

                if (firstMismatch < 0)
                {
                    resumed.Add(type.Name);
                }
                else
                {
                    diverged.Add(
                        $"{type.Name} diverges at draw {firstMismatch} of {ComparedDraws}"
                    );
                }
            }

            /*
                A sweep that discovers nothing reads exactly like a clean run, so what it matched is
                asserted before what it found.
            */
            Assert.That(
                resumed.Count + diverged.Count,
                Is.GreaterThanOrEqualTo(15),
                "The sweep is matching fewer generators than the package ships."
            );
            Assert.IsEmpty(diverged, string.Join(Environment.NewLine, diverged));
        }

        private static IEnumerable<Type> GeneratorTypes()
        {
            Assembly runtime = typeof(IRandom).Assembly;
            Type[] types;
            try
            {
                types = runtime.GetTypes();
            }
            catch (ReflectionTypeLoadException partial)
            {
                types = partial.Types.Where(type => type != null).ToArray();
            }

            foreach (Type type in types)
            {
                if (
                    type.IsAbstract
                    || type.IsInterface
                    || type.IsGenericTypeDefinition
                    || !typeof(IRandom).IsAssignableFrom(type)
                )
                {
                    continue;
                }

                yield return type;
            }
        }
    }
}

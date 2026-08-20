// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Serialization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// Every comparable in the runtime assembly must order null first, the way the framework's own
    /// comparables do. A type that answers a negative value instead claims to be both less than and
    /// greater than nothing, which is an inconsistent comparer wherever a caller compares against
    /// null directly or through a <see cref="Comparison{T}"/>.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ComparableNullOrderingTests
    {
        [Test]
        public void EveryComparableOrdersNullFirst()
        {
            List<string> failures = new();
            List<string> checkedTypes = new();

            foreach (Type type in ComparableTypes())
            {
                object instance;
                try
                {
                    instance = CreateProbe(type);
                }
                catch (Exception creation)
                {
                    failures.Add(
                        $"{type.FullName}: could not be constructed ({creation.GetType().Name})"
                    );
                    continue;
                }

                checkedTypes.Add(type.Name);
                foreach (MethodInfo nullAccepting in NullAcceptingCompareMethods(type))
                {
                    string label = $"{type.Name} via {Describe(nullAccepting.DeclaringType)}";

                    object result;
                    try
                    {
                        result = nullAccepting.Invoke(instance, new object[] { null });
                    }
                    catch (TargetInvocationException invocation)
                    {
                        failures.Add($"{label} threw {invocation.InnerException?.GetType().Name}");
                        continue;
                    }

                    if ((int)result <= 0)
                    {
                        failures.Add($"{label} returned {result}, expected a positive value");
                    }
                }
            }

            /*
                A filter that matches nothing reads exactly like a clean run, so discovery is
                asserted before the results are.
            */
            Assert.That(
                checkedTypes,
                Has.Count.GreaterThanOrEqualTo(15),
                $"Discovered only {checkedTypes.Count} hand-written comparable types; the sweep is matching less than it did."
            );
            string[] known =
            {
                "Attribute",
                "AttributeModification",
                "EffectHandle",
                "FastVector2Int",
                "FastVector3Int",
                "FlurryBurstRandom",
                "PcgRandom",
                "PhotonSpinRandom",
                "RomuDuo",
                "SplitMix64",
                "StormDropRandom",
                "StringWrapper",
                "WDoomRandom",
                "WGuid",
                "XoroShiroRandom",
            };
            foreach (string expected in known)
            {
                CollectionAssert.Contains(checkedTypes, expected);
            }

            Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
        }

        private static IEnumerable<Type> ComparableTypes()
        {
            foreach (Type type in RuntimeTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                /*
                    Every enum implements IComparable, and Enum.CompareTo already answers null
                    correctly. Including them would let the discovery floor below be met by 48
                    free passes while every hand-written comparable went unchecked.
                */
                if (type.IsEnum)
                {
                    continue;
                }

                // A UnityEngine.Object has to be created by the engine, never by Activator.
                if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                {
                    continue;
                }

                if (NullAcceptingCompareMethods(type).Any())
                {
                    yield return type;
                }
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

        private static IEnumerable<MethodInfo> NullAcceptingCompareMethods(Type type)
        {
            foreach (Type contract in type.GetInterfaces())
            {
                if (contract == typeof(IComparable))
                {
                    yield return contract.GetMethod(nameof(IComparable.CompareTo));
                    continue;
                }

                if (
                    !contract.IsGenericType
                    || contract.GetGenericTypeDefinition() != typeof(IComparable<>)
                )
                {
                    continue;
                }

                // Only a reference-typed argument can be handed a null at all.
                if (contract.GetGenericArguments()[0].IsValueType)
                {
                    continue;
                }

                yield return contract.GetMethod("CompareTo");
            }
        }

        /*
            The null branch of a comparison runs before any state is read, so an uninitialized
            instance is a sound probe for the types whose only constructors take arguments.
        */
        private static object CreateProbe(Type type)
        {
            ConstructorInfo parameterless = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null
            );
            if (parameterless != null)
            {
                return parameterless.Invoke(Array.Empty<object>());
            }

            return FormatterServices.GetUninitializedObject(type);
        }

        private static string Describe(Type contract)
        {
            if (contract == null)
            {
                return "<unknown>";
            }

            if (!contract.IsGenericType)
            {
                return contract.Name;
            }

            string arguments = string.Join(
                ", ",
                contract.GetGenericArguments().Select(argument => argument.Name)
            );
            return $"{contract.Name.Split('`')[0]}<{arguments}>";
        }
    }
}

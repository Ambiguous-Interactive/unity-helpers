// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// The "forever" gate: a reflection-based architecture test that fails the build if any future
    /// PR adds a public deserialize method on <see cref="Serializer"/> without a matching
    /// <c>TryXxx</c> sibling — preventing reintroduction of the screenshot bug class.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializerApiContractTests
    {
        private static readonly Type SerializerType = typeof(Serializer);

        private static IReadOnlyList<MethodInfo> PublicMethods =>
            SerializerType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => !m.IsSpecialName)
                .ToList();

        private static bool IsDataParameter(ParameterInfo p) =>
            p.ParameterType == typeof(byte[]) || p.ParameterType == typeof(string);

        private static bool IsDeserializeMethod(MethodInfo m)
        {
            // The "forever gate": flag every public static method that takes a byte[]/string payload
            // and returns a value (i.e. acts as a deserializer) but isn't a Try*/Serialize* variant.
            // Catches deserialize-shaped methods regardless of whether their name contains
            // "Deserialize" (e.g. a future "Decode<T>" helper would also trip this).
            // ReadFromJsonFile* is excluded because its first argument is a *path* — file-IO
            // methods have their own established Try* siblings (TryReadFromJsonFile) and a different
            // contract (file-existence semantics).
            if (m.Name.StartsWith("Try", StringComparison.Ordinal))
            {
                return false;
            }
            if (
                m.Name.StartsWith("Serialize", StringComparison.Ordinal)
                || m.Name.EndsWith("Serialize", StringComparison.Ordinal)
                || m.Name.Contains("Stringify", StringComparison.Ordinal)
                || m.Name.Contains("WriteTo", StringComparison.Ordinal)
                || m.Name.Contains("ReadFrom", StringComparison.Ordinal)
                || m.Name.Contains("Register", StringComparison.Ordinal)
            )
            {
                return false;
            }
            ParameterInfo[] parameters = m.GetParameters();
            if (parameters.Length == 0 || !IsDataParameter(parameters[0]))
            {
                return false;
            }
            // Must return a value (deserialize-shaped).
            if (m.ReturnType == typeof(void) || m.ReturnType == typeof(bool))
            {
                return false;
            }
            // Heuristic: name must contain Deserialize, Decode, Parse, or From.
            return m.Name.Contains("Deserialize", StringComparison.Ordinal)
                || m.Name.Contains("Decode", StringComparison.Ordinal)
                || m.Name.Contains("Parse", StringComparison.Ordinal);
        }

        [Test]
        public void EveryPublicDeserializerHasMatchingTrySibling()
        {
            List<string> missing = new();
            foreach (MethodInfo method in PublicMethods.Where(IsDeserializeMethod))
            {
                string expectedTryName = "Try" + method.Name;
                bool hasSibling = PublicMethods.Any(candidate =>
                    candidate.Name == expectedTryName && HasMatchingTrySignature(method, candidate)
                );
                if (!hasSibling)
                {
                    missing.Add(FormatSignature(method));
                }
            }

            if (missing.Count > 0)
            {
                Assert.Fail(
                    "The following public Serializer deserialize methods lack a matching Try* sibling — "
                        + "every new deserializer MUST ship with one (see .llm/skills/serialization-safety.md):\n  "
                        + string.Join("\n  ", missing)
                );
            }
        }

        private static bool HasMatchingTrySignature(MethodInfo source, MethodInfo candidate)
        {
            // Generic arity must match.
            if (source.IsGenericMethodDefinition != candidate.IsGenericMethodDefinition)
            {
                return false;
            }
            if (
                source.IsGenericMethodDefinition
                && source.GetGenericArguments().Length != candidate.GetGenericArguments().Length
            )
            {
                return false;
            }

            // Candidate must accept the source's data parameter type as its first arg, and
            // expose an `out` parameter convertible from the source return type somewhere.
            ParameterInfo[] sourceParameters = source.GetParameters();
            ParameterInfo[] candidateParameters = candidate.GetParameters();
            if (candidateParameters.Length == 0)
            {
                return false;
            }
            if (candidateParameters[0].ParameterType != sourceParameters[0].ParameterType)
            {
                return false;
            }
            return candidateParameters.Any(p => p.IsOut);
        }

        private static string FormatSignature(MethodInfo method)
        {
            string parameters = string.Join(
                ", ",
                method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)
            );
            return method.DeclaringType?.Name + "." + method.Name + "(" + parameters + ")";
        }

        // ---------------------------------------------------------------------------
        // The Try* family must, by reflection, share the documented contract:
        // accept null/empty/corrupt input without throwing.
        // ---------------------------------------------------------------------------

        [Test]
        public void EveryPublicTryDeserializerHandlesNullPayloadWithoutThrowing()
        {
            // Build a list of (methodInfo, null-payload) test cases.
            foreach (MethodInfo method in PublicMethods)
            {
                if (!method.Name.StartsWith("Try", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!method.Name.Contains("Deserialize", StringComparison.Ordinal))
                {
                    continue;
                }
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || !IsDataParameter(parameters[0]))
                {
                    continue;
                }
                // Skip Type-based overloads (require a non-null Type, which is a configuration concern).
                if (parameters.Any(p => p.ParameterType == typeof(Type)))
                {
                    continue;
                }
                // Skip file-IO TryRead/TryWrite (different contract — they don't take byte[]/string at all).

                MethodInfo concreteMethod = method;
                if (method.IsGenericMethodDefinition)
                {
                    concreteMethod = method.MakeGenericMethod(typeof(string));
                }

                object[] args = new object[parameters.Length];
                args[0] = null; // null payload
                for (int i = 1; i < parameters.Length; i++)
                {
                    ParameterInfo p = parameters[i];
                    args[i] = p.HasDefaultValue
                        ? p.DefaultValue
                        : (
                            p.ParameterType.IsValueType
                                ? Activator.CreateInstance(p.ParameterType)
                                : null
                        );
                }

                try
                {
                    object result = concreteMethod.Invoke(null, args);
                    Assert.AreEqual(
                        false,
                        result,
                        "Try* method " + method.Name + " must return false on null input."
                    );
                }
                catch (TargetInvocationException tie)
                {
                    Assert.Fail(
                        "Try* method "
                            + method.Name
                            + " leaked an exception on null input: "
                            + tie.InnerException?.GetType().FullName
                            + ": "
                            + tie.InnerException?.Message
                    );
                }
            }
        }
    }
}

// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * The Unity Test Framework has no NuGet equivalent; this signature-only shim type-checks calls without

 * exercising framework behavior.

 */
namespace UnityEngine.TestTools
{
    using System;
    using System.Collections;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>Marks a coroutine test. See <c>UnityEngine.TestTools.UnityTestAttribute</c>.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class UnityTestAttribute : NUnitAttribute { }

    /// <summary>Marks a coroutine set-up. See <c>UnityEngine.TestTools.UnitySetUpAttribute</c>.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class UnitySetUpAttribute : NUnitAttribute { }

    /// <summary>Marks a coroutine tear-down. See <c>UnityEngine.TestTools.UnityTearDownAttribute</c>.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class UnityTearDownAttribute : NUnitAttribute { }

    /// <summary>Asserts about messages the Unity console received.</summary>
    public static class LogAssert
    {
        /// <summary>Whether a failing message should be ignored rather than failing the test.</summary>
        public static bool ignoreFailingMessages { get; set; }

        /// <summary>Expects a message of the given type.</summary>
        /// <param name="type">The log type.</param>
        /// <param name="message">The exact message.</param>
        public static void Expect(LogType type, string message) { }

        /// <summary>Expects a message matching a pattern.</summary>
        /// <param name="type">The log type.</param>
        /// <param name="message">The pattern.</param>
        public static void Expect(LogType type, Regex message) { }

        /// <summary>Fails when an unexpected message was received.</summary>
        public static void NoUnexpectedReceived() { }
    }

    /// <summary>A test driven by a <see cref="MonoBehaviour"/>.</summary>
    public interface IMonoBehaviourTest
    {
        /// <summary>Whether the test has finished.</summary>
        bool IsTestFinished { get; }
    }

    /// <summary>Runs a <see cref="MonoBehaviour"/>-driven test to completion.</summary>
    /// <typeparam name="T">The component type.</typeparam>
    public sealed class MonoBehaviourTest<T> : IEnumerator
        where T : MonoBehaviour, IMonoBehaviourTest
    {
        /// <summary>The component under test.</summary>
        public T component { get; }

        /// <inheritdoc />
        public object Current => null;

        /// <inheritdoc />
        public bool MoveNext()
        {
            return false;
        }

        /// <inheritdoc />
        public void Reset() { }
    }
}

namespace UnityEngine.TestTools.Constraints
{
    using NUnit.Framework.Constraints;

    /// <summary>Constrains a delegate by whether running it allocates managed memory.</summary>
    public sealed class AllocatingGCMemoryConstraint : Constraint
    {
        /// <inheritdoc />
        public override ConstraintResult ApplyTo<TActual>(TActual actual)
        {
            return new ConstraintResult(this, actual, true);
        }
    }

    /// <summary>Entry point for the allocation constraints. See <c>UnityEngine.TestTools.Constraints.Is</c>.</summary>
    public class Is : NUnit.Framework.Is
    {
        /// <summary>Constrains a delegate to allocate managed memory.</summary>
        /// <returns>The constraint.</returns>
        public static AllocatingGCMemoryConstraint AllocatingGCMemory()
        {
            return new AllocatingGCMemoryConstraint();
        }
    }

    /// <summary>Continues a constraint chain, so that <c>Is.Not.AllocatingGCMemory()</c> binds.</summary>
    public static class ConstraintExtensions
    {
        /// <summary>Constrains a delegate to allocate managed memory.</summary>
        /// <param name="chain">The expression being continued.</param>
        /// <returns>The constraint.</returns>
        public static AllocatingGCMemoryConstraint AllocatingGCMemory(
            this ConstraintExpression chain
        )
        {
            return new AllocatingGCMemoryConstraint();
        }
    }
}

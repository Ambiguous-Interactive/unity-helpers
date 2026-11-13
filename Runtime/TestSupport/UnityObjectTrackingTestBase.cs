// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS UNH003: Coroutine lifecycle hooks add idle frames to synchronous tests.
namespace WallstopStudios.UnityHelpers.TestSupport
{
#if UNITY_INCLUDE_TESTS
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using Object = UnityEngine.Object;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>Tracks transient Unity objects for synchronous NUnit cleanup.</summary>
    /// <remarks>Only track objects owned by the test. Use a UnityTest lifecycle fixture when deferred destruction is under test.</remarks>
    public abstract class UnityObjectTrackingTestBase
    {
        private readonly List<Object> _trackedObjects = new();
        private bool _scopeActive;

        /// <summary>Starts an isolated synchronous object scope.</summary>
        [SetUp]
        public void BeginObjectTrackingScope()
        {
            Assert.IsFalse(_scopeActive, "The prior synchronous scope is still active.");
            Assert.That(_trackedObjects.Count, Is.Zero);
            _scopeActive = true;
        }

        /// <summary>Destroys tracked objects immediately before the next case.</summary>
        [TearDown]
        public void EndObjectTrackingScope()
        {
            if (!_scopeActive)
            {
                return;
            }
            string failure = null;
            try
            {
                for (int index = _trackedObjects.Count - 1; 0 <= index; --index)
                {
                    Object instance = _trackedObjects[index];
                    if (instance == null)
                    {
                        continue;
                    }
                    try
                    {
                        Object.DestroyImmediate(instance); // UNH-SUPPRESS UNH001: This scope owns cleanup of these tracked instances.
                        if (instance != null)
                        {
                            failure = "A tracked transient object survived synchronous cleanup.";
                        }
                    }
                    catch (Exception exception)
                        when (exception is not OutOfMemoryException and not StackOverflowException)
                    {
                        failure = exception.ToString();
                    }
                }
            }
            finally
            {
                _trackedObjects.Clear();
                _scopeActive = false;
            }
            Assert.That(failure, Is.Null);
        }

        /// <summary>Creates and tracks a transient ScriptableObject.</summary>
        protected T CreateScriptableObject<T>()
            where T : ScriptableObject
        {
            Assert.IsTrue(_scopeActive, "A synchronous object scope must be active.");
            T instance = ScriptableObject.CreateInstance<T>();
            return Track(instance);
        }

        /// <summary>Tracks a collection of transient objects owned by the test.</summary>
        protected void TrackRange<T>(IEnumerable<T> instances)
            where T : Object
        {
            Assert.IsTrue(_scopeActive, "An object tracking scope must be active.");
            if (instances == null)
            {
                return;
            }
            if (instances is IReadOnlyList<T> list)
            {
                int count = list.Count;
                for (int index = 0; index < count; ++index)
                {
                    Track(list[index]);
                }
                return;
            }
            foreach (T instance in instances)
            {
                Track(instance);
            }
        }

        /// <summary>Tracks a transient object, refusing persistent assets in the Editor.</summary>
        protected T Track<T>(T instance)
            where T : Object
        {
            Assert.IsTrue(_scopeActive, "A synchronous object scope must be active.");
            if (instance == null)
            {
                return instance;
            }
#if UNITY_EDITOR
            Assert.IsFalse(
                EditorUtility.IsPersistent(instance),
                "Only transient objects belong in this scope."
            );
#endif
            _trackedObjects.Add(instance);
            return instance;
        }
    }
#endif
}

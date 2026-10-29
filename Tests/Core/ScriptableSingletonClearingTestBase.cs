// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>Shares singleton cleanup contracts between editor and runtime fixtures.</summary>
    public abstract class ScriptableSingletonClearingTestBase : CommonTestBase
    {
        private static void Clear(bool global)
        {
            if (global)
            {
                ScriptableObjectSingletonRegistry.ClearAllInstances();
            }
            else
            {
                ClearingScriptableSingleton.ClearInstance();
            }
        }

        [TearDown]
        public void ResetSingletonCaches()
        {
            ClearingScriptableSingleton.ClearInstance();
            ClearingPeerScriptableSingleton.ClearInstance();
        }

        [Test]
        public void DestroyedInstanceCannotReloadInsideItsClearCallback(
            [Values(false, true)] bool global
        )
        {
            ClearingScriptableSingleton original = Track(
                ScriptableObject.CreateInstance<ClearingScriptableSingleton>()
            );
            ClearingScriptableSingleton replacement = Track(
                ScriptableObject.CreateInstance<ClearingScriptableSingleton>()
            );
            ClearingScriptableSingleton._lazyInstance = new Lazy<ClearingScriptableSingleton>(() =>
                original
            );
            Assert.AreSame(original, ClearingScriptableSingleton.Instance);
            ClearingScriptableSingleton observed = replacement;
            original.clearing = () =>
            {
                Object.DestroyImmediate(original); // UNH-SUPPRESS: Verifies reload refusal after destruction inside cleanup.
                observed = ClearingScriptableSingleton.Instance;
            };

            Clear(global);

            Assert.IsTrue(ReferenceEquals(observed, null));
            Assert.IsFalse(ClearingScriptableSingleton._lazyInstance.IsValueCreated);
            Assert.AreSame(replacement, ClearingScriptableSingleton.Instance);
        }

        [Test]
        public void LiveInstanceRemainsAvailableInsideItsClearCallback(
            [Values(false, true)] bool global
        )
        {
            ClearingScriptableSingleton original = Track(
                ScriptableObject.CreateInstance<ClearingScriptableSingleton>()
            );
            ClearingScriptableSingleton._lazyInstance = new Lazy<ClearingScriptableSingleton>(() =>
                original
            );
            Assert.AreSame(original, ClearingScriptableSingleton.Instance);
            ClearingScriptableSingleton observed = null;
            original.clearing = () =>
            {
                observed = ClearingScriptableSingleton.Instance;
                ClearingScriptableSingleton.ClearInstance();
            };

            Clear(global);

            Assert.AreSame(original, observed);
            Assert.IsFalse(ClearingScriptableSingleton._lazyInstance.IsValueCreated);
        }

        [Test]
        public void GlobalClearDoesNotLoadAnUncachedSingletonFromACallback()
        {
            ClearingPeerScriptableSingleton replacement = Track(
                ScriptableObject.CreateInstance<ClearingPeerScriptableSingleton>()
            );
            int loads = 0;
            Lazy<ClearingPeerScriptableSingleton> pending = new(() =>
            {
                ++loads;
                return replacement;
            });
            ClearingPeerScriptableSingleton observed = replacement;
            Action callback = () =>
            {
                ClearingPeerScriptableSingleton._lazyInstance = pending;
                observed = ClearingPeerScriptableSingleton.Instance;
            };
            ScriptableObjectSingletonRegistry.Register(callback);
            try
            {
                ScriptableObjectSingletonRegistry.ClearAllInstances();
                Assert.AreEqual(0, loads);
                Assert.IsTrue(ReferenceEquals(observed, null));
                Assert.IsFalse(pending.IsValueCreated);
            }
            finally
            {
                ScriptableObjectSingletonRegistry.Unregister(callback);
            }
            ClearingPeerScriptableSingleton._lazyInstance = pending;
            Assert.AreSame(replacement, ClearingPeerScriptableSingleton.Instance);
            Assert.AreEqual(1, loads);
        }

        [Test]
        public void ReentrantGlobalClearInvokesEachCallbackOnceAndCanRunAgain()
        {
            int calls = 0;
            bool requesting = false;
            Action callback = () =>
            {
                ++calls;
                if (!requesting)
                {
                    requesting = true;
                    ScriptableObjectSingletonRegistry.ClearAllInstances();
                    requesting = false;
                }
            };
            ScriptableObjectSingletonRegistry.Register(callback);
            try
            {
                ScriptableObjectSingletonRegistry.ClearAllInstances();
                Assert.AreEqual(1, calls);
                ScriptableObjectSingletonRegistry.ClearAllInstances();
                Assert.AreEqual(2, calls);
            }
            finally
            {
                ScriptableObjectSingletonRegistry.Unregister(callback);
            }
        }

        [Test]
        public void ClearDuringFirstLoadDoesNotPublishTheRetiredValue()
        {
            ClearingScriptableSingleton original = Track(
                ScriptableObject.CreateInstance<ClearingScriptableSingleton>()
            );
            ClearingScriptableSingleton._lazyInstance = new Lazy<ClearingScriptableSingleton>(() =>
            {
                ClearingScriptableSingleton.ClearInstance();
                return original;
            });

            ClearingScriptableSingleton observed = ClearingScriptableSingleton.Instance;

            Assert.IsTrue(ReferenceEquals(observed, null));
            Assert.IsFalse(ClearingScriptableSingleton.HasInstance);
            Assert.AreSame(original, ClearingScriptableSingleton.Instance);
        }

        [Test]
        public void DestroyDuringFirstLoadReturnsNull()
        {
            ClearingScriptableSingleton original = Track(
                ScriptableObject.CreateInstance<ClearingScriptableSingleton>()
            );
            ClearingScriptableSingleton._lazyInstance = new Lazy<ClearingScriptableSingleton>(() =>
            {
                Object.DestroyImmediate(original); // UNH-SUPPRESS: Verifies publication after destruction during loading.
                return original;
            });

            Assert.IsTrue(ReferenceEquals(ClearingScriptableSingleton.Instance, null));
            Assert.IsFalse(ClearingScriptableSingleton.HasInstance);
        }

        [Test]
        public void CallbackExceptionStillReleasesClearState([Values(false, true)] bool global)
        {
            ClearingScriptableSingleton original = Track(
                ScriptableObject.CreateInstance<ClearingScriptableSingleton>()
            );
            ClearingScriptableSingleton._lazyInstance = new Lazy<ClearingScriptableSingleton>(() =>
                original
            );
            Assert.AreSame(original, ClearingScriptableSingleton.Instance);
            original.clearing = () =>
            {
                original.clearing = null;
                throw new InvalidOperationException(
                    nameof(CallbackExceptionStillReleasesClearState)
                );
            };
            LogAssert.Expect(
                LogType.Exception,
                new Regex(nameof(CallbackExceptionStillReleasesClearState))
            );

            Clear(global);

            Assert.IsFalse(ClearingScriptableSingleton.HasInstance);
            Assert.AreSame(original, ClearingScriptableSingleton.Instance);
        }

        [Test]
        public void IndividualClearAllowsOtherSingletonTypesToLoad()
        {
            ClearingScriptableSingleton original = Track(
                ScriptableObject.CreateInstance<ClearingScriptableSingleton>()
            );
            ClearingPeerScriptableSingleton other = Track(
                ScriptableObject.CreateInstance<ClearingPeerScriptableSingleton>()
            );
            ClearingScriptableSingleton._lazyInstance = new Lazy<ClearingScriptableSingleton>(() =>
                original
            );
            ClearingPeerScriptableSingleton._lazyInstance =
                new Lazy<ClearingPeerScriptableSingleton>(() => other);
            Assert.AreSame(original, ClearingScriptableSingleton.Instance);
            ClearingPeerScriptableSingleton observed = null;
            original.clearing = () => observed = ClearingPeerScriptableSingleton.Instance;

            ClearingScriptableSingleton.ClearInstance();

            Assert.AreSame(other, observed);
            Assert.IsTrue(ClearingPeerScriptableSingleton.HasInstance);
        }

        [Test]
        public void GlobalCallbackFailureDoesNotSkipOtherCallbacksOrPoisonLaterClears()
        {
            int calls = 0;
            Action failing = () =>
                throw new InvalidOperationException(
                    nameof(GlobalCallbackFailureDoesNotSkipOtherCallbacksOrPoisonLaterClears)
                );
            Action succeeding = () => ++calls;
            ScriptableObjectSingletonRegistry.Register(failing);
            ScriptableObjectSingletonRegistry.Register(succeeding);
            try
            {
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex(
                        nameof(GlobalCallbackFailureDoesNotSkipOtherCallbacksOrPoisonLaterClears)
                    )
                );
                ScriptableObjectSingletonRegistry.ClearAllInstances();
                Assert.AreEqual(1, calls);
                ScriptableObjectSingletonRegistry.Unregister(failing);
                ScriptableObjectSingletonRegistry.ClearAllInstances();
                Assert.AreEqual(2, calls);
            }
            finally
            {
                ScriptableObjectSingletonRegistry.Unregister(failing);
                ScriptableObjectSingletonRegistry.Unregister(succeeding);
            }
        }
    }
}

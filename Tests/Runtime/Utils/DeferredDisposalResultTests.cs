// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DeferredDisposalResultTests
    {
        [UnityTest]
        public IEnumerator DisposeAsyncInvokesDelegate()
        {
            bool disposed = false;
            DeferredDisposalResult<int> result = new(
                7,
                async () =>
                {
                    await Task.Yield();
                    disposed = true;
                }
            );

            Assert.AreEqual(7, result.result);

            ValueTask vt = result.DisposeAsync();
            Task t = vt.AsTask();
            while (!t.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(disposed);
        }

        [Test]
        public void ConstructorThrowsOnNullDisposeDelegate()
        {
            Assert.Throws<ArgumentNullException>(() => new DeferredDisposalResult<int>(1, null));
        }
    }
}

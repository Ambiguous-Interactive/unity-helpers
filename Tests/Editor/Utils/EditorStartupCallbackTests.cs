// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    /// Pins that arming two hooks still runs the work once.
    /// </summary>
    /// <remarks>
    /// Both <c>AssemblyReloadEvents.afterAssemblyReload</c> and <c>EditorApplication.delayCall</c>
    /// are armed, because an editor nobody is interacting with may never pump the tick and the
    /// reload event is a callback Unity invokes. The cost of arming both is that both can fire, and
    /// the work behind them migrates assets and clears caches
    /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/684">#684</see>).
    /// The hooks themselves are Unity's and cannot be raised on demand, so the one-shot is asserted
    /// directly -- which is the half that carries the guarantee.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Editor")]
    public sealed class EditorStartupCallbackTests
    {
        [Test]
        [TestCase(1, TestName = "WorkRunsOnceForOneHook")]
        [TestCase(2, TestName = "WorkRunsOnceWhenBothHooksFire")]
        [TestCase(5, TestName = "WorkRunsOnceUnderRepeatedFiring")]
        public void WorkRunsAtMostOnce(int fireCount)
        {
            int ran = 0;
            EditorStartupCallback.OneShot oneShot = new(() => ++ran);

            for (int index = 0; index < fireCount; ++index)
            {
                oneShot.Run();
            }

            Assert.AreEqual(
                1,
                ran,
                $"Firing {fireCount} time(s) ran the startup work {ran} time(s)."
            );
        }

        [Test]
        public void ANullActionIsIgnored()
        {
            EditorStartupCallback.OneShot oneShot = new(null);

            Assert.DoesNotThrow(() => oneShot.Run());
            Assert.DoesNotThrow(() => EditorStartupCallback.RunOnce(null));
        }
    }
}
#endif

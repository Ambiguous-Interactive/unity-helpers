// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using NUnit.Framework;
    using ProtoBuf.Meta;

    /// <summary>
    /// Teaches the oracle the surrogate pairs this assembly's fixtures rely on, once per run.
    /// </summary>
    /// <remarks>
    /// <c>RuntimeTypeModel.Default</c> is process-wide and <b>freezes a type the first time it
    /// serializes one</b>, so a second fixture calling <c>SetSurrogate</c> fails with "the type
    /// cannot be changed once a serializer has been generated" -- and which fixture that is depends
    /// on execution order, so the same change passes or fails according to what else is in the run.
    /// Registering from one assembly-level fixture removes the ordering from the question entirely.
    /// </remarks>
    [SetUpFixture]
    public sealed class OracleModelSetup
    {
        [OneTimeSetUp]
        public void RegisterSurrogates()
        {
            /*
             * The runtime oracle mapping and generated assembly declaration must describe the same surrogate
             * pair.
             */
            RuntimeTypeModel
                .Default.Add(typeof(ForeignVector3), false)
                .SetSurrogate(typeof(ForeignVector3Surrogate));
        }
    }
}

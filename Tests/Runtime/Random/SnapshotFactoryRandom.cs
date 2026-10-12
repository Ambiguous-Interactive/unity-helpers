// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoNotSerialized]
    internal sealed class SnapshotFactoryRandom : AbstractRandom
    {
        public override RandomState InternalState => BuildState(_next);

        private uint _next = 123;

        public SnapshotFactoryRandom() { }

        private SnapshotFactoryRandom(RandomState state)
        {
            _next = (uint)state.State1;
            RestoreCommonState(state);
        }

        public RandomState CreateWithPayload(IReadOnlyList<byte> payload)
        {
            return BuildState(_next, payload: payload);
        }

        public override uint NextUint()
        {
            return ++_next;
        }

        public override IRandom Copy()
        {
            return new SnapshotFactoryRandom(InternalState);
        }
    }
}

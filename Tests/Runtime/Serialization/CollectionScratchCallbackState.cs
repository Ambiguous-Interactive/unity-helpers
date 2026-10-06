// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.IO;

    public sealed class CollectionScratchCallbackState
    {
        public Action BeforeAction;
        public readonly IOException Failure = new IOException("Collection write failed.");
        public int FailingValue = 2;
        public int BeforeCalls;
        public int AfterCalls;
        public int ParentBeforeCalls;
        public int ParentAfterCalls;
        public bool FailBefore;
        public bool FailAfter;
        public bool FailParentBefore;
        public bool FailParentAfter;
    }
}

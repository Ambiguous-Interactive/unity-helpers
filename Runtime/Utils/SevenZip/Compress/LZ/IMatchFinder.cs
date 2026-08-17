// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The code in this file is from the 7-Zip LZMA SDK, Public Domain, https://www.7-zip.org/sdk.html.
// This is vendored upstream source with only namespace and style adjustments; the design is the
// original author's. See docs/project/third-party-notices.md.

// IMatchFinder.cs

namespace SevenZip.Compression.LZ
{
    using System;

    internal interface IInWindowStream
    {
        void SetStream(System.IO.Stream inStream);
        void Init();
        void ReleaseStream();
        Byte GetIndexByte(Int32 index);
        UInt32 GetMatchLen(Int32 index, UInt32 distance, UInt32 limit);
        UInt32 GetNumAvailableBytes();
    }

    internal interface IMatchFinder : IInWindowStream
    {
        void Create(
            UInt32 historySize,
            UInt32 keepAddBufferBefore,
            UInt32 matchMaxLen,
            UInt32 keepAddBufferAfter
        );
        UInt32 GetMatches(UInt32[] distances);
        void Skip(UInt32 num);
    }
}

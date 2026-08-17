This package contains third-party software components governed by the license(s) indicated below.

## Serialization & Compression

### protobuf-net

- Description: .NET runtime/library for Protocol Buffers serialization by Marc Gravell.
- Upstream: [GitHub repository](https://github.com/protobuf-net/protobuf-net)
- License: Apache License 2.0
- License URL: [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- Notes: Uses attributes such as [ProtoContract]/[ProtoMember] and runtime `ProtoBuf.Serializer`.

### 7-Zip LZMA SDK

- Description: LZMA compression/decompression implementation (encoder/decoder) used via `SevenZip.Compression.LZMA`.
- Upstream: [7-Zip LZMA SDK](https://www.7-zip.org/sdk.html)
- License: Public Domain (per 7-Zip LZMA SDK)
- Notes: Integrated sources under `Runtime/Utils/SevenZip/Compress/LZMA`.

## Editor Tools

### Unity-Serializable-Dictionary

- Description: Serializable dictionary implementation enabling Unity serialization of generic dictionaries.
- Upstream: [GitHub repository](https://github.com/JDSherbert/Unity-Serializable-Dictionary)
- License: MIT License
- License URL: [MIT License](https://opensource.org/license/MIT)
- Notes: Adapted naming and serialization cache handling to align with Wallstop Studios Unity Helpers conventions.

### Unity Editor Toolbox (Inline Editor)

- Description: Inline inspector drawer inspiration for editing object references in-place.
- Upstream: [GitHub repository](https://github.com/arimger/Unity-Editor-Toolbox)
- License: MIT License
- License URL: [MIT License](https://opensource.org/license/MIT)
- Notes: Portions of `WInLineEditorDrawer` build upon concepts from the toolbox's InlineEditor drawer implementation.

### SeniaAnimationEventEditor

- Description: Editor window for inspecting and editing `AnimationEvent` entries on an `AnimationClip`.
- Author: yujen
- Upstream: [GitHub gist](https://gist.github.com/yujen/5e1cd78e2a341260b38029de08a449da)
- License: Not recorded in this repository.
- Notes: Portions of `Editor/AnimationEventEditor.cs` are adapted from this gist.

## Sorting Algorithms

The following sorting algorithm implementations in `Runtime/Core/Extension/Sorting/` are adapted from or inspired by third-party sources.

### Pattern-Defeating QuickSort (pdqsort)

- Description: Hybrid sorting algorithm combining quicksort with insertion sort and heapsort fallback.
- Author: Orson Peters
- Upstream: [GitHub repository](https://github.com/orlp/pdqsort)
- License: zlib License
- Notes: C# adaptation retaining pattern-detection heuristics while operating on `IList<T>`.

### Grail Sort

- Description: Block merge sort algorithm achieving stable O(n log n) sorting with O(1) extra space.
- Author: Mrrl (Andrey Astrelin)
- Upstream: [GitHub repository](https://github.com/Mrrl/GrailSort)
- License: MIT License
- Notes: Adaptation uses pooled buffers instead of manual block buffers while keeping stability.

### WikiSort (Block Merge Sort)

- Description: In-place stable merge sort using block rearrangement.
- Author: Mike McFadden (BonzaiThePenguin)
- Upstream: [GitHub repository](https://github.com/BonzaiThePenguin/WikiSort)
- License: Public Domain
- Notes: Adaptation uses a pooled full-size buffer for simplicity.

### PowerSort

- Description: Adaptive mergesort leveraging natural runs with optimal merge scheduling.
- Authors: J. Ian Munro and Sebastian Wild
- Upstream: [arXiv paper](https://arxiv.org/abs/1805.04154)
- License: CC BY 4.0 (paper); algorithm is public domain
- Notes: Implementation detects runs and merges them with pooled buffers.

### sort-research-rs Algorithms (Glidesort, Fluxsort, Ipnsort)

- Description: Modern high-performance sorting algorithms from the sort-research-rs project.
- Authors: Orson Peters, Lukas Bergdoll (Voultapher)
- Upstream: [GitHub repository](https://github.com/Voultapher/sort-research-rs)
- License: Apache License 2.0 / MIT License (dual-licensed)
- Notes: C# adaptations of Glidesort (stable galloping merges), Fluxsort (dual-pivot quicksort), and Ipnsort (introspective quicksort with median-of-medians).

### IPS4o Sort

- Description: In-place parallel super scalar samplesort.
- Authors: Michael Axtmann, Sascha Witt, Daniel Ferizovic, Peter Sanders
- Upstream: [arXiv paper](https://arxiv.org/abs/1705.02257)
- License: Academic paper; algorithm concepts are freely implementable
- Notes: Single-threaded C# adaptation with multiway partitioning.

### YamSort

- Description: Stable top-down bisection mergesort that adapts its insertion-sort threshold and merge strategy to how sequential the data turns out to be.
- Author: Gary Gende
- Upstream: [GitHub repository](https://github.com/gendeg/YamSort)
- License: MIT License
- Notes: Adaptation in `Runtime/Core/Extension/Sorting/IListSortYam.cs` replaces the upstream `Span`/`IComparable` surface with `IList<T>` and an explicit comparer.

## Random Number Generators

The following PRNG implementations in `Runtime/Core/Random/` are adapted from or inspired by third-party sources.

### PCG Random

- Description: Permuted Congruential Generator family of PRNGs with excellent statistical properties.
- Author: Melissa O'Neill
- Upstream: [PCG Random website](https://www.pcg-random.org/)
- Paper: [PCG: A Family of Simple Fast Space-Efficient Statistically Good Algorithms for Random Number Generation](https://www.pcg-random.org/paper.html)
- License: Apache License 2.0
- Notes: Implementation based on the reference PCG Random.

### Xoroshiro / Xoshiro / SplitMix64

- Description: Fast, high-quality PRNGs with small state.
- Authors: David Blackman, Sebastiano Vigna
- Upstream: [Scrambled Linear PRNGs (xoshiro/xoroshiro)](https://arxiv.org/abs/1805.01407); [Fast Splittable PRNGs (SplitMix64)](https://2014.splashcon.org/details/oopsla2014/48/Fast-Splittable-Pseudorandom-Number-Generators)
- License: CC0 1.0 Universal (Public Domain)
- Notes: Implements xoroshiro128\*\* and SplitMix64 variants.

### RomuDuo

- Description: Rotate-multiply PRNG family optimized for modern CPUs.
- Authors: Mark A. Overton
- Upstream: [ROMU website (archived)](https://web.archive.org/web/20240101000000*/https://romu-random.org/)
- License: CC0 1.0 Universal (Public Domain)
- Notes: Implements the RomuDuo variant with two 64-bit state words.

### WyRandom (wyhash)

- Description: Fast PRNG based on the wyhash hash function.
- Author: Wang Yi
- Upstream: [GitHub repository](https://github.com/wangyi-fudan/wyhash)
- License: The Unlicense (Public Domain)
- .NET Reference: [cocowalla/wyhash-dotnet](https://github.com/cocowalla/wyhash-dotnet) (MIT License)
- Notes: Implementation references the cocowalla .NET port.

### Will Stafford Parsons Algorithms

The following algorithms are by Will Stafford Parsons (wileylooper). Note: The original GitHub repositories are currently offline.

- **IllusionFlow**: Hybridized PCG + xorshift design.
- **FlurryBurst**: Six-word ARX-style generator.
- **StormDrop**: Large-state ARX generator inspired by SHISHUA.
- **PhotonSpin**: 20-word ring-buffer generator.
- **BlastCircuit**: Four-word ARX-style generator.
- **WaveSplat**: One-word chaotic generator.
- **Meteor Sort**: Gap-sequence-based hybrid sorting algorithm.
- **Ghost Sort**: Hybrid gap-based sorting algorithm.

License: These implementations are used with attribution to the original author. Please refer to the individual repositories for specific licensing terms if they become available again.

### WDoomRandom (technique only, clean-room)

- Description: A table-walk generator: draw from a constant 256-entry table, advance a wrapping index. The technique was popularized by DOOM (id Software, 1993).
- Author: Technique popularized by id Software; this implementation is Wallstop Studios'.
- Upstream: [DOOM Wiki: Random number generator](https://doomwiki.org/wiki/Random_number_generator)
- License: MIT (this package). **No third-party code or data is included.** DOOM's source is GPL-2.0 and its 256-byte table is GPL-2.0 data; neither was read nor copied. `Runtime/Core/Random/WDoomRandom.cs` was written clean-room from a description of the technique, and its table is this package's own permutation of 0-255, shuffled by `SplitMix64` from a fixed seed. This entry records the inspiration, not a dependency.

### Perlin Noise (Improved Noise)

- Description: Gradient noise function; the default permutation table and the fade and gradient functions come from Ken Perlin's reference implementation.
- Author: Ken Perlin
- Upstream: Not recorded in this repository (Ken Perlin's Improved Noise reference implementation).
- License: Not recorded in this repository.
- Notes: Implemented in `Runtime/Core/Random/PerlinNoise.cs`, which adds seeded permutation shuffling and Unity vector overloads.

## Runtime Utilities

The following runtime helpers adapt code or techniques published by others.

### Ramer-Douglas-Peucker Line Simplification

- Description: Polyline simplification by recursive perpendicular-distance splitting.
- Authors: Algorithm by Ramer, Douglas, and Peucker; the C# implementation adapted here was published by Craig Selbert.
- Upstream: [CodeProject article](https://www.codeproject.com/Articles/18936/A-Csharp-Implementation-of-Douglas-Peucker-Line-Ap)
- License: Not recorded in this repository.
- Notes: `Runtime/Core/Helper/LineHelper.cs` adapts the implementation for Unity vector types.

### Unity Community Wiki TextureScale

- Description: Bilinear and point texture scaling routines for `Texture2D`.
- Author: Not recorded in this repository.
- Upstream: [Unity Community wiki](http://wiki.unity3d.com/index.php/TextureScale)
- License: Not recorded in this repository.
- Notes: `Runtime/Utils/TextureScale.cs` adapts these routines with pooled buffers, task-based parallelism, input validation, and center-aligned sampling.

### Java ThreadLocalRandom (bounded double)

- Description: Technique for bounding a double below an exclusive maximum by adjusting its bit representation.
- Author: OpenJDK contributors; individual authors are not recorded in this repository.
- Upstream: [OpenJDK 8 ThreadLocalRandom](http://grepcode.com/file/repository.grepcode.com/java/root/jdk/openjdk/8-b132/java/util/concurrent/ThreadLocalRandom.java#356)
- License: Not recorded in this repository.
- Notes: `WallMath.BoundedDouble` follows this technique.

## Academic & Historical Acknowledgments

The following algorithms are based on well-known academic work and are implemented from published descriptions:

### Sorting Algorithms

- **TimSort**: Hybrid stable sort by Tim Peters (Python) and OpenJDK. [Python description](https://bugs.python.org/file4451/timsort.txt)
- **SmoothSort**: Heap-based adaptive sort by Edsger Dijkstra. Further analysis by Stefan Edelkamp and Armin Wegener.
- **JesseSort**: Dual-patience sort hybrid by Jesse Michel. [GitHub](https://github.com/lewj85/jessesort)
- **greeNsort**: Symmetric mergesort by Jens Oehlschlegel. [Website](https://www.greensort.org)
- **Ska Sort**: Branch-friendly dual-pivot quicksort by Malte Skarupke. [Blog post](https://probablydance.com/2016/12/27/i-wrote-a-faster-sorting-algorithm/)
- **PowerSort+**: Enhanced run-priority mergesort by Sebastian Wild and Martin Nebel.

### Random Number Generators

- **XorShift**: Classic PRNG by George Marsaglia (2003). [Paper](https://doi.org/10.18637/jss.v008.i14)
- **Linear Congruential Generator**: Park-Miller variant (1988). "Random Number Generators: Good Ones Are Hard to Find" Communications of the ACM 31(10):1192-1201
- **Squirrel Noise**: Hash-based noise function by Squirrel Eiserloh. [GDC Talk](https://youtu.be/LWFzPP8ZbdU?t=2673)

### Sampling & Geometry

- **Uniform Random Rotations**: Shoemake, "Uniform Random Rotations", Graphics Gems III. Implemented in `RandomExtensions.NextRotation`.
- **Sphere Point Picking**: Marsaglia's rejection method for uniform points on a sphere. Implemented in `RandomExtensions`.

## Additional Notes

- System.Text.Json and other .NET BCL components are used as part of the .NET runtime and are subject to their respective licenses (e.g., MIT for dotnet/runtime). No vendored sources from these components are included in this repository.

## Full License Texts

### MIT License

Used by: Unity-Serializable-Dictionary, Unity Editor Toolbox, Grail Sort, YamSort, cocowalla/wyhash-dotnet

```text
MIT License

Copyright (c) [year] [copyright holders]

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Apache License 2.0

Used by: protobuf-net, PCG Random, sort-research-rs algorithms

```text
Apache License
Version 2.0, January 2004
https://www.apache.org/licenses/

TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

1. Definitions.
   "License" shall mean the terms and conditions for use, reproduction, and
   distribution as defined by Sections 1 through 9 of this document.
   "Licensor" shall mean the copyright owner or entity authorized by the
   copyright owner that is granting the License.
   "Legal Entity" shall mean the union of the acting entity and all other
   entities that control, are controlled by, or are under common control with
   that entity.
   "You" (or "Your") shall mean an individual or Legal Entity exercising
   permissions granted by this License.
   "Source" form shall mean the preferred form for making modifications,
   including but not limited to software source code, documentation source,
   and configuration files.
   "Object" form shall mean any form resulting from mechanical transformation
   or translation of a Source form, including but not limited to compiled
   object code, generated documentation, and conversions to other media types.
   "Work" shall mean the work of authorship, whether in Source or Object form,
   made available under the License, as indicated by a copyright notice that
   is included in or attached to the work.
   "Derivative Works" shall mean any work, whether in Source or Object form,
   that is based on (or derived from) the Work and for which the editorial
   revisions, annotations, elaborations, or other modifications represent, as
   a whole, an original work of authorship.
   "Contribution" shall mean any work of authorship, including the original
   version of the Work and any modifications or additions to that Work or
   Derivative Works thereof, that is intentionally submitted to Licensor for
   inclusion in the Work by the copyright owner or by an individual or Legal
   Entity authorized to submit on behalf of the copyright owner.
   "Contributor" shall mean Licensor and any individual or Legal Entity on
   behalf of whom a Contribution has been received by Licensor and
   subsequently incorporated within the Work.

2. Grant of Copyright License.
   Subject to the terms and conditions of this License, each Contributor
   hereby grants to You a perpetual, worldwide, non-exclusive, no-charge,
   royalty-free, irrevocable copyright license to reproduce, prepare
   Derivative Works of, publicly display, publicly perform, sublicense, and
   distribute the Work and such Derivative Works in Source or Object form.

3. Grant of Patent License.
   Subject to the terms and conditions of this License, each Contributor
   hereby grants to You a perpetual, worldwide, non-exclusive, no-charge,
   royalty-free, irrevocable (except as stated in this section) patent license
   to make, have made, use, offer to sell, sell, import, and otherwise
   transfer the Work.

4. Redistribution.
   You may reproduce and distribute copies of the Work or Derivative Works
   thereof in any medium, with or without modifications, and in Source or
   Object form, provided that You meet the following conditions:
   (a) You must give any other recipients of the Work or Derivative Works a
       copy of this License; and
   (b) You must cause any modified files to carry prominent notices stating
       that You changed the files; and
   (c) You must retain, in the Source form of any Derivative Works that You
       distribute, all copyright, patent, trademark, and attribution notices
       from the Source form of the Work; and
   (d) If the Work includes a "NOTICE" text file as part of its distribution,
       then any Derivative Works that You distribute must include a readable
       copy of the attribution notices contained within such NOTICE file,
       excluding those notices that do not pertain to any part of the
       Derivative Works.

5. Submission of Contributions.
   Unless You explicitly state otherwise, any Contribution intentionally
   submitted for inclusion in the Work by You to the Licensor shall be under
   the terms and conditions of this License, without any additional terms or
   conditions.

6. Trademarks.
   This License does not grant permission to use the trade names, trademarks,
   service marks, or product names of the Licensor, except as required for
   reasonable and customary use in describing the origin of the Work and
   reproducing the content of the NOTICE file.

7. Disclaimer of Warranty.
   Unless required by applicable law or agreed to in writing, Licensor
   provides the Work (and each Contributor provides its Contributions) on an
   "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
   or implied.

8. Limitation of Liability.
   In no event and under no legal theory, whether in tort (including
   negligence), contract, or otherwise, unless required by applicable law
   (such as deliberate and grossly negligent acts) or agreed to in writing,
   shall any Contributor be liable to You for damages, including any direct,
   indirect, special, incidental, or consequential damages of any character
   arising as a result of this License or out of the use or inability to use
   the Work.

9. Accepting Warranty or Additional Liability.
   While redistributing the Work or Derivative Works thereof, You may choose
   to offer, and charge a fee for, acceptance of support, warranty, indemnity,
   or other liability obligations and/or rights consistent with this License.

END OF TERMS AND CONDITIONS
```

### zlib License

Used by: pdqsort

```text
zlib License

Copyright (c) [year] [copyright holders]

This software is provided 'as-is', without any express or implied warranty.
In no event will the authors be held liable for any damages arising from the
use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not claim
   that you wrote the original software. If you use this software in a
   product, an acknowledgment in the product documentation would be
   appreciated but is not required.

2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.

3. This notice may not be removed or altered from any source distribution.
```

### CC0 1.0 Universal (Public Domain Dedication)

Used by: Xoroshiro/Xoshiro/SplitMix64, RomuDuo

```text
CC0 1.0 Universal

CREATIVE COMMONS CORPORATION IS NOT A LAW FIRM AND DOES NOT PROVIDE LEGAL
SERVICES. DISTRIBUTION OF THIS DOCUMENT DOES NOT CREATE AN ATTORNEY-CLIENT
RELATIONSHIP. CREATIVE COMMONS PROVIDES THIS INFORMATION ON AN "AS-IS" BASIS.
CREATIVE COMMONS MAKES NO WARRANTIES REGARDING THE USE OF THIS DOCUMENT OR
THE INFORMATION OR WORKS PROVIDED HEREUNDER, AND DISCLAIMS LIABILITY FOR
DAMAGES RESULTING FROM THE USE OF THIS DOCUMENT OR THE INFORMATION OR WORKS
PROVIDED HEREUNDER.

Statement of Purpose

The laws of most jurisdictions throughout the world automatically confer
exclusive Copyright and Related Rights (defined below) upon the creator and
subsequent owner(s) (each and all, an "owner") of an original work of
authorship and/or a database (each, a "Work").

Certain owners wish to permanently relinquish those rights to a Work for the
purpose of contributing to a commons of creative, cultural and scientific
works ("Commons") that the public can reliably and without fear of later
claims of infringement build upon, modify, incorporate in other works, reuse
and redistribute as freely as possible in any form whatsoever and for any
purposes, including without limitation commercial purposes.

The person associating CC0 with a Work (the "Affirmer"), to the extent that
he or she is an owner of Copyright and Related Rights in the Work, voluntarily
elects to apply CC0 to the Work and publicly distribute the Work under its
terms, with knowledge of his or her Copyright and Related Rights in the Work
and the meaning and intended legal effect of CC0 on those rights.

1. Copyright and Related Rights.
   A Work made available under CC0 may be protected by copyright and related
   or neighboring rights ("Copyright and Related Rights").

2. Waiver.
   To the greatest extent permitted by, but not in contravention of,
   applicable law, Affirmer hereby overtly, fully, permanently, irrevocably
   and unconditionally waives, abandons, and surrenders all of Affirmer's
   Copyright and Related Rights and associated claims and causes of action.

3. Public License Fallback.
   Should any part of the Waiver for any reason be judged legally invalid or
   ineffective under applicable law, then the Waiver shall be preserved to
   the maximum extent permitted. In addition, to the extent the Waiver is so
   judged Affirmer hereby grants to each affected person a royalty-free, non
   transferable, non sublicensable, non exclusive, irrevocable and
   unconditional license to exercise Affirmer's Copyright and Related Rights
   in the Work.

4. Limitations and Disclaimers.
   a. No trademark or patent rights held by Affirmer are waived, abandoned,
      surrendered, licensed or otherwise affected by this document.
   b. Affirmer offers the Work as-is and makes no representations or
      warranties of any kind concerning the Work.
   c. Affirmer disclaims responsibility for clearing rights of other persons
      that may apply to the Work.
   d. Affirmer understands and acknowledges that Creative Commons is not a
      party to this document and has no duty or obligation with respect to
      this CC0 or use of the Work.
```

### The Unlicense

Used by: wyhash

```text
This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or distribute
this software, either in source code form or as a compiled binary, for any
purpose, commercial or non-commercial, and by any means.

In jurisdictions that recognize copyright laws, the author or authors of this
software dedicate any and all copyright interest in the software to the
public domain. We make this dedication for the benefit of the public at large
and to the detriment of our heirs and successors. We intend this dedication
to be an overt act of relinquishment in perpetuity of all present and future
rights to this software under copyright law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

For more information, please refer to <https://unlicense.org>
```

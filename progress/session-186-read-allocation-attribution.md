# Session 186 — attributing, and removing, WallstopProto's read allocation

Branch: `dev/wallstop/session-186-read-allocation`. Baseline: `main` at `5676639f`, package `3.5.1`.

## Why this was next

The exhaustive GitHub audit found 35 open issues, no open or draft pull request, and no prior-session
branch left unfinished; `main` was green at `5676639f` with its post-merge matrix still running. #343
remains the highest gameplay-impacting item, and the open piece of it that a game feels every frame
is #398: the write path reuses a caller's buffer and allocates nothing, while the read path allocated
**5,272 B/op against protobuf-net's 4,112** for the same object graph. Garbage on a per-frame or
per-save read is a frame-time spike, not a micro-optimization.

#398's own instruction was to measure before pooling anything. Session 184 had already built the
aggregate benchmark it asked for, so the open work started one step later.

## The aggregate could not say what to fix

The 1,160 B gap was one number over five member shapes. The first change was therefore an
instrument, not an optimization: one contract per member shape, each measured against protobuf-net
decoding the **identical** graph, so the difference is the serializer's overhead rather than the
payload's. It attributed the whole gap to two shapes and exonerated three:

| Shape                       | Before      | After   | protobuf-net |
| --------------------------- | ----------: | ------: | -----------: |
| `int[128]`                  | 1,744 B/op  | **560** |          560 |
| `List<int>[128]`            | 1,208 B/op  | **592** |          624 |
| `string`                    | 88 B/op     | 88      |           88 |
| `Dictionary<string,int>[32]`| 3,384 B/op  | 3,384   |        3,384 |
| nested contract             | 96 B/op     | 96      |           96 |

The string, map and nested paths already matched the oracle byte for byte. Every byte of the gap was
the repeated path.

## The cause was the accumulator, and the count was already on the wire

An array member decoded into a `List<T>` that doubled from empty and was then copied out of, so 128
`int`s left six abandoned buffers plus a duplicate of the answer. A packed run carries the element
count already: `WProtoReader.CountPackedElements` reads it off the encoded bytes without consuming
them — **exactly**, because a varint element ends at the byte whose continuation bit is clear, and a
fixed-width run divides — and the generator spends it:

- arrays accumulate into `WProtoArrayBuilder<T>`, a struct that reserves once and **hands its buffer
  over** when it comes out exactly full, so there is no growth and no copy;
- every `List<T>`-backed accumulator (a `List<T>` member, an interface-typed member,
  `ReadOnlyCollection<T>`, `Stack<T>`, and a deferred read's pending list) is sized through
  `WProtoRepeated.Reserve`;
- `HashSet<T>`, `Queue<T>`, `LinkedList<T>` and consumer collections are left alone: no capacity API
  they all have on every Unity version this package supports.

The result is `5,272 -> 4,088 B/op` aggregate, **below the oracle's 4,112**, with throughput
unchanged (2,153-2,237 ns/op across three runs against a 2,213 ns/op baseline). Nothing about the
wire format moved, so no golden vector changed.

**An unpacked run gets none of this and should not.** It is a sequence of separate fields that may be
interleaved with other members, so its length is unknowable until it ends. That is what protobuf-net
writes, and reading it still grows exactly as before.

## The bar is the oracle, not a constant

Both gates — the aggregate and the per-shape one — assert against protobuf-net's own allocation for
the same contract rather than a hand-written ceiling. Two implementations returning the same graph
must allocate the same, so anything above it is overhead this package chose; and no number needs
re-tuning when a runtime changes what a `Dictionary` costs. The previous fixed ceiling of `5,272`
carried a comment asking for exactly this when the allocations came down.

## Verification

- Generator suite, protobuf-net **3.2.56**: 318/318, three consecutive runs.
- Generator suite, protobuf-net **2.4.9**: 317/317. The v2 oracle allocates far more on every shape
  (1,800 B/op for `int[128]`), which is why the comparison is pinned per oracle rather than shared.
- `npm run typecheck:unity`: all four legs (runtime default and legacy, tests default and legacy),
  zero warnings.
- Real Unity **6000.4.6f1** editor over the MCP bridge: **39 of 39 package assemblies fresh**, empty
  console, and **210 of 210** WProto fixtures passing in PlayMode — the new read-sizing fixture plus
  every collection, map, include, surrogate, marshal, generic, facade and wire-format fixture.
- The editor also caught a defect the desktop suite could not: `AVarintRunCountsItsElementsExactly`
  wrote 1,000 mixed-sign `int`s into a fixed 4 KB scratch buffer, and a negative `int32` varint is
  ten bytes. Only the 1000-element case failed. The scratch is sized from the widest encoding now.
- No Unity license is configured in this devcontainer (`setup-license.ps1 -Check` reports it), so the
  Docker EditMode/PlayMode legs are CI's; the MCP editor is the local substitute.

## Folded-in CI work

**#428 — the Copilot reviewer's quota failure is not a repository failure.** It fails with HTTP 402
before reading any code, twice on #427, and every push re-requests it and reproduces the same red.
No repository change can clear it. The supported policy (the issue's option 3) is now written where a
landing session actually reads it — [ship-changes Step 10](../.llm/skills/ship-changes.md) — with the
signature to recognize it by (no analysis produced, sub-minute duration, HTTP 402) and the rule that
repository-owned checks are what "all green" means. Restoring the quota or dropping the reviewer from
the required set stays an owner action in organization settings.

## What #398 has left, and why it is not this session's work

The string interner and "merge into the caller's existing collection" both change what a read hands
back rather than how it accumulates, and the measurement does not indict either: `string` and the map
path already allocate exactly what protobuf-net does. Reopen them with a shape that measures worse,
not on the reasoning in the issue.

## Session limitation to hand back

This session had no GitHub connector and no token: the branch pushes over the `pr` SSH remote, but
**opening the pull request and filing follow-up issues need the VS Code GitHub extension**. Nothing
else in the session depended on it.

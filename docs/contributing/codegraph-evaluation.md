# CodeGraph evaluation

[Issue #710](https://github.com/Ambiguous-Interactive/unity-helpers/issues/710) asks whether CodeGraph
should be installed automatically for agent sessions. The September 5, 2026 decision is **no automatic
installation of 1.6.0**. Focused queries recover useful source and caller relationships, but this
release also omits a direct C# overload call and resolves a delegate type to an unrelated private
enum member. These graph defects, together with a separate qualified CLI lookup defect, make its
dependency results insufficiently reliable for automatic agent integration. Optional use with
source verification remains reasonable; re-evaluate when these controls pass.

## Measured environment

The experiment used an isolated archive of local merge `a62af3d6`, whose tree is identical to
published commit `14adde00`. The host ran Linux x64, Node 22.23.2 and
npm package `@colbymchenry/codegraph@1.6.0`. Telemetry was disabled with `CODEGRAPH_TELEMETRY=0`.
No agent installer, global configuration, package dependency or development-container setting changed.
The [upstream README](https://github.com/colbymchenry/codegraph/blob/b9ca4b7981116909900368cc1686a1074cd4d4c1/README.md)
describes the CLI and its single-tool MCP interface. These measurements concern the published npm
binary, whose platform package includes its own runtime. The inspected implementation is the
[published Linux x64 1.6.0 package](https://registry.npmjs.org/@colbymchenry/codegraph-linux-x64/-/codegraph-linux-x64-1.6.0.tgz),
under `node_modules/@colbymchenry/codegraph-linux-x64/lib/`: `dist/bin/codegraph.js` implements CLI
caller lookup, `dist/mcp/tools.js` implements focused output and the MCP tool allowlist, and
`dist/graph/traversal.js` implements graph traversal. This pins the implementation being evaluated;
the README alone is not evidence of the published binary's behavior.

| Measurement                                |        Observed result |
| ------------------------------------------ | ---------------------: |
| Full initialization, process wall time     |           9.52 seconds |
| Parsing/indexing time reported by the tool |            3.7 seconds |
| Peak child-process resident memory         |          2,262,396 KiB |
| Installed package tree                     |                283 MiB |
| Project index                              |                179 MiB |
| Indexed source files                       |                  2,389 |
| Nodes / edges                              |       57,694 / 155,630 |
| Qualified caller commands                  | 0.15–0.45 seconds each |

Peak memory is the child-process high-water measurement, not a steady-state server claim. No
whole-session token saving, MCP latency improvement or CI cost reduction was measured.

## Caller controls and root cause

The controls compare known direct production/test call sites against the result, excluding strings
that merely name an API. For example, `AssetPostprocessorContractTests` stores a search expression;
it is not itself a caller of `Schedule`.

| Qualified CLI query                     | Expected caller files | Returned caller files |
| --------------------------------------- | --------------------: | --------------------: |
| `SerializationCapacityLimits.Clamp`     |                     6 |                     6 |
| `SerializationCapacityLimits.TryAccept` |                     6 |                     6 |
| `ValidationResults.MergeScopedRun`      |                     1 |                     1 |
| `AssetPostprocessorDeferral.Schedule`   |                     5 |                     0 |
| `EditorTheme.Apply`                     |                     2 |                     0 |

These missing **external caller results** are a CLI selection defect: querying the graph API with
each method's exact node ID returns the five `Schedule` caller files and both public `Apply` caller
files. This does not establish that every relationship inside those methods is correct. A minimal two-file
C# control also resolves correctly with and without `#if UNITY_EDITOR`; the conditional-compilation
hypothesis was rejected.

The published CLI's `dist/bin/codegraph.js` compares the entire qualified query against the node's
simple name. When that removes every candidate, it uses only the highest-ranked search result.
That result may be a class or the wrong overload. In this experiment the two-parameter `Apply`
overload ranks above the public one-parameter overload and has no direct callers in the index.
An empty caller list therefore cannot be treated as evidence that a change has no dependents.

## Focused alternatives and graph fidelity

The primary `explore` command recovers useful relationships. The initial theme query returns
24,872 bytes and 70 symbols, including unrelated validation methods also named `Apply`; the import
callback query returns 25,023 bytes and 77 symbols, both with `--max-files 6`. More focused commands
substantially reduce that output:

| Command, with the same project `--path`                                     | Output bytes | Useful result                                                  |
| --------------------------------------------------------------------------- | -----------: | -------------------------------------------------------------- |
| `node Apply --file Editor/Styles/EditorTheme.cs`                            |        1,506 | Both overload bodies and the two external caller files         |
| `node Schedule --file Editor/AssetProcessors/AssetPostprocessorDeferral.cs` |        3,195 | Method body and 12 caller methods in five files                |
| `explore 'Editor/Styles/EditorTheme.cs Apply' --max-files 1`                |        7,619 | Six symbols in one source file, with additional impact context |

The focused `explore` result still includes unrelated `Apply` methods in its impact context. The
`node` results are a useful navigation aid, so broad-query size alone would not justify rejection.
The default MCP exposes `explore`; `node` can be enabled through `CODEGRAPH_MCP_TOOLS`. These CLI
measurements do not establish default MCP latency or imply that its lookup shares the CLI defect.

Exact-node SDK queries independently reveal two graph defects:

- `EditorTheme.Apply(VisualElement)` directly calls `Apply(VisualElement, bool)` at source line 19.
  The first node's `getCallees` and the second node's `getCallers` both return empty arrays. A minimal
  newly added C# class with `Resolve(int value) => Resolve(value, 1)` and a two-parameter overload
  reproduces the missing relationship after a successful `sync`: both overloads are indexed, but
  neither has callers or callees. This is independent of the CLI's wrong candidate selection and
  does not require Unity conditional compilation.
- The `Action` parameter type in `AssetPostprocessorDeferral.Schedule(Action drain)` is linked to
  the unrelated private enum member `SingleThreadedThreadPool.WorkItemType.Action`, rather than
  `System.Action`. Its edge kind is **`references`**, with `resolvedBy: exact-match` and confidence
  `0.9`; it is not a call edge. The enum member is inaccessible from the referring class, so this
  relationship cannot describe the C# source correctly.

The SDK's `getCallees` combines `calls`, `references`, `imports` and `instantiates`, while the focused
output labels its trail `Calls`. Inspect edge kinds before treating these results as a direct call
oracle. In particular, `Schedule` legitimately references its `PendingDrains` field and the
`DrainScheduled` method group assigned to `EditorApplication.delayCall`; those are not additional
wrong targets. Delegate registration does not itself directly invoke the registered method.

The decision therefore rests on demonstrated graph fidelity problems, not a requirement to replace
all text search. The positive controls and focused output support continued optional evaluation,
but neither indexing speed nor unmeasured token savings outweigh these defects for automatic use.

## Replay

Install the exact release in a disposable location and index a disposable package checkout:

```bash
experiment_dir="$(mktemp -d)"
npm install --prefix "$experiment_dir/tool" --ignore-scripts --no-audit --no-fund @colbymchenry/codegraph@1.6.0
mkdir -p "$experiment_dir/subject"
git archive 14adde00 | tar -x -C "$experiment_dir/subject"
export CODEGRAPH_TELEMETRY=0
codegraph_cli="$experiment_dir/tool/node_modules/.bin/codegraph"
"$codegraph_cli" init --yes "$experiment_dir/subject"
"$codegraph_cli" callers EditorTheme.Apply --path "$experiment_dir/subject" --json
"$codegraph_cli" callers AssetPostprocessorDeferral.Schedule --path "$experiment_dir/subject" --json
"$codegraph_cli" explore 'Find callers of EditorTheme.Apply' --path "$experiment_dir/subject" --max-files 6
"$codegraph_cli" node Apply --file Editor/Styles/EditorTheme.cs --path "$experiment_dir/subject"
"$codegraph_cli" node Schedule --file Editor/AssetProcessors/AssetPostprocessorDeferral.cs --path "$experiment_dir/subject"
"$codegraph_cli" explore 'Editor/Styles/EditorTheme.cs Apply' --path "$experiment_dir/subject" --max-files 1
```

For the overload control, add this file inside the disposable subject, run
`codegraph sync --path <subject>`, then inspect each method node by file and start line:

```csharp
namespace CodeGraphControls
{
    public static class OverloadTarget
    {
        public static int Resolve(int value) { return Resolve(value, 1); }
        public static int Resolve(int value, int offset) { return value + offset; }
    }
}
```

Use the exact installed package's SDK (`require('@colbymchenry/codegraph')`), open the subject with
`await CodeGraph.open(subject)`, search for the symbol, and select the method nodes by `filePath`
and `startLine`. Inspect `getCallers(node.id)` and `getCallees(node.id)`, including each returned
`edge.kind` and target. This bypasses CLI candidate selection; do not use an unqualified search's
first result as an independent oracle.

The installer is intentionally not part of this replay: `codegraph install` configures agents,
while `init` builds only the selected project's index. Retain ordinary source inspection as the
oracle when evaluating a later release, including overloaded names and source added after indexing.

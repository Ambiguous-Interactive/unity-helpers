# unity-mcp-fixture-runner - Part 1

## Split Content

## When to Use

- There is no Unity license, and a change needs to run against a real editor
- A change touches `Editor/` or `Tests/`, which `npm run typecheck:unity` does not compile
- A fixture has to be selected, run and its result believed

For timing, allocation and staleness gates, see
[unity-mcp-measurement](../skills/unity-mcp-measurement.md). For the licensed Docker legs, see
[unity-devcontainer-testing](../skills/unity-devcontainer-testing.md).

### Match regression evidence to the required test mode

When acceptance requires EditMode and PlayMode, verify that the target regression is discovered
and passes in each requested mode. A green Editor suite does not cover an oracle selected only
from a Runtime test assembly. Inspect assembly selection and the actual NUnit case results before
claiming coverage. Share the oracle through runtime-capable test support with thin fixtures in
each required assembly; let the real TestRunnerApi perform setup, parameter expansion and teardown.
Skipped or inconclusive target cases do not satisfy the requirement, even when the overall job passes.

## The RunCommand contract, measured on 2026-08-27 (editor 6000.4.6f1)

### Bridge backend determines the tool names

`UNITY_MCP_BACKEND` selects the child MCP server the bridge spawns; the
[MCP server catalog](../../scripts/mcp/README.md) documents the options. Everything measured below
is the `relay` backend, whose tools are
`Unity_*`-prefixed. Under the default `cli` backend the Pipeline package serves differently named
tools with the same jobs: `Unity_RunCommand` → `eval` / `run_script`,
`Unity_ManageEditor GetState` → `editor_status`, `Unity_ReadConsole` → `get_console_logs`. The
result shapes differ (`eval` returns structured output, not an `executionLogs` field), so the
timings and traps in this skill are relay measurements until re-measured against Pipeline.

The host is the `com.unity.ai.assistant` package inside the editor (a throw stack names
`AgentRunCommand.Execute` in that package), not the standalone unity-mcp-server where
[#583](https://github.com/Ambiguous-Interactive/unity-helpers/issues/583) watched every execution
fail closed with `No logs available`. That failure does not reproduce here: the template below
compiles, runs, and its output arrives.

```csharp
using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        result.Log("RESULT answer");
    }
}
```

- The class MUST be named `CommandScript`, `internal`, with
  `public void Execute(ExecutionResult result)`. `IRunCommand` declares that one method and nothing
  else. The sandbox wraps the class in `Unity.AI.Assistant.Agent.Dynamic.Extension.Editor`.
- `result.Log(string)` text arrives in the tool result's `executionLogs` field. **`result.Log` does
  not format** — `"{0:E3}"` prints literally; build the string first.
- `Debug.Log` is NOT in `executionLogs` — it goes to the editor console only. Only `result.Log`
  output is relayed back.
- `result.LogWarning` / `result.LogError` / `Debug.LogWarning` / any exception from the body are
  reported as `success: false` with
  `UNEXPECTED_ERROR: Command was executed partially, but reported warnings or errors:` followed by
  the complete log payload. **The body ran to completion** — read the payload before concluding the
  probe failed; the discriminator is whether your RESULT line is inside it. A deliberate
  `result.LogError` is therefore the way a probe reports failure without losing its evidence.
- `ExecutionResult` surface worth knowing (fields, since properties do not appear):
  `RegisterObjectCreation(Object|Component)`, `RegisterObjectModification(Object, string)`,
  `DestroyObject(Object)`, `Log/LogWarning/LogError(string, params Object[])`, `GetFormattedLogs()`,
  `ConsoleLogs`, `Logs`, `UndoGroup`, `CommandName`, `SuccessfullyStarted`.
- Statement-level scripts fail `CS8805`; everything must live in the class.

### When the editor refuses to recompile your edited sources (measured 2026-08-27)

`AssetDatabase.Refresh()` inside a sandbox command can return normally and recompile nothing, and
`CompilationPipeline.RequestScriptCompilation()` called unqualified resolves to the
`Unity.CompilationPipeline` namespace collision (see below) or otherwise does nothing. The recipe
that actually moved the pipeline:

```csharp
bool invoked = EditorApplication.ExecuteMenuItem("Assets/Refresh");
UnityEditor
    .Compilation
    .CompilationPipeline
    .RequestScriptCompilation(UnityEditor.Compilation.RequestScriptCompilationOptions.None);
```

`RequestScriptCompilation` then reports `isCompiling=True` within the same command, and the domain
reload follows a minute or two later.

**The failure mode that hides this: a compile error in a package test assembly is INVISIBLE.** The
editor keeps serving the stale DLL, reports no error in the tool result, and `Unity_ReadConsole`
returns zero entries for the CS error. A session burned forty minutes on probes that "looked up
null" before comparing source mtime against the DLL from inside the sandbox:

```csharp
string dll = System.IO.Path.Combine(UnityEngine.Application.dataPath, "..",
    "Library", "ScriptAssemblies", "WallstopStudios.UnityHelpers.Tests.Core.dll");
bool stale = !System.Text.Encoding.ASCII
    .GetString(System.IO.File.ReadAllBytes(dll)).Contains("YourNewSymbolName");
```

`GetLastWriteTime` comparisons work too. Diagnose with `npm run typecheck:tests` from the container
— it catches the same CS error in seconds — fix, and only then re-probe. Never conclude "the
sandbox cannot see my change" from a null lookup; check for a hidden compile error first.

### A NEW `.cs` file DOES reach the pipeline -- the 2026-08-31 claim is refuted

[#656](https://github.com/Ambiguous-Interactive/unity-helpers/issues/656) recorded that a file
**added** under `Tests/Runtime/**` from inside the container never reached the compiled assembly
while a **modified** one did, and blamed Unity's Directory Monitoring preference. **Re-measured
2026-09-01 on the same editor `6000.4.6f1` and the same project, and it does not reproduce.** Two
subjects, both written from the container and probed minutes later:

| Subject                                  | AssetDatabase                            | `sourceFiles`                                                                                       | Loaded type           |
| ---------------------------------------- | ---------------------------------------- | --------------------------------------------------------------------------------------------------- | --------------------- |
| `Runtime/Utils/StackAllocation.cs`       | imported, Unity wrote the `.meta` itself | in `WallstopStudios.UnityHelpers`                                                                   | present in the `.dll` |
| `Tests/Runtime/Issue656PipelineProbe.cs` | GUID resolves, `MonoScript` loads        | in `WallstopStudios.UnityHelpers.Tests.Runtime` under **both** `AssembliesType.Editor` and `Player` | --                    |

So Directory Monitoring is not eating new directory entries, and **do not spend a session working
around a limitation that is not there.** Add the fixture.

**What to check before concluding a file is missing, because it is what the original probe did not.**
Ask whether the ASSEMBLY appeared, not only whether your file did:

```csharp
foreach (UnityEditor.Compilation.Assembly assembly in
    UnityEditor.Compilation.CompilationPipeline.GetAssemblies(
        UnityEditor.Compilation.AssembliesType.Editor))
{
    if (assembly.name == "WallstopStudios.UnityHelpers.Tests.Runtime")
    {
        result.Log("RESULT sources=" + assembly.sourceFiles.Length);
    }
}
```

A zero here means the scope is wrong and every file in that assembly reads as absent -- which looks
exactly like "my file did not land". Measured: `PlayerWithoutTestAssemblies` contains **no** test
assembly at all (73 assemblies, `sawTestsRuntime=False`), so a probe using it reports a clean miss
for a file that is compiled. `Editor` sees 247 assemblies and `Player` 91, and the probe file is in
both. Same shape as [honest-gates](../skills/honest-gates.md): a search that found nothing must first prove
it had somewhere to look.

### Anything needing consent kills the command, and the log payload with it

Measured session 244. A sandbox call Unity treats as needing user consent aborts the **whole**
command with `UNEXPECTED_ERROR: User interactions are not supported for MCP tool calls` and
**discards the log payload**, so you cannot see how far it got -- and `try`/`catch` does not catch
it. It is a runtime trap, not static analysis: the same call sitting in unreachable code runs fine.

Bisected:

| Operation                                                                                                                                                       | In `Assets/` | In `Packages/<embedded package>/` |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------ | --------------------------------- |
| `AssetDatabase.DeleteAsset`, `File.Delete`, `Directory.Delete`                                                                                                  | **blocked**  | **blocked** (even under `Temp/`)  |
| `AssetDatabase.CreateFolder`                                                                                                                                    | **blocked**  | works                             |
| `File.WriteAllText`, `Directory.CreateDirectory`, `CreateAsset`, `ImportAsset`, `SaveAssetIfDirty`, `PrefabUtility.SaveAsPrefabAsset`, `ForceReserializeAssets` | work         | work                              |

**So build subjects inside the embedded package** -- which is the container's own working tree --
and clean them up with `rm -rf` from the container, where nothing is blocked. To run a committed
fixture that hard-codes `Assets/`, set its private path fields by reflection before invoking the
test methods: every assertion in the committed file then runs, and only the two lines choosing the
root are bypassed.

### A `Unity_RunCommand` that times out is usually an expired MCP session

Measured 2026-09-01. Four consecutive `Unity_RunCommand` calls returned `The operation timed out`,
including one whose whole body was a single `result.Log`. Throughout, `Unity_ManageEditor`
`GetState` answered instantly with `IsCompiling: false`, and `Unity_ReadConsole` showed no error --
so **a live editor and a working sibling tool do not mean `RunCommand` works.**

The next call returned `MCP server "unity-mcp-remote" session expired`, and the one after that
succeeded. **So retry once: the timeout is the symptom, the expiry notice is the diagnosis, and the
reconnect is automatic.** Do not read a timeout as a broken bridge, a busy editor, or a bad script,
and do not start rewriting the probe -- a session burned five calls and half an hour on that
reading before the expiry surfaced.

# Unity MCP Fixture Runner

<!-- trigger: mcp-fixture, mcp-test, unity-mcp-run, reflection-fixture | Run the package's real test fixtures through the Unity MCP bridge without a license | Feature -->

## Reference Parts

- [Part 1](../references/unity-mcp-fixture-runner-part-1.md)
- [Part 2](../references/unity-mcp-fixture-runner-part-2.md)
- [Part 3](../references/unity-mcp-fixture-runner-part-3.md)

## When to Use

[Read section](../references/unity-mcp-fixture-runner-part-1.md#when-to-use)

### [Match regression evidence to the required test mode](../references/unity-mcp-fixture-runner-part-1.md#match-regression-evidence-to-the-required-test-mode)

## The RunCommand contract, measured on 2026-08-27 (editor 6000.4.6f1)

[Read section](../references/unity-mcp-fixture-runner-part-1.md#the-runcommand-contract-measured-on-2026-08-27-editor-600046f1)

### [Bridge backend determines the tool names](../references/unity-mcp-fixture-runner-part-1.md#bridge-backend-determines-the-tool-names)

### [When the editor refuses to recompile your edited sources (measured 2026-08-27)](../references/unity-mcp-fixture-runner-part-1.md#when-the-editor-refuses-to-recompile-your-edited-sources-measured-2026-08-27)

### [A NEW `.cs` file DOES reach the pipeline -- the 2026-08-31 claim is refuted](../references/unity-mcp-fixture-runner-part-1.md#a-new-cs-file-does-reach-the-pipeline----the-2026-08-31-claim-is-refuted)

### [Anything needing consent kills the command, and the log payload with it](../references/unity-mcp-fixture-runner-part-1.md#anything-needing-consent-kills-the-command-and-the-log-payload-with-it)

### [A `Unity_RunCommand` that times out is usually an expired MCP session](../references/unity-mcp-fixture-runner-part-1.md#a-unity_runcommand-that-times-out-is-usually-an-expired-mcp-session)

## No license? The MCP editor still runs the real fixtures

[Read section](../references/unity-mcp-fixture-runner-part-2.md#no-license-the-mcp-editor-still-runs-the-real-fixtures)

### [The editor compiles what `typecheck:unity` cannot, and an empty console proves nothing](../references/unity-mcp-fixture-runner-part-3.md#the-editor-compiles-what-typecheckunity-cannot-and-an-empty-console-proves-nothing)

### [Three things the probe itself gets wrong (session 224)](../references/unity-mcp-fixture-runner-part-3.md#three-things-the-probe-itself-gets-wrong-session-224)

### [Three the bridge itself gets wrong (session 225)](../references/unity-mcp-fixture-runner-part-3.md#three-the-bridge-itself-gets-wrong-session-225)

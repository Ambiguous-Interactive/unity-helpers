# Use Unity Helpers with an AI Assistant

The repository's [llms.txt index](https://github.com/wallstop/unity-helpers/blob/main/llms.txt)
provides a short feature overview and links to the public guides. Give it to an assistant when you
want help using Unity Helpers in your game.

## Provide your project context

Include the package version, Unity version, scripting backend and the feature you need. State any
constraints, such as preserving existing saves or avoiding allocations during a frame update.

For example:

```text
I use Unity Helpers 3.6.2 in Unity 2022.3 with an IL2CPP player.
Help me choose a collection for a read-mostly entity lookup.
Keys include every signed integer value, and callers require IDictionary<int, Health>.
Use the public collection guide and explain unsupported keys and interfaces.
```

Ask the assistant to open the linked feature page and check the actual overloads before generating
code. The index is a starting point, not a complete API reference. Documentation on main can include
unreleased features; use the release tag matching your installed package when that distinction matters.

## Review the result

- Compile the generated example in your Unity project, including its namespace and asmdef references.
- Check null handling, failure results and ownership of any pooled data.
- Exercise the shipping player backend and stripping level when reflection or generated code is involved.
- Read performance results in their stated environment; an Editor measurement is not a device guarantee.
- Keep retained-save compatibility checks when changing serialization code.

## Contributing to Unity Helpers itself

Package contributors should also read the [contribution guide](./contributing.md) and
[repository agent guidelines](https://github.com/wallstop/unity-helpers/blob/main/.llm/context.md).
Those describe this repository's development checks. You do not need its coding-agent setup,
runner credentials or test harness to consume Unity Helpers in a game.

## Related

- [Feature Index](../overview/index.md)
- [Getting Started](../overview/getting-started.md)
- [Serialization choices](../features/serialization/serialization.md)

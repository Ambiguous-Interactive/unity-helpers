# Reference identity and Unity object maps

`ReferenceIdentityComparer<T>` compares managed references with `ReferenceEquals`
and hashes them with `RuntimeHelpers.GetHashCode`. It does not call a key's
`Equals` or `GetHashCode` override. Use its shared `Instance` in dictionaries and
sets when value equality would hide distinct objects.

```csharp
using System.Collections.Generic;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Helper;

Dictionary<GameObject, int> counts = new(
    ReferenceIdentityComparer<GameObject>.Instance);
```

A destroyed Unity object still has a managed reference. That reference remains a
valid lookup or removal key, and two different destroyed objects remain distinct.
The comparer accepts null: two managed null references compare equal and null
hashes to zero. A normal `Dictionary` still refuses null keys.

## Pruning destroyed Unity keys

`UnityObjectMap<TKey, TValue>` in `WallstopStudios.UnityHelpers.Utils` uses the
comparer and provides explicit cleanup. Use all map operations on Unity's main
thread. It is not a concurrent collection.

```csharp
using WallstopStudios.UnityHelpers.Utils;

using UnityObjectMap<GameObject, int> counts = new(autoPruneOnSceneUnload: true);
counts.TrySet(source, 1);
if (counts.TryGetValue(source, out int count))
{
    UnityEngine.Debug.Log(count);
}
int removed = counts.PruneDestroyed();
```

- `TrySet` inserts or replaces an entry. Managed null keys are refused. Values
  can be null, and destroyed keys can be stored for diagnostics.
- `TryGetValue`, `ContainsKey` and `Remove` ask about the exact managed reference,
  including destroyed objects. `Count` includes entries awaiting cleanup.
- `PruneDestroyed` removes exactly the destroyed keys and returns their count.
  Live keys remain. Its temporary list comes from `Buffers<TKey>.List`.
- `Clear` releases all entries. `Dispose` also disables automatic pruning.
  Disposal is idempotent; later insertion and lookup return false.

Set `AutoPruneOnSceneUnload` to opt in or out. A single cached scene-unload
handler services all enrolled maps. Weak registration lets an abandoned map and
its values be collected. Repeated opt-in does not add another subscription.

At `SubsystemRegistration`, enrolled maps are cleared, their opt-in flags are
reset, and the handler is removed. This prevents previous play-session state
from surviving when domain reload is disabled. Enable automatic pruning again
when initializing each play session. Maps that never opt in stay entirely
manual, including session cleanup.

The logger and serializer use the same public comparer. Run
`npm run test:stable-assets` for portable comparer and canonicalizer tests. The
Unity fixtures also check destroyed-key lookup, exact pruning and lifecycle
cleanup.

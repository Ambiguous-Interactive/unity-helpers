# use-serializable-types-patterns - Part 2

## Split Content

### Weighted Random Selection

```csharp
public sealed class LootDropper : MonoBehaviour
{
    [SerializeField]
    private SerializableDictionary<string, int> _dropWeights = new()
    {
        { "Common", 70 },
        { "Uncommon", 20 },
        { "Rare", 8 },
        { "Epic", 2 }
    };

    public string GetRandomDrop(IRandom random)
    {
        int totalWeight = 0;
        foreach (KeyValuePair<string, int> entry in _dropWeights)
        {
            totalWeight += entry.Value;
        }

        int roll = random.Next(totalWeight);
        int cumulative = 0;

        foreach (KeyValuePair<string, int> entry in _dropWeights)
        {
            cumulative += entry.Value;
            if (roll < cumulative)
            {
                return entry.Key;
            }
        }

        return "Common";
    }
}
```

### State Machine Transitions

```csharp
public sealed class StateMachine : MonoBehaviour
{
    [SerializeField]
    private SerializableDictionary<string, SerializableHashSet<string>> _validTransitions = new();

    private string _currentState = "Idle";

    public bool TryTransition(string targetState)
    {
        if (!_validTransitions.TryGetValue(_currentState, out SerializableHashSet<string> allowed))
        {
            return false;
        }

        if (!allowed.Contains(targetState))
        {
            return false;
        }

        _currentState = targetState;
        return true;
    }
}
```

### Save/Load with Versioning

```csharp
[ProtoContract]
public class VersionedSaveData
{
    [ProtoMember(1)]
    public int Version { get; set; } = 2;

    [ProtoMember(2)]
    public SerializableDictionary<string, int> PlayerStats { get; set; } = new();

    [ProtoMember(3)]
    public SerializableHashSet<WGuid> CompletedQuests { get; set; } = new();

    [ProtoMember(4)]
    public SerializableNullable<DateTime> LastPlayed { get; set; } = new();

    public void Migrate()
    {
        // Handle version migrations
        if (Version < 2)
        {
            // Migration logic from v1 to v2
            Version = 2;
        }
    }
}
```

### Caching with Nullable

```csharp
public sealed class ExpensiveCalculation : MonoBehaviour
{
    [SerializeField]
    private SerializableNullable<float> _cachedResult = new();

    [SerializeField]
    private SerializableDictionary<string, float> _parameterCache = new();

    public float Calculate(string parameter)
    {
        if (_parameterCache.TryGetValue(parameter, out float cached))
        {
            return cached;
        }

        float result = PerformExpensiveCalculation(parameter);
        _parameterCache[parameter] = result;
        return result;
    }

    public void InvalidateCache()
    {
        _cachedResult.Clear();
        _parameterCache.Clear();
    }

    private float PerformExpensiveCalculation(string parameter)
    {
        // Expensive work here
        return 0f;
    }
}
```

---

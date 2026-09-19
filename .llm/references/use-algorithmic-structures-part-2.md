# use-algorithmic-structures - Part 2

## Split Content

## Trie

Array-backed prefix tree for fast string operations. Optimized for prefix search and exact word lookup with minimal allocations.

### API

```csharp
Trie trie = new Trie(wordCollection);

trie.Contains(word);                              // Exact match
trie.GetWordsWithPrefix(prefix, results, max);    // Prefix search
trie.Count;                                       // Number of words

// Iteration
foreach (string word in trie) { }
```

### Example: Command Autocomplete

```csharp
public class CommandSystem
{
    private readonly Trie _commands;
    private readonly Dictionary<string, Action> _handlers;

    public CommandSystem()
    {
        string[] validCommands = { "spawn", "speed", "spectate", "save", "settings" };
        _commands = new Trie(validCommands);
        _handlers = new Dictionary<string, Action>();
    }

    public List<string> GetSuggestions(string input, int maxResults = 5)
    {
        List<string> results = new List<string>();
        _commands.GetWordsWithPrefix(input, results, maxResults);
        return results;
    }

    public bool TryExecute(string command)
    {
        if (_commands.Contains(command) && _handlers.TryGetValue(command, out Action handler))
        {
            handler?.Invoke();
            return true;
        }
        return false;
    }
}
```

### Example: Word Validation

```csharp
public class WordValidator
{
    private readonly Trie _dictionary;

    public WordValidator(IEnumerable<string> validWords)
    {
        _dictionary = new Trie(validWords);
    }

    public bool IsValidWord(string word)
    {
        return _dictionary.Contains(word);
    }

    public bool HasWordsStartingWith(string prefix)
    {
        List<string> results = new List<string>();
        _dictionary.GetWordsWithPrefix(prefix, results, maxResults: 1);
        return results.Count > 0;
    }
}
```

---

## TimedCache\<T\>

Lightweight time-based cache that recomputes values after a TTL expires. Optional jitter prevents thundering herd.

### API

```csharp
TimedCache<T> cache = new TimedCache<T>(
    valueProducer,      // Factory function
    cacheTtl,           // Time to live in seconds
    useJitter,          // Optional: spread refreshes
    timeProvider,       // Optional: custom time source
    jitterOverride      // Optional: custom jitter amount
);

cache.Value;            // Get cached value, recomputes if expired
cache.Reset();          // Force recomputation on next access
```

### Example: Expensive Query Cache

```csharp
public class EnemyRadar : MonoBehaviour
{
    private TimedCache<int> _nearbyEnemyCount;
    private TimedCache<Enemy> _closestEnemy;
    private Collider[] _colliders = new Collider[32];
    private int enemyLayer;

    private void Awake()
    {
        // Recompute enemy count every 0.5 seconds with jitter
        _nearbyEnemyCount = new TimedCache<int>(
            () => Physics.OverlapSphereNonAlloc(transform.position, 50f, _colliders, enemyLayer),
            cacheTtl: 0.5f,
            useJitter: true
        );

        // Cache closest enemy for 0.25 seconds
        _closestEnemy = new TimedCache<Enemy>(
            () => FindClosestEnemy(),
            cacheTtl: 0.25f
        );
    }

    public int NearbyEnemyCount => _nearbyEnemyCount.Value;
    public Enemy ClosestEnemy => _closestEnemy.Value;

    private Enemy FindClosestEnemy() { /* ... */ return null; }
}
```

### Example: Configuration Cache

```csharp
public class ConfigCache
{
    private readonly TimedCache<GameConfig> _config;

    public ConfigCache()
    {
        // Reload config every 60 seconds
        _config = new TimedCache<GameConfig>(
            () => LoadConfigFromFile(),
            cacheTtl: 60f,
            useJitter: true  // Spread reloads across instances
        );
    }

    public GameConfig Config => _config.Value;

    public void ForceReload() => _config.Reset();

    private GameConfig LoadConfigFromFile() { /* ... */ return null; }
}
```

---

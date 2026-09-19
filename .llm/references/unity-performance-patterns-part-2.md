# unity-performance-patterns - Part 2

## Split Content

## GameObject Pooling

### Unity's ObjectPool (Unity 2021+)

```csharp
using UnityEngine.Pool;

public class BulletManager : MonoBehaviour
{
    [SerializeField] private GameObject _bulletPrefab;

    private ObjectPool<GameObject> _bulletPool;

    void Awake()
    {
        _bulletPool = new ObjectPool<GameObject>(
            createFunc: () => Instantiate(_bulletPrefab),
            actionOnGet: bullet => bullet.SetActive(true),
            actionOnRelease: bullet => bullet.SetActive(false),
            actionOnDestroy: bullet => Destroy(bullet),
            defaultCapacity: 50,
            maxSize: 200
        );
    }

    public GameObject SpawnBullet(Vector3 position)
    {
        GameObject bullet = _bulletPool.Get();
        bullet.transform.position = position;
        return bullet;
    }

    public void ReturnBullet(GameObject bullet)
    {
        _bulletPool.Release(bullet);
    }
}
```

### What to Pool

- Projectiles (bullets, missiles)
- Particle effects
- Enemies that spawn/despawn frequently
- UI elements that appear/disappear
- Audio sources for sound effects
- Any frequently Instantiated/Destroyed object

See [use-pooling](../skills/use-pooling.md) for detailed pooling patterns.

---

## Debug.Log Performance

### Remove in Production

```csharp
// ❌ BAD: Debug.Log still executes in builds (string allocation)
Debug.Log($"Player position: {transform.position}");

// ✅ GOOD: Conditional compilation
#if UNITY_EDITOR
Debug.Log($"Player position: {transform.position}");
#endif

// ✅ GOOD: Use [Conditional] attribute for debug methods
[System.Diagnostics.Conditional("UNITY_EDITOR")]
private void LogDebug(string message)
{
    Debug.Log(message);
}
```

---

## String Operations in Unity

### Update Text Efficiently

```csharp
// ❌ BAD: Creates strings every frame
void Update()
{
    scoreText.text = "Score: " + score.ToString();  // 3 allocations!
}

// ✅ BETTER: Only update when changed
private int _lastScore = -1;

void Update()
{
    if (score != _lastScore)
    {
        scoreText.text = "Score: " + score.ToString();
        _lastScore = score;
    }
}

// ✅ BEST: Separate label from value
public TMP_Text scoreLabelText;  // "Score: "
public TMP_Text scoreValueText;  // Just the number

void Start()
{
    scoreLabelText.text = "Score: ";
}

void UpdateScore()
{
    scoreValueText.text = score.ToString();
}
```

---

## Async Operations

### Use Unity's Awaitable (Unity 2023+)

```csharp
// ✅ GOOD: Unity's Awaitable uses pooling internally
async Awaitable LoadDataAsync()
{
    await Awaitable.WaitForSecondsAsync(1f);
    await Awaitable.NextFrameAsync();
}

// ✅ GOOD: Load scenes asynchronously
public async Awaitable LoadSceneAsync(string sceneName)
{
    AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
    asyncLoad.allowSceneActivation = false;

    while (asyncLoad.progress < 0.9f)
    {
        await Awaitable.NextFrameAsync();
    }
    asyncLoad.allowSceneActivation = true;
}
```

---

## Memory Cleanup

### Scene Transitions

```csharp
// Call during scene transitions to clean up
public void CleanupMemory()
{
    Resources.UnloadUnusedAssets();
    System.GC.Collect();
}
```

### Addressables Cleanup

```csharp
// When using Addressables
Addressables.Release(handle);
Addressables.ReleaseInstance(gameObject);
```

---

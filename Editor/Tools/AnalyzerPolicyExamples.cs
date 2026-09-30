// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    internal static class AnalyzerPolicyExamples
    {
        internal static bool TryGet(string diagnosticId, out string badCode, out string goodCode)
        {
            return TryGet(diagnosticId, out badCode, out goodCode, out _);
        }

        internal static bool TryGetExplanation(string diagnosticId, out string explanation)
        {
            return TryGet(diagnosticId, out _, out _, out explanation);
        }

        internal static bool TryGet(
            string diagnosticId,
            out string badCode,
            out string goodCode,
            out string explanation
        )
        {
            string selectedBadCode;
            string selectedGoodCode;
            string selectedExplanation;
            switch (diagnosticId)
            {
                case "WUH001":
                    selectedBadCode = "return cache.GetOrAdd(key, CreateValue);";
                    selectedGoodCode =
                        "private static readonly\n    Func<string, Value> Factory = CreateValue;\n\nreturn cache.GetOrAdd(key, Factory);";
                    selectedExplanation =
                        "Cache the factory delegate once; Unity's C# version allocates method-group delegates on each lookup.";
                    break;
                case "WUH002":
                    selectedBadCode =
                        "[SerializeField]\nprivate SerializableDictionary<\n    string,\n    List<Item>\n> items;";
                    selectedGoodCode =
                        "[SerializeField]\nprivate SerializableDictionary<\n    string,\n    SerializableList<Item>\n> items;";
                    selectedExplanation =
                        "Unity drops nested collections. A serializable wrapper preserves the inner list.";
                    break;
                case "WUH003":
                    selectedBadCode = "component?.gameObject.SetActive(true);";
                    selectedGoodCode =
                        "if (component != null)\n{\n    component.gameObject.SetActive(true);\n}";
                    selectedExplanation =
                        "Unity object equality also checks whether the native object was destroyed.";
                    break;
                case "WUH004":
                    selectedBadCode = "Assert.IsNotNull(component);";
                    selectedGoodCode = "Assert.IsTrue(component != null);";
                    selectedExplanation =
                        "NUnit null assertions test the managed wrapper; Unity equality checks the native object.";
                    break;
                case "WUH005":
                    selectedBadCode = "float angle = UnityEngine.Random.Range(0f, 360f);";
                    selectedGoodCode =
                        "private readonly IRandom random =\n    new PcgRandom(seed: 12345L);\n\nfloat angle = random.NextFloat(0f, 360f);";
                    selectedExplanation =
                        "Use an independent, seedable generator so a system's random draws can be replayed.";
                    break;
                case "WUH006":
                    selectedBadCode = "player.ApplyEffect(immobilize);";
                    selectedGoodCode = "immobilizeHandle = player.ApplyEffect(immobilize);";
                    selectedExplanation =
                        "Keep the returned handle so an infinite effect can be removed later.";
                    break;
                case "WUH007":
                    selectedBadCode = "StartCoroutine(RefreshRoutine());";
                    selectedGoodCode = "refreshRoutine = StartCoroutine(RefreshRoutine());";
                    selectedExplanation =
                        "Keep the coroutine handle to stop the routine without stopping unrelated work.";
                    break;
                case "WUH008":
                    selectedBadCode = "_ = map.TryGetValue(key, out Item item);\nitem.Refresh();";
                    selectedGoodCode =
                        "if (map.TryGetValue(key, out Item item))\n{\n    item.Refresh();\n}";
                    selectedExplanation =
                        "Read an out value only on the path where the Try call succeeds.";
                    break;
                case "WUH009":
                    selectedBadCode =
                        "protected override void OnDestroy()\n{\n    base.OnDestroy();\n    ReleaseResources();\n}";
                    selectedGoodCode =
                        "protected override void OnDestroy()\n{\n    ReleaseResources();\n    base.OnDestroy();\n}";
                    selectedExplanation =
                        "Finish teardown work before the base releases resources it may need.";
                    break;
                case "WUH010":
                    selectedBadCode = "Item item = items[key];";
                    selectedGoodCode =
                        "if (items.TryGetValue(key, out Item item))\n{\n    Use(item);\n}";
                    selectedExplanation =
                        "Handle missing keys with one lookup instead of a throwing indexer read.";
                    break;
                case "WUH011":
                    selectedBadCode =
                        "SerializedStringComparer comparer = new();\nDictionary<string, Item> items =\n    new(comparer);\ncomparer.compareMode =\n    SerializedStringComparer.StringCompareMode\n        .OrdinalIgnoreCase;";
                    selectedGoodCode =
                        "SerializedStringComparer comparer = new();\nDictionary<string, Item> items =\n    new(comparer.Freeze());\ncomparer.compareMode =\n    SerializedStringComparer.StringCompareMode\n        .OrdinalIgnoreCase;";
                    selectedExplanation =
                        "Freeze the comparer given to a collection so later edits cannot invalidate stored key hashes.";
                    break;
                case "WUH012":
                    selectedBadCode =
                        "[SerializeField]\nprivate List<GameObject> targets;\n\nforeach (GameObject target in targets)\n{\n    target.SetActive(true);\n}";
                    selectedGoodCode =
                        "[SerializeField]\nprivate List<GameObject> targets;\n\nforeach (GameObject target in targets)\n{\n    if (target != null)\n    {\n        target.SetActive(true);\n    }\n}";
                    selectedExplanation =
                        "A serialized list can retain empty rows after an asset or component is removed.";
                    break;
                case "WUH013":
                    selectedBadCode =
                        "for (int index = 0;\n    index < items.Length;\n    index++)\n{\n    Use(items[index]);\n}";
                    selectedGoodCode = "foreach (Item item in items)\n{\n    Use(item);\n}";
                    selectedExplanation =
                        "Enumerating an array or List<T> allocates nothing when the index is only used to read each item.";
                    break;
                case "WUH014":
                    selectedBadCode =
                        "public struct Lease : IDisposable\n{\n    private bool disposed;\n    public void Dispose() { disposed = true; }\n}";
                    selectedGoodCode =
                        "public sealed class Lease : IDisposable\n{\n    private bool disposed;\n    public void Dispose() { disposed = true; }\n}";
                    selectedExplanation =
                        "A disposable struct is copied by value; shared reference state makes disposal visible to every owner.";
                    break;
                case "WUH015":
                    selectedBadCode = "private int Awake()\n{\n    return 1;\n}";
                    selectedGoodCode = "private void Awake()\n{\n    Initialize();\n}";
                    selectedExplanation =
                        "Unity callbacks require the supported signature; Awake returns void.";
                    break;
                case "WUH016":
                    selectedBadCode =
                        "class Parent : MonoBehaviour\n{\n    protected virtual void Awake() { }\n}\nclass Child : Parent\n{\n    private void Awake() { Initialize(); }\n}";
                    selectedGoodCode =
                        "class Parent : MonoBehaviour\n{\n    protected virtual void Awake() { }\n}\nclass Child : Parent\n{\n    protected override void Awake()\n    {\n        base.Awake();\n        Initialize();\n    }\n}";
                    selectedExplanation =
                        "Override inherited callbacks and explicitly chain the base when both implementations must run.";
                    break;
                case "WUH017":
                    selectedBadCode =
                        "if (GetComponent<SpriteRenderer>() != null)\n{\n    Render();\n}";
                    selectedGoodCode =
                        "if (TryGetComponent<SpriteRenderer>(out _))\n{\n    Render();\n}";
                    selectedExplanation =
                        "TryGetComponent avoids the Editor allocation for absent components.";
                    break;
                case "WUH018":
                    selectedBadCode = "if (mode == \"Deserialize\")\n{\n    Read();\n}";
                    selectedGoodCode =
                        "if (string.Equals(\n    mode, \"Deserialize\",\n    StringComparison.Ordinal))\n{\n    Read();\n}";
                    selectedExplanation =
                        "State the intended comparison policy explicitly; string operator equality is ordinal and case-sensitive.";
                    break;
                case "WUH019":
                    selectedBadCode =
                        "for (int index = 0;\n    index < rows.Length;\n    index++)\n{\n    output[index] = rows[index];\n}";
                    selectedGoodCode =
                        "int rowCount = rows.Length;\nfor (int index = 0;\n    index < rowCount;\n    index++)\n{\n    output[index] = rows[index];\n}";
                    selectedExplanation =
                        "Cache a stable size for indexed traversal. Keep live bounds when callbacks can change the collection.";
                    break;
                default:
                    badCode = string.Empty;
                    goodCode = string.Empty;
                    explanation = string.Empty;
                    return false;
            }
            badCode = selectedBadCode;
            goodCode = selectedGoodCode;
            explanation = selectedExplanation;
            return true;
        }
    }
#endif
}

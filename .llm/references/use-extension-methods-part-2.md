# use-extension-methods - Part 2

## Split Content

## IEnumerable Extensions

### AsList - Avoid Unnecessary Allocation

```csharp
// ✅ Returns original if already IList, otherwise creates new List
IList<Item> items = someEnumerable.AsList();
```

### Shuffled - Returns New Shuffled Sequence

```csharp
// Returns lazy enumerable in random order
IEnumerable<Card> shuffled = cards.Shuffled();
IEnumerable<Card> shuffled2 = cards.Shuffled(myRandom);
```

**Note**: Allocates. For in-place shuffle, use `list.Shuffle()` instead.

### Infinite - Repeating Sequence

```csharp
// Create infinite repeating sequence
IEnumerable<Color> colors = new[] { Color.red, Color.green, Color.blue }.Infinite();

// Take first 10 from infinite cycle
var first10 = colors.Take(10).ToList();
```

### Ordered - Natural Ordering

```csharp
// Sort by natural IComparable order
IEnumerable<int> sorted = numbers.Ordered();
```

### ToLinkedList

```csharp
LinkedList<Item> linked = items.ToLinkedList();
```

---

## String Extensions

### Case Conversion

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

string input = "helloWorld";

input.ToCase(StringCase.PascalCase);    // "HelloWorld"
input.ToCase(StringCase.CamelCase);     // "helloWorld"
input.ToCase(StringCase.SnakeCase);     // "hello_world"
input.ToCase(StringCase.KebabCase);     // "hello-world"
input.ToCase(StringCase.TitleCase);     // "Hello World"
input.ToCase(StringCase.UpperCase);     // "HELLOWORLD"
input.ToCase(StringCase.LowerCase);     // "helloworld"
```

### Byte Conversion

```csharp
// String to UTF-8 bytes
byte[] bytes = "Hello".GetBytes();

// Bytes back to string
string text = bytes.GetString();
```

### JSON Serialization

```csharp
// Serialize any object to JSON
string json = myObject.ToJson();
```

### LevenshteinDistance - Fuzzy Matching

```csharp
// Calculate edit distance between strings
int distance = "kitten".LevenshteinDistance("sitting");  // Returns 3
```

**Performance**: O(n\*m), **Allocations**: Uses pooled arrays

### Center - Pad String

```csharp
string centered = "hi".Center(6);  // "  hi  "
```

---

## Color Extensions

### ToHex - Color to Hex String

```csharp
using WallstopStudios.UnityHelpers.Core.Extension;

Color color = new Color(1f, 0.5f, 0f, 1f);

string hexRGBA = color.ToHex();              // "#FF8000FF"
string hexRGB = color.ToHex(includeAlpha: false);  // "#FF8000"
```

**Performance**: O(1), **Allocations**: One string

### GetAverageColor - Sprite Color Analysis

```csharp
// Get average color from sprite
Color avg = sprite.GetAverageColor();

// With specific averaging method
Color avgLAB = sprite.GetAverageColor(ColorAveragingMethod.LAB);      // Perceptually accurate
Color avgHSV = sprite.GetAverageColor(ColorAveragingMethod.HSV);      // Preserves vibrancy
Color avgWeighted = sprite.GetAverageColor(ColorAveragingMethod.Weighted);
Color dominant = sprite.GetAverageColor(ColorAveragingMethod.Dominant);  // Most common color

// From multiple sprites
Color combined = sprites.GetAverageColor();
```

**Warning**: NOT thread-safe, modifies texture import settings.

### GetComplement - Complementary Color

```csharp
// Get complementary color (180° hue rotation)
Color complement = color.GetComplement();

// With randomization for variety
Color varied = color.GetComplement(PRNG.Instance, variance: 0.1f);
```

**Performance**: O(1), **Allocations**: None

---

## Performance Summary

### Zero-Allocation Methods ✅

| Extension                | Type       | Notes                        |
| ------------------------ | ---------- | ---------------------------- |
| `Shuffle()`              | IList      | Fisher-Yates in-place        |
| `GetRandomElement()`     | IList      | O(1)                         |
| `RemoveAtSwapBack()`     | IList      | O(1), unordered              |
| `IndexOf(predicate)`     | IList      | O(n)                         |
| `LastIndexOf(predicate)` | IList      | O(n)                         |
| `Shift()`                | IList      | Three reversals              |
| `Reverse(start, end)`    | IList      | In-place                     |
| `Sort()`                 | IList      | Pooled sorting algorithms    |
| `TryRemove()`            | Dictionary | -                            |
| `GetOrElse()`            | Dictionary | Read-only                    |
| `ToHex()`                | Color      | Allocates result string only |
| `GetComplement()`        | Color      | -                            |

### Allocating Methods ⚠️

| Extension           | Type        | Allocation                |
| ------------------- | ----------- | ------------------------- |
| `GetOrAdd()`        | Dictionary  | Value if created          |
| `Merge()`           | Dictionary  | New dictionary            |
| `Shuffled()`        | IEnumerable | LINQ structures           |
| `AsList()`          | IEnumerable | List if not already IList |
| `ToLinkedList()`    | IEnumerable | New LinkedList            |
| `FindAll()`         | IList       | New List                  |
| `GetAverageColor()` | Sprite      | Pixel array               |

---

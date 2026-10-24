# Custom JSON Converter Performance

A custom `JsonConverter<T>` can reduce repeated member lookup for a frequently serialized model.
It also makes you responsible for the model's JSON contract, including missing fields, unknown
properties, nulls, enums and nested values. Start with the package's normal JSON options, profile
the actual save or message path, and add a converter only where the benefit justifies that work.

The package does not currently ship a general-purpose JSON source generator. The measurements
below compare a hand-written converter with the reflection path; they are not measurements of a
shipped generated-converter feature.

## Register a converter

Create options once, add your converter before first use, then reuse those options for the same
contract. `Serializer.CreateNormalJsonOptions()` returns an options instance you can customize.
Insert your converter first when it must take precedence over an existing converter.

<!-- doc-sample: compiles -->

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using WallstopStudios.UnityHelpers.Core.Serialization;

public static class JsonSaveSettings
{
    public static JsonSerializerOptions Create<T>(JsonConverter<T> converter)
    {
        JsonSerializerOptions options = Serializer.CreateNormalJsonOptions();
        if (converter != null)
        {
            options.Converters.Insert(0, converter);
        }
        return options;
    }
}
```

Pass the returned options to the package's JSON operations, such as
`Serializer.JsonSerialize(value, options)`. Configure the options before the first serialization
or deserialization; do not modify an options instance already in use. Keep the cached instance
with the code that owns the contract.

Before adopting a converter, test both read directions with retained data: the new reader must
load old JSON, and any older reader you still support must load new output. A converter that is
faster because it drops fields or changes defaults is a format change.

<a id="the-measurement"></a>
<a id="why-these-are-lower-bounds"></a>

## Historical comparison

These measurements were taken on **Unity 6000.4.6f1, Editor Mono, 2026-08-29**. The model had ten
members: integers, a float, a bool, a string, an enum, a `Vector3`, a list and a nested object.
The small and large cases used lists of 4 and 256 integers respectively.

The reference used the package's normal or fast JSON options. The custom converter used cached
UTF-8 property names and explicit member reads and writes. Both paths produced identical bytes
and read each other's payloads into equal values before timing. Timings alternated the two paths
and retained repeated samples. A ratio above one favors the custom converter.

The three recorded runs were:

| Workload                |   Run 1 |   Run 2 |   Run 3 | Worst spread seen |
| ----------------------- | ------: | ------: | ------: | ----------------: |
| Normal serialize, 4     | 2.0900x | 2.1168x | 2.0814x |              6.4% |
| Normal deserialize, 4   | 3.0124x | 3.0559x |   3.00x |              3.8% |
| Normal serialize, 256   | 1.5643x | 1.5477x | 1.5372x |              4.7% |
| Normal deserialize, 256 | 1.6889x | 1.6499x | 1.6614x |             11.5% |
| Fast serialize, 4       | 2.0027x | 2.0028x | 2.0026x |              6.0% |
| Fast deserialize, 4     | 3.0161x | 3.0054x |   3.00x |              4.2% |
| Fast serialize, 256     | 1.4040x | 1.4100x |   1.41x |              4.6% |
| Fast deserialize, 256   | 1.6676x | 1.6676x |   1.64x |              7.1% |

Most workloads exceeded the measurement's 3% within-run spread limit in at least one run.
The similar ratios across runs do not remove that instability. The following four rows met that
limit in the third run:

| Workload                 | Ratio | Reference Spread | Subject Spread |
| ------------------------ | ----: | ---------------: | -------------: |
| Normal deserialize small |  3.00 |           0.0218 |         0.0181 |
| Fast deserialize small   |  3.00 |           0.0228 |         0.0280 |
| Fast serialize large     |  1.41 |           0.0269 |         0.0215 |
| Fast deserialize large   |  1.64 |           0.0290 |         0.0223 |

These are historical observations for one model and environment. They do not establish a lower
bound, an upper bound, or a guaranteed speedup for another converter, model or backend. There is
no current IL2CPP or WebGL result on this page.

<a id="reading-the-result"></a>
<a id="practical-rules"></a>
<a id="caveats"></a>

## Decide what to change

- **Occasional saves:** prefer the standard options unless profiling shows a visible delay.
  Maintaining a converter may cost more than it saves.
- **Frequent messages or replay frames:** measure serialization and deserialization separately.
  Model size and shape can move the benefit substantially.
- **Options:** choose normal or fast settings for their supported behavior. Fast settings change
  parsing and member policies; they are not a substitute for a custom converter.
- **Allocation:** reuse the options and measure the complete call. These timing ratios do not
  establish allocation or retained-memory savings.
- **Players:** verify converter registration, stripping and round trips in each target backend.
  A working Editor converter does not establish AOT availability.

See [JSON options](../features/serialization/serialization.md#creating-custom-options) for package
presets and [reading performance measurements](./baseline-tests-performance.md) for comparison limits.

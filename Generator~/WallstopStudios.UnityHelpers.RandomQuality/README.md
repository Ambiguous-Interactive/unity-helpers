# Random quality byte stream

This host turns a package `IRandom` implementation into deterministic little-endian `uint` bytes for
PractRand, TestU01 adapters, or another external statistical battery. It is test infrastructure under
`Generator~`; Unity ignores it and it is not included in the package.

```bash
dotnet run --project Generator~/WallstopStudios.UnityHelpers.RandomQuality \
  -c Release -- \
  --generator PcgRandom \
  --seed 00010203-0405-0607-0809-0a0b0c0d0e0f \
  --bytes 1073741824 \
  | RNG_test stdin32
```

Use `--list` for the explicit generator inventory. `UnityRandom` is deliberately absent because its
state belongs to a live Unity player; the standalone host cannot reproduce that engine-owned stream.
`NativePcgRandom` is deliberately absent because it is a Unity-native value type rather than an
`IRandom` implementation. Unity runtime tests cover its numeric behavior; this host exposes managed
streams that external batteries can consume without Unity collections.
The weak generators remain in the inventory as expected-failure controls. A scheduled workflow must
pin the external battery version, record its command and seed, upload the complete report, and compare
results against per-generator expected outcomes. Long statistical runs must not become nondeterministic
pull-request gates.

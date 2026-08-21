# Unity Helpers Roadmap

This roadmap outlines planned enhancements to Unity Helpers. All "Currently shipping" features are production-ready and available now. See the main [README](../readme.md) for current capabilities.

## 1. Comprehensive Inspector Tooling

**Currently shipping:** Attribute and property drawer suite covering enum toggles, dropdowns, conditional display, grouping, buttons, and validation. Key attributes: `WEnumToggleButtons`, `WShowIf`, `WValueDropDown`, `WGroup`, `WButton`, `WNotNull`, `ValidateAssignment`.

**Next up:**

- Odin Inspector migration tooling
- Investigate color themes (had a version of this for WGroup, had to scrap it due to complexity)
- Tabbed/section navigation with persistent layout bookmarks
- Visual instrumentation (progress bars, warning badges, inline state telemetry)
- Additional attributes, serializable types

## 2. Expanded Editor Tooling

**Currently shipping:** Animation Creator, Sprite Sheet Animation Creator, Animation Event Editor, plus 20+ sprite/texture/prefab utilities. See [Editor Tools Guide](../features/editor-tools/editor-tools-guide.md) for full list.

**Next up:**

- Animation Creator enhancements
- Sprite Sheet Animation Creator enhancements
- Animation Event Editor refinements
- Additional automation surfaces

## 3. Advanced Random & Statistical Testing

**Currently shipping:** 20+ high-quality RNG implementations (IllusionFlow, PcgRandom, XoroShiro, SplitMix64, RomuDuo, FlurryBurst, PhotonSpin, etc.) with extensive `IRandom` API. Repository contributors also have a deterministic binary stream host for external statistical batteries. See [Random Performance](../performance/random-performance.md).

**Next up:**

- Pin PractRand/TestU01 versions and add scheduled suites with per-generator expected outcomes and complete report artifacts
- Automated quality reports: histograms, percentile deltas, change detection for PR gates
- Higher-level sampling: Poisson disk, stratified sampling, correlated noise, shuffled streams, deterministic scenario builders
- Investigation of Job/Burst-aware stream schedulers: seed pools, jump-ahead APIs, reservoir/permutation helpers with property-based tests

## 4. Enhanced Spatial Trees

**Currently shipping:** Production 2D trees (QuadTree2D, KdTree2D, RTree2D, SpatialHash2D) and experimental 3D variants (OctTree3D, KdTree3D, RTree3D, SpatialHash3D). See [2D Performance](../performance/spatial-tree-2d-performance.md) and [3D Performance](../performance/spatial-tree-3d-performance.md).

**Next up:**

- Graduate 3D trees to production: profiling data, comprehensive docs, parity with 2D APIs
- Mutable/incremental updates/variants: localized inserts/removals without full rebuilds
- Unity Physics parity: ray/capsule/sphere casts, overlap tests, PhysicsScene adapter structs
- Investigate streaming builders: tile-based loading for large worlds, job-based construction

## 5. UI Toolkit Enhancements

**Currently shipping:** LayeredImage and MultiFileSelectorElement custom visual elements with samples and persistence helpers.

**Next up:**

- Control pack: dockable panes, inspector tab bars, data tables, curve editors, virtualized multi-column lists
- Theme/palette system: USS/UXML snippets with runtime/editor parity samples
- Performance patterns: batched bindings, incremental painters, list virtualization utilities with comprehensive docs
- Automation dashboards: UI Toolkit-based wizards for workflow automation

## 6. Utility Expansion

**Currently shipping:** Extensive utilities covering pooling (Buffers, array pools), singleton patterns, animation helpers, sprite utilities, compression, math extensions, and more. See [Helper Utilities](../features/utilities/helper-utilities.md).

**Next up:**

- Cross-system bridges: effects ↔ serialization, pooling ↔ DI containers, random ↔ spatial query fuzzers with ready-made samples
- Math/combinatorics helpers: curve fitting, statistics, interpolation packs, IO/localization conveniences
- Service patterns: task/tween schedulers, async job orchestrators, gameplay timers with integrated diagnostics

## 7. Performance Program

**Currently shipping:** Comprehensive benchmarks for random generators, spatial trees, reflection helpers, and IList sorting. See [Random Performance](../performance/random-performance.md), [Spatial Tree Performance](../performance/spatial-tree-2d-performance.md), and [Reflection Performance](../performance/reflection-performance.md).

**Next up:**

- Automated benchmark harness: CI integration, baseline storage, regression detection per subsystem
- Investigate burst/Jobs optimizations: hot loop rewrites for spatial queries, pooling, math helpers with analyzer hints
- Allocation/GC audits: Roslyn analyzers and NUnit tests enforcing zero-allocation guarantees for critical APIs
- Safety analyzers: custom Roslyn rule that flags `SerializableNullable<T>.Value` access without a preceding `HasValue` check (Unity asmdef-friendly package)

## 8. Attribute & Tag System Evolution

**Currently shipping:** Data-driven effects system with attributes, tags, effect stacks, and metadata caches. ScriptableObject-driven effect authoring with cosmetics and duration management. See [Effects System](../features/effects/effects-system.md).

**Next up:**

- Effect visualization: inspector timeline for active effects, stack inspection, debug overlays
- Attribute graphs: dependency tracking with automatic recalculation when modifiers change
- Migration tools: schema evolution helpers for effects and attribute definitions

## 9. Relational Component Enhancements

**Currently shipping:** Component auto-wiring attributes (SiblingComponent, ParentComponent, ChildComponent) with DI integrations for VContainer, Zenject, and Reflex. See [Relational Components](../features/relational-components/relational-components.md).

**Next up:**

- Performance improvements: cached reflection paths, Roslyn source generators for zero-reflection wiring
- Enhanced validation: editor-time dependency visualization, hierarchy relationship graphs
- Advanced querying: interface-based resolution, filtered component searches

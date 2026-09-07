# Skill: Defensive Editor Programming

<!-- trigger: editor defensive, serializedproperty, asset safety, editor validation | Editor code - handle Unity Editor edge cases | Core -->

**Trigger**: When writing Unity Editor code (custom inspectors, property drawers, editor windows, asset processors). Editor code requires additional defensive patterns beyond standard defensive programming.

---

## When to Use This Skill

Use this skill when:

- Creating custom PropertyDrawers or Editors
- Working with SerializedProperty and SerializedObject
- Loading or manipulating assets via AssetDatabase
- Writing or editing an `AssetPostprocessor` subclass (see [asset-postprocessor-safety](./asset-postprocessor-safety.md) - every import-phase callback must defer work)
- Caching editor state that may be invalidated by Unity reloads
- Handling serialization in editor contexts
- Writing event/callback handlers in editor tools

---

## Why Editor Code Needs Extra Defense

Editor code is especially vulnerable because:

- User can modify inspector values at any time
- SerializedProperty may reference destroyed objects
- Assets may be deleted mid-operation
- Unity reload (domain reload, assembly reload) may invalidate cached state
- Selection can change during multi-frame operations
- Undo/Redo can restore unexpected states

---

## Detailed Guidance

This skill is an overview. For detailed patterns and code examples, see the focused skills below:

### Multi-Object Editing and Undo

See [editor-multi-object-editing](./editor-multi-object-editing.md) for:

- Render-phase vs callback-phase property modification
- BeginChangeCheck/EndChangeCheck guards for IMGUI value reads
- GenericMenu callback requirements (undo, all options, apply)
- Display value writeback prevention
- showMixedValue ordering
- Mixed/invalid value display conventions
- Undo support approaches (SerializedProperty API vs direct mutation)
- Odin Inspector mixed value detection

### Forbidden APIs and Value Handling

See [editor-api-rules](./editor-api-rules.md) for:

- `EditorGUI.Popup` prohibition (Linux phantom rows)
- Display label normalization consistency
- IMGUI vs UI Toolkit value handling differences
- Unity string serialization null-to-empty conversion

### Singleton Asset Management

See [editor-singleton-patterns](./editor-singleton-patterns.md) for:

- Singleton path consistency with `ScriptableSingletonPath` attribute
- Asset creation pattern (PauseBatch + SaveAssets + ImportAsset)
- Lazy singleton caching after asset creation (`ClearInstance()`)
- Singleton instance path validation in find-or-create patterns

### Progress Cleanup

Every operation that displays `EditorUi` progress must clear it in a `finally` covering the first
display, the processing loop, and batch disposal. Clear progress before fallible save, refresh, or
disposal calls inside an existing `finally`; a clear placed after them can still be skipped. Preserve
each tool's cancellation and partial-result behavior. Test thrown processing and cancellation through
the internal `EditorUi` callbacks, restoring both callbacks in `finally`
([#648](https://github.com/Ambiguous-Interactive/unity-helpers/issues/648)).

### Code Samples

Dispose owned `SerializedObject` and `SerializedProperty` instances before destroying their target.
Record ownership as each pending wrapper is created so failed initialization can release it. Cached
wrappers need the same cleanup on invalidation and window disable; clearing a dictionary only drops
managed references. Mixed preview caches must distinguish copied textures from Unity-owned
`AssetPreview` textures and destroy only their own copies ([#734](https://github.com/Ambiguous-Interactive/unity-helpers/issues/734)).

Detailed code patterns are in dedicated sample files:

- [SerializedProperty Safety](./code-samples/editor-serialized-property-safety.md) - Safe property access, height calculation, nested property access
- [Asset Operations Safety](./code-samples/editor-asset-operations-safety.md) - Safe asset loading, iteration, and creation
- [Cache Invalidation Safety](./code-samples/editor-cache-invalidation-safety.md) - Safe caching with domain reload handling
- [Serialization Safety](./code-samples/editor-serialization-safety.md) - Defensive deserialization, EditorPrefs safety
- [Event/Callback Safety](./code-samples/editor-event-callback-safety.md) - Safe event invocation and callback registration

---

## Quick Checklist for Editor Code

Before submitting editor code, verify:

- [ ] SerializedProperty null-checked before access ([SerializedProperty Safety](./code-samples/editor-serialized-property-safety.md))
- [ ] SerializedObject.targetObject validated ([SerializedProperty Safety](./code-samples/editor-serialized-property-safety.md))
- [ ] Asset paths validated before AssetDatabase operations ([Asset Operations Safety](./code-samples/editor-asset-operations-safety.md))
- [ ] Selection validated (may be empty or contain nulls) ([Asset Operations Safety](./code-samples/editor-asset-operations-safety.md))
- [ ] Cache invalidation handles domain reload ([Cache Invalidation Safety](./code-samples/editor-cache-invalidation-safety.md))
- [ ] Cache invalidation handles target object changes ([Cache Invalidation Safety](./code-samples/editor-cache-invalidation-safety.md))
- [ ] Event handlers wrapped in try-catch ([Event/Callback Safety](./code-samples/editor-event-callback-safety.md))
- [ ] Event subscriptions cleaned up in OnDisable ([Event/Callback Safety](./code-samples/editor-event-callback-safety.md))
- [ ] Deserialization handles corrupt/missing data ([Serialization Safety](./code-samples/editor-serialization-safety.md))
- [ ] EditorPrefs access handles missing keys ([Serialization Safety](./code-samples/editor-serialization-safety.md))
- [ ] Display label fallbacks applied consistently across all code paths ([editor-api-rules](./editor-api-rules.md))
- [ ] No `EditorGUI.Popup` usage — use `GenericMenu` instead ([editor-api-rules](./editor-api-rules.md))
- [ ] Multi-object editing supported ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] No property modification during OnGUI render phase ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] All IMGUI value reads wrapped in `BeginChangeCheck`/`EndChangeCheck` ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] No computed display values written back to properties during render ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] GenericMenu callbacks handle ALL options ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] `EditorGUI.showMixedValue` set BEFORE index/value calculations ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] Em dash (`\u2014`) displayed for mixed values ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] "(Invalid)" suffix shown for out-of-range indices ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] `Undo.RecordObjects(targetObjects, ...)` used for multi-object undo ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] `Undo.FlushUndoRecordObjects()` called after direct object mutation ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] Odin drawers manually check mixed values ([editor-multi-object-editing](./editor-multi-object-editing.md))
- [ ] Odin drawers filter `Property.Tree.WeakTargets` for null before `Undo.RecordObjects` (see [odin-undo-safety](./odin-undo-safety.md))
- [ ] Standard and Odin drawer variants updated consistently
- [ ] No `stringValue == null` checks ([editor-api-rules](./editor-api-rules.md))
- [ ] `AssetDatabase.CreateAsset` followed by `SaveAssets` + `ImportAsset(ForceSynchronousImport)` ([editor-singleton-patterns](./editor-singleton-patterns.md))
- [ ] `ScriptableObjectSingleton<T>.ClearInstance()` called after creating singleton assets ([editor-singleton-patterns](./editor-singleton-patterns.md))
- [ ] Singleton `Instance` results validated for correct path ([editor-singleton-patterns](./editor-singleton-patterns.md))

---

## Related Skills

- [asset-postprocessor-safety](./asset-postprocessor-safety.md) - AssetPostprocessor import-phase rules (avoiding SendMessage warnings)
- [editor-multi-object-editing](./editor-multi-object-editing.md) - Multi-object editing patterns and undo support
- [editor-api-rules](./editor-api-rules.md) - Forbidden APIs and value handling rules
- [editor-singleton-patterns](./editor-singleton-patterns.md) - Singleton asset management patterns
- [defensive-programming](./defensive-programming.md) - Core defensive patterns for all code
- [create-editor-tool](./create-editor-tool.md) - Editor tool creation patterns
- [create-test](./create-test.md) - Test edge cases and error conditions
- [create-property-drawer](./create-property-drawer.md) - PropertyDrawer patterns including display label normalization

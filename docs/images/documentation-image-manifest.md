# Documentation Image Manifest

Which images under `docs/images/` are **generated** by the editor-surface capture harness, which
are still **hand-captured**, and how to regenerate the generated ones.

The harness lives in `Tests/Editor/Capture/`. It hosts a real shipped editor surface in a hidden
popup window, renders that window's panel into an offscreen render target, and writes the
surface's own laid-out rect as a 24-bit PNG. It never reads the desktop: no screen-pixel reader,
no native window capture, and no programmatic skin switching.

---

## Scope of this manifest

| Format         | Status                                                                         |
| -------------- | ------------------------------------------------------------------------------ |
| PNG (still)    | Automatable. 13 of the repository's 70 PNGs are generated today.               |
| GIF (animated) | **Out of scope.** The harness captures single frames; it cannot record a GIF.  |
| WebP           | **Out of scope.** Unity's `EncodeToPNG` has no WebP counterpart in the editor. |
| SVG (diagram)  | Not applicable. Diagrams are authored, not captured.                           |

Animated capture (`gif` / `webp`) remains open work under
[issue 442](https://github.com/wallstop/unity-helpers/issues/442).

---

## How to regenerate

Capture needs a **real graphics device**. Every capture test calls `Assert.Ignore` when
`SystemInfo.graphicsDeviceType` is `Null`, which is what happens under `-nographics`.

### In the Unity Editor (the supported path)

1. Open the project that embeds this package in a normal editor session (not `-batchmode`).
2. Open **Window → General → Test Runner → EditMode**.
3. Run the explicit regeneration test:

   ```text
   WallstopStudios.UnityHelpers.Tests.Editor.Capture.WButtonDocumentationImageTests.RegenerateDocumentationImages
   ```

   It is marked `[Explicit]` and `[Category("DocumentationCapture")]` so an ordinary test run
   never rewrites files under `docs/`. Selecting it directly in the Test Runner runs it.

4. Review the changed PNGs in the diff before committing them.

### From the command line

`scripts/unity/run-tests.sh` accepts the same filter:

```bash
bash scripts/unity/run-tests.sh --filter "WallstopStudios.UnityHelpers.Tests.Editor.Capture.WButtonDocumentationImageTests"
```

That Docker leg launches Unity with `-nographics`, so the capture tests **skip** there by design
and no image is rewritten. The filter is the right one; the runner has to be a graphical editor.
Use it to confirm the fixture's non-capture assertions (catalog, manifest, and documentation
cross-checks) still hold.

---

## Generated images

All thirteen are WButton inspector surfaces, drawn by this package's own `WButtonInspector` over
target components in `Tests/Editor/Capture/Targets/`. The catalog that drives them is
`Tests/Editor/Capture/WButtonDocumentationImageCatalog.cs`.

| Image                                                     | Capture target            | Shows                                        |
| --------------------------------------------------------- | ------------------------- | -------------------------------------------- |
| `inspector/buttons/button-overview.png`                   | `WButtonOverviewExample`  | The four supported execution shapes          |
| `inspector/buttons/button-order.png`                      | `ButtonPositioning`       | Buttons sorted by `drawOrder`                |
| `inspector/buttons/button-groupings.png`                  | `ButtonGrouping`          | Combat and Persistence groups                |
| `inspector/buttons/button-colorings.png`                  | `ButtonColoring`          | Built-in dark and light color keys           |
| `inspector/buttons/inspector-button-display-names.png`    | Two targets, side by side | Method names against explicit display names  |
| `inspector/buttons/inspector-button-draw-order.png`       | `PlayerController`        | `drawOrder` against declaration order        |
| `inspector/buttons/inspector-button-groups.png`           | `GameManager`             | `groupName` headers                          |
| `inspector/buttons/inspector-button-group-priority.png`   | `ActionPanel`             | `groupPriority` ordering                     |
| `inspector/buttons/inspector-button-group-placement.png`  | `MixedPlacementExample`   | `groupPlacement` above and below properties  |
| `inspector/buttons/inspector-button-advanced-layout.png`  | `AdvancedButtonLayout`    | Priority and placement combined, on an asset |
| `inspector/buttons/inspector-button-complete-example.png` | `LevelManager`            | Every parameter working together             |
| `inspector/buttons/player-debug.png`                      | `PlayerDebug`             | Health and Economy groups over fields        |
| `inspector/buttons/level-generator-with-parameters.png`   | `LevelGenerator`          | A button method that takes parameters        |

### What a generated image looks like

- The **inspector body plus its component header**, not the whole Inspector window. The
  hand-captured images included the editor's window chrome and its tab; the harness crops to the
  surface's own rect, so a generated image has no window frame around it.
- **Truecolor without alpha** (PNG color type 2). The hand-captured images are RGBA.
- Rendered against **whatever skin the capturing editor is set to**, because switching skins
  programmatically is exactly the desktop-reading behaviour the harness refuses. Capture with the
  dark skin for consistency with the images already committed.
- Rendered against the project's **`UnityHelpersSettings`**. Placement, foldout behaviour, page
  size and the custom color palette all change what the inspector draws, so a different settings
  asset produces different images -- `inspector-button-complete-example.png` in particular shows
  the `Danger`, `Success` and `Warning` color keys, which have to exist in the capturing project's
  palette to appear.
- Driving a panel outside an editor view makes every drawer that asks for a cursor rect log
  `EditorGUIUtility.AddCursorRect called outside an editor OnGUI`. The pixels are correct; only
  the mouse cursor, which an offscreen capture has no use for, is not applied. The fixture
  tolerates that one message for the length of a capture and then asserts nothing else was
  logged.

---

## Still hand-captured

### In the same folder

| Image                                                    | Why it is not generated yet                                           |
| -------------------------------------------------------- | --------------------------------------------------------------------- |
| `inspector/buttons/helper-button-settings.png`           | A `SettingsProvider` surface, not an inspector; needs a second driver |
| `inspector/buttons/helper-settings-color-settings.png`   | Same, plus a populated custom color palette                           |
| `inspector/buttons/inspector-button-settings-colors.png` | Same                                                                  |
| `inspector/buttons/button-color-themes.png`              | Needs custom color keys registered in project settings first          |
| `inspector/buttons/history-dice-rolls-2.png`             | Needs invocation history populated before the frame is drawn          |
| `inspector/buttons/id-history.png`                       | Same                                                                  |

### Everywhere else

Fifty-seven PNGs remain hand-captured. By folder:

| Folder                                           | Hand-captured PNGs | Notes                                                    |
| ------------------------------------------------ | -----------------: | -------------------------------------------------------- |
| `docs/images/editor-tools`                       |                 14 | Editor windows and wizards; each needs a driver          |
| `docs/images/editor-tools/unity-method-analyzer` |                  5 | Two are native menus, which cannot be captured offscreen |
| `docs/images/inspector`                          |                 10 | Mixed inspector and runtime views                        |
| `docs/images/inspector/selection`                |                  7 | Dropdown drawers; the popup is a second window           |
| `docs/images/inspector/validation`               |                  6 | Property drawers; the next clear candidate group         |
| `docs/images/serialization`                      |                  6 | Collection drawers; also good candidates                 |
| `docs/images/inspector/inline-editor`            |                  3 | Needs a referenced asset to embed                        |
| `docs/images/inspector/buttons`                  |                  6 | Listed individually above                                |

### Finishing the backlog

Adding a group is three steps and needs no new harness code:

1. Add one target type per image under `Tests/Editor/Capture/Targets/` (one file per type).
2. Add a catalog entry naming the image path and its target types.
3. Add the row to the generated table above; `ManifestListsEveryCatalogImage` fails otherwise.

One thing to know before writing a target: **`Tests/Editor/Capture/Targets/` is an all-platform
assembly on purpose**, even though it sits under `Tests/Editor/`. Unity refuses to attach a
`MonoBehaviour` compiled into an editor-only assembly, and `GameObject.AddComponent` returns
`null` for one **without logging anything** -- measured on 6000.4.6f1, where the identical type
attached fine once it moved to an all-platform assembly. A component target must live in
`WallstopStudios.UnityHelpers.Tests.Capture.Targets`; a `ScriptableObject` target would work
either way.

A **property-drawer** group (`docs/images/inspector/validation`) is the natural next one: those
images are the same shape as the WButton ones, a plain inspector over a component whose fields
carry the attribute under test.

A **settings-window** group needs one new piece: a surface factory that hosts a
`SettingsProvider` the way `InspectorSurface` hosts an `Editor`.

---

## What the harness guarantees

`Tests/Editor/Capture/EditorSurfaceCaptureTests.cs` holds the contract tests:

- the frame is not blank (a cleared target has exactly one distinct color);
- the PNG is truecolor without alpha, both in memory and on disk;
- the crop is the surface's own rect, so window chrome and canvas slack are excluded;
- distinct surfaces produce byte-distinct PNGs;
- `RenderTexture.active` and `GL.sRGBWrite` are restored, and the host window and both textures
  are destroyed, **including when the capture fails**;
- a surface that does not fit the canvas is refused rather than silently clipped.

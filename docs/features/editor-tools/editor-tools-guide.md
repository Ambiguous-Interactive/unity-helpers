# Editor Tools Guide

**Batch tools for the asset work Unity makes you do one asset at a time.** Crop a folder of sprites,
build clips from a naming convention, apply one set of import settings to 400 textures, or find the
serialized field Unity is silently throwing away — 20+ editor tools in all.

Everything here lives under `Tools > Wallstop Studios > Unity Helpers` unless stated otherwise. Two
tools also expose a public C# API you can drive from your own editor scripts:
[Texture Settings Applier](#texture-settings-applier) and
[Sprite Settings Applier](#sprite-settings-applier).

---

## Find the tool you need

| I want to...                                       | Tool                                                                        |
| -------------------------------------------------- | --------------------------------------------------------------------------- |
| Strip transparent padding off a folder of sprites  | [Sprite Cropper](#sprite-cropper)                                           |
| Apply one set of import settings to many textures  | [Texture Settings Applier](#texture-settings-applier)                       |
| Apply different sprite settings per folder or name | [Sprite Settings Applier](#sprite-settings-applier)                         |
| Stop guessing texture max sizes                    | [Fit Texture Size](#fit-texture-size)                                       |
| Put pivots on the visual center of mass            | [Sprite Pivot Adjuster](#sprite-pivot-adjuster)                             |
| Upscale a batch of PNGs                            | [Texture Resizer](#texture-resizer)                                         |
| Blur backgrounds for a UI pause menu               | [Image Blur Tool](#image-blur-tool)                                         |
| Turn `Run_0.png ... Run_7.png` into a clip         | [Animation Creator](#animation-creator)                                     |
| Turn a sliced sheet into several clips             | [Sprite Sheet Animation Creator](#sprite-sheet-animation-creator)           |
| Split a sheet back into individual PNGs            | [Sprite Sheet Extractor](#sprite-sheet-extractor)                           |
| Change the timing or frame order of a clip         | [Sprite Animation Editor](#sprite-animation-editor-animation-viewer-window) |
| Add footstep / hitbox events to a clip             | [Animation Event Editor](#animation-event-editor)                           |
| Sync clips between two folders                     | [Animation Copier](#animation-copier)                                       |
| Build a sprite atlas from a regex or a label       | [Sprite Atlas Generator](#sprite-atlas-generator)                           |
| Catch broken prefabs before they ship              | [Prefab Checker](#prefab-checker)                                           |
| Catch a missing `override` on a lifecycle method   | [Unity Method Analyzer](#unity-method-analyzer)                             |
| Find a field Unity is silently not serializing     | [Serialized Field Validator](#serialized-field-validator)                   |
| React to assets being created or deleted           | [Asset Change Detection](./asset-change-detection.md)                       |
| Recompile without touching a file                  | [Request Script Compilation](#request-script-recompilation)                 |
| Keep failing test output after the run             | [Failed Tests Exporter](#failed-tests-exporter)                             |

Inspector attributes (`[WGroup]`, `[WButton]`, `[WEnumToggleButtons]`, `[WNotNull]` and friends) are
covered in the [Inspector documentation](../inspector/inspector-overview.md). The drawers documented
here are the ones these tools use most.

---

<a id="texture--sprite-tools"></a>
<a id="texture-sprite-tools"></a>

## Texture & Sprite Tools

### Image Blur Tool

`Tools > Wallstop Studios > Unity Helpers > Image Blur`

You need a blurred copy of a background so the pause menu can sit on top of it without a render
texture. This applies a Gaussian blur to a batch of images and writes the results next to the
originals.

1. Drag your background textures (or the folder holding them) onto **Drag & Drop Images/Folders Here**.
2. Set **Blur Radius** — the slider runs `1` to `200`; `10` is a soft haze, `50` is a heavy frost.
3. Click **Apply Blur**.

Each source produces a new file beside it; the originals are left alone:

```text
Assets/UI/Backgrounds/
  pause_bg.png                 # untouched
  pause_bg_blurred_24.png      # written by the tool
```

`.jpg` and `.jpeg` sources stay JPEG; every other format is written as PNG. Re-running at the same
radius appends `_1`, `_2` and so on rather than overwriting.

**Before you run it:** the tool sets `Read/Write Enabled` and `Compression: Uncompressed` on every
source texture it reads and does not put them back. Re-apply your import settings afterwards, or run
[Texture Settings Applier](#texture-settings-applier) over the folder.

> **Visual Demo**
>
> ![Image Blur Tool showing before/after comparison as blur radius slider is adjusted](../../images/editor-tools/image-blur-before-after.gif)
>
> _Adjusting blur radius from 0 to 200 pixels on a UI background texture_

---

### Sprite Cropper

`Tools > Wallstop Studios > Unity Helpers > Sprite Cropper`

Art tools export the whole canvas, so a 40-pixel-tall character often ships inside a 256x256 texture
that is mostly transparency. Sprite Cropper trims each texture to its alpha bounds, moves the pivot
so the sprite does not shift in the scene, and shrinks the 9-slice border by the same amount.

1. Drop `Assets/Sprites/Characters` into **Input directories**.
2. Leave **Sprite Name Regex** at `.*`, or narrow it (`^player_`) to crop one character.
3. Set **Left/Right/Top/Bottom Padding** to `2` if a shader samples outside the sprite (outlines,
   glow); otherwise leave them at `0`.
4. Tick **Only Necessary** so already-tight sprites are skipped.
5. Click **Find Sprites To Process**, then **Process N Sprites**.

Results are written beside the source, or into **Output Directory** if you set one:

```text
Assets/Sprites/Characters/
  player_idle_0.png            # 256x256, mostly transparency
  Cropped_player_idle_0.png    # 48x64, same pivot, sits in the same place in-scene
```

**Overwrite Originals** writes in place instead.

**Before you run it:**

- Output is always PNG bytes. In overwrite mode a `.jpg` source keeps its `.jpg` name and holds PNG
  data.
- Only `Sprite Import Mode: Single` textures are cropped. Multi-sprite sheets are reported and
  skipped — run [Sprite Sheet Extractor](#sprite-sheet-extractor) first if you need them split.
- There is no undo, so commit before you run it.
- The collapsed **Danger Zone** at the bottom rewrites `.prefab`, `.unity`, `.asset`, `.mat`,
  `.anim` and `.overrideController` files to point at the `Cropped_*` sprites. It makes you tick "I
  understand the risks and want to proceed." first, and it is the one action here you cannot walk
  back without version control.

> **Visual Demo**
>
> ![Sprite Cropper showing original sprite with padding, then cropped result side-by-side](../../images/editor-tools/sprite-cropper-comparison.gif)
>
> _Before and after: transparent padding removed while preserving sprite content and pivot_

Cropping first also makes [Sprite Atlas Generator](#sprite-atlas-generator) pack tighter.

---

### Texture Settings Applier

`Tools > Wallstop Studios > Unity Helpers > Texture Settings Applier`

An artist drops 200 tiles into the project and every one arrives on Unity's defaults — bilinear
filtering on pixel art, mipmaps you do not want, 8192 max size. This window applies one set of
importer settings to every texture under a folder, with per-platform overrides.

1. Drag `Assets/Sprites/Tiles` into **Directory Sources** (or add individual textures under
   **Specific Textures**).
2. Tick **Apply Filter Mode** and choose `Point`; tick **Apply MipMaps** and leave
   **Generate MipMaps** off.
3. Under **Default Platform Settings**, set **Max Texture Size** to `2048` and **Compression** to
   `CompressedHQ`. That group has no apply toggles — whatever it shows is written.
4. Click **Calculate Stats** — it reports `Textures to process` and `Textures that will change`, and
   **Preview (N)** lists the first 200 paths.
5. Click **Apply Settings to Textures**.

Settings that tend to travel together:

| For                   | Filter Mode | Wrap Mode | Generate MipMaps | Compression    |
| --------------------- | ----------- | --------- | ---------------- | -------------- |
| Pixel art and UI      | `Point`     | `Clamp`   | off              | `CompressedHQ` |
| Character sprites     | `Bilinear`  | `Clamp`   | off              | `CompressedHQ` |
| Tiling world textures | `Trilinear` | `Repeat`  | on               | `CompressedHQ` |

**Platform Overrides** adds an entry per platform (`Standalone`, `Android`, `iPhone`, `WebGL`,
`Switch`, ...). Each entry has its own apply toggles, so you can cap Android at `1024` while
Standalone keeps `2048`.

Nothing on disk is rewritten — only import settings — and the change is recorded as a single
`Apply Texture Settings` undo step. **Require Changes Before Apply** (on by default) skips the
reimport entirely when nothing would differ.

#### Applying texture settings from a script

The same logic is public, so a build step or a custom importer can use it directly:

```csharp
using UnityEditor;
using UnityEngine;
using WallstopStudios.UnityHelpers.Editor.Sprites;

public static class TileImportStandard
{
    public static void ApplyTo(string assetPath)
    {
        TextureSettingsApplierAPI.Config config = new()
        {
            applyFilterMode = true,
            filterMode = FilterMode.Point,
            applyMipMaps = true,
            generateMipMaps = false,
            applyPlatformMaxTextureSize = true,
            platformMaxTextureSize = 2048,
            platformOverrides = new[]
            {
                new TextureSettingsApplierAPI.PlatformOverride
                {
                    name = "Android",
                    applyMaxTextureSize = true,
                    maxTextureSize = 1024,
                },
            },
        };

        if (!TextureSettingsApplierAPI.WillTextureSettingsChange(assetPath, in config))
        {
            return;
        }

        if (
            TextureSettingsApplierAPI.TryUpdateTextureSettings(
                assetPath,
                in config,
                out TextureImporter importer
            )
        )
        {
            importer.SaveAndReimport();
        }
    }
}
```

`Config` is a struct with no defaults, so every field you care about must be set explicitly, and the
API never calls `SaveAndReimport()` for you — that is deliberate, so you can batch a whole folder
inside one `AssetDatabase.StartAssetEditing()` block. The default-platform name string is
`"DefaultTexturePlatform"`.

> **Visual Reference**
>
> ![Texture Settings Applier window showing configuration options](../../images/editor-tools/texture-settings-applier.png)
>
> _Texture Settings Applier with filter mode, wrap mode, and compression options_

---

### Sprite Pivot Adjuster

`Tools > Wallstop Studios > Unity Helpers > Sprite Pivot Adjuster`

A walk cycle where the character leans forward on some frames wobbles if every pivot is `(0.5, 0.5)`,
because the geometric center of the texture is not the visual center of the character. This computes
an alpha-weighted center of mass per sprite and writes it as a custom pivot.

1. Add `Assets/Sprites/Characters/Player` to **Input Directories**.
2. Leave **Alpha Cutoff** at `0.01` so anti-aliased fringe pixels do not drag the pivot outward.
3. Leave **Skip Unchanged (fuzzy)** on — it avoids reimporting when the pivot moves less than
   `0.001`.
4. Click **Find Sprites To Process**, then **Dry Run** to see the counts, then
   **Adjust Pivots in Directory**.

Import settings only; each changed importer is recorded as an `Adjust Sprite Pivot` undo step.

**Before you run it:**

- Single-sprite textures only.
- Textures without `Read/Write Enabled` are skipped, not fixed. Turn Read/Write on with
  [Texture Settings Applier](#texture-settings-applier) first.
- This window shares its remembered directory list with [Sprite Cropper](#sprite-cropper).

> **Visual Reference**
>
> ![Sprite Pivot Adjuster window showing alpha-weighted pivot calculation](../../images/editor-tools/sprite-pivot-adjuster.png)
>
> _Sprite Pivot Adjuster with alpha cutoff slider and directory selection_

---

### Sprite Settings Applier

`Tools > Wallstop Studios > Unity Helpers > Sprite Settings Applier`

Same idea as [Texture Settings Applier](#texture-settings-applier), but sprite-specific (pixels per
unit, pivot, sprite mode, extrude) and driven by _profiles_ — so `ui_` sprites can import at 100 PPU
with bilinear filtering while everything in `Assets/Sprites/World` imports at 16 PPU with point
filtering, in one pass.

1. Drag `Assets/Sprites` into **Directory Sources**.
2. Under **Sprite Settings Profiles**, add one profile per rule. Each has **Match By**
   (`Any`, `NameContains`, `PathContains`, `Regex`, `Extension`), **Match Pattern**, and a
   **Priority** — the highest matching priority wins.
3. Tick only the `Apply ...` toggles you want; each one reveals its value field.
4. **Calculate Stats**, then **Apply Settings to Sprites**.

**Save Profiles Asset** writes the rule set to a `SpriteSettingsProfileCollection` asset so the rest
of the team gets the same rules; **Load Profiles Asset** reads one back. There is no
`Assets > Create` entry for that asset — the button is how you make one.

#### Applying sprite settings from a script

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WallstopStudios.UnityHelpers.Editor.Sprites;

public static class SpriteImportStandard
{
    private static readonly List<SpriteSettings> Profiles = new()
    {
        new SpriteSettings
        {
            matchBy = SpriteSettings.MatchMode.PathContains,
            matchPattern = "sprites/world",
            priority = 1,
            applyPixelsPerUnit = true,
            pixelsPerUnit = 16,
            applyFilterMode = true,
            filterMode = FilterMode.Point,
        },
        new SpriteSettings
        {
            matchBy = SpriteSettings.MatchMode.NameContains,
            matchPattern = "ui_",
            priority = 5,
            applyPixelsPerUnit = true,
            pixelsPerUnit = 100,
            applyFilterMode = true,
            filterMode = FilterMode.Bilinear,
        },
    };

    public static void ApplyTo(string assetPath)
    {
        List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
            SpriteSettingsApplierAPI.PrepareProfiles(Profiles);

        if (!SpriteSettingsApplierAPI.WillTextureSettingsChange(assetPath, prepared))
        {
            return;
        }

        if (
            SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                assetPath,
                prepared,
                out TextureImporter importer
            )
        )
        {
            importer.SaveAndReimport();
        }
    }
}
```

`PrepareProfiles` compiles the regexes once — hoist it out of the per-asset loop. `NameContains`
matches the file name, `PathContains` and `Regex` match the full asset path, and `Regex` is always
case-insensitive. Applying a `pivot` also forces `Sprite Alignment` to `Custom`.

> **Visual Reference**
>
> ![Sprite Settings Applier window showing profile configuration](../../images/editor-tools/sprite-settings-applier.png)
>
> _Sprite Settings Applier with profile matching modes and import settings_

---

### Texture Resizer

`Tools > Wallstop Studios > Unity Helpers > Texture Resizer`

Scales PNGs up on disk. Reach for this when the source art is genuinely too small for the resolution
you ship at and you want the bigger pixels baked into the file rather than paid for at runtime.

1. Drag a folder into **Texture Source Paths**, or add textures to **Textures**.
2. Pick **Scaling Resize Algorithm**: `Point` for pixel art (no blur), `Bilinear` for everything
   else.
3. Set **Num Resizes** — each pass grows the texture by
   `width / (Pixels Per Unit * Width Multiplier)` (and the same for height), so several small passes
   scale more gently than one large one.
4. Tick **Dry Run** and click **Resize** — the console logs
   `[DryRun] Would resize <name> to [WxH]` for every match.
5. Clear **Dry Run** and click **Resize** for real.

**Before you run it:**

- Only `.png` files are processed; anything else is counted as skipped.
- With **Output Folder** empty this **overwrites the originals in place** and there is no undo. Set
  **Output Folder** to write copies instead.
- The stock multipliers (`0.54` width, `0.245` height) grow a texture non-uniformly — a 128x128
  becomes 130x133 in one pass. Set both to the same value if you want a square scale.
- The final size is clamped to 16384 on each axis.

> **Visual Reference**
>
> ![Texture Resizer window showing resize algorithm options](../../images/editor-tools/texture-resizer.png)
>
> _Texture Resizer with bilinear/point algorithm selection and multiplier settings_

---

### Fit Texture Size

`Tools > Wallstop Studios > Unity Helpers > Fit Texture Size`

Every texture in a fresh project imports at `Max Size: 2048`, whatever its real dimensions. A 64x64
icon wastes an import slot; a 4096-wide sky is silently halved. This reads each source file's actual
dimensions and sets `Max Size` to the power of two that fits.

1. Add `Assets/Sprites` to the source list (the window pre-fills it if that folder exists).
2. Choose a **Fit Mode**:

   | Mode             | Effect                                                        |
   | ---------------- | ------------------------------------------------------------- |
   | `GrowAndShrink`  | Smallest power of two that fits the source. The usual choice. |
   | `GrowOnly`       | Raise `Max Size` when the source is bigger; never lower it.   |
   | `ShrinkOnly`     | Lower `Max Size` to the tightest fit; never raise it.         |
   | `RoundToNearest` | Nearest power of two to the source size, ties rounding up.    |

3. Narrow the run if you want to: **Only Current Selection**, **Only Sprites**, **Name Filter**
   (substring, or a regex with **Use Regex**), **Label Filter (CSV)**.
4. Set **Min Allowed Size** / **Max Allowed Size** to keep results inside a range (defaults `32` and
   `8192`).
5. Click **Calculate Potential Changes**. You get
   `N textures would be modified ... Grows: G, Shrinks: S, Unchanged: U`.
6. Click **Run Fit Texture Size**.

A 1920x1080 source under `GrowAndShrink` lands on `2048`; a 64x64 source under `ShrinkOnly` lands on
`64`.

Import settings only, recorded as a `Fit Texture Size` undo step per asset, and cancellable from the
progress bar. **Apply to Standalone / Android / iOS** additionally writes a platform override with
the same size. **Fit Mode** is not persisted across a domain reload — re-select it after a
recompile.

> **Visual Reference**
>
> ![Fit Texture Size window showing fit mode options and preview](../../images/editor-tools/fit-texture-size.png)
>
> _Fit Texture Size with GrowAndShrink/GrowOnly/ShrinkOnly mode selection_

---

## Animation Tools

### Sprite Animation Editor (Animation Viewer Window)

`Tools > Wallstop Studios > Unity Helpers > Sprite Animation Editor`

Unity's Animation window is built for curves, and reordering four sprite keyframes in it is more work
than it should be. This opens a clip as a list of frames you can drag, with the animation playing
next to it.

1. Click **Browse Clips (Multi)...** and pick `PlayerWalk.anim` (and any other clips you want open at
   the same time).
2. Click the clip in the left panel to make it active. The preview starts playing immediately.
3. Drag a frame in the **Frames** panel to move it, or type a new position into its **Order:** field
   and press Enter.
4. Set **Preview FPS** and click **Apply Preview FPS** to see the new speed.
5. Click **Save Active Clip**.

**Before you save:** saving writes the preview FPS onto the clip and re-spaces every keyframe to
match. That is usually what you want after changing the speed of a walk cycle, but it means you cannot save a
reorder while leaving the original timing alone.

This is an editor for frame _order_ and _speed_. Adding or removing frames is
[Animation Creator](#animation-creator)'s job; the `X` next to a loaded clip closes it in this window
rather than deleting anything. One clip previews at a time.

> **Visual Demo**
>
> ![Sprite Animation Editor showing animation preview playing while frames are dragged to reorder](../../images/editor-tools/sprite-animation-editor-reorder.gif)
>
> _Drag-and-drop frame reordering with real-time preview updates_
>
> ![Sprite Animation Editor FPS adjustment showing animation speed changing live](../../images/editor-tools/sprite-animation-editor-fps.gif)
>
> _Adjusting FPS and seeing immediate preview speed change_

---

### Animation Creator

`Tools > Wallstop Studios > Unity Helpers > Animation Creator`

An artist hands you `Player_Idle_0.png` through `Player_Attack_11.png` — sixty files, eight
animations. Making those clips by hand is an hour of dragging, and Unity's default sort puts frame 10
before frame 2. This groups the sprites by name and writes the clips.

1. Add `Assets/Sprites/Player` as a source folder.
2. Leave **Sprite Name Regex** at `.*`, or narrow it (`^Player_Attack`) to build one clip.
3. Click **Generate Auto-Parse Preview** to see the groups it found before anything is created.
4. Click **Auto-Parse Matched Sprites into Animations**. You get one entry per group under
   **Animation Data**.
5. Set **FPS** and **Loop** per clip.
6. Click **Create Animations**.

These naming patterns are detected without any configuration:

```text
Player_Idle_0.png,  Player_Idle_1.png     -> Player_Idle,  frames 0..N
slime-walk-01.png,  slime-walk-02.png     -> slime-walk,   frames 1..N
Mage/Attack (0).png, Mage/Attack (1).png  -> Attack,       frames 0..N
```

`@2x` density suffixes are stripped before grouping.

If your files do not fit any of those, tick **Enable Custom Group Regex** and supply one with `base`
and `index` named groups. Use the **Regex Tester** box to check it against a real file name before
applying:

```text
^(?<base>.*?)(?:_|\s|-)?(?<index>\d+)$    # base plus trailing digits
^Enemy_(?<base>Walk)_(?<index>\d+)$        # only Enemy_Walk_N
```

**Where clips land:** each clip is written next to the first sprite in its group, with a unique name.
There is no output folder picker — use [Animation Copier](#animation-copier) to move a generated set
into `Assets/Animations`.

**Also worth knowing:**

- **Prefix Leaf Folder Name** / **Prefix Full Folder Path** keep `Idle` from four different
  characters from colliding, and **Resolve Duplicate Animation Names** (on by default) renames any
  that still do.
- Frames are ordered by Unity's natural sort at creation time, so `2` sorts before `10`.
- **Framerate Mode** is `Constant` (one FPS, the default at `12`) or `Curve`, where an
  `AnimationCurve` drives FPS across the clip — the **Flat / Ease In / Ease Out / Sync** buttons give
  you a starting shape. Use `Curve` for an attack that snaps and then holds.
- **Preview** plays a clip entry before you create anything.
- **Configuration Persistence** saves the whole window state to `.animation-creator.json` in a source
  folder, so the next person to import that folder gets your settings.

> **Visual Demo**
>
> ![Animation Creator auto-parse workflow showing sprites being grouped and clips generated](../../images/editor-tools/animation-creator-auto-parse.gif)
>
> _One-click auto-parse: sprites grouped by naming pattern, clips generated instantly_

Next: adjust the results in [Sprite Animation Editor](#sprite-animation-editor-animation-viewer-window),
or add events with [Animation Event Editor](#animation-event-editor).

---

### Animation Copier

`Tools > Wallstop Studios > Unity Helpers > Animation Copier`

[Animation Creator](#animation-creator) leaves clips beside their sprites, but you keep clips in
`Assets/Animations`. Re-running it after an art update produces a mix of genuinely new clips,
genuinely changed clips, and dozens that are byte-for-byte what you already have. This tells the three
apart and moves only what matters.

1. Set **Source Path** (defaults to `Assets/Sprites`) and **Destination Path** (defaults to
   `Assets/Animations`).
2. Click **Analyze Source & Destination**.
3. Read the counts: **- New:**, **- Changed:**, **- Unchanged (Duplicates):**.
4. Expand **New** and **Changed** to review, using **Filter** (plus **Regex**) and
   **Select All** / **Select None** to pick what moves.
5. Tick **Dry Run (no changes)** and run the copy once to see what it would do.
6. Clear it, then click **Copy New (N)** or **Copy Changed (N)**.

Copying a changed clip preserves its GUID, so every Animator that already references it keeps
working.

"Changed" is decided by comparing clip contents field by field — frame rate, length, wrap mode, every
curve key, every event and its parameters — not by an asset hash, so a re-import that produces an
identical clip does not show up as a change.

**Cleanup** is where the destructive buttons live: **Delete N Unchanged Source Duplicates** removes
the redundant copies left behind in the source folder, and
**Mirror Delete Destination Orphans (N)** deletes destination clips with no source any more. Both
honour **Dry Run**. **Export Preview Report** writes the analysis to a file if you would rather review
it outside the editor.

> **Visual Reference**
>
> ![Animation Copier window showing source/destination analysis](../../images/editor-tools/animation-copier.png)
>
> _Animation Copier with new/changed/unchanged/orphan groups and copy actions_

---

### Sprite Sheet Animation Creator

`Tools > Wallstop Studios > Unity Helpers > Sprite Sheet Animation Creator`

Your character is one `hero.png` sliced into 48 sprites in the Sprite Editor, and frames 0-7 are the
idle, 8-15 the run, 16-23 the attack. This lets you select those ranges visually and turn each into a
clip.

1. Assign the sliced texture to **Sprite Sheet**, or click **Load Sprites** and pick the file.
2. Drag across the thumbnails to select frames 0-7.
3. Click **Add Animation Definition**. Name it `Hero_Idle`.
4. Set **Default FPS:**, **Looping:** and **Cycle Offset:**, then hit **Preview This** and use
   **▶ Play** to check it.
5. Repeat for the run and attack ranges — the **Start Idx:** / **End Idx:** fields let you correct a
   selection by hand.
6. Click **Generate Animation Files** and choose an output folder.

**FPS Curve:** takes an `AnimationCurve` instead of a flat rate, so an attack can hold on its impact
frame. The curve changes how far apart keyframes are placed; the generated clip's own frame rate is
always 60, which is what lets fractional timings land cleanly.

The texture must already be sliced (`Sprite Mode: Multiple`) — this window reads Unity's sprites, it
does not slice for you.

> **Visual Demo**
>
> ![Sprite Sheet Animation Creator showing drag-select across sprite thumbnails with live preview](../../images/editor-tools/sprite-sheet-creator-select.gif)
>
> _Drag-select frame ranges on sprite sheet thumbnails with instant preview playback_

---

### Sprite Sheet Extractor

`Tools > Wallstop Studios > Unity Helpers > Sprite Sheet Extractor`

The reverse of the tool above: take a packed sheet and write each sprite out as its own PNG. Needed
whenever something downstream wants files rather than sub-assets — a third-party animation tool, or
[Sprite Cropper](#sprite-cropper), which only handles single-sprite textures.

1. Add the folder holding your sheets to **Input Directories**.
2. Pick a **Mode**:

   | Mode             | Use when                                                                      |
   | ---------------- | ----------------------------------------------------------------------------- |
   | `FromMetadata`   | The sheet is already sliced in Unity. Keeps names, rects, pivots and borders. |
   | `GridBased`      | Even cells. Set **Columns**/**Rows** or leave **Grid Size Mode** on `Auto`.   |
   | `PaddedGrid`     | Even cells with gutters. Set **Left/Right/Top/Bottom** padding.               |
   | `AlphaDetection` | Sprites are scattered irregularly. Tune **Alpha Threshold** (default `0.01`). |

3. Click **Find Sprite Sheets**, then **Preview Slicing** to see the detected rects drawn over the
   texture (**Show Overlay**).
4. Choose a **Pivot Mode** — `Center`, the eight edge and corner presets, or `Custom` with explicit
   **X** / **Y**.
5. Set **Output Directory**, then click **Extract N Sprite(s)**.

**Auto-detection:** with **Grid Size Mode** on `Auto`, the **Algorithm** dropdown picks how cell size
is inferred: `AutoBest` (tries each and stops once one reaches 90% confidence), `UniformGrid`,
`BoundaryScoring`, `ClusterCentroid`, `DistanceTransform` or `RegionGrowing`. Filling in
**Expected Sprite Count** dramatically improves the result, and **Snap to Divisor** keeps the cell
size an exact divisor of the texture.

**Also worth knowing:**

- **Dry Run** turns the extract button into `Dry Run: Preview N Sprite(s)` and writes nothing.
- Per-sheet settings override the global ones; **Apply Global to All** pushes yours down, and
  **Save Config** writes a `<texture>.spritesheet.json` beside the sheet so a re-extraction is
  reproducible. A `Config Stale` badge appears when the texture has changed since.
- **Preserve Import Settings** (on by default) copies the source's importer settings to each output.
- The **Danger Zone: Reference Replacement** section points existing assets at the extracted
  sprites. Like the one in Sprite Cropper, it requires ticking
  "I understand the risks and want to proceed." and it is not undoable.

---

### Animation Event Editor

`Tools > Wallstop Studios > Unity Helpers > AnimationEvent Editor`

Putting a footstep sound on frame 4 of a run cycle in Unity's Animation window means finding the
right time value and typing a method name from memory. This shows the sprite at each event, lists the
methods that are actually callable, and edits the parameter with the right field type.

First, mark the methods you want to fire:

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;

public sealed class PlayerAnimationEvents : MonoBehaviour
{
    [AnimationEvent]
    public void PlayFootstep() { }

    [AnimationEvent]
    public void SpawnSlashVfx(string socketName) { }

    [AnimationEvent]
    public void EnableHitbox(int hitboxIndex) { }

    [AnimationEvent]
    public void SetStance(Stance stance) { }
}
```

Then:

1. Drag the GameObject holding the `Animator` into **Animator Object**.
2. Pick the clip from **Animation** (use **Animation Search** to filter a long list).
3. Type `4` into **FrameIndex** and click **Add Event**.
4. Choose **TypeName** `PlayerAnimationEvents`, then **MethodName** `PlayFootstep`.
5. Click **Save**.

**Method signatures Unity accepts** — and therefore the only ones this window lists — return `void`
and take either no parameter or exactly one of: `int`, `float`, `string`, `UnityEngine.Object`, or an
enum. `UnityEngine.Object` means that exact type; a method taking `GameObject` or `Sprite` will not
appear.

**Explicit Mode** is on by default and lists only `[AnimationEvent]` methods, which is what keeps the
dropdown readable in a real project. Turn it off to see every signature-valid method on every
`MonoBehaviour` (private ones included) and use **Type Search** / **Method Search** to narrow.

By default an `[AnimationEvent]` method is offered on its declaring class only. Use
`[AnimationEvent(ignoreDerived = false)]` when subclasses should offer it too.

**Also worth knowing:**

- **Control Frame Time** off (the default) means you work in whole frames; on, you edit the raw time
  value.
- The sprite preview needs `Read/Write Enabled` on the texture. When it is missing you get a **Fix**
  button that turns it on.
- **Move Up** / **Move Down** swap events that sit at the same time; **Re-Order** sorts everything by
  time. **Duplicate** copies an event, and **Reset** reverts to the last saved state.
- The Save / Reset / Re-Order row only appears once you have unsaved changes; otherwise the window
  says `No changes detected...`.
- Shortcuts: `Delete` removes the focused event, `Ctrl+D` duplicates it, arrow keys navigate. The
  duplicate shortcut is `Ctrl` only, including on macOS.

---

## Sprite Atlas Tools

### Sprite Atlas Generator

`Tools > Wallstop Studios > Unity Helpers > Sprite Atlas Generator`

A `.spriteatlas` asset holds a hand-maintained list of sprites, so every new sprite an artist adds is
one someone has to remember to drag in. This drives the atlas from a rule instead — a regex, an asset
label, or both — and rescans on demand.

1. Click **Create New Config in 'Assets/Data'**. That makes a `ScriptableSpriteAtlas` asset (you can
   also use `Assets > Create > Wallstop Studios > Unity Helpers > Scriptable Sprite Atlas Config`).
2. Set **Output Sprite Atlas Directory** and **Output Sprite Atlas Name** — the atlas is written to
   `<directory>/<name>.spriteatlas`.
3. Click **Add New Source Folder Entry** and point it at `Assets/Sprites/Characters`.
4. Choose how sprites are selected:

   | Selection Mode    | What it matches                                                                               |
   | ----------------- | --------------------------------------------------------------------------------------------- |
   | `Regex`           | Every pattern in **Regexes (AND logic)** must match the asset path.                           |
   | `Labels`          | Asset labels, combined by **Label Selection Mode**: `All` (every label) or `AnyOf` (any one). |
   | `Regex \| Labels` | Both, joined by **Regex & Tags Logic** (`And` / `Or`).                                        |

5. Click **Scan Folders for '<config name>'**. The window reports `To Add: N sprites.` and
   `To Remove: N sprites.` before anything changes.
6. Click **Sync List To Scan Result (N add, N remove)**.
7. Click **Generate/Update '<name>.spriteatlas' ONLY**, then **Pack All Generated Sprite Atlases** —
   or **Generate + Pack** to do both.

A character atlas that picks up every new idle frame automatically:

```text
Folder Path:          Assets/Sprites/Characters
Selection Mode:       Regex
Regexes (AND logic):  ["player_", "_idle_"]
Max Texture Size:     2048
Padding:              4
Compression:          CompressedHQ
```

A UI atlas driven by labels, so artists opt sprites in from the Inspector rather than by filename:

```text
Folder Path:            Assets/Sprites/UI
Selection Mode:         Labels
Label Selection Mode:   AnyOf
Labels:                 ["icon", "hud"]
Max Texture Size:       1024
Padding:                2
```

The **Labels** fields are dropdowns populated from every label in the project — see
[Sprite Label Processor](#sprite-label-processor) for what keeps that list current.

**Also worth knowing:**

- Each source folder entry has an exclusion block — **Exclude Regexes (OR logic)**,
  **Exclude Labels** and **Exclude Path Prefixes** — for keeping work-in-progress or reference art out of a
  shipped atlas.
- **Platform Overrides** on the config set per-platform max size and compression for Standalone,
  iPhone and Android independently.
- **Force Uncompressed for N Source Sprites in '<config>'** sets the _source_ sprites to uncompressed
  so the packer has full-quality input. It asks for confirmation first, and it changes those source
  assets.
- Regex matching is case-insensitive. A pattern that fails to compile is logged and matches nothing.

> **Visual Reference**
>
> ![Sprite Atlas Generator window showing regex configuration](../../images/editor-tools/sprite-atlas-generator.png)
>
> _Sprite Atlas Generator with regex-based sprite selection and packing settings_
>
> ![Sprite Atlas Generator scan results showing sprites to add](../../images/editor-tools/sprite-atlas-generator-scan.gif)
>
> _Scanning folders and previewing which sprites will be added to the atlas_

---

<a id="validation--quality-tools"></a>
<a id="validation-quality-tools"></a>

## Validation & Quality Tools

### Prefab Checker

`Tools > Wallstop Studios > Unity Helpers > Prefab Checker`

Someone deletes a script, and forty prefabs quietly acquire a "Missing (Mono Script)" slot that only
shows up when a scene loads. Prefab Checker walks every prefab under a folder and logs the problems
with clickable links.

1. Click **Add Folder** and pick `Assets/Prefabs` (already added for you if that folder exists).
2. Leave the default checks on and click **Run Checks**.
3. Click a console line to select the offending prefab.

| Check                            | Reports                                                     | Default |
| -------------------------------- | ----------------------------------------------------------- | ------- |
| **Missing Scripts**              | Components whose script asset is gone                       | On      |
| **Nulls in Lists/Arrays**        | `null` elements inside a serialized list or array           | On      |
| **Missing Required Components**  | A `[RequireComponent]` dependency that is not on the prefab | On      |
| **Null Object References**       | Unassigned `UnityEngine.Object` fields                      | On      |
| **Only if [ValidateAssignment]** | Narrows the check above to annotated fields only            | On      |
| **Disabled Root GameObject**     | Prefab roots saved inactive                                 | On      |
| **Empty String Fields**          | Serialized `string` fields left empty                       | Off     |
| **Disabled Components**          | `Behaviour` components saved disabled                       | Off     |

Everything except **Empty String Fields** is logged as an error; empty strings are warnings.

**Narrowing a run:** **Include Labels (comma)** and **Exclude Labels (comma)** filter by asset label,
and **Deny Component Types (comma names)** flags prefabs carrying a component you have banned
(a debug-only behaviour, say).

**Fixing and reporting:** **Fix Missing Scripts** strips dead component slots, but it stays disabled
until you tick **Enable Auto-fix options** — the gate is deliberate, because the fix deletes data.
**Export Report (JSON)** and **Export Report (CSV)** write the same findings to a file for a build
step or a review.

Annotate the fields you actually care about so the null check stays useful:

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;

public sealed class Turret : MonoBehaviour
{
    [ValidateAssignment]
    public Transform muzzle;

    [ValidateAssignment]
    public GameObject projectilePrefab;

    // Not annotated: optional, and a null here is not a bug.
    public AudioClip idleHum;
}
```

> **Visual Reference**
>
> ![Prefab Checker window showing validation check options](../../images/editor-tools/prefab-checker.png)
>
> _Prefab Checker with configurable validation checks and folder selection_
>
> ![Prefab Checker results in console showing detected issues](../../images/editor-tools/prefab-checker-results.png)
>
> _Console output showing detected prefab issues with clickable links_

---

### Unity Method Analyzer

`Tools > Wallstop Studios > Unity Helpers > Unity Method Analyzer`

A `private void Start()` in a subclass whose base class also has `private void Start()` compiles
cleanly, and Unity calls only one of them. This scans your C# source for that family of mistake.

1. Add `Assets/Scripts` to the source directories.
2. Click **Analyze Code**.
3. Double-click a result to jump to the line.

| It finds                    | Example                                                                        |
| --------------------------- | ------------------------------------------------------------------------------ |
| A missing `override`        | A derived method hiding the base one instead of overriding it                  |
| A wrong lifecycle signature | `OnCollisionEnter(Collider c)` where Unity calls `OnCollisionEnter(Collision)` |
| A shadowed lifecycle method | Base and derived both declare `private void Start()`                           |
| A static lifecycle method   | `static void Awake()`, which Unity never calls                                 |

Results group by file, severity or category, and export as JSON or Markdown for a CI gate.

Silence a deliberate case — a test fixture that exists precisely to be wrong — with
`[SuppressAnalyzer]`:

```csharp
[SuppressAnalyzer("Test fixture for analyzer validation")]
public sealed class DeliberatelyShadowedStart : BaseBehaviour
{
    private void Start() { }
}
```

**Full reference:** [Unity Method Analyzer Guide](./unity-method-analyzer.md)

> **Visual Demo**
>
> ![Unity Method Analyzer window showing detected issues](../../images/editor-tools/unity-method-analyzer/analyzer-overview.png)
>
> _The analyzer scanning a project and displaying categorized issues_

---

### Serialized Field Validator

`Tools > Wallstop Studios > Unity Helpers > Validate Serialized Fields In Selection`

Unity declines to serialize some types and declines silently. Select the script and run the command;
it names every field that will be empty after the next domain reload, and what to use instead.

```csharp
public sealed class Loot : ScriptableObject
{
    public Dictionary<string, int> drops;   // gone on the next domain reload
    public (int, float) weightedRoll;       // gone, and it IS [Serializable]
}
```

`SerializedObject.FindProperty("drops")` returns `null`, `JsonUtility.ToJson` omits the field, and
nothing is logged. Whatever a designer authored into it is gone, usually discovered from a build.
`[Serializable]` is not the discriminator, which is what makes the rule hard to work out from
outside: `ValueTuple<int, float>` carries it and is dropped anyway.

The console tells you the fix:

```text
Loot.drops is declared as Dictionary<string, int>, which Unity does not serialize. Anything
authored into it is gone on the next domain reload. Use SerializableDictionary<string, int>
instead.
```

| Declared as                             | Use instead                                  |
| --------------------------------------- | -------------------------------------------- |
| `Dictionary<TKey, TValue>`              | `SerializableDictionary<TKey, TValue>`       |
| `SortedDictionary<TKey, TValue>`        | `SerializableSortedDictionary<TKey, TValue>` |
| `HashSet<T>`                            | `SerializableHashSet<T>`                     |
| `SortedSet<T>`                          | `SerializableSortedSet<T>`                   |
| `Nullable<T>`                           | `SerializableNullable<T>`                    |
| `ValueTuple<...>`, `Tuple<...>`         | `SerializableValueTuple<...>`                |
| `KeyValuePair<TKey, TValue>`            | `SerializableValueTuple<TKey, TValue>`       |
| `Queue<T>`, `Stack<T>`, `LinkedList<T>` | `List<T>`                                    |

**How it decides:** it constructs the type, wraps it in a `SerializedObject`, and asks which fields
arrived. That is Unity's own answer rather than a model of its rules, so it cannot misreport the
generic user types Unity has serialized since 2020. Every `public` or `[SerializeField]` field with
no `SerializedProperty` is reported, inherited ones included.

**Silencing a field:** mark it `[NonSerialized]`. That is the standard way to say "runtime only",
Unity honours it, and so does this.

**Why the selection and not the whole project:** validating a type means constructing one, and
constructing every type in a project runs the startup half of the project. Select what you just
wrote — a `MonoScript`, a prefab, a scene object or an asset. A prefab contributes every component on
it.

---

## Custom Component Editors

### MatchColliderToSprite Editor

`MatchColliderToSprite` reshapes a `PolygonCollider2D` to the current sprite in `OnValidate`, which
does not fire when the sprite is swapped from code or by an animation. The inspector adds a
**MatchColliderToSprite** button that runs the same pass on demand, recorded as a
`Match Collider To Sprite` undo step.

Reach for it after changing a sprite at runtime, or whenever the collider and the sprite have drifted
apart. See [MatchColliderToSprite](../inspector/utility-components.md#matchcollidertosprite) for the
component itself.

---

### PolygonCollider2DOptimizer Editor

An auto-generated `PolygonCollider2D` from a detailed sprite can carry hundreds of points, and Physics
2D pays for every one of them. This inspector shows a single **Tolerance** field and an **Optimize**
button that simplifies the outline in place.

Raise **Tolerance** until the silhouette starts to visibly change, then back off one step. `0.1`–`0.5`
is the usual working range; above `2.0` you are trading shape for point count. Set **Tolerance** to
`0` and click **Optimize** to restore the original outline — the component keeps it. **Optimize** is
also recorded as an `Optimize Polygon Collider` undo step.

See [PolygonCollider2DOptimizer](../inspector/utility-components.md#polygoncollider2doptimizer) for
the component.

---

### EnhancedImage Editor

`EnhancedImage` extends Unity's `Image` with an HDR tint and a shape mask, both of which need the
package's `BackgroundMask` material to do anything. The custom inspector is mostly there to notice
when that material is missing.

- If the component is still on Unity's **Default UI Material**, a yellow
  **Incorrect Material Detected - Try Fix?** button appears. Clicking it finds and assigns
  `Shaders/Materials/BackgroundMask-Material.mat` as a `Fix EnhancedImage Material` undo step.
- **HDR Color** multiplies the image; push intensity above `1.0` to make it bloom under
  post-processing.
- **Shape Mask** takes a `Texture2D`. It only does something if the assigned material's shader
  exposes a `_ShapeMask` texture property — the inspector says so in the tooltip rather than failing
  silently.

See [EnhancedImage](../inspector/visual-components.md#enhancedimage-ugui) for the component and its
runtime API.

---

<a id="property-drawers--attributes"></a>
<a id="property-drawers-attributes"></a>

## Property Drawers & Attributes

These are the inspector attributes the tools above lean on most. The
[Inspector documentation](../inspector/inspector-overview.md) covers the full set.

<a id="winlineeditor-property-drawer"></a>

### WInLineEditor Property Drawer

Tuning an ability means selecting the `AbilityConfig` asset, editing it, then selecting the character
again to see the result. `[WInLineEditor]` draws the referenced asset's own inspector underneath the
field so you never leave.

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;

public sealed class AbilityLoadout : MonoBehaviour
{
    // Inherits the project-wide default from Unity Helpers Settings.
    [WInLineEditor]
    public AbilityConfig primary;

    // Always open, no object picker, taller body.
    [WInLineEditor(WInLineEditorMode.AlwaysExpanded, inspectorHeight: 260f, drawObjectField: false)]
    public AbilityConfig secondary;

    // Collapsed by default, with the texture preview turned on.
    [WInLineEditor(WInLineEditorMode.FoldoutCollapsed, drawPreview: true, previewHeight: 96f)]
    public Texture2D icon;
}
```

| Mode               | Behaviour                                                        |
| ------------------ | ---------------------------------------------------------------- |
| `UseSettings`      | Default. Follows the Inline Editors setting in Project Settings. |
| `AlwaysExpanded`   | Inspector is always drawn.                                       |
| `FoldoutExpanded`  | Foldout, open on first draw.                                     |
| `FoldoutCollapsed` | Foldout, closed on first draw.                                   |

Constructor parameters, in positional order:
`mode`, `inspectorHeight` (default `200`, floored at `160`), `drawPreview` (`false`),
`previewHeight` (`64`, floored at `40`), `drawObjectField` (`true`), `drawHeader` (`true`),
`enableScrolling` (`true`), `minInspectorWidth` (`520`; below this width a horizontal scrollbar
appears, `0` disables that). Prefer named arguments — the order is easy to get wrong.

**Field-only.** Unlike the other attributes here, `[WInLineEditor]` targets fields, so on an
auto-property you need `[field: WInLineEditor]`.

It reuses Unity's own editor for the target, so custom inspectors, validation and undo all still
work. Foldout animation speed lives in
[Inspector Settings](../inspector/inspector-settings.md#inline-editor-settings).

> **Visual Reference**
>
> ![WInLineEditor showing embedded ScriptableObject inspector and expansion](../../images/editor-tools/winlineeditor-expanded.gif)
>
> _WInLineEditor with embedded inspector for a ScriptableObject reference with foldout and collapse transitions_

---

### WShowIf Property Drawer

Half the fields on a spawner only matter when it is set to burst mode, and showing them the rest of
the time is how designers set the wrong one. `[WShowIf]` hides a field until its condition holds.

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;

public sealed class EnemySpawner : MonoBehaviour
{
    public enum SpawnMode
    {
        Continuous = 1,
        Burst = 2,
    }

    public SpawnMode mode = SpawnMode.Continuous;

    [WShowIf(nameof(mode), expectedValues = new object[] { SpawnMode.Burst })]
    public int burstSize = 5;

    [WShowIf(nameof(mode), inverse: true, expectedValues: new object[] { SpawnMode.Burst })]
    public float spawnsPerSecond = 1f;

    public bool useSpawnBudget;

    [WShowIf(nameof(useSpawnBudget))]
    public int maxAlive = 20;

    [WShowIf(nameof(maxAlive), WShowIfComparison.GreaterThan, 50)]
    public bool warnAboutPerformance;

    public string overrideLayerName;

    [WShowIf(nameof(overrideLayerName), WShowIfComparison.IsNotNullOrEmpty)]
    public int sortingOrder;
}
```

`inverse` and `comparison` are **constructor arguments** (`inverse: true`), not object-initializer
assignments — `inverse = true` will not compile. `expectedValues` accepts either form.

| Comparison                          | Use for                                     |
| ----------------------------------- | ------------------------------------------- |
| `Equal`, `NotEqual`                 | Any equatable value; the default is `Equal` |
| `GreaterThan`, `GreaterThanOrEqual` | Numbers and any `IComparable`               |
| `LessThan`, `LessThanOrEqual`       | Numbers and any `IComparable`               |
| `IsNull`, `IsNotNull`               | Object references                           |
| `IsNullOrEmpty`, `IsNotNullOrEmpty` | Strings and collections                     |

`WShowIfComparison.Unknown` exists only for backward compatibility and is marked obsolete.

The condition can be a field, a property (including a `[field: SerializeField]` auto-property), a
parameterless method, or a dotted path into a nested serialized type:

```csharp
[WShowIf(nameof(damage) + "." + nameof(DamageProfile.isCritical))]
public float criticalMultiplier;
```

> **Visual Reference**
>
> ![WShowIf showing field visibility changing based on toggle](../../images/editor-tools/wshowif-boolean.gif)
>
> _Field appears/disappears based on enum toggle state_

---

### StringInList Property Drawer

A `string` field that has to match an animator state name or an asset label is a typo waiting to
happen. `[StringInList]` turns it into a dropdown of the values that are actually valid.

```csharp
using System.Collections.Generic;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Core.Helper;

public sealed class SpawnRule : MonoBehaviour
{
    // Fixed set.
    [StringInList("Idle", "Run", "Jump")]
    public string startState = "Idle";

    // Every asset label in the project, kept current by the Sprite Label Processor.
    [StringInList(typeof(Helpers), nameof(Helpers.GetAllSpriteLabelNames))]
    public List<string> requiredLabels = new();

    // A provider on this same type.
    [StringInList(nameof(GetPoolNames))]
    public string poolName;

    private IEnumerable<string> GetPoolNames()
    {
        yield return "Grunt";
        yield return "Archer";
        yield return "Brute";
    }
}
```

That second field is how the package itself populates the label pickers in
[Sprite Atlas Generator](#sprite-atlas-generator).

A provider method takes no parameters and returns `string[]` or `IEnumerable<string>`. It may be
`private`, and it may be static or an instance method. `[StringInList(typeof(T), "Method")]` looks for
a static method on `T` first, then an instance one; `[StringInList("Method")]` looks on the decorated
object's own type.

- On an `int` field the dropdown selects by index.
- On an array or list you get a UI Toolkit list with a dropdown per element, plus add, remove and
  drag-to-reorder.
- Typing filters the list. `Tab` accepts the highlighted match, `Enter` just fills the search box.
- Long lists paginate. The page size is
  **Project Settings ▸ Wallstop Studios ▸ Unity Helpers ▸ StringInList Page Size** (default `25`),
  shared with the `SerializableType` drawer.

> **Visual Reference**
>
> ![StringInList dropdown with search and pagination](../../images/editor-tools/stringinlist-dropdown.png)
>
> _StringInList dropdown showing search filtering and pagination_
>
> ![StringInList with list field showing add/remove/reorder](../../images/editor-tools/stringinlist-list.gif)
>
> _StringInList on a List field with per-element dropdowns and drag reordering_

---

### IntDropDown Property Drawer

For an `int` that only has a handful of legal values, a free text field lets someone type `3000` into
a texture size. `[IntDropDown]` restricts it to the list.

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;

public sealed class AtlasSettings : MonoBehaviour
{
    [IntDropDown(32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384)]
    public int maxTextureSize = 2048;

    [IntDropDown(0, 2, 4, 8, 16, 32)]
    public int padding = 4;
}
```

Those two fields are lifted from `ScriptableSpriteAtlas`, which is exactly how
[Sprite Atlas Generator](#sprite-atlas-generator) keeps its packing settings legal.

The attribute is spelled `IntDropDown` with a capital `D`. Like `StringInList` it also accepts a
provider: `[IntDropDown(typeof(T), nameof(T.Method))]` or `[IntDropDown(nameof(Method))]`, returning
`int[]` or `IEnumerable<int>`.

> **Visual Reference**
>
> ![IntDropDown showing texture size options](../../images/editor-tools/intdropdown-texturesize.gif)
>
> _IntDropDown for texture sizes showing power-of-two options_

---

### WValueDropDown Property Drawer

`StringInList` and `IntDropDown` for everything else — floats, bools, enums, Unity structs, and your
own serializable types.

```csharp
using System.Collections.Generic;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;

public sealed class WeaponTuning : MonoBehaviour
{
    [WValueDropDown(0.5f, 1.0f, 1.5f, 2.0f)]
    public float damageMultiplier = 1.0f;

    [WValueDropDown("Easy", "Normal", "Hard", "Insane")]
    public string difficulty = "Normal";

    // Instance provider: the options depend on this component's own state.
    [WValueDropDown(nameof(GetMuzzleOffsets), typeof(Vector2))]
    public Vector2 muzzleOffset;

    public List<Vector2> configuredMuzzles = new();

    private IEnumerable<Vector2> GetMuzzleOffsets()
    {
        return configuredMuzzles;
    }
}
```

There is a `params` overload for every primitive (`bool`, `char`, all the integer widths, `float`,
`double`, `string`). For anything else, use a provider:

- `[WValueDropDown(typeof(Library), nameof(Library.GetPresets))]` — static provider, element type
  inferred from the return type.
- `[WValueDropDown(nameof(GetOptions), typeof(Vector2))]` — instance provider on the decorated type,
  value type stated.
- `[WValueDropDown(typeof(Preset), presetA, presetB)]` — an inline list of custom-typed values.

Providers follow the same rules as `StringInList`: parameterless, may be private, returning an array
or `IEnumerable`. Labels come from `ToString()`, so give a custom type a readable one.

Full constructor reference:
[Inspector Selection Attributes](../inspector/inspector-selection-attributes.md#wvaluedropdown).

> **Visual Reference**
>
> ![WValueDropDown with predefined values](../../images/inspector/selection/wvalue-dropdown-basic.gif)
>
> _WValueDropDown showing predefined integer, float, and string values_

---

### WReadOnly Property Drawer

Shows a serialized value in the inspector without letting anyone edit it — useful for a number your
code derives and a designer should only ever read.

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;

public sealed class Health : MonoBehaviour
{
    public int baseHealth = 100;
    public int equipmentBonus;

    [WReadOnly]
    public int totalHealth;

    private void OnValidate()
    {
        totalHealth = baseHealth + equipmentBonus;
    }
}
```

No parameters and nothing to configure. It works on any serialized type and keeps the field's normal
height and layout.

For attributes that _reject_ a bad value rather than just displaying it, see
[Inspector Validation Attributes](../inspector/inspector-validation-attributes.md).

> **Visual Reference**
>
> ![WReadOnly showing grayed-out calculated value in inspector](../../images/editor-tools/wreadonly-inspector.png)
>
> _WReadOnly field showing totalHealth as a non-editable calculated value_

---

<a id="automation--utilities"></a>
<a id="automation-utilities"></a>

## Automation & Utilities

<a id="scriptableobject-singleton-creator"></a>

### ScriptableObject Singleton Creator

Runs automatically on editor load. No menu item.

`ScriptableObjectSingleton<T>` loads its asset from `Resources`, which means a new teammate's first
run of the game fails on a settings asset nobody committed. This watches for singleton types with no
asset and creates them, and relocates any that end up in the wrong folder.

```csharp
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Utils;

public sealed class GameSettings : ScriptableObjectSingleton<GameSettings>
{
    public float masterVolume = 1f;
    public bool enableVSync = true;
}

[ScriptableSingletonPath("Settings/Audio")]
public sealed class MusicSettings : ScriptableObjectSingleton<MusicSettings>
{
    public float musicVolume = 0.8f;
}
```

Write those two files, let the editor reload, and you have:

```text
Assets/
  Resources/
    GameSettings.asset                   # no [ScriptableSingletonPath]
    Settings/
      Audio/
        MusicSettings.asset              # [ScriptableSingletonPath("Settings/Audio")]
```

Then `GameSettings.Instance.masterVolume` works in play mode and in a build, with nothing to
remember.

Move `GameSettings.asset` somewhere else by hand and the next domain reload moves it back — the
attribute is the source of truth for where it lives.

See [Singleton Utilities](../utilities/singletons.md) for the runtime base class, its lookup order
and Odin compatibility.

---

### Sprite Label Processor

Runs automatically on sprite import. No menu item.

Asset labels are a good way to say "this sprite belongs in the UI atlas", but there is no cheap way
to ask Unity for every label in the project — you would have to load every asset. This
`AssetPostprocessor` maintains the list as sprites are imported, so a dropdown can be populated
instantly.

That is what makes this one line work:

```csharp
using System.Collections.Generic;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Core.Helper;

public sealed class AtlasRule : MonoBehaviour
{
    [StringInList(typeof(Helpers), nameof(Helpers.GetAllSpriteLabelNames))]
    public List<string> labels = new();
}
```

`Helpers.GetAllSpriteLabelNames()` returns the cached array; the
`GetAllSpriteLabelNames(List<string> destination)` overload fills a list you own instead of
allocating. [Sprite Atlas Generator](#sprite-atlas-generator) uses exactly this for its label
pickers.

**Editor-only.** The cache is built by an `AssetPostprocessor`, so both methods return empty in a
player build, in batch mode, and in CI. Do not build runtime behaviour on them.

Only `.png`, `.jpg` and `.jpeg` assets under `Assets` whose importer type is `Sprite` contribute
labels, and the result is sorted so a dropdown is stable between sessions.

---

<a id="request-script-recompilation"></a>

### Request Script Compilation

`Tools > Wallstop Studios > Unity Helpers > Request Script Compilation` — `Ctrl/Cmd + Alt + R`

You generated a `.cs` file from outside Unity — a codegen step, a script pulled by a package tool —
and the editor has not noticed. Rather than touching a file to force a rebuild, press the shortcut.

It runs a forced synchronous `AssetDatabase.Refresh` (so the new file is imported first) and then
`CompilationPipeline.RequestScriptCompilation()`. If Unity is already compiling it logs
`Script compilation already in progress; manual request skipped.` and does nothing, so leaning on the
shortcut is harmless.

Rebind it under `Edit > Shortcuts` in **Wallstop Studios ▸ Request Script Compilation**.

> **Visual Reference**
>
> ![Request Script Recompilation menu item](../../images/editor-tools/request-recompilation.png)
>
> ![Request Script Recompilation shortcut](../../images/editor-tools/request-recompilation-shortcut.png)

---

<a id="project-settings-unity-helpers"></a>

### Project Settings: Unity Helpers

`Edit > Project Settings > Wallstop Studios > Unity Helpers`

One panel for the package's project-wide defaults: inspector pagination, inline editor behaviour,
button and toggle colours, and the coroutine wait buffers.

**Coroutine wait buffers.** `yield return new WaitForSeconds(0.25f)` allocates every time it runs.
The package pools those instructions, and this panel controls the pool: a quantization step (how
finely wait times are bucketed), a cap on distinct cached entries, and whether the cap evicts
least-recently-used. Settings are written to
`Assets/Resources/Wallstop Studios/Unity Helpers/UnityHelpersBufferSettings.asset` and applied on
domain reload and player start, so they ship with the build.

**Inspector pagination.** `StringInListPageSize` (default `25`) sets how many options a
[StringInList](#stringinlist-property-drawer) dropdown shows per page; `EnumToggleButtonsPageSize`
(default `15`) does the same for `[WEnumToggleButtons]` grids. Raise them if lists feel cramped,
lower them if a big enum makes the inspector sluggish. Individual fields can override both.

Full reference: [Inspector Settings](../inspector/inspector-settings.md).

> **Visual Reference**
>
> ![Project Settings panel for Unity Helpers](../../images/editor-tools/project-settings-unity-helpers.png)
>
> _Centralized configuration panel in Unity's Project Settings_

---

### Attribute Metadata Cache Generator

Runs automatically on editor load and after every recompile. No menu item.

The [Effects system](../effects/effects-system.md) and the
[relational component attributes](../relational-components/relational-components.md) resolve fields
by reflection. Doing that on the first frame is a visible stall, and on IL2CPP some of it cannot be
done at all. This walks your types at edit time and bakes the answers into an
`AttributeMetadataCache` asset that ships with the build.

You do not call the generator. It runs on load, and the `AttributeMetadataCache` asset's inspector
has a **Purge & Refresh Cache** button for when you want to force it.

Read the baked data if you are building tooling of your own:

```csharp
using System;
using UnityEngine;
using WallstopStudios.UnityHelpers.Tags;

public static class AttributeInspectorSupport
{
    public static void Dump(Type componentType)
    {
        AttributeMetadataCache cache = AttributeMetadataCache.Instance;

        // Every attribute name declared anywhere in the project.
        string[] all = cache.AllAttributeNames;

        if (cache.TryGetFieldNames(componentType, out string[] fieldNames))
        {
            Debug.Log($"{componentType.Name} declares {fieldNames.Length} of {all.Length} attributes.");
        }

        if (
            cache.TryGetRelationalFields(
                componentType,
                out AttributeMetadataCache.RelationalFieldMetadata[] relational
            )
        )
        {
            foreach (AttributeMetadataCache.RelationalFieldMetadata field in relational)
            {
                Debug.Log($"{field.fieldName} is a {field.attributeKind} field.");
            }
        }
    }
}
```

What gets baked: the attribute fields on every `AttributesComponent` type, the
`[ParentComponent]` / `[ChildComponent]` / `[SiblingComponent]` metadata, assembly-qualified type
names so they resolve at runtime, whether each field is a single value, array, `List` or `HashSet`,
and whether its element type is an interface.

---

### Editor Utilities

`WallstopStudios.UnityHelpers.Editor.Utils.EditorUtilities`

A wizard that creates an asset should create it where the user is looking, not in `Assets/`. Unity
does not expose the Project window's selected folder, so this does.

```csharp
using UnityEditor;
using UnityEngine;
using WallstopStudios.UnityHelpers.Editor.Utils;

public static class CreateAbilityAsset
{
    [MenuItem("Assets/Create/Game/Ability Config")]
    private static void Create()
    {
        AbilityConfig asset = ScriptableObject.CreateInstance<AbilityConfig>();

        string folder = EditorUtilities.GetCurrentPathOfProjectWindow();
        string directory = string.IsNullOrEmpty(folder) ? "Assets" : folder;

        AssetDatabase.CreateAsset(
            asset,
            AssetDatabase.GenerateUniqueAssetPath($"{directory}/NewAbilityConfig.asset")
        );
        AssetDatabase.SaveAssets();
        Selection.activeObject = asset;
    }
}
```

`GetCurrentPathOfProjectWindow()` returns an asset-relative path such as `Assets/Data/Abilities`, or
an empty string when it cannot determine one — always have a fallback, as above. It reaches an
internal Unity API by reflection, so treat the empty-string case as normal rather than exceptional.

---

<a id="failed-tests-exporter"></a>

### Failed Tests Exporter

`Tools > Wallstop Studios > Unity Helpers > Export Failed Tests` / `Clear Failed Tests`

A flaky test that fails once in twenty runs is invisible if the only record is the Test Runner window
you already closed. Enable this in
`Edit > Project Settings > Wallstop Studios > Unity Helpers` and every failure — name, message, stack
trace — is captured and written to `failed-tests-YYYY-MM-DD-HHmmss.txt` in a folder you choose
(project root by default).

Disabled by default. Both menu items are greyed out until there are failures to act on.

Full setup and API: [Failed Tests Exporter](./failed-tests-exporter.md).

---

### MultiFile Selector

`MultiFileSelectorElement` is the UI Toolkit control the tools above use for "pick several files or
folders". It is public, so your own editor windows can use it too.

In the editor it remembers selections through `EditorPrefs` and offers Reveal in Finder / Show in
Explorer. In a player build it falls back to `PlayerPrefs` and enumerates files under the
application's data root, dropping the editor-only affordances.

Persistence is opt-in per element, via a `persistenceKey`. Stored scopes are managed from
`Tools > Wallstop Studios > Unity Helpers > Multi-File Selector Persistence`, which can
**Run Cleanup Now** or drop scopes unused for more than **Max age (days)** (default `30`) on editor
startup.

There is a runnable example in the `UI Toolkit - MultiFile Selector (Editor)` sample, importable from
the Package Manager.

---

## Quick Reference

### Menu items

Everything under `Tools > Wallstop Studios > Unity Helpers`:

| Menu item                               | Section                                                                     |
| --------------------------------------- | --------------------------------------------------------------------------- |
| Animation Copier                        | [Animation Copier](#animation-copier)                                       |
| Animation Creator                       | [Animation Creator](#animation-creator)                                     |
| AnimationEvent Editor                   | [Animation Event Editor](#animation-event-editor)                           |
| Clear Failed Tests                      | [Failed Tests Exporter](#failed-tests-exporter)                             |
| Export Failed Tests                     | [Failed Tests Exporter](#failed-tests-exporter)                             |
| Fit Texture Size                        | [Fit Texture Size](#fit-texture-size)                                       |
| Image Blur                              | [Image Blur Tool](#image-blur-tool)                                         |
| Multi-File Selector Persistence         | [MultiFile Selector](#multifile-selector)                                   |
| Prefab Checker                          | [Prefab Checker](#prefab-checker)                                           |
| Proto Schema Exporter                   | [Serialization](../serialization/serialization.md)                          |
| Request Script Compilation              | [Request Script Compilation](#request-script-recompilation)                 |
| Sprite Animation Editor                 | [Sprite Animation Editor](#sprite-animation-editor-animation-viewer-window) |
| Sprite Atlas Generator                  | [Sprite Atlas Generator](#sprite-atlas-generator)                           |
| Sprite Cropper                          | [Sprite Cropper](#sprite-cropper)                                           |
| Sprite Pivot Adjuster                   | [Sprite Pivot Adjuster](#sprite-pivot-adjuster)                             |
| Sprite Settings Applier                 | [Sprite Settings Applier](#sprite-settings-applier)                         |
| Sprite Sheet Animation Creator          | [Sprite Sheet Animation Creator](#sprite-sheet-animation-creator)           |
| Sprite Sheet Extractor                  | [Sprite Sheet Extractor](#sprite-sheet-extractor)                           |
| Texture Resizer                         | [Texture Resizer](#texture-resizer)                                         |
| Texture Settings Applier                | [Texture Settings Applier](#texture-settings-applier)                       |
| Unity Method Analyzer                   | [Unity Method Analyzer](#unity-method-analyzer)                             |
| Validate Serialized Fields In Selection | [Serialized Field Validator](#serialized-field-validator)                   |

Elsewhere:

- `Assets > Create > Wallstop Studios > Unity Helpers > Scriptable Sprite Atlas Config` —
  [Sprite Atlas Generator](#sprite-atlas-generator)
- `Edit > Project Settings > Wallstop Studios > Unity Helpers` —
  [Project Settings](#project-settings-unity-helpers)

Runs with no menu item: [ScriptableObject Singleton Creator](#scriptableobject-singleton-creator),
[Sprite Label Processor](#sprite-label-processor),
[Attribute Metadata Cache Generator](#attribute-metadata-cache-generator),
[Asset Change Detection](./asset-change-detection.md).

Shared across the windows: folder fields accept dragged Project-window folders, the folders you pick
are remembered per tool and offered again next time, and long operations report progress in the
console rather than blocking silently.

---

### What writes to disk

Most of these tools only change import settings, which Unity records as an undo step and which you
can revert by reverting the `.meta` file. These are the ones that write real files:

| Tool                                              | Effect                                                                         |
| ------------------------------------------------- | ------------------------------------------------------------------------------ |
| [Image Blur Tool](#image-blur-tool)               | Writes new files; **leaves sources on Read/Write + Uncompressed**              |
| [Sprite Cropper](#sprite-cropper)                 | Writes PNGs; overwrites sources when **Overwrite Originals** is on             |
| [Texture Resizer](#texture-resizer)               | Overwrites source PNGs unless **Output Folder** is set                         |
| [Sprite Sheet Extractor](#sprite-sheet-extractor) | Writes new PNGs; the Danger Zone rewrites referencing assets                   |
| [Sprite Cropper](#sprite-cropper) Danger Zone     | Rewrites `.prefab`, `.unity`, `.asset`, `.mat`, `.anim`, `.overrideController` |
| [Animation Copier](#animation-copier)             | Copies and (in Cleanup) deletes `.anim` assets                                 |
| [Prefab Checker](#prefab-checker)                 | Read-only unless you enable auto-fix and click **Fix Missing Scripts**         |

Everything else — [Texture Settings Applier](#texture-settings-applier),
[Sprite Settings Applier](#sprite-settings-applier),
[Sprite Pivot Adjuster](#sprite-pivot-adjuster), [Fit Texture Size](#fit-texture-size) — touches
import settings only and records a named undo step.

None of the tools that rewrite image data have an undo. Commit before running them.

---

### Common workflows

**A folder of new character art:**

1. [Sprite Sheet Extractor](#sprite-sheet-extractor) if it arrived as sheets.
2. [Sprite Cropper](#sprite-cropper) to trim the padding.
3. [Sprite Settings Applier](#sprite-settings-applier) for PPU and filter mode.
4. [Sprite Pivot Adjuster](#sprite-pivot-adjuster) so the pivots agree.
5. [Animation Creator](#animation-creator) to build the clips.
6. [Animation Event Editor](#animation-event-editor) for footsteps and hitboxes.
7. [Sprite Atlas Generator](#sprite-atlas-generator) to pack them.

**Before you open a pull request:**

1. [Prefab Checker](#prefab-checker) over the prefab folders you touched.
2. [Unity Method Analyzer](#unity-method-analyzer) over `Assets/Scripts`.
3. [Serialized Field Validator](#serialized-field-validator) on any new `MonoBehaviour` or
   `ScriptableObject`.

**Trimming build size:**

1. [Fit Texture Size](#fit-texture-size) in `GrowAndShrink` mode across `Assets/Sprites`.
2. [Sprite Cropper](#sprite-cropper) on anything still padded.
3. [Texture Settings Applier](#texture-settings-applier) with crunch compression and per-platform max
   sizes.

---

### When something does not work

| Symptom                                    | Likely cause                                                                                                                  |
| ------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------- |
| A sprite tool skips files silently         | It is a multi-sprite texture, or `Read/Write Enabled` is off. Cropper and Pivot Adjuster need Single sprites.                 |
| **Scan Folders** finds nothing             | The regex does not match, or the folder path is outside `Assets/`. Invalid regexes are logged and match nothing.              |
| An atlas looks worse than the source       | Run **Force Uncompressed for N Source Sprites** before packing.                                                               |
| Clips landed in the wrong folder           | [Animation Creator](#animation-creator) writes beside the first sprite. Move them with [Animation Copier](#animation-copier). |
| An `[AnimationEvent]` method is not listed | It must return `void` and take zero or one `int` / `float` / `string` / `UnityEngine.Object` / enum parameter.                |
| Prefab Checker reports nothing             | The relevant checks may be off — **Empty String Fields** and **Disabled Components** are off by default.                      |
| **Fix Missing Scripts** is greyed out      | Tick **Enable Auto-fix options** first.                                                                                       |
| A tool did nothing in a CI or batch run    | Dialogs and folder pickers are suppressed in batch mode, so anything that needs one is skipped.                               |

---

### Related documentation

- [Inspector Attributes](../inspector/inspector-overview.md) — the full attribute set
- [Asset Change Detection](./asset-change-detection.md) — run code when assets change
- [Unity Method Analyzer](./unity-method-analyzer.md) — the analyzer in detail
- [Failed Tests Exporter](./failed-tests-exporter.md) — capturing test failures
- [Singleton Utilities](../utilities/singletons.md) — `ScriptableObjectSingleton<T>`
- [Effects System](../effects/effects-system.md) — what the attribute metadata cache serves

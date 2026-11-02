#!/usr/bin/env python3
"""Generate PNG test fixtures through the locked uv fixtures group."""

from __future__ import annotations

import argparse
import colorsys
import uuid
from pathlib import Path

from cli_encoding import configure_output
from fixture_assets import Color, write_metadata
from PIL import Image


def generate_guid() -> str:
    """Generate a 32-character lowercase hex GUID for Unity meta files."""
    return uuid.uuid4().hex


def generate_sprite_id() -> str:
    """Generate a 32-character sprite ID for Unity sprite metadata."""
    return uuid.uuid4().hex


def generate_internal_id() -> int:
    """Generate a random internal ID for Unity sprite metadata."""
    return uuid.uuid4().int % (1 << 64) - (1 << 63)


def hsv_to_rgb_255(hue: float, saturation: float, value: float) -> Color:
    """Convert HSV (0-1 range) to RGB (0-255 range)."""
    r, g, b = colorsys.hsv_to_rgb(hue, saturation, value)
    return (int(r * 255), int(g * 255), int(b * 255), 255)


def create_grid_sprite_sheet(
    width: int, height: int, grid_columns: int, grid_rows: int, output_path: str
) -> None:
    """
    Create a sprite sheet with a grid of cells, each filled with a distinct HSV color.

    This matches the logic in SharedSpriteTestFixtures.CreateSpriteSheet:
    - Each cell gets a unique hue based on spriteIndex / totalCells
    - saturation = 0.8
    - value = 0.9
    """
    cell_width = width // grid_columns
    cell_height = height // grid_rows
    total_cells = grid_rows * grid_columns

    image = Image.new("RGBA", (width, height), (0, 0, 0, 255))

    for row in range(grid_rows):
        for col in range(grid_columns):
            sprite_index = row * grid_columns + col
            hue = sprite_index / total_cells
            color = hsv_to_rgb_255(hue, 0.8, 0.9)

            start_x = col * cell_width
            start_y = row * cell_height

            image.paste(color, (start_x, start_y, start_x + cell_width, start_y + cell_height))

    image.save(output_path, "PNG")
    print(f"Created: {output_path}")


def create_single_color_sprite(width: int, height: int, color: Color, output_path: str) -> None:
    """Create a single-color sprite."""
    image = Image.new("RGBA", (width, height), color)
    image.save(output_path, "PNG")
    print(f"Created: {output_path}")


def generate_sprite_meta_entry(name: str, x: int, y: int, width: int, height: int) -> str:
    """Generate a single sprite entry for the spriteSheet."""
    sprite_id = generate_sprite_id()
    internal_id = generate_internal_id()
    return f"""    - serializedVersion: 2
      name: {name}
      rect:
        serializedVersion: 2
        x: {x}
        y: {y}
        width: {width}
        height: {height}
      alignment: 0
      pivot: {{x: 0.5, y: 0.5}}
      border: {{x: 0, y: 0, z: 0, w: 0}}
      outline: []
      physicsShape: []
      tessellationDetail: -1
      bones: []
      spriteID: {sprite_id}
      internalID: {internal_id}
      vertices: []
      indices:
      edges: []
      weights: []"""


def generate_meta_file(
    output_path: str,
    texture_name: str,
    width: int,
    height: int,
    grid_columns: int,
    grid_rows: int,
    is_single_mode: bool = False,
) -> None:
    """Generate a Unity meta file for a sprite sheet texture."""
    guid = generate_guid()
    cell_width = width // grid_columns if grid_columns > 0 else width
    cell_height = height // grid_rows if grid_rows > 0 else height

    sprite_mode = 1 if is_single_mode else 2

    sprites_yaml = ""
    name_file_id_table = ""

    if not is_single_mode and grid_columns > 0 and grid_rows > 0:
        sprite_entries: list[str] = []
        name_entries: list[str] = []

        for row in range(grid_rows):
            for col in range(grid_columns):
                sprite_index = row * grid_columns + col
                sprite_name = f"{texture_name}_sprite_{sprite_index}"

                # Unity coordinates: Y=0 is bottom, so flip row order
                # Row 0 in our grid is at Y = height - cell_height (top of texture)
                # Row (grid_rows-1) is at Y = 0 (bottom of texture)
                y_coord = (grid_rows - 1 - row) * cell_height
                x_coord = col * cell_width

                entry = generate_sprite_meta_entry(
                    sprite_name, x_coord, y_coord, cell_width, cell_height
                )
                sprite_entries.append(entry)

                # Get the internal_id from the last generated entry
                lines = entry.split("\n")
                internal_id_line = next(line for line in lines if "internalID:" in line)
                internal_id = internal_id_line.split(":")[1].strip()
                name_entries.append(f"      {sprite_name}: {internal_id}")

        sprites_yaml = "\n".join(sprite_entries)
        name_file_id_table = "\n".join(name_entries)

    sprites_field = "    sprites:\n" + sprites_yaml if sprites_yaml else "    sprites: []"
    names_field = (
        "    nameFileIdTable:\n" + name_file_id_table
        if name_file_id_table
        else "    nameFileIdTable: {}"
    )
    meta_content = f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 1
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: {sprite_mode}
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: Standalone
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 3
    buildTarget: WebGL
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
{sprites_field}
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
{names_field}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

    write_metadata(Path(output_path), meta_content)


def main() -> None:
    configure_output()
    parser = argparse.ArgumentParser(description="Generate sprite test fixtures.")
    parser.add_argument("--output-dir", type=Path)
    args = parser.parse_args()
    output_dir = (
        Path(args.output_dir)
        if args.output_dir is not None
        else Path(__file__).resolve().parents[1] / "Tests/Editor/TestAssets/Sprites"
    )

    output_dir.mkdir(parents=True, exist_ok=True)
    print(f"Output directory: {output_dir}")

    # Define all fixtures: (name, width, height, grid_columns, grid_rows, is_single)
    fixtures = [
        ("test_2x2_grid", 64, 64, 2, 2, False),
        ("test_4x4_grid", 128, 128, 4, 4, False),
        ("test_8x8_grid", 256, 256, 8, 8, False),
        ("test_single", 32, 32, 1, 1, True),
        ("test_wide", 128, 64, 4, 2, False),
        ("test_tall", 64, 128, 2, 4, False),
        ("test_odd", 63, 63, 3, 3, False),
        ("test_large_512", 512, 512, 16, 16, False),
        ("test_npot_100x200", 100, 200, 2, 4, False),
        ("test_npot_150x75", 150, 75, 3, 1, False),
        ("test_prime_127", 127, 127, 1, 1, True),
        ("test_small_16x16", 16, 16, 4, 4, False),
        ("test_boundary_256", 256, 256, 4, 4, False),
    ]

    for name, width, height, grid_cols, grid_rows, is_single in fixtures:
        png_path = str(output_dir / f"{name}.png")
        meta_path = str(output_dir / f"{name}.png.meta")

        if is_single:
            # For single mode sprites, use a solid color
            color = (255, 0, 0, 255) if name == "test_single" else (0, 255, 0, 255)
            create_single_color_sprite(width, height, color, png_path)
        else:
            create_grid_sprite_sheet(width, height, grid_cols, grid_rows, png_path)

        generate_meta_file(meta_path, name, width, height, grid_cols, grid_rows, is_single)

    print("\nAll PNG files created successfully!")
    print(f"\nFiles in {output_dir}:")
    for f in sorted(output_dir.glob("*.png")):
        print(f"  - {f.name}")


if __name__ == "__main__":
    main()

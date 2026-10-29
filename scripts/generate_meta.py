"""Generate Unity metadata with the package's importer defaults and CRLF bytes."""

from __future__ import annotations

import argparse
import sys
import time
import uuid
from pathlib import Path
from typing import cast

FOLDER_TEMPLATE: str = """fileFormatVersion: 2
guid: $guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
IMPORTER_TEMPLATES: dict[str, str] = {
    "cs": """fileFormatVersion: 2
guid: $guid
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "jslib": """fileFormatVersion: 2
guid: $guid
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      WebGL: WebGL
    second:
      enabled: 1
      settings: {}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "asmdef": """fileFormatVersion: 2
guid: $guid
AssemblyDefinitionImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "asmref": """fileFormatVersion: 2
guid: $guid
AssemblyDefinitionReferenceImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "shader": """fileFormatVersion: 2
guid: $guid
ShaderImporter:
  externalObjects: {}
  defaultTextures: []
  nonModifiableTextures: []
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "shadergraph|shadersubgraph|hlsl|cginc": """fileFormatVersion: 2
guid: $guid
ScriptedImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 2
  userData:
  assetBundleName:
  assetBundleVariant:
  script: {fileID: 11500000, guid: 625f186215c104763be7675aa2d941aa, type: 3}
""",
    "compute": """fileFormatVersion: 2
guid: $guid
ComputeShaderImporter:
  externalObjects: {}
  currentAPIMask: 0
  currentBuildTarget: 0
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "uss": """fileFormatVersion: 2
guid: $guid
timeCreated: $timestamp
""",
    "uxml": """fileFormatVersion: 2
guid: $guid
timeCreated: $timestamp
""",
    "mat": """fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 2100000
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "asset": """fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "prefab": """fileFormatVersion: 2
guid: $guid
PrefabImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "unity": """fileFormatVersion: 2
guid: $guid
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "package.json": """fileFormatVersion: 2
guid: $guid
PackageManifestImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "json": """fileFormatVersion: 2
guid: $guid
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "md|txt|xml|yaml|yml|html|htm|css|js|ts|log|cfg|ini|conf|gitignore|gitattributes": """fileFormatVersion: 2
guid: $guid
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "rsp": """fileFormatVersion: 2
guid: $guid
timeCreated: $timestamp
""",
    "png|jpg|jpeg|tga|psd|gif|bmp|tif|tiff|iff|pict|exr|hdr": """fileFormatVersion: 2
guid: $guid
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {}
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
  isReadable: 0
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
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 100
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
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
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
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
    nameFileIdTable: {}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "anim": """fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 7400000
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "controller": """fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 9100000
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "wav|mp3|ogg|aiff|aif|flac": """fileFormatVersion: 2
guid: $guid
AudioImporter:
  externalObjects: {}
  serializedVersion: 7
  defaultSettings:
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {}
  forceToMono: 0
  normalize: 1
  ambisonic: 0
  loadInBackground: 0
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "fbx|obj|dae|3ds|blend|dxf|max|mb|ma": """fileFormatVersion: 2
guid: $guid
ModelImporter:
  serializedVersion: 22200
  internalIDToNameTable: []
  externalObjects: {}
  materials:
    materialImportMode: 2
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    resampleCurves: 1
    optimizeGameObjects: 0
    removeConstantScaleCurves: 0
    motionNodeName:
    rigImportErrors:
    rigImportWarnings:
    animationImportErrors:
    animationImportWarnings:
    animationRetargetingWarnings:
    animationDoRetargetingWarnings: 0
    importAnimatedCustomProperties: 0
    importConstraints: 0
    animationCompression: 1
    animationRotationError: 0.5
    animationPositionError: 0.5
    animationScaleError: 0.5
    animationWrapMode: 0
    extraExposedTransformPaths: []
    extraUserProperties: []
    clipAnimations: []
    isReadable: 0
  meshes:
    lODScreenPercentages: []
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    useSRGBMaterialColor: 1
    sortHierarchyByName: 1
    importPhysicalCameras: 1
    importVisibility: 1
    importBlendShapes: 1
    importCameras: 1
    importLights: 1
    nodeNameCollisionStrategy: 1
    fileIdsGeneration: 2
    swapUVChannels: 0
    generateSecondaryUV: 0
    useFileUnits: 1
    keepQuads: 0
    weldVertices: 1
    bakeAxisConversion: 0
    preserveHierarchy: 0
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    optimizeBones: 1
    meshOptimizationFlags: -1
    indexFormat: 0
    secondaryUVAngleDistortion: 8
    secondaryUVAreaDistortion: 15.000001
    secondaryUVHardAngle: 88
    secondaryUVMarginMethod: 1
    secondaryUVMinLightmapResolution: 40
    secondaryUVMinObjectScale: 1
    secondaryUVPackMargin: 4
    useFileScale: 1
    strictVertexDataChecks: 0
  tangentSpace:
    normalSmoothAngle: 60
    normalImportMode: 0
    tangentImportMode: 3
    normalCalculationMode: 4
    legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes: 0
    blendShapeNormalImportMode: 1
  referencedClips: []
  importAnimation: 1
  humanDescription:
    serializedVersion: 3
    human: []
    skeleton: []
    armTwist: 0.5
    foreArmTwist: 0.5
    upperLegTwist: 0.5
    legTwist: 0.5
    armStretch: 0.05
    legStretch: 0.05
    feetSpacing: 0
    globalScale: 1
    rootMotionBoneName:
    hasTranslationDoF: 0
    hasExtraRoot: 0
    skeletonHasParents: 1
  lastHumanDescriptionAvatarSource: {instanceID: 0}
  autoGenerateAvatarMappingIfUnspecified: 1
  animationType: 2
  humanoidOversampling: 1
  avatarSetup: 0
  addHumanoidExtraRootOnlyWhenUsingAvatar: 1
  importBlendShapeDeformPercent: 1
  remapMaterialsIfMaterialImportModeIsNone: 0
  additionalBone: 0
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "ttf|otf|fnt": """fileFormatVersion: 2
guid: $guid
TrueTypeFontImporter:
  externalObjects: {}
  serializedVersion: 4
  fontSize: 16
  forceTextureCase: -2
  characterSpacing: 0
  characterPadding: 1
  includeFontData: 1
  fontName:
  fallbackFontReferences: []
  customCharacters:
  fontRenderingMode: 0
  ascentCalculationMode: 1
  useLegacyBoundsCalculation: 0
  shouldRoundAdvanceValue: 1
  userData:
  assetBundleName:
  assetBundleVariant:
""",
    "*": """fileFormatVersion: 2
guid: $guid
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
}


def render_metadata(asset: Path, guid: str, timestamp: int) -> bytes:
    if asset.is_dir():
        template = FOLDER_TEMPLATE
    elif asset.name == "package.json":
        template = IMPORTER_TEMPLATES["package.json"]
    else:
        extension = asset.name.rsplit(".", 1)[-1].lower() if "." in asset.name else ""
        template = IMPORTER_TEMPLATES["*"]
        for extensions, candidate in IMPORTER_TEMPLATES.items():
            if extension in extensions.split("|"):
                template = candidate
                break
    return (
        template.replace("$guid", guid)
        .replace("$timestamp", str(timestamp))
        .replace("\n", "\r\n")
        .encode("utf-8")
    )


def generate_metadata(asset: Path, *, force: bool = False) -> tuple[Path, str]:
    absolute = asset.absolute()
    if not absolute.exists():
        raise ValueError(f"Error: '{absolute}' does not exist")
    if absolute.parent == absolute:
        raise ValueError("Refusing to create metadata for a filesystem root.")
    destination = Path(f"{absolute}.meta")
    if destination.exists() and not force:
        raise ValueError(f"Error: '{destination}' already exists. Use --force to overwrite.")
    guid = uuid.uuid4().hex
    content = render_metadata(absolute, guid, int(time.time()))
    with destination.open("wb" if force else "xb") as stream:
        stream.write(content)
    return destination, guid


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=Path, help="Existing file or directory")
    parser.add_argument(
        "--force", action="store_true", help="Replace existing metadata with a new GUID"
    )
    if len(sys.argv) == 1:
        parser.print_usage(sys.stderr)
        return 1
    args = parser.parse_args()
    try:
        destination, guid = generate_metadata(
            cast("Path", args.path), force=cast("bool", args.force)
        )
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1
    print(f"Created: {destination}")
    print(f"GUID: {guid}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

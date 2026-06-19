#!/usr/bin/env bash
# generate-meta.sh - Generate Unity .meta files for new assets
#
# Usage:
#   ./scripts/generate-meta.sh <file-or-directory-path>
#   ./scripts/generate-meta.sh path/to/file.cs
#   ./scripts/generate-meta.sh path/to/folder
#
# This script generates a Unity-compatible .meta file with a unique GUID.
# It determines the appropriate importer type based on the file extension.
#
# Supported file types:
#   .cs           -> MonoImporter
#   .asmdef       -> AssemblyDefinitionImporter
#   .asmref       -> AssemblyDefinitionReferenceImporter
#   .shader       -> ShaderImporter
#   .shadergraph  -> ScriptedImporter
#   .shadersubgraph -> ScriptedImporter
#   .compute      -> ComputeShaderImporter
#   .uss          -> StyleSheetImporter (simple format)
#   .uxml         -> UIDocumentImporter (simple format)
#   .mat          -> NativeFormatImporter
#   .asset        -> NativeFormatImporter
#   .prefab       -> DefaultImporter
#   .unity        -> DefaultImporter
#   .json         -> TextScriptImporter
#   .md           -> TextScriptImporter
#   .txt          -> TextScriptImporter
#   .xml          -> TextScriptImporter
#   .yaml/.yml    -> TextScriptImporter
#   .html/.htm    -> TextScriptImporter
#   .css          -> TextScriptImporter
#   .js           -> TextScriptImporter
#   .rsp          -> DefaultImporter (simple format)
#   directories   -> DefaultImporter with folderAsset: yes
#   other         -> DefaultImporter

set -euo pipefail

# Generate a random 32-character lowercase hex GUID (Unity format)
generate_guid() {
    # Use /dev/urandom for secure randomness with od (more portable than xxd)
    if [[ -r /dev/urandom ]]; then
        od -A n -t x1 -N 16 /dev/urandom | tr -d ' \n' | cut -c1-32
    else
        # Fallback: use date and random numbers with md5sum
        local base
        base=$(date +%s%N)$(( RANDOM * RANDOM ))
        echo -n "$base" | md5sum | cut -c1-32
    fi
}

# Get the importer type and content based on file extension
get_meta_content() {
    local filepath="$1"
    local guid="$2"
    local ext=""
    local basename
    
    basename=$(basename "$filepath")
    
    # Check if it's a directory
    if [[ -d "$filepath" ]]; then
        cat <<EOF
fileFormatVersion: 2
guid: $guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
        return
    fi
    
    # Get file extension (lowercase)
    if [[ "$basename" == *.* ]]; then
        ext="${basename##*.}"
        ext=$(echo "$ext" | tr '[:upper:]' '[:lower:]')
    fi
    
    case "$ext" in
        cs)
            cat <<EOF
fileFormatVersion: 2
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
EOF
            ;;
        asmdef)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
AssemblyDefinitionImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        asmref)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
AssemblyDefinitionReferenceImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        shader)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
ShaderImporter:
  externalObjects: {}
  defaultTextures: []
  nonModifiableTextures: []
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        shadergraph|shadersubgraph|hlsl|cginc)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
ScriptedImporter:
  internalIDToNameTable: []
  externalObjects: {}
  serializedVersion: 2
  userData:
  assetBundleName:
  assetBundleVariant:
  script: {fileID: 11500000, guid: 625f186215c104763be7675aa2d941aa, type: 3}
EOF
            ;;
        compute)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
ComputeShaderImporter:
  externalObjects: {}
  currentAPIMask: 0
  currentBuildTarget: 0
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        uss)
            # USS files use simple format with timeCreated
            local timestamp
            timestamp=$(date +%s)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
timeCreated: $timestamp
EOF
            ;;
        uxml)
            # UXML files use simple format with timeCreated
            local timestamp
            timestamp=$(date +%s)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
timeCreated: $timestamp
EOF
            ;;
        mat)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 2100000
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        asset)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        prefab)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
PrefabImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        unity)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        json)
            # Special case for package.json
            if [[ "$basename" == "package.json" ]]; then
                cat <<EOF
fileFormatVersion: 2
guid: $guid
PackageManifestImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            else
                cat <<EOF
fileFormatVersion: 2
guid: $guid
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            fi
            ;;
        md|txt|xml|yaml|yml|html|htm|css|js|ts|log|cfg|ini|conf|gitignore|gitattributes)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        rsp)
            # RSP files use simple format with timeCreated
            local timestamp
            timestamp=$(date +%s)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
timeCreated: $timestamp
EOF
            ;;
        png|jpg|jpeg|tga|psd|gif|bmp|tif|tiff|iff|pict|exr|hdr)
            # Textures have complex importers - use a minimal default that Unity will expand
            cat <<EOF
fileFormatVersion: 2
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
EOF
            ;;
        anim)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 7400000
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        controller)
            cat <<EOF
fileFormatVersion: 2
guid: $guid
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 9100000
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
        wav|mp3|ogg|aiff|aif|flac)
            cat <<EOF
fileFormatVersion: 2
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
EOF
            ;;
        fbx|obj|dae|3ds|blend|dxf|max|mb|ma)
            cat <<EOF
fileFormatVersion: 2
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
EOF
            ;;
        ttf|otf|fnt)
            cat <<EOF
fileFormatVersion: 2
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
EOF
            ;;
        *)
            # Default fallback
            cat <<EOF
fileFormatVersion: 2
guid: $guid
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
EOF
            ;;
    esac
}

# Main script
main() {
    if [[ $# -lt 1 ]]; then
        echo "Usage: $0 <file-or-directory-path> [--force]" >&2
        echo "" >&2
        echo "Generates a Unity .meta file for the specified file or directory." >&2
        echo "" >&2
        echo "Options:" >&2
        echo "  --force    Overwrite existing .meta file if it exists" >&2
        exit 1
    fi

    local filepath="$1"
    local force=false
    
    if [[ "${2:-}" == "--force" ]]; then
        force=true
    fi
    
    # Resolve to absolute path if needed
    if [[ ! "$filepath" = /* ]]; then
        filepath="$(pwd)/$filepath"
    fi
    
    # Remove trailing slash from directories
    filepath="${filepath%/}"
    
    # Check if the file/directory exists
    if [[ ! -e "$filepath" ]]; then
        echo "Error: '$filepath' does not exist" >&2
        exit 1
    fi
    
    local metapath="${filepath}.meta"
    
    # Check if meta file already exists
    if [[ -e "$metapath" ]] && [[ "$force" != true ]]; then
        echo "Error: '$metapath' already exists. Use --force to overwrite." >&2
        exit 1
    fi
    
    # Generate GUID and meta content
    local guid
    guid=$(generate_guid)
    
    # Write meta file
    get_meta_content "$filepath" "$guid" > "$metapath"
    
    echo "Created: $metapath"
    echo "GUID: $guid"
}

main "$@"

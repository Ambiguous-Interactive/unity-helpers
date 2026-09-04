// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    public sealed class ImageBlurTool : EditorWindow
    {
        internal const string TemporaryTextureName = "ImageBlurTool Temporary Blur";

        public List<Object> imageSources = new();

        private readonly List<Texture2D> _orderedTextures = new();
        private readonly List<Texture2D> _manualTextures = new();

        private int _blurRadius = 1;
        private Vector2 _scrollPosition;

        private GUIStyle _impactButtonStyle;
        private SerializedObject _serializedObject;
        private SerializedProperty _imageSourcesProperty;

        private readonly List<Object> _lastSeenImageSources = new();

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Image Blur")]
        public static void ShowWindow()
        {
            GetWindow<ImageBlurTool>("Image Blur Tool");
        }

        internal SerializedObject SerializedStateForTesting => _serializedObject;

        private void BindSerializedState()
        {
            ReleaseSerializedState();
            _serializedObject = new SerializedObject(this);
            _imageSourcesProperty = _serializedObject.FindProperty(nameof(imageSources));
        }

        private void ReleaseSerializedState()
        {
            _imageSourcesProperty = null;
            _serializedObject?.Dispose();
            _serializedObject = null;
        }

        private void OnDisable()
        {
            ReleaseSerializedState();
        }

        private void OnEnable()
        {
            BindSerializedState();
        }

        private void OnGUI()
        {
            if (_serializedObject == null)
            {
                BindSerializedState();
            }

            _serializedObject.Update();

            _impactButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                normal = { textColor = Color.yellow },
                fontStyle = FontStyle.Bold,
            };

            EditorGUILayout.LabelField("Image Blur Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Manual Folder Selection", EditorStyles.boldLabel);
            PersistentDirectoryGUI.PathSelectorObjectArray(
                _imageSourcesProperty,
                nameof(ImageBlurTool)
            );

            bool changed = _serializedObject.ApplyModifiedProperties();
            if (!changed)
            {
                int aCount = _lastSeenImageSources.Count;
                int bCount = imageSources.Count;
                if (aCount != bCount)
                {
                    changed = true;
                }
                else
                {
                    for (int i = 0; i < aCount; i++)
                    {
                        if (!ReferenceEquals(_lastSeenImageSources[i], imageSources[i]))
                        {
                            changed = true;
                            break;
                        }
                    }
                }
            }
            if (changed)
            {
                _lastSeenImageSources.Clear();
                _lastSeenImageSources.AddRange(imageSources);
                _manualTextures.Clear();
                for (int i = 0; i < imageSources.Count; i++)
                {
                    Object directory = imageSources[i];
                    if (directory == null)
                    {
                        continue;
                    }
                    string path = AssetDatabase.GetAssetPath(directory);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    TrySyncDirectory(path, _manualTextures);
                }
            }

            Event evt = Event.current;
            Rect dropArea = GUILayoutUtility.GetRect(0f, 75f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag & Drop Images/Folders Here");

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                {
                    if (!dropArea.Contains(evt.mousePosition))
                    {
                        return;
                    }

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (Object draggedObject in DragAndDrop.objectReferences)
                        {
                            if (draggedObject == null)
                            {
                                continue;
                            }

                            string path = AssetDatabase.GetAssetPath(draggedObject);
                            if (string.IsNullOrWhiteSpace(path))
                            {
                                continue;
                            }

                            if (AssetDatabase.IsValidFolder(path))
                            {
                                TrySyncDirectory(path, _orderedTextures);
                            }
                            else if (
                                draggedObject is Texture2D texture
                                && !_orderedTextures.Contains(texture)
                            )
                            {
                                _orderedTextures.Add(texture);
                            }
                        }
                    }

                    break;
                }
            }

            EditorGUILayout.Space();

            if (0 < _orderedTextures.Count || 0 < _manualTextures.Count)
            {
                EditorGUILayout.LabelField("Selected Images:", EditorStyles.boldLabel);
                _scrollPosition = EditorGUILayout.BeginScrollView(
                    _scrollPosition,
                    GUILayout.Height(200)
                );
                using (
                    WallstopStudios.UnityHelpers.Utils.Buffers<Texture2D>.HashSet.Get(
                        out HashSet<Texture2D> seen
                    )
                )
                {
                    for (int i = 0; i < _manualTextures.Count; i++)
                    {
                        Texture2D t = _manualTextures[i];
                        if (t == null || !seen.Add(t))
                        {
                            continue;
                        }
                        EditorGUILayout.ObjectField(t.name, t, typeof(Texture2D), false);
                    }
                    for (int i = 0; i < _orderedTextures.Count; i++)
                    {
                        Texture2D t = _orderedTextures[i];
                        if (t == null || !seen.Add(t))
                        {
                            continue;
                        }
                        EditorGUILayout.ObjectField(t.name, t, typeof(Texture2D), false);
                    }
                }
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Clear Selection", _impactButtonStyle))
                {
                    _orderedTextures.Clear();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Drag images or folders into the area above to select them for blurring.",
                    MessageType.Info
                );
            }

            EditorGUILayout.Space();
            _blurRadius = EditorGUILayout.IntSlider("Blur Radius", _blurRadius, 1, 200);
            EditorGUILayout.Space();

            if (GUILayout.Button("Apply Blur", _impactButtonStyle))
            {
                ApplyBlurToSelectedTextures();
            }
        }

        internal static void TrySyncDirectory(string directory, List<Texture2D> output)
        {
            if (!AssetDatabase.IsValidFolder(directory))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { directory });
            foreach (string guid in guids)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    AssetDatabase.GUIDToAssetPath(guid)
                );
                if (texture != null && !output.Contains(texture))
                {
                    output.Add(texture);
                }
            }
        }

        private void ApplyBlurToSelectedTextures()
        {
            Texture2D[] toProcess;
            using (
                WallstopStudios.UnityHelpers.Utils.Buffers<Texture2D>.HashSet.Get(
                    out HashSet<Texture2D> seen
                )
            )
            using (
                WallstopStudios.UnityHelpers.Utils.Buffers<Texture2D>.List.Get(
                    out List<Texture2D> combined
                )
            )
            {
                for (int i = 0; i < _manualTextures.Count; i++)
                {
                    Texture2D t = _manualTextures[i];
                    if (t != null && seen.Add(t))
                    {
                        combined.Add(t);
                    }
                }
                for (int i = 0; i < _orderedTextures.Count; i++)
                {
                    Texture2D t = _orderedTextures[i];
                    if (t != null && seen.Add(t))
                    {
                        combined.Add(t);
                    }
                }
                toProcess = combined.ToArray();
            }
            ApplyBlurToTextures(toProcess, _blurRadius, EditorUi.Info);
        }

        internal void ApplyBlurToTextures(
            IReadOnlyList<Texture2D> textures,
            int radius,
            Action<string, string> reportCompletion
        )
        {
            if (textures == null || textures.Count == 0)
            {
                return;
            }

            int processedCount = 0;
            int successfulCount = 0;
            bool wroteOutput = false;
            try
            {
                for (int i = 0; i < textures.Count; i++)
                {
                    Texture2D originalTexture = textures[i];
                    if (originalTexture == null)
                    {
                        continue;
                    }

                    EditorUi.ShowProgress(
                        "Applying Blur",
                        $"Processing {originalTexture.name}...",
                        (float)processedCount / textures.Count
                    );
                    try
                    {
                        if (TryWriteBlurredTexture(originalTexture, radius))
                        {
                            successfulCount++;
                            wroteOutput = true;
                        }
                    }
                    catch (Exception exception)
                    {
                        this.LogError(
                            $"Failed to blur texture: {originalTexture.name}.",
                            exception
                        );
                    }
                    processedCount++;
                }

                if (wroteOutput)
                {
                    AssetDatabase.Refresh();
                }
            }
            finally
            {
                EditorUi.ClearProgress();
            }

            reportCompletion?.Invoke(
                "Blur Operation Complete",
                $"Successfully blurred {successfulCount} of {processedCount} images."
            );
        }

        internal bool TryWriteBlurredTexture(Texture2D originalTexture, int radius)
        {
            string assetPath = AssetDatabase.GetAssetPath(originalTexture);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                this.LogError($"Texture is not a project asset: {originalTexture.name}.");
                return false;
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            bool importerSettingsChanged = false;
            bool originalReadable = false;
            TextureImporterCompression originalCompression = default;
            Texture2D blurredTexture = null;
            try
            {
                if (importer != null)
                {
                    originalReadable = importer.isReadable;
                    originalCompression = importer.textureCompression;
                    importerSettingsChanged =
                        !originalReadable
                        || originalCompression != TextureImporterCompression.Uncompressed;
                    if (importerSettingsChanged)
                    {
                        Undo.RecordObject(importer, "Prepare Texture for Blur");
                        importer.isReadable = true;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.SaveAndReimport();
                    }
                }

                Texture2D currentTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (currentTexture == null || !currentTexture.isReadable)
                {
                    this.LogError(
                        $"Texture is null or could not be made readable: {assetPath}. Please check 'Read/Write Enabled' in its import settings if the issue persists. Skipping."
                    );
                    return false;
                }

                blurredTexture = CreateBlurredTexture(currentTexture, radius);
                if (blurredTexture == null)
                {
                    this.LogError($"Failed to create blurred texture for: {originalTexture.name}.");
                    return false;
                }

                string directory = Path.GetDirectoryName(assetPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return false;
                }

                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                string sourceExtension = Path.GetExtension(assetPath);
                bool encodeJpeg =
                    string.Equals(sourceExtension, ".jpg", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceExtension, ".jpeg", StringComparison.OrdinalIgnoreCase);
                string outputExtension = encodeJpeg ? sourceExtension : ".png";
                string newPathBase = Path.Combine(directory, $"{fileName}_blurred_{radius}");
                string finalPath = newPathBase + outputExtension;
                int counter = 0;
                while (File.Exists(finalPath))
                {
                    counter++;
                    finalPath = $"{newPathBase}_{counter}{outputExtension}";
                }

                byte[] bytes = encodeJpeg
                    ? blurredTexture.EncodeToJPG(100)
                    : blurredTexture.EncodeToPNG();
                if (bytes == null)
                {
                    this.LogError($"Failed to encode texture: {currentTexture.name}.");
                    return false;
                }

                File.WriteAllBytes(finalPath, bytes);
                this.Log($"Saved blurred image to: {finalPath}");
                return true;
            }
            finally
            {
                if (blurredTexture != null)
                {
                    DestroyImmediate(blurredTexture);
                }

                if (importerSettingsChanged)
                {
                    TextureImporter currentImporter =
                        AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (currentImporter != null)
                    {
                        currentImporter.isReadable = originalReadable;
                        currentImporter.textureCompression = originalCompression;
                        currentImporter.SaveAndReimport();
                    }
                }
            }
        }

        internal static Texture2D BlurredForTests(Texture2D original, int radius)
        {
            return CreateBlurredTexture(original, radius);
        }

        internal static float[] KernelForTests(int radius)
        {
            return GenerateGaussianKernel(radius);
        }

        private static Texture2D CreateBlurredTexture(Texture2D original, int radius)
        {
            Texture2D blurred = new(original.width, original.height, original.format, false)
            {
                name = TemporaryTextureName,
            };
            try
            {
                Color[] pixels = original.GetPixels();
                int width = original.width;
                int height = original.height;

                /*
                    Every intermediate is pooled. Texture2D.SetPixels accepts an array longer than the
                    texture (unlike SetPixels32, measured), so the destination can be pooled as well.
                */
                using PooledArray<Color> pooledBlurred = SystemArrayPool<Color>.Get(
                    pixels.Length,
                    out Color[] blurredPixels
                );

                /*
                    Both passes run over premultiplied color, so a transparent texel cannot tint a visible
                    neighbor. The straight color rides alongside because it is the only meaningful answer
                    where the blurred alpha reaches zero and cannot be divided back out.
                */
                using PooledArray<Color> pooledPremultiplied = SystemArrayPool<Color>.Get(
                    pixels.Length,
                    out Color[] premultiplied
                );
                for (int i = 0; i < pixels.Length; i++)
                {
                    premultiplied[i] = TextureResampling.Premultiply(pixels[i]);
                }

                // Temporary buffers for the first pass
                using PooledArray<Color> pooledTemp = SystemArrayPool<Color>.Get(
                    pixels.Length,
                    out Color[] tempPixels
                );
                using PooledArray<Color> pooledTempStraight = SystemArrayPool<Color>.Get(
                    pixels.Length,
                    out Color[] tempStraight
                );

                // Generate the kernel for the weighted average
                float[] kernel = GenerateGaussianKernel(radius);

                // --- Horizontal Pass ---
                Parallel.For(
                    0,
                    height,
                    y =>
                    {
                        int yOffset = y * width;
                        for (int x = 0; x < width; x++)
                        {
                            Color weightedSum = Color.clear;
                            Color straightSum = Color.clear;
                            float weightTotal = 0f;

                            for (int k = -radius; k <= radius; k++)
                            {
                                int currentX = x + k;
                                if (0 <= currentX && currentX < width)
                                {
                                    float weight = kernel[k + radius];
                                    weightedSum += premultiplied[yOffset + currentX] * weight;
                                    straightSum += pixels[yOffset + currentX] * weight;
                                    weightTotal += weight;
                                }
                            }
                            tempPixels[yOffset + x] = weightedSum / weightTotal;
                            tempStraight[yOffset + x] = straightSum / weightTotal;
                        }
                    }
                );

                // --- Vertical Pass ---
                Parallel.For(
                    0,
                    width,
                    x =>
                    {
                        for (int y = 0; y < height; y++)
                        {
                            Color weightedSum = Color.clear;
                            Color straightSum = Color.clear;
                            float weightTotal = 0f;

                            for (int k = -radius; k <= radius; k++)
                            {
                                int currentY = y + k;
                                if (0 <= currentY && currentY < height)
                                {
                                    float weight = kernel[k + radius];
                                    weightedSum += tempPixels[(currentY * width) + x] * weight;
                                    straightSum += tempStraight[(currentY * width) + x] * weight;
                                    weightTotal += weight;
                                }
                            }
                            blurredPixels[(y * width) + x] = TextureResampling.Unpremultiply(
                                weightedSum / weightTotal,
                                straightSum / weightTotal
                            );
                        }
                    }
                );

                blurred.SetPixels(blurredPixels);
                blurred.Apply();
                return blurred;
            }
            catch
            {
                DestroyImmediate(blurred);
                throw;
            }
        }

        private static float[] GenerateGaussianKernel(int radius)
        {
            int size = radius * 2 + 1;
            float[] kernel = new float[size];
            float sigma = radius / 3.0f; // A good rule of thumb for sigma
            float twoSigmaSquare = 2.0f * sigma * sigma;
            float sum = 0f;

            for (int i = 0; i < size; i++)
            {
                int distance = i - radius;
                kernel[i] =
                    Mathf.Exp(-(distance * distance) / twoSigmaSquare)
                    / (Mathf.Sqrt(Mathf.PI * twoSigmaSquare));
                sum += kernel[i];
            }

            // Normalize the kernel so that the weights sum to 1
            for (int i = 0; i < size; i++)
            {
                kernel[i] /= sum;
            }

            return kernel;
        }
    }
}

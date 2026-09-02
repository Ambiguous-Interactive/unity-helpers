// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Visuals.UIToolkit
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    public sealed class LayeredImage : VisualElement
    {
        /// <remarks>
        /// The tolerance covers float drift only. A half-8-bit-step of slack used to be added on top,
        /// which discarded an extra alpha level at some cutoffs and none at others - so the same cutoff
        /// selected different pixels here than <see cref="ColorQuantization.ToThresholdByte"/> selects
        /// everywhere else in the package, and a pixel the caller asked to keep was dropped. Composited
        /// alpha does not land on the 8-bit grid, so the plain comparison is the rule; drift is the only
        /// thing needing slack.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAlphaEffectivelyInvisible(float alpha, float cutoff)
        {
            float maxMagnitude = Mathf.Max(Mathf.Abs(alpha), Mathf.Abs(cutoff));
            float fudge = Mathf.Max(1e-6f * maxMagnitude, Mathf.Epsilon * 8f);
            return alpha <= cutoff + fudge;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanPlayTimed(float fps)
        {
            if (fps <= 0 || float.IsNaN(fps) || float.IsInfinity(fps))
            {
                return false;
            }

            double frameMilliseconds = 1000d / fps;
            return 0 < frameMilliseconds
                && frameMilliseconds <= TimeSpan.MaxValue.TotalMilliseconds;
        }

        public float Fps
        {
            get => _fps;
            set
            {
                if (_fps == value)
                {
                    if (CanSelfUpdate())
                    {
                        StartSelfUpdate();
                    }
                    else
                    {
                        StopSelfUpdate();
                    }

                    return;
                }

                StopSelfUpdate();
                _fps = value;
                if (!CanSelfUpdate())
                {
                    return;
                }

                StartSelfUpdate();
            }
        }

        private static long GetFrameIntervalMilliseconds(float fps)
        {
            return Math.Max(1L, (long)Math.Round(1000d / fps));
        }

        private static TimeSpan GetFrameInterval(float fps)
        {
            return TimeSpan.FromMilliseconds(GetFrameIntervalMilliseconds(fps));
        }

        private bool CanSelfUpdate()
        {
            return _updatesSelf && 1 < _computed.Length && CanPlayTimed(_fps);
        }

        private void StartSelfUpdate()
        {
            if (!CanSelfUpdate() || panel == null || _selfUpdateItem != null)
            {
                return;
            }

            _selfUpdateItem = schedule
                .Execute(HandleSelfUpdate)
                .Every(GetFrameIntervalMilliseconds(_fps));
        }

        private void StopSelfUpdate()
        {
            _selfUpdateItem?.Pause();
            _selfUpdateItem = null;
        }

        private void HandleSelfUpdate(TimerState _)
        {
            if (panel == null || !CanSelfUpdate())
            {
                StopSelfUpdate();
                return;
            }

            Update(force: true);
        }

        private void HandleAttachToPanel(AttachToPanelEvent _)
        {
            StartSelfUpdate();
        }

        private void HandleDetachFromPanel(DetachFromPanelEvent _)
        {
            StopSelfUpdate();
        }

        private const int ParallelBlendThreshold = 2048;

        private readonly AnimatedSpriteLayer[] _layers;
        private readonly Texture2D[] _computed;
        internal Texture2D[] ComputedTexturesForTests => _computed;
        internal bool SelfUpdateActiveForTests => _selfUpdateItem != null;

        internal void SetElapsedSinceLastFrameForTests(TimeSpan elapsedSinceLastFrame)
        {
            _timer.Stop();
            _lastTick = _timer.Elapsed - elapsedSinceLastFrame;
        }

        private readonly Color _backgroundColor;
        private readonly Rect? _largestArea;
        private readonly Stopwatch _timer;
        private readonly bool _updatesSelf;
        private readonly float _pixelCutoff;

        private TimeSpan _lastTick;
        private IVisualElementScheduledItem _selfUpdateItem;
        private float _fps;
        private int _index;

        public LayeredImage(
            IEnumerable<AnimatedSpriteLayer> inputSpriteLayers,
            Color? backgroundColor = null,
            float fps = AnimatedSpriteLayer.FrameRate,
            bool updatesSelf = true,
            float pixelCutoff = 0.01f
        )
        {
            _pixelCutoff = pixelCutoff;
            using (
                PooledResource<List<AnimatedSpriteLayer>> lease =
                    Buffers<AnimatedSpriteLayer>.List.Get(out List<AnimatedSpriteLayer> buffer)
            )
            {
                buffer.AddRange(inputSpriteLayers);
                _layers = buffer.ToArray();
            }
            _backgroundColor = backgroundColor ?? Color.white;
            _computed = ComputeTextures();
            _largestArea = null;
            _updatesSelf = updatesSelf;

            foreach (Texture2D computedTexture in _computed)
            {
                if (computedTexture == null)
                {
                    continue;
                }

                if (_largestArea == null)
                {
                    _largestArea = new Rect(0, 0, computedTexture.width, computedTexture.height);
                }
                else
                {
                    Rect currentLargest = _largestArea.Value;
                    currentLargest.width = Mathf.Max(currentLargest.width, computedTexture.width);
                    currentLargest.height = Mathf.Max(
                        currentLargest.height,
                        computedTexture.height
                    );
                    _largestArea = currentLargest;
                }
            }

            _timer = Stopwatch.StartNew();
            RegisterCallback<AttachToPanelEvent>(HandleAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(HandleDetachFromPanel);
            Render(0);
            Fps = fps;
        }

        public void Update(bool force = false)
        {
            if (panel == null)
            {
                return;
            }

            if (_computed.Length == 0)
            {
                return;
            }

            if (!CanPlayTimed(_fps))
            {
                return;
            }

            TimeSpan elapsed = _timer.Elapsed;
            TimeSpan deltaTime = GetFrameInterval(_fps);

            /*
                Prevent time accumulation drift: if _lastTick has fallen significantly behind
                (e.g., editor was paused/unfocused, or this is the first update after construction),
                clamp it BEFORE checking the frame advance condition. This prevents rapid "catch-up"
                animation that makes the preview appear to run at too high FPS.
                Allow at most one frame of lag before resetting to current time.
            */
            if (deltaTime + deltaTime < elapsed - _lastTick)
            {
                _lastTick = elapsed - deltaTime;
            }

            if (!force && elapsed < _lastTick + deltaTime)
            {
                return;
            }

            _index = _index.WrappedIncrement(_computed.Length);
            _lastTick += deltaTime;

            Render(_index);
        }

        private void Render(int index)
        {
            if (index < 0 || _computed.Length <= index)
            {
                return;
            }

            Texture2D computed = _computed[index];
            if (computed != null)
            {
                style.backgroundImage = computed;
                style.width = computed.width;
                style.height = computed.height;
            }
            else
            {
                style.backgroundImage = null;
                style.width = _largestArea?.width ?? 0;
                style.height = _largestArea?.height ?? 0;
            }

            style.marginRight = 0;
            style.marginBottom = 0;
            if (_largestArea == null)
            {
                return;
            }

            Rect largestAreaRect = _largestArea.Value;
            float currentWidth = computed != null ? computed.width : _largestArea?.width ?? 0;
            float currentHeight = computed != null ? computed.height : _largestArea?.height ?? 0;

            if (currentWidth < largestAreaRect.width)
            {
                style.marginRight = largestAreaRect.width - currentWidth;
            }
            if (currentHeight < largestAreaRect.height)
            {
                style.marginBottom = largestAreaRect.height - currentHeight;
            }
        }

        private Texture2D[] ComputeTextures()
        {
            AnimatedSpriteLayer[] layers = _layers;
            if (layers == null || layers.Length == 0)
            {
                return Array.Empty<Texture2D>();
            }

            int frameCount = 0;
            for (int layerIndex = 0; layerIndex < layers.Length; ++layerIndex)
            {
                Sprite[] layerFrames = layers[layerIndex].frames;
                if (layerFrames == null)
                {
                    continue;
                }

                int layerFrameCount = layerFrames.Length;
                if (frameCount < layerFrameCount)
                {
                    frameCount = layerFrameCount;
                }
            }

            if (frameCount == 0)
            {
                return Array.Empty<Texture2D>();
            }

            FrameCompositor compositor = new(layers, _backgroundColor, _pixelCutoff);
            Texture2D[] computed = new Texture2D[frameCount];

            for (int frameIndex = 0; frameIndex < frameCount; ++frameIndex)
            {
                computed[frameIndex] = compositor.ComposeFrame(frameIndex);
            }

            return computed;
        }

        private static void ComposeSpriteOntoBuffer(
            Color[] bufferPixels,
            int bufferWidth,
            int bufferHeight,
            Color[] spritePixels,
            int spriteWidth,
            int spriteHeight,
            float baseX,
            float baseY,
            float layerAlpha,
            float pixelCutoff
        )
        {
            if (spriteWidth == 0 || spriteHeight == 0)
            {
                return;
            }

            BlendSpriteRowJob job = new(
                bufferPixels,
                bufferWidth,
                bufferHeight,
                spritePixels,
                spriteWidth,
                baseX,
                baseY,
                layerAlpha,
                pixelCutoff
            );

            if (ParallelBlendThreshold <= spriteWidth * spriteHeight)
            {
                Parallel.For(0, spriteHeight, job.Execute);
                return;
            }

            job.RunSequential(spriteHeight);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BlendSpriteRow(
            Color[] bufferPixels,
            int bufferWidth,
            int bufferHeight,
            Color[] spritePixels,
            int spriteWidth,
            float baseX,
            float baseY,
            float layerAlpha,
            float pixelCutoff,
            int spriteRow
        )
        {
            int spriteRowOffset = spriteRow * spriteWidth;
            float targetY = baseY + spriteRow;
            int bufferY = Mathf.FloorToInt(targetY);
            if (bufferY < 0 || bufferHeight <= bufferY)
            {
                return;
            }

            int bufferRowOffset = bufferY * bufferWidth;

            for (int spriteColumn = 0; spriteColumn < spriteWidth; ++spriteColumn)
            {
                Color spritePixel = spritePixels[spriteRowOffset + spriteColumn];
                float spriteAlpha = spritePixel.a * layerAlpha;
                if (IsAlphaEffectivelyInvisible(spriteAlpha, pixelCutoff))
                {
                    continue;
                }

                if (1f < spriteAlpha)
                {
                    spriteAlpha = 1f;
                }

                int bufferX = Mathf.FloorToInt(baseX + spriteColumn);
                if (bufferX < 0 || bufferWidth <= bufferX)
                {
                    continue;
                }

                int bufferIndex = bufferRowOffset + bufferX;
                ref Color destination = ref bufferPixels[bufferIndex];

                float destinationAlpha = destination.a;
                if (IsAlphaEffectivelyInvisible(destinationAlpha, pixelCutoff))
                {
                    destination.r = spritePixel.r;
                    destination.g = spritePixel.g;
                    destination.b = spritePixel.b;
                    destination.a = spriteAlpha;
                    continue;
                }

                float inverseSourceAlpha = 1f - spriteAlpha;
                float outAlpha = spriteAlpha + destinationAlpha * inverseSourceAlpha;
                if (IsAlphaEffectivelyInvisible(outAlpha, pixelCutoff))
                {
                    destination = Color.clear;
                    continue;
                }

                if (1f < outAlpha)
                {
                    outAlpha = 1f;
                }

                float invOutAlpha = 1f / outAlpha;
                float sourceWeight = spriteAlpha * invOutAlpha;
                float destinationWeight = destinationAlpha * inverseSourceAlpha * invOutAlpha;

                float destR = destination.r;
                float destG = destination.g;
                float destB = destination.b;

                destination.r = spritePixel.r * sourceWeight + destR * destinationWeight;
                destination.g = spritePixel.g * sourceWeight + destG * destinationWeight;
                destination.b = spritePixel.b * sourceWeight + destB * destinationWeight;
                destination.a = outAlpha;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetLayerFrame(
            in AnimatedSpriteLayer layer,
            int frameIndex,
            out Sprite sprite
        )
        {
            Sprite[] frames = layer.frames;
            if (frames == null || frameIndex < 0 || frames.Length <= frameIndex)
            {
                sprite = null;
                return false;
            }

            sprite = frames[frameIndex];
            return sprite != null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 GetPixelOffset(in AnimatedSpriteLayer layer, int frameIndex)
        {
            Vector2[] offsets = layer.perFramePixelOffsets;
            if (offsets == null || frameIndex < 0 || offsets.Length <= frameIndex)
            {
                return Vector2.zero;
            }

            return offsets[frameIndex];
        }

        private readonly struct FrameCompositor
        {
            private readonly AnimatedSpriteLayer[] _layers;
            private readonly Color _backgroundColor;
            private readonly float _pixelCutoff;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public FrameCompositor(
                AnimatedSpriteLayer[] layers,
                Color backgroundColor,
                float pixelCutoff
            )
            {
                _layers = layers;
                _backgroundColor = backgroundColor;
                _pixelCutoff = pixelCutoff;
            }

            public Texture2D ComposeFrame(int frameIndex)
            {
                AnimatedSpriteLayer[] layers = _layers;
                int layerCount = layers.Length;

                using PooledArray<LayerFrameInfo> frameInfoLease =
                    SystemArrayPool<LayerFrameInfo>.Get(
                        layerCount,
                        out LayerFrameInfo[] frameInfos
                    );

                int infoCount = 0;
                float overallMinX = float.MaxValue;
                float overallMaxX = float.MinValue;
                float overallMinY = float.MaxValue;
                float overallMaxY = float.MinValue;
                bool hasVisibleSprite = false;
                bool hasFractionalPlacement = false;

                for (int layerIndex = 0; layerIndex < layerCount; ++layerIndex)
                {
                    ref readonly AnimatedSpriteLayer layer = ref layers[layerIndex];
                    float layerAlpha = layer.alpha;
                    if (layerAlpha <= 0f || !TryGetLayerFrame(layer, frameIndex, out Sprite sprite))
                    {
                        continue;
                    }

                    Texture2D texture = sprite.texture;
                    if (texture == null || !texture.isReadable)
                    {
                        continue;
                    }

                    Rect spriteRect = sprite.rect;
                    int spriteRectWidth = Mathf.FloorToInt(spriteRect.width);
                    int spriteRectHeight = Mathf.FloorToInt(spriteRect.height);
                    if (spriteRectWidth <= 0 || spriteRectHeight <= 0)
                    {
                        continue;
                    }

                    Vector2 pixelOffset = GetPixelOffset(layer, frameIndex);
                    Vector2 pivot = sprite.pivot;

                    float baseX = pixelOffset.x - pivot.x;
                    float baseY = pixelOffset.y - pivot.y;

                    // Track if any layer uses fractional placement (e.g., centered pivot)
                    if (!hasFractionalPlacement)
                    {
                        // Consider values effectively integral within a tiny epsilon
                        bool baseXIsIntegral = Mathf.Abs(baseX - Mathf.Round(baseX)) <= 1e-5f;
                        bool baseYIsIntegral = Mathf.Abs(baseY - Mathf.Round(baseY)) <= 1e-5f;
                        hasFractionalPlacement = !(baseXIsIntegral && baseYIsIntegral);
                    }
                    float spriteMinX = baseX;
                    float spriteMaxX = baseX + spriteRect.width;
                    float spriteMinY = baseY;
                    float spriteMaxY = baseY + spriteRect.height;

                    if (spriteMinX < overallMinX)
                    {
                        overallMinX = spriteMinX;
                    }

                    if (overallMaxX < spriteMaxX)
                    {
                        overallMaxX = spriteMaxX;
                    }

                    if (spriteMinY < overallMinY)
                    {
                        overallMinY = spriteMinY;
                    }

                    if (overallMaxY < spriteMaxY)
                    {
                        overallMaxY = spriteMaxY;
                    }

                    frameInfos[infoCount++] = new LayerFrameInfo(
                        texture,
                        Mathf.FloorToInt(spriteRect.x),
                        Mathf.FloorToInt(spriteRect.y),
                        spriteRectWidth,
                        spriteRectHeight,
                        baseX,
                        baseY,
                        layerAlpha
                    );

                    hasVisibleSprite = true;
                }

                if (!hasVisibleSprite)
                {
                    return null;
                }

                int compositeOriginX = Mathf.FloorToInt(overallMinX);
                int compositeOriginY = Mathf.FloorToInt(overallMinY);
                int compositeWidth = Mathf.CeilToInt(overallMaxX) - compositeOriginX;
                int compositeHeight = Mathf.CeilToInt(overallMaxY) - compositeOriginY;

                if (compositeWidth <= 0 || compositeHeight <= 0)
                {
                    return null;
                }

                int compositeLength = compositeWidth * compositeHeight;
                using PooledArray<Color> compositeLease = SystemArrayPool<Color>.Get(
                    compositeLength,
                    out Color[] bufferPixels
                );

                bufferPixels.AsSpan(0, compositeLength).Clear();

                float pixelCutoff = _pixelCutoff;

                for (int infoIndex = 0; infoIndex < infoCount; ++infoIndex)
                {
                    ref readonly LayerFrameInfo info = ref frameInfos[infoIndex];

                    Color[] spritePixels = info.Texture.GetPixels(
                        info.SourceX,
                        info.SourceY,
                        info.Width,
                        info.Height
                    );

                    ComposeSpriteOntoBuffer(
                        bufferPixels,
                        compositeWidth,
                        compositeHeight,
                        spritePixels,
                        info.Width,
                        info.Height,
                        info.BaseX - compositeOriginX,
                        info.BaseY - compositeOriginY,
                        info.Alpha,
                        pixelCutoff
                    );
                }

                return FinalizeTexture(
                    bufferPixels,
                    compositeWidth,
                    compositeHeight,
                    pixelCutoff,
                    hasFractionalPlacement
                );
            }

            private Texture2D FinalizeTexture(
                Color[] bufferPixels,
                int compositeWidth,
                int compositeHeight,
                float pixelCutoff,
                bool preserveCompositeBounds
            )
            {
                int minX = compositeWidth;
                int maxX = -1;
                int minY = compositeHeight;
                int maxY = -1;
                bool anyVisible = false;

                for (int y = 0; y < compositeHeight; ++y)
                {
                    int rowOffset = y * compositeWidth;
                    int rowMin = compositeWidth;
                    int rowMax = -1;

                    for (int x = 0; x < compositeWidth; ++x)
                    {
                        // Treat pixels with alpha equal to cutoff as invisible
                        if (IsAlphaEffectivelyInvisible(bufferPixels[rowOffset + x].a, pixelCutoff))
                        {
                            continue;
                        }

                        anyVisible = true;
                        if (x < rowMin)
                        {
                            rowMin = x;
                        }

                        if (rowMax < x)
                        {
                            rowMax = x;
                        }
                    }

                    if (rowMax < rowMin)
                    {
                        continue;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    if (maxY < y)
                    {
                        maxY = y;
                    }

                    if (rowMin < minX)
                    {
                        minX = rowMin;
                    }

                    if (maxX < rowMax)
                    {
                        maxX = rowMax;
                    }
                }

                if (!anyVisible || maxX < minX || maxY < minY)
                {
                    return null;
                }

                if (preserveCompositeBounds)
                {
                    // Keep full composite bounds when fractional placement (e.g., centered pivot)
                    minX = 0;
                    minY = 0;
                    maxX = compositeWidth - 1;
                    maxY = compositeHeight - 1;
                }

                int finalWidth = maxX - minX + 1;
                int finalHeight = maxY - minY + 1;
                int finalLength = finalWidth * finalHeight;

                using PooledArray<Color> finalLease = SystemArrayPool<Color>.Get(
                    finalLength,
                    out Color[] finalPixels
                );

                Span<Color> finalSpan = finalPixels.AsSpan(0, finalLength);
                finalSpan.Fill(_backgroundColor);

                for (int y = 0; y < finalHeight; ++y)
                {
                    int destinationRow = y * finalWidth;
                    int sourceRow = (minY + y) * compositeWidth + minX;

                    for (int x = 0; x < finalWidth; ++x)
                    {
                        Color pixel = bufferPixels[sourceRow + x];
                        // Exclude pixels with alpha equal to cutoff
                        if (!IsAlphaEffectivelyInvisible(pixel.a, pixelCutoff))
                        {
                            finalPixels[destinationRow + x] = pixel;
                        }
                    }
                }

                Texture2D finalTexture = new(
                    finalWidth,
                    finalHeight,
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: false
                )
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };

                finalTexture.SetPixels(finalPixels);
                finalTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return finalTexture;
            }

            private readonly struct LayerFrameInfo
            {
                public readonly Texture2D Texture;
                public readonly int SourceX;
                public readonly int SourceY;
                public readonly int Width;
                public readonly int Height;
                public readonly float BaseX;
                public readonly float BaseY;
                public readonly float Alpha;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public LayerFrameInfo(
                    Texture2D texture,
                    int sourceX,
                    int sourceY,
                    int width,
                    int height,
                    float baseX,
                    float baseY,
                    float alpha
                )
                {
                    Texture = texture;
                    SourceX = sourceX;
                    SourceY = sourceY;
                    Width = width;
                    Height = height;
                    BaseX = baseX;
                    BaseY = baseY;
                    Alpha = alpha;
                }
            }
        }

        private readonly struct BlendSpriteRowJob
        {
            private readonly Color[] _bufferPixels;
            private readonly int _bufferWidth;
            private readonly int _bufferHeight;
            private readonly Color[] _spritePixels;
            private readonly int _spriteWidth;
            private readonly float _baseX;
            private readonly float _baseY;
            private readonly float _layerAlpha;
            private readonly float _pixelCutoff;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public BlendSpriteRowJob(
                Color[] bufferPixels,
                int bufferWidth,
                int bufferHeight,
                Color[] spritePixels,
                int spriteWidth,
                float baseX,
                float baseY,
                float layerAlpha,
                float pixelCutoff
            )
            {
                _bufferPixels = bufferPixels;
                _bufferWidth = bufferWidth;
                _bufferHeight = bufferHeight;
                _spritePixels = spritePixels;
                _spriteWidth = spriteWidth;
                _baseX = baseX;
                _baseY = baseY;
                _layerAlpha = layerAlpha;
                _pixelCutoff = pixelCutoff;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Execute(int spriteRow)
            {
                BlendSpriteRow(
                    _bufferPixels,
                    _bufferWidth,
                    _bufferHeight,
                    _spritePixels,
                    _spriteWidth,
                    _baseX,
                    _baseY,
                    _layerAlpha,
                    _pixelCutoff,
                    spriteRow
                );
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RunSequential(int spriteHeight)
            {
                for (int spriteRow = 0; spriteRow < spriteHeight; ++spriteRow)
                {
                    Execute(spriteRow);
                }
            }
        }
    }
}

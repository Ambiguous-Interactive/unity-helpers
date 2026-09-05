// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS: This IS a helper class managing its own Unity object lifecycle, not a test class

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Provides utility methods for creating in-memory textures during testing.
    /// All textures created through this helper are tracked and can be cleaned up
    /// via <see cref="Cleanup"/> or by using the <see cref="IDisposable"/> pattern.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This helper is designed to replace disk-based texture creation in tests,
    /// significantly improving test performance by avoiding file I/O operations
    /// and AssetDatabase imports.
    /// </para>
    /// <para>
    /// All textures are created with <see cref="TextureFormat.RGBA32"/> by default
    /// and are readable (not marked as no-longer-readable) to support pixel-level
    /// assertions in tests.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// // Using the disposable pattern
    /// using (TextureTestHelper helper = new TextureTestHelper())
    /// {
    ///     Texture2D texture = helper.CreateSolidTexture(64, 64, Color.red);
    ///     // Use texture in tests...
    /// } // All textures automatically destroyed
    ///
    /// // Manual cleanup
    /// TextureTestHelper helper = new TextureTestHelper();
    /// Texture2D checkerboard = helper.CreateCheckerboardTexture(64, 64, 8, Color.black, Color.white);
    /// // Use texture...
    /// helper.Cleanup();
    /// ]]></code>
    /// </example>
    public sealed class TextureTestHelper : IDisposable
    {
        private readonly List<Object> _trackedObjects;
        private bool _disposed;

        /// <summary>
        /// Gets the number of objects currently being tracked by this helper.
        /// </summary>
        public int TrackedCount => _trackedObjects.Count;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextureTestHelper"/> class.
        /// </summary>
        public TextureTestHelper()
        {
            _trackedObjects = new List<Object>();
        }

        /// <summary>
        /// Creates an in-memory texture filled with a solid color.
        /// </summary>
        /// <param name="width">The width of the texture in pixels. Must be positive.</param>
        /// <param name="height">The height of the texture in pixels. Must be positive.</param>
        /// <param name="fillColor">The color to fill the entire texture with.</param>
        /// <param name="format">The texture format. Defaults to <see cref="TextureFormat.RGBA32"/>.</param>
        /// <returns>A new texture filled with the specified color, or null if parameters are invalid.</returns>
        /// <remarks>
        /// The texture is created without mipmaps and with <see cref="FilterMode.Point"/>
        /// for pixel-perfect testing. The texture remains readable for assertion purposes.
        /// </remarks>
        public Texture2D CreateSolidTexture(
            int width,
            int height,
            Color fillColor,
            TextureFormat format = TextureFormat.RGBA32
        )
        {
            if (_disposed)
            {
                return null;
            }

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            Texture2D texture = CreateBaseTexture(width, height, format);
            if (texture == null)
            {
                return null;
            }

            int pixelCount = width * height;
            Color[] pixels = new Color[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                pixels[i] = fillColor;
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return texture;
        }

        /// <summary>
        /// Creates an in-memory texture with a checkerboard pattern.
        /// </summary>
        /// <param name="width">The width of the texture in pixels. Must be positive.</param>
        /// <param name="height">The height of the texture in pixels. Must be positive.</param>
        /// <param name="cellSize">The size of each checkerboard cell in pixels. Must be positive.</param>
        /// <param name="color1">The color of cells where (cellX + cellY) is even.</param>
        /// <param name="color2">The color of cells where (cellX + cellY) is odd.</param>
        /// <returns>A new texture with a checkerboard pattern, or null if parameters are invalid.</returns>
        /// <remarks>
        /// Useful for visual testing and verifying texture transformations where
        /// pattern preservation is important.
        /// </remarks>
        public Texture2D CreateCheckerboardTexture(
            int width,
            int height,
            int cellSize,
            Color color1,
            Color color2
        )
        {
            if (_disposed)
            {
                return null;
            }

            if (width <= 0 || height <= 0 || cellSize <= 0)
            {
                return null;
            }

            Texture2D texture = CreateBaseTexture(width, height, TextureFormat.RGBA32);
            if (texture == null)
            {
                return null;
            }

            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                int cellY = y / cellSize;
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int cellX = x / cellSize;
                    bool isEvenCell = ((cellX + cellY) & 1) == 0;
                    pixels[rowOffset + x] = isEvenCell ? color1 : color2;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return texture;
        }

        /// <summary>
        /// Creates an in-memory texture with a horizontal gradient.
        /// </summary>
        /// <param name="width">The width of the texture in pixels. Must be positive.</param>
        /// <param name="height">The height of the texture in pixels. Must be positive.</param>
        /// <param name="leftColor">The color at the left edge (x = 0).</param>
        /// <param name="rightColor">The color at the right edge (x = width - 1).</param>
        /// <returns>A new texture with a horizontal gradient, or null if parameters are invalid.</returns>
        public Texture2D CreateHorizontalGradientTexture(
            int width,
            int height,
            Color leftColor,
            Color rightColor
        )
        {
            if (_disposed)
            {
                return null;
            }

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            Texture2D texture = CreateBaseTexture(width, height, TextureFormat.RGBA32);
            if (texture == null)
            {
                return null;
            }

            Color[] pixels = new Color[width * height];
            float widthMinusOne = 1 < width ? width - 1 : 1;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    float t = x / widthMinusOne;
                    pixels[rowOffset + x] = Color.Lerp(leftColor, rightColor, t);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return texture;
        }

        /// <summary>
        /// Creates an in-memory texture with a vertical gradient.
        /// </summary>
        /// <param name="width">The width of the texture in pixels. Must be positive.</param>
        /// <param name="height">The height of the texture in pixels. Must be positive.</param>
        /// <param name="bottomColor">The color at the bottom edge (y = 0).</param>
        /// <param name="topColor">The color at the top edge (y = height - 1).</param>
        /// <returns>A new texture with a vertical gradient, or null if parameters are invalid.</returns>
        public Texture2D CreateVerticalGradientTexture(
            int width,
            int height,
            Color bottomColor,
            Color topColor
        )
        {
            if (_disposed)
            {
                return null;
            }

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            Texture2D texture = CreateBaseTexture(width, height, TextureFormat.RGBA32);
            if (texture == null)
            {
                return null;
            }

            Color[] pixels = new Color[width * height];
            float heightMinusOne = 1 < height ? height - 1 : 1;

            for (int y = 0; y < height; y++)
            {
                float t = y / heightMinusOne;
                Color rowColor = Color.Lerp(bottomColor, topColor, t);
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    pixels[rowOffset + x] = rowColor;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return texture;
        }

        /// <summary>
        /// Creates an in-memory texture with a grid pattern suitable for sprite sheet testing.
        /// Each cell is filled with a distinct color based on its position in the grid.
        /// </summary>
        /// <param name="width">The total width of the texture in pixels. Must be positive.</param>
        /// <param name="height">The total height of the texture in pixels. Must be positive.</param>
        /// <param name="columns">The number of columns in the grid. Must be positive.</param>
        /// <param name="rows">The number of rows in the grid. Must be positive.</param>
        /// <returns>A new texture with a grid pattern, or null if parameters are invalid.</returns>
        /// <remarks>
        /// <para>
        /// Cell colors are generated using HSV color space to ensure visually distinct
        /// colors for each cell. The hue is distributed evenly across all cells.
        /// </para>
        /// <para>
        /// Each cell's dimensions are calculated as width/columns by height/rows.
        /// Non-evenly divisible dimensions will result in some cells being slightly
        /// larger due to integer division.
        /// </para>
        /// </remarks>
        public Texture2D CreateGridTexture(int width, int height, int columns, int rows)
        {
            if (_disposed)
            {
                return null;
            }

            if (width <= 0 || height <= 0 || columns <= 0 || rows <= 0)
            {
                return null;
            }

            Texture2D texture = CreateBaseTexture(width, height, TextureFormat.RGBA32);
            if (texture == null)
            {
                return null;
            }

            int cellWidth = width / columns;
            int cellHeight = height / rows;
            int totalCells = columns * rows;

            Color[] pixels = new Color[width * height];

            for (int row = 0; row < rows; row++)
            {
                int cellStartY = row * cellHeight;
                int cellEndY = (row == rows - 1) ? height : cellStartY + cellHeight;

                for (int col = 0; col < columns; col++)
                {
                    int cellIndex = row * columns + col;
                    float hue = (float)cellIndex / totalCells;
                    Color cellColor = Color.HSVToRGB(hue, 0.8f, 0.9f);

                    int cellStartX = col * cellWidth;
                    int cellEndX = (col == columns - 1) ? width : cellStartX + cellWidth;

                    for (int y = cellStartY; y < cellEndY; y++)
                    {
                        int rowOffset = y * width;
                        for (int x = cellStartX; x < cellEndX; x++)
                        {
                            pixels[rowOffset + x] = cellColor;
                        }
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return texture;
        }

        /// <summary>
        /// Creates an in-memory texture designed for sprite sheet extraction testing.
        /// Each cell has a distinct, identifiable color and an optional border.
        /// </summary>
        /// <param name="width">The total width of the texture in pixels. Must be positive.</param>
        /// <param name="height">The total height of the texture in pixels. Must be positive.</param>
        /// <param name="columns">The number of sprite columns. Must be positive.</param>
        /// <param name="rows">The number of sprite rows. Must be positive.</param>
        /// <param name="borderWidth">The width of the border around each cell in pixels. Defaults to 0.</param>
        /// <param name="borderColor">The color of the border. Defaults to transparent.</param>
        /// <returns>A new texture suitable for sprite sheet testing, or null if parameters are invalid.</returns>
        /// <remarks>
        /// <para>
        /// This method creates textures that are ideal for testing sprite sheet extraction
        /// algorithms. Each sprite cell has a unique color that can be verified after extraction.
        /// </para>
        /// <para>
        /// When <paramref name="borderWidth"/> is greater than 0, a border is drawn around
        /// each cell. This is useful for testing border detection and trimming functionality.
        /// </para>
        /// </remarks>
        public Texture2D CreateTestSpriteSheet(
            int width,
            int height,
            int columns,
            int rows,
            int borderWidth = 0,
            Color? borderColor = null
        )
        {
            if (_disposed)
            {
                return null;
            }

            if (width <= 0 || height <= 0 || columns <= 0 || rows <= 0 || borderWidth < 0)
            {
                return null;
            }

            Texture2D texture = CreateBaseTexture(width, height, TextureFormat.RGBA32);
            if (texture == null)
            {
                return null;
            }

            Color actualBorderColor = borderColor ?? Color.clear;
            int cellWidth = width / columns;
            int cellHeight = height / rows;
            int totalCells = columns * rows;

            Color[] pixels = new Color[width * height];

            for (int row = 0; row < rows; row++)
            {
                int cellStartY = row * cellHeight;
                int cellEndY = (row == rows - 1) ? height : cellStartY + cellHeight;

                for (int col = 0; col < columns; col++)
                {
                    int cellIndex = row * columns + col;
                    float hue = (float)cellIndex / totalCells;
                    Color cellColor = Color.HSVToRGB(hue, 0.8f, 0.9f);

                    int cellStartX = col * cellWidth;
                    int cellEndX = (col == columns - 1) ? width : cellStartX + cellWidth;

                    for (int y = cellStartY; y < cellEndY; y++)
                    {
                        int rowOffset = y * width;
                        bool isVerticalBorder =
                            0 < borderWidth
                            && (y < cellStartY + borderWidth || cellEndY - borderWidth <= y);

                        for (int x = cellStartX; x < cellEndX; x++)
                        {
                            bool isHorizontalBorder =
                                0 < borderWidth
                                && (x < cellStartX + borderWidth || cellEndX - borderWidth <= x);

                            if (isVerticalBorder || isHorizontalBorder)
                            {
                                pixels[rowOffset + x] = actualBorderColor;
                            }
                            else
                            {
                                pixels[rowOffset + x] = cellColor;
                            }
                        }
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return texture;
        }

        /// <summary>
        /// Creates an in-memory texture using a custom pixel factory function.
        /// </summary>
        /// <param name="width">The width of the texture in pixels. Must be positive.</param>
        /// <param name="height">The height of the texture in pixels. Must be positive.</param>
        /// <param name="pixelFactory">A function that returns the color for each pixel given (x, y) coordinates.</param>
        /// <param name="format">The texture format. Defaults to <see cref="TextureFormat.RGBA32"/>.</param>
        /// <returns>A new texture with pixels set by the factory function, or null if parameters are invalid.</returns>
        /// <remarks>
        /// Provides maximum flexibility for creating test textures with arbitrary patterns.
        /// The pixel factory is called for each pixel with coordinates (x, y) where x is in [0, width)
        /// and y is in [0, height). Coordinates follow Unity's convention where y=0 is the bottom.
        /// </remarks>
        public Texture2D CreateTextureWithFactory(
            int width,
            int height,
            Func<int, int, Color> pixelFactory,
            TextureFormat format = TextureFormat.RGBA32
        )
        {
            if (_disposed)
            {
                return null;
            }

            if (width <= 0 || height <= 0 || pixelFactory == null)
            {
                return null;
            }

            Texture2D texture = CreateBaseTexture(width, height, format);
            if (texture == null)
            {
                return null;
            }

            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    pixels[rowOffset + x] = pixelFactory(x, y);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return texture;
        }

        /// <summary>
        /// Creates a sprite from an in-memory texture.
        /// Both the texture and sprite are tracked for cleanup.
        /// </summary>
        /// <param name="width">The width of the texture in pixels. Must be positive.</param>
        /// <param name="height">The height of the texture in pixels. Must be positive.</param>
        /// <param name="fillColor">The color to fill the texture with.</param>
        /// <param name="pixelsPerUnit">The number of pixels per world unit. Must be positive.</param>
        /// <param name="pivot">The pivot point of the sprite. Defaults to center (0.5, 0.5).</param>
        /// <returns>A new sprite backed by a solid color texture, or null if parameters are invalid.</returns>
        public Sprite CreateSprite(
            int width,
            int height,
            Color fillColor,
            float pixelsPerUnit = 100f,
            Vector2? pivot = null
        )
        {
            if (_disposed)
            {
                return null;
            }

            if (width <= 0 || height <= 0 || pixelsPerUnit <= 0f)
            {
                return null;
            }

            Texture2D texture = CreateSolidTexture(width, height, fillColor);
            if (texture == null)
            {
                return null;
            }

            Vector2 actualPivot = pivot ?? new Vector2(0.5f, 0.5f);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                actualPivot,
                pixelsPerUnit
            );

            if (sprite != null)
            {
                _trackedObjects.Add(sprite);
            }

            return sprite;
        }

        /// <summary>
        /// Creates a sprite from an existing texture with a specified region.
        /// The sprite is tracked for cleanup; the texture must be tracked separately if needed.
        /// </summary>
        /// <param name="texture">The source texture. Must not be null.</param>
        /// <param name="rect">The region of the texture to use for the sprite.</param>
        /// <param name="pixelsPerUnit">The number of pixels per world unit. Must be positive.</param>
        /// <param name="pivot">The pivot point of the sprite. Defaults to center (0.5, 0.5).</param>
        /// <returns>A new sprite from the texture region, or null if parameters are invalid.</returns>
        public Sprite CreateSpriteFromTexture(
            Texture2D texture,
            Rect rect,
            float pixelsPerUnit = 100f,
            Vector2? pivot = null
        )
        {
            if (_disposed)
            {
                return null;
            }

            if (texture == null || pixelsPerUnit <= 0f)
            {
                return null;
            }

            if (rect.x < 0 || rect.y < 0 || rect.width <= 0 || rect.height <= 0)
            {
                return null;
            }

            if (texture.width < rect.xMax || texture.height < rect.yMax)
            {
                return null;
            }

            Vector2 actualPivot = pivot ?? new Vector2(0.5f, 0.5f);
            Sprite sprite = Sprite.Create(texture, rect, actualPivot, pixelsPerUnit);

            if (sprite != null)
            {
                _trackedObjects.Add(sprite);
            }

            return sprite;
        }

        /// <summary>
        /// Tracks an externally created object for cleanup.
        /// </summary>
        /// <typeparam name="T">The type of object to track, must derive from <see cref="Object"/>.</typeparam>
        /// <param name="obj">The object to track. Null objects are ignored.</param>
        /// <returns>The same object that was passed in, for method chaining.</returns>
        public T Track<T>(T obj)
            where T : Object
        {
            if (obj != null)
            {
                _trackedObjects.Add(obj);
            }
            return obj;
        }

        /// <summary>
        /// Gets all tracked objects. Useful for integration with other cleanup systems.
        /// </summary>
        /// <param name="result">The list to populate with tracked objects. Cleared before populating.</param>
        public void GetTrackedObjects(List<Object> result)
        {
            if (result == null)
            {
                return;
            }

            result.Clear();
            foreach (UnityEngine.Object trackedObjectsElement in _trackedObjects)
            {
                result.Add(trackedObjectsElement);
            }
        }

        /// <summary>
        /// Destroys all tracked textures and sprites, releasing their memory.
        /// </summary>
        /// <remarks>
        /// This method can be called multiple times safely. After cleanup,
        /// the helper can still be used to create new textures.
        /// </remarks>
        public void Cleanup()
        {
            for (int i = _trackedObjects.Count - 1; 0 <= i; i--)
            {
                Object obj = _trackedObjects[i];
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }
            _trackedObjects.Clear();
        }

        /// <summary>
        /// Disposes this helper, cleaning up all tracked resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Cleanup();
            _disposed = true;
        }

        /// <summary>
        /// Creates a base texture with standard settings for testing.
        /// </summary>
        private Texture2D CreateBaseTexture(int width, int height, TextureFormat format)
        {
            if (_disposed)
            {
                return null;
            }

            Texture2D texture = new Texture2D(width, height, format, mipChain: false, linear: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            _trackedObjects.Add(texture);
            return texture;
        }
    }
}

// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestAssets
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Manages shared animation test fixtures with reference counting.
    /// Provides pooled Texture2D, Sprite, and AnimationClip instances for animation-related tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a centralized way to manage animation test fixtures that are shared
    /// across multiple test classes. Instead of creating textures and sprites at runtime per-test,
    /// it provides cached instances that can be reused across tests.
    /// </para>
    /// <para>
    /// Thread safety: Public methods that modify shared state (AcquireFixtures, ReleaseFixtures,
    /// PreloadAllAssets, etc.) use locks for correctness. The lazy-loading property getters for
    /// cached assets do NOT use locks; this is acceptable because Unity APIs must be called
    /// from the main thread anyway.
    /// </para>
    /// </remarks>
    public static class SharedAnimationTestFixtures
    {
        private const int DefaultSpritePoolSize = 20;
        private const int SpriteTextureSize = 4;

        private static readonly object Lock = new();
        private static int _referenceCount;
        private static bool _fixturesCreated;

        private static Texture2D _sharedSpriteTexture;

        private static readonly List<Sprite> CachedSprites = new(DefaultSpritePoolSize);

        private static AnimationClip _sharedEmptyClip;

        /// <summary>
        /// Gets the shared texture used for creating sprites. Created on first access.
        /// </summary>
        /// <remarks>
        /// The texture is a simple 4x4 RGBA32 texture suitable for basic animation tests.
        /// </remarks>
        public static Texture2D SharedSpriteTexture
        {
            get
            {
                if (_sharedSpriteTexture == null)
                {
                    _sharedSpriteTexture = new Texture2D(
                        SpriteTextureSize,
                        SpriteTextureSize,
                        TextureFormat.RGBA32,
                        false
                    );
                    _sharedSpriteTexture.name = "SharedAnimationTestTexture";
                }
                return _sharedSpriteTexture;
            }
        }

        /// <summary>
        /// Gets the current reference count for diagnostic purposes.
        /// </summary>
        public static int ReferenceCount
        {
            get
            {
                lock (Lock)
                {
                    return _referenceCount;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether fixtures have been created.
        /// </summary>
        public static bool FixturesCreated
        {
            get
            {
                lock (Lock)
                {
                    return _fixturesCreated;
                }
            }
        }

        /// <summary>
        /// Gets a cached sprite at the specified index. Creates sprites as needed.
        /// </summary>
        /// <param name="index">The index of the sprite to retrieve.</param>
        /// <returns>A cached sprite at the specified index.</returns>
        public static Sprite GetCachedSprite(int index)
        {
            EnsureSpritesCreated(index + 1);
            return CachedSprites[index];
        }

        /// <summary>
        /// Gets a list of cached sprites with the specified count.
        /// </summary>
        /// <param name="count">The number of sprites to retrieve.</param>
        /// <returns>A list containing the first <paramref name="count"/> cached sprites.</returns>
        public static List<Sprite> GetCachedSprites(int count)
        {
            EnsureSpritesCreated(count);
            return CachedSprites.GetRange(0, count);
        }

        /// <summary>
        /// Gets the shared empty animation clip. Created on first access.
        /// </summary>
        /// <returns>A shared animation clip that can be used as a template.</returns>
        public static AnimationClip GetEmptyClip()
        {
            if (_sharedEmptyClip == null)
            {
                _sharedEmptyClip = new AnimationClip { name = "SharedTestClip" };
            }
            return _sharedEmptyClip;
        }

        /// <summary>
        /// Creates a fresh animation clip with the specified frame rate.
        /// </summary>
        /// <param name="frameRate">The frame rate for the animation clip.</param>
        /// <returns>A new animation clip instance.</returns>
        /// <remarks>
        /// Unlike <see cref="GetEmptyClip"/>, this method creates a new instance each time.
        /// The caller is responsible for tracking and disposing the clip.
        /// </remarks>
        public static AnimationClip CreateAnimationClip(float frameRate = 24f)
        {
            return new AnimationClip { frameRate = frameRate, name = "TestAnimationClip" };
        }

        /// <summary>
        /// Acquires a reference to the shared fixtures. Creates fixtures if this is the first call.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call this method in your test fixture's <c>[OneTimeSetUp]</c> method. The method is
        /// thread-safe and uses reference counting to track how many consumers are using the fixtures.
        /// </para>
        /// </remarks>
        public static void AcquireFixtures()
        {
            lock (Lock)
            {
                _referenceCount++;
                if (!_fixturesCreated)
                {
                    CreateFixtures();
                    _fixturesCreated = true;
                }
            }
        }

        /// <summary>
        /// Releases a reference to the shared fixtures. Cleans up when the last reference is released.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call this method in your test fixture's <c>[OneTimeTearDown]</c> method. The method is
        /// thread-safe and uses reference counting to determine when to clean up.
        /// </para>
        /// </remarks>
        public static void ReleaseFixtures()
        {
            lock (Lock)
            {
                _referenceCount--;
                if (_referenceCount <= 0)
                {
                    CleanupFixtures();
                    _referenceCount = 0;
                    _fixturesCreated = false;
                }
            }
        }

        /// <summary>
        /// Preloads all assets into cache. Call this from assembly-level setup to ensure
        /// all assets are loaded once before any tests run.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe and can be called multiple times safely.
        /// It will create the shared texture, pre-create sprites, and prepare the shared clip.
        /// </remarks>
        public static void PreloadAllAssets()
        {
            lock (Lock)
            {
                if (!_fixturesCreated)
                {
                    AcquireFixtures();
                }

                _ = SharedSpriteTexture;
                EnsureSpritesCreated(DefaultSpritePoolSize);
                _ = GetEmptyClip();
            }
        }

        /// <summary>
        /// Releases all cached assets. Call this from assembly-level teardown.
        /// </summary>
        /// <remarks>
        /// This destroys all cached assets and resets the fixture state.
        /// </remarks>
        public static void ReleaseAllCachedAssets()
        {
            lock (Lock)
            {
                CleanupFixtures();
                _referenceCount = 0;
                _fixturesCreated = false;
            }
        }

        /// <summary>
        /// Forces cleanup of fixture references regardless of reference count.
        /// </summary>
        public static void ForceCleanup()
        {
            lock (Lock)
            {
                CleanupFixtures();
                _referenceCount = 0;
                _fixturesCreated = false;
            }
        }

        private static void CreateFixtures()
        {
            _ = SharedSpriteTexture;

            EnsureSpritesCreated(10);
        }

        private static void EnsureSpritesCreated(int count)
        {
            while (CachedSprites.Count < count)
            {
                Sprite sprite = Sprite.Create(
                    SharedSpriteTexture,
                    new Rect(0, 0, SpriteTextureSize, SpriteTextureSize),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect
                );
                sprite.name = $"CachedSprite_{CachedSprites.Count:D3}";
                CachedSprites.Add(sprite);
            }
        }

        private static void CleanupFixtures()
        {
            foreach (Sprite sprite in CachedSprites)
            {
                if (sprite != null)
                {
                    Object.DestroyImmediate(sprite);
                }
            }
            CachedSprites.Clear();

            if (_sharedSpriteTexture != null)
            {
                Object.DestroyImmediate(_sharedSpriteTexture);
                _sharedSpriteTexture = null;
            }

            if (_sharedEmptyClip != null)
            {
                Object.DestroyImmediate(_sharedEmptyClip);
                _sharedEmptyClip = null;
            }
        }
    }
#endif
}

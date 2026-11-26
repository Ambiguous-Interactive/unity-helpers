// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using File = System.IO.File;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DurableFileSidecarMoveTests
    {
        private static readonly string[] Suffixes = { "-wal", "-shm", "-journal" };

        private string _directory;
        private string _source;
        private string _destination;

        internal static string GetTemporaryRoot()
        {
#if NET9_0_OR_GREATER
            return ResolveTemporaryDirectory(new DirectoryInfo(Path.GetTempPath()));
#else
            return UnityEngine.Application.temporaryCachePath;
#endif
        }

#if NET9_0_OR_GREATER
        private static string ResolveTemporaryDirectory(DirectoryInfo directory)
        {
            DirectoryInfo parent = directory.Parent;
            if (parent == null)
            {
                return directory.FullName;
            }

            string resolvedParent = ResolveTemporaryDirectory(parent);
            DirectoryInfo candidate = new(Path.Combine(resolvedParent, directory.Name));
            FileSystemInfo target = candidate.ResolveLinkTarget(returnFinalTarget: true);
            return target == null
                ? candidate.FullName
                : ResolveTemporaryDirectory(new DirectoryInfo(target.FullName));
        }
#endif

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(GetTemporaryRoot(), $"durable-sidecars-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            _source = Path.Combine(_directory, "save.db");
            _destination = Path.Combine(_directory, "save.damaged");
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_directory, recursive: true);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RefusesGroupWhileAnyMemberHasAnActiveWriter(bool lockSource, bool lockSidecar)
        {
            string sourceSidecar = _source + Suffixes[0];
            string destinationSidecar = _destination + Suffixes[0];
            File.WriteAllText(_source, "database");
            File.WriteAllText(sourceSidecar, "journal");
            string lockedPath = lockSource ? _source : _destination;
            if (lockSidecar)
            {
                lockedPath += Suffixes[0];
            }

            Assert.IsTrue(
                DurableFile.TryOpenStagingOwnership(
                    lockedPath + DurableFile.TemporarySuffix,
                    out FileStream writer,
                    out Exception lockError
                ),
                lockError?.ToString()
            );
            using (writer)
            {
                Assert.IsFalse(
                    DurableFile.TryMoveWithSidecars(
                        _source,
                        _destination,
                        Suffixes,
                        out Exception error
                    )
                );
                Assert.IsInstanceOf<IOException>(error);
                Assert.AreEqual("database", File.ReadAllText(_source));
                Assert.AreEqual("journal", File.ReadAllText(sourceSidecar));
                Assert.IsFalse(File.Exists(_destination));
                Assert.IsFalse(File.Exists(destinationSidecar));
            }

            Assert.IsTrue(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    Suffixes,
                    out Exception retryError
                ),
                retryError?.ToString()
            );
            Assert.AreEqual("database", File.ReadAllText(_destination));
            Assert.AreEqual("journal", File.ReadAllText(destinationSidecar));
            CollectionAssert.AreEquivalent(
                new[] { _destination, destinationSidecar },
                Directory.GetFiles(_directory)
            );
        }

        [Test]
        public void RefusesToMoveAFileIntoItsOwnWriterLock()
        {
            File.WriteAllText(_source, "database");
            string lockPath = _source + DurableFile.TemporarySuffix + DurableFile.OwnershipSuffix;

            Assert.IsFalse(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    lockPath,
                    Array.Empty<string>(),
                    out Exception error
                )
            );
            Assert.IsInstanceOf<ArgumentException>(error);
            Assert.AreEqual("database", File.ReadAllText(_source));
            Assert.IsFalse(File.Exists(lockPath));
        }

        [Test]
        public void MovesDatabaseAndPresentSidecars()
        {
            File.WriteAllText(_source, "database");
            File.WriteAllText(_source + "-wal", "write-ahead");
            File.WriteAllText(_source + "-journal", "journal");

            Assert.IsTrue(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    Suffixes,
                    out Exception error
                )
            );
            Assert.IsTrue(error == null);
            Assert.AreEqual("database", File.ReadAllText(_destination));
            Assert.AreEqual("write-ahead", File.ReadAllText(_destination + "-wal"));
            Assert.AreEqual("journal", File.ReadAllText(_destination + "-journal"));
            Assert.IsFalse(File.Exists(_source));
            foreach (string suffix in Suffixes)
            {
                Assert.IsFalse(File.Exists(_source + suffix));
            }
            Assert.IsFalse(File.Exists(_destination + "-shm"));
        }

        [TestCase("")]
        [TestCase("-wal")]
        [TestCase("-shm")]
        [TestCase("-journal")]
        public void DestinationCollisionRestoresEveryCompletedMove(string occupiedSuffix)
        {
            File.WriteAllText(_source, "database");
            foreach (string suffix in Suffixes)
            {
                File.WriteAllText(_source + suffix, suffix);
            }
            File.WriteAllText(_destination + occupiedSuffix, "existing destination");

            Assert.IsFalse(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    Suffixes,
                    out Exception error
                )
            );
            Assert.IsInstanceOf<IOException>(error);
            Assert.AreEqual("database", File.ReadAllText(_source));
            Assert.AreEqual(
                "existing destination",
                File.ReadAllText(_destination + occupiedSuffix)
            );
            if (occupiedSuffix.Length != 0)
            {
                Assert.IsFalse(File.Exists(_destination));
            }
            foreach (string suffix in Suffixes)
            {
                Assert.AreEqual(suffix, File.ReadAllText(_source + suffix));
                if (!string.Equals(suffix, occupiedSuffix, StringComparison.Ordinal))
                {
                    Assert.IsFalse(File.Exists(_destination + suffix));
                }
            }
        }

        [Test]
        public void RefusesAnOrphanedDestinationSidecar()
        {
            File.WriteAllText(_source, "database");
            File.WriteAllText(_destination + "-wal", "unrelated journal");

            Assert.IsFalse(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    Suffixes,
                    out Exception error
                )
            );
            Assert.IsInstanceOf<IOException>(error);
            Assert.AreEqual("database", File.ReadAllText(_source));
            Assert.AreEqual("unrelated journal", File.ReadAllText(_destination + "-wal"));
            Assert.IsFalse(File.Exists(_destination));
        }

        [Test]
        public void RefusesDirectorySidecarAndRestoresDatabase()
        {
            File.WriteAllText(_source, "database");
            Directory.CreateDirectory(_source + "-wal");
            File.WriteAllText(Path.Combine(_source + "-wal", "marker"), "keep");

            Assert.IsFalse(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    Suffixes,
                    out Exception error
                )
            );
            Assert.IsInstanceOf<IOException>(error);
            Assert.AreEqual("database", File.ReadAllText(_source));
            Assert.AreEqual("keep", File.ReadAllText(Path.Combine(_source + "-wal", "marker")));
            Assert.IsFalse(File.Exists(_destination));
            Assert.IsFalse(Directory.Exists(_destination + "-wal"));
        }

        [Test]
        public void DoesNotMoveSidecarsWhenTheDatabaseIsMissing()
        {
            File.WriteAllText(_source + "-wal", "write-ahead");

            Assert.IsFalse(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    Suffixes,
                    out Exception error
                )
            );
            Assert.IsInstanceOf<FileNotFoundException>(error);
            Assert.AreEqual("write-ahead", File.ReadAllText(_source + "-wal"));
            Assert.IsFalse(File.Exists(_destination + "-wal"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("/escape")]
        [TestCase("\\escape")]
        public void InvalidSuffixDoesNotMoveAnyFile(string suffix)
        {
            File.WriteAllText(_source, "database");
            File.WriteAllText(_source + "-wal", "write-ahead");

            Assert.IsFalse(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    new[] { "-wal", suffix },
                    out Exception error
                )
            );
            Assert.IsInstanceOf<ArgumentException>(error);
            Assert.AreEqual("database", File.ReadAllText(_source));
            Assert.AreEqual("write-ahead", File.ReadAllText(_source + "-wal"));
            Assert.IsFalse(File.Exists(_destination));
        }

        [Test]
        public void RefusesDuplicateAndAliasedGroupPaths()
        {
            File.WriteAllText(_source, "database");
            File.WriteAllText(_source + "-wal", "write-ahead");

            Assert.IsFalse(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    new[] { "-wal", "-WAL" },
                    out Exception duplicate
                )
            );
            Assert.IsInstanceOf<ArgumentException>(duplicate);
            Assert.IsFalse(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _source + "-wal",
                    new[] { "-wal" },
                    out Exception alias
                )
            );
            Assert.IsInstanceOf<ArgumentException>(alias);
            Assert.AreEqual("database", File.ReadAllText(_source));
            Assert.AreEqual("write-ahead", File.ReadAllText(_source + "-wal"));
            Assert.IsFalse(File.Exists(_destination));
        }

        [Test]
        public void EmptySidecarListMovesOnlyTheDatabase()
        {
            File.WriteAllText(_source, "database");
            File.WriteAllText(_source + "-wal", "write-ahead");

            Assert.IsTrue(
                DurableFile.TryMoveWithSidecars(
                    _source,
                    _destination,
                    ReadOnlySpan<string>.Empty,
                    out Exception error
                )
            );
            Assert.IsTrue(error == null);
            Assert.AreEqual("database", File.ReadAllText(_destination));
            Assert.AreEqual("write-ahead", File.ReadAllText(_source + "-wal"));
            Assert.IsFalse(File.Exists(_destination + "-wal"));
        }
    }
}

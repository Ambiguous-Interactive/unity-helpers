// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using ProtoBuf;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Additional tests for Serializer methods that were not covered by existing test files.
    /// Tests BinarySerialize/Deserialize, generic Serialize/Deserialize, and file I/O methods.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializerAdditionalTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Serializable]
        [ProtoContract]
        private sealed class TestMessage
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }

            [ProtoMember(3)]
            public List<int> Values { get; set; }
        }

        [Serializable]
        [ProtoContract]
        private sealed class ComplexMessage
        {
            [ProtoMember(1)]
            public int Integer { get; set; }

            [ProtoMember(2)]
            public double Double { get; set; }

            [ProtoMember(3)]
            public string Text { get; set; }

            [ProtoMember(4)]
            public byte[] Data { get; set; }

            [ProtoMember(5)]
            public List<string> StringList { get; set; }

            [ProtoMember(6)]
            public Dictionary<string, int> Dictionary { get; set; }
        }

        [Test]
        public void BinarySerializeSimpleObjectReturnsValidByteArray()
        {
            TestMessage msg = new()
            {
                Id = 42,
                Name = "Test",
                Values = new List<int> { 1, 2, 3 },
            };

            byte[] serialized = Serializer.BinarySerialize(msg);

            Assert.NotNull(serialized);
            Assert.Greater(serialized.Length, 0);
        }

        [Test]
        public void BinaryDeserializeValidDataReturnsCorrectObject()
        {
            TestMessage original = new()
            {
                Id = 123,
                Name = "Binary Test",
                Values = new List<int> { 10, 20, 30 },
            };

            byte[] serialized = Serializer.BinarySerialize(original);
            TestMessage deserialized = Serializer.BinaryDeserialize<TestMessage>(serialized);

            Assert.NotNull(deserialized);
            Assert.AreEqual(original.Id, deserialized.Id);
            Assert.AreEqual(original.Name, deserialized.Name);
            CollectionAssert.AreEqual(original.Values, deserialized.Values);
        }

        [Test]
        public void BinarySerializeWithBufferReusesBuffer()
        {
            TestMessage msg = new()
            {
                Id = 1,
                Name = "Test",
                Values = new List<int> { 1 },
            };
            byte[] buffer = null;

            int length = Serializer.BinarySerialize(msg, ref buffer);

            Assert.NotNull(buffer);
            Assert.Greater(length, 0);
            Assert.GreaterOrEqual(buffer.Length, length);
        }

        [Test]
        public void BinarySerializeMultipleCallsWithSameBufferNoDataCorruption()
        {
            TestMessage msg1 = new()
            {
                Id = 111,
                Name = "First",
                Values = new List<int> { 1, 2 },
            };
            TestMessage msg2 = new()
            {
                Id = 222,
                Name = "Second",
                Values = new List<int> { 3, 4 },
            };
            byte[] buffer = null;

            int len1 = Serializer.BinarySerialize(msg1, ref buffer);
            byte[] data1 = buffer.Take(len1).ToArray();

            int len2 = Serializer.BinarySerialize(msg2, ref buffer);
            byte[] data2 = buffer.Take(len2).ToArray();

            TestMessage clone1 = Serializer.BinaryDeserialize<TestMessage>(data1);
            TestMessage clone2 = Serializer.BinaryDeserialize<TestMessage>(data2);

            Assert.AreEqual(msg1.Id, clone1.Id);
            Assert.AreEqual(msg1.Name, clone1.Name);
            Assert.AreEqual(msg2.Id, clone2.Id);
            Assert.AreEqual(msg2.Name, clone2.Name);
        }

        [Test]
        public void BinaryRoundTripNullValuesHandledCorrectly()
        {
            TestMessage msg = new()
            {
                Id = 5,
                Name = null,
                Values = null,
            };

            byte[] serialized = Serializer.BinarySerialize(msg);
            TestMessage deserialized = Serializer.BinaryDeserialize<TestMessage>(serialized);

            Assert.AreEqual(msg.Id, deserialized.Id);
            Assert.IsTrue(
                deserialized.Name == null,
                "Name should be null after round-trip of null value"
            );
            Assert.IsTrue(
                deserialized.Values == null,
                "Values should be null after round-trip of null value"
            );
        }

        [Test]
        public void BinaryRoundTripEmptyCollectionsPreservedCorrectly()
        {
            TestMessage msg = new()
            {
                Id = 7,
                Name = string.Empty,
                Values = new List<int>(),
            };

            byte[] serialized = Serializer.BinarySerialize(msg);
            TestMessage deserialized = Serializer.BinaryDeserialize<TestMessage>(serialized);

            Assert.AreEqual(msg.Id, deserialized.Id);
            Assert.AreEqual(string.Empty, deserialized.Name);
            Assert.NotNull(deserialized.Values);
            Assert.IsEmpty(deserialized.Values);
        }

        [Test]
        public void BinaryRoundTripComplexObjectAllFieldsCorrect()
        {
            ComplexMessage msg = new()
            {
                Integer = int.MaxValue,
                Double = Math.PI,
                Text = "Complex test with unicode: 世界 🌍",
                Data = new byte[] { 1, 2, 3, 255, 0, 128 },
                StringList = new List<string> { "a", "b", null, "", "c" },
                Dictionary = new Dictionary<string, int>
                {
                    ["one"] = 1,
                    ["two"] = 2,
                    ["three"] = 3,
                },
            };

            byte[] serialized = Serializer.BinarySerialize(msg);
            ComplexMessage deserialized = Serializer.BinaryDeserialize<ComplexMessage>(serialized);

            Assert.AreEqual(msg.Integer, deserialized.Integer);
            Assert.AreEqual(msg.Double, deserialized.Double);
            Assert.AreEqual(msg.Text, deserialized.Text);
            CollectionAssert.AreEqual(msg.Data, deserialized.Data);
            CollectionAssert.AreEqual(msg.StringList, deserialized.StringList);
            CollectionAssert.AreEqual(msg.Dictionary, deserialized.Dictionary);
        }

        [Test]
        public void BinaryRoundTripLargeDataHandledCorrectly()
        {
            ComplexMessage msg = new()
            {
                Integer = 999,
                Double = 3.14159,
                Text = new string('X', 50_000),
                Data = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 256)).ToArray(),
                StringList = Enumerable.Range(0, 1_000).Select(i => $"Item_{i}").ToList(),
                Dictionary = Enumerable.Range(0, 500).ToDictionary(i => $"Key_{i}", i => i * 2),
            };

            byte[] serialized = Serializer.BinarySerialize(msg);
            ComplexMessage deserialized = Serializer.BinaryDeserialize<ComplexMessage>(serialized);

            Assert.AreEqual(msg.Integer, deserialized.Integer);
            Assert.AreEqual(msg.Text.Length, deserialized.Text.Length);
            Assert.AreEqual(msg.Data.Length, deserialized.Data.Length);
            Assert.AreEqual(msg.StringList.Count, deserialized.StringList.Count);
            Assert.AreEqual(msg.Dictionary.Count, deserialized.Dictionary.Count);
        }

        [Test]
        public void GenericSerializeSystemBinaryDelegatesToBinarySerialize()
        {
            TestMessage msg = new()
            {
                Id = 100,
                Name = "Binary",
                Values = new List<int> { 1 },
            };

#pragma warning disable CS0618 // Type or member is obsolete
            byte[] result = Serializer.Serialize(msg, SerializationType.SystemBinary);
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.NotNull(result);
            Assert.Greater(result.Length, 0);

            TestMessage deserialized = Serializer.Deserialize<TestMessage>(
                result,
#pragma warning disable CS0618 // Type or member is obsolete
                SerializationType.SystemBinary
#pragma warning restore CS0618 // Type or member is obsolete
            );
            Assert.AreEqual(msg.Id, deserialized.Id);
            Assert.AreEqual(msg.Name, deserialized.Name);
        }

        [Test]
        public void GenericSerializeProtobufDelegatesToProtoSerialize()
        {
            TestMessage msg = new()
            {
                Id = 200,
                Name = "Proto",
                Values = new List<int> { 2 },
            };

            byte[] result = Serializer.Serialize(msg, SerializationType.Protobuf);

            Assert.NotNull(result);
            Assert.Greater(result.Length, 0);

            TestMessage deserialized = Serializer.Deserialize<TestMessage>(
                result,
                SerializationType.Protobuf
            );
            Assert.AreEqual(msg.Id, deserialized.Id);
            Assert.AreEqual(msg.Name, deserialized.Name);
        }

        [Test]
        public void GenericSerializeJsonDelegatesToJsonSerialize()
        {
            TestMessage msg = new()
            {
                Id = 300,
                Name = "Json",
                Values = new List<int> { 3 },
            };

            byte[] result = Serializer.Serialize(msg, SerializationType.Json);

            Assert.NotNull(result);
            Assert.Greater(result.Length, 0);

            TestMessage deserialized = Serializer.Deserialize<TestMessage>(
                result,
                SerializationType.Json
            );
            Assert.AreEqual(msg.Id, deserialized.Id);
            Assert.AreEqual(msg.Name, deserialized.Name);
        }

        [Test]
        public void GenericSerializeInvalidSerializationTypeThrowsException()
        {
            TestMessage msg = new() { Id = 1, Name = "Test" };

            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.Serialize(msg, (SerializationType)999)
            );
        }

        [Test]
        public void GenericDeserializeInvalidSerializationTypeThrowsException()
        {
            byte[] data = { 1, 2, 3 };

            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.Deserialize<TestMessage>(data, (SerializationType)999)
            );
        }

        [Test]
        public void GenericSerializeNoneTypeThrowsException()
        {
            TestMessage msg = new() { Id = 1, Name = "Test" };

            Assert.Throws<SerializationConfigurationException>(() =>
#pragma warning disable CS0618 // Type or member is obsolete
                Serializer.Serialize(msg, SerializationType.None)
#pragma warning restore CS0618 // Type or member is obsolete
            );
        }

        [Test]
        public void GenericDeserializeNoneTypeThrowsException()
        {
            byte[] data = { 1, 2, 3 };

            Assert.Throws<SerializationConfigurationException>(() =>
#pragma warning disable CS0618 // Type or member is obsolete
                Serializer.Deserialize<TestMessage>(data, SerializationType.None)
#pragma warning restore CS0618 // Type or member is obsolete
            );
        }

        [Test]
        public void GenericSerializeWithBufferSystemBinaryWorksCorrectly()
        {
            TestMessage msg = new() { Id = 111, Name = "BufferBinary" };
            byte[] buffer = null;

#pragma warning disable CS0618 // Type or member is obsolete
            int length = Serializer.Serialize(msg, SerializationType.SystemBinary, ref buffer);
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.Greater(length, 0);
            Assert.NotNull(buffer);
            Assert.GreaterOrEqual(buffer.Length, length);

            byte[] data = buffer.Take(length).ToArray();
            TestMessage deserialized = Serializer.Deserialize<TestMessage>(
                data,
#pragma warning disable CS0618 // Type or member is obsolete
                SerializationType.SystemBinary
#pragma warning restore CS0618 // Type or member is obsolete
            );
            Assert.AreEqual(msg.Id, deserialized.Id);
        }

        [Test]
        public void GenericSerializeWithBufferProtobufWorksCorrectly()
        {
            TestMessage msg = new() { Id = 222, Name = "BufferProto" };
            byte[] buffer = null;

            int length = Serializer.Serialize(msg, SerializationType.Protobuf, ref buffer);

            Assert.Greater(length, 0);
            Assert.NotNull(buffer);

            byte[] data = buffer.Take(length).ToArray();
            TestMessage deserialized = Serializer.Deserialize<TestMessage>(
                data,
                SerializationType.Protobuf
            );
            Assert.AreEqual(msg.Id, deserialized.Id);
        }

        [Test]
        public void GenericSerializeWithBufferJsonWorksCorrectly()
        {
            TestMessage msg = new() { Id = 333, Name = "BufferJson" };
            byte[] buffer = null;

            int length = Serializer.Serialize(msg, SerializationType.Json, ref buffer);

            Assert.Greater(length, 0);
            Assert.NotNull(buffer);

            byte[] data = buffer.Take(length).ToArray();
            TestMessage deserialized = Serializer.Deserialize<TestMessage>(
                data,
                SerializationType.Json
            );
            Assert.AreEqual(msg.Id, deserialized.Id);
        }

        [Test]
        public void GenericSerializeWithBufferInvalidTypeThrowsException()
        {
            TestMessage msg = new() { Id = 1 };
            byte[] buffer = null;

            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.Serialize(msg, (SerializationType)999, ref buffer)
            );
        }

        [Test]
        public void GenericRoundTripAllTypesProduceConsistentResults()
        {
            TestMessage msg = new()
            {
                Id = 555,
                Name = "AllTypes",
                Values = new List<int> { 5, 10, 15 },
            };

            foreach (
                SerializationType type in new[]
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    SerializationType.SystemBinary,
#pragma warning restore CS0618 // Type or member is obsolete
                    SerializationType.Protobuf,
                    SerializationType.Json,
                }
            )
            {
                byte[] serialized = Serializer.Serialize(msg, type);
                TestMessage deserialized = Serializer.Deserialize<TestMessage>(serialized, type);

                Assert.AreEqual(msg.Id, deserialized.Id, $"Failed for {type}");
                Assert.AreEqual(msg.Name, deserialized.Name, $"Failed for {type}");
                CollectionAssert.AreEqual(msg.Values, deserialized.Values, $"Failed for {type}");
            }
        }

        [Test]
        public void WriteToJsonFileSimpleObjectCreatesFile()
        {
            string filePath = Path.Combine(_tempDirectory, "test.json");
            TestMessage msg = new()
            {
                Id = 1,
                Name = "FileTest",
                Values = new List<int> { 1, 2 },
            };

            Serializer.WriteToJsonFile(msg, filePath);

            Assert.IsTrue(File.Exists(filePath));
            string content = File.ReadAllText(filePath);
            Assert.IsNotEmpty(content);
        }

        [Test]
        public void ReadFromJsonFileExistingFileReturnsCorrectObject()
        {
            string filePath = Path.Combine(_tempDirectory, "read_test.json");
            TestMessage original = new()
            {
                Id = 42,
                Name = "ReadTest",
                Values = new List<int> { 10, 20, 30 },
            };

            Serializer.WriteToJsonFile(original, filePath);
            TestMessage loaded = Serializer.ReadFromJsonFile<TestMessage>(filePath);

            Assert.NotNull(loaded);
            Assert.AreEqual(original.Id, loaded.Id);
            Assert.AreEqual(original.Name, loaded.Name);
            CollectionAssert.AreEqual(original.Values, loaded.Values);
        }

        [Test]
        public void WriteToJsonFilePrettyTrueFormatsWithIndentation()
        {
            string filePath = Path.Combine(_tempDirectory, "pretty.json");
            TestMessage msg = new() { Id = 1, Name = "Pretty" };

            Serializer.WriteToJsonFile(msg, filePath, pretty: true);

            string content = File.ReadAllText(filePath);
            Assert.IsTrue(
                content.Contains("\n") || content.Contains("\r"),
                "Pretty printed JSON should contain newlines"
            );
        }

        [Test]
        public void WriteToJsonFilePrettyFalseFormatsCompact()
        {
            string filePath = Path.Combine(_tempDirectory, "compact.json");
            TestMessage msg = new() { Id = 1, Name = "Compact" };

            Serializer.WriteToJsonFile(msg, filePath, pretty: false);

            string content = File.ReadAllText(filePath);
            // Compact format should have fewer or no newlines
            Assert.IsNotEmpty(content);
        }

        [Test]
        public void WriteToJsonFileWithCustomOptionsUsesProvidedOptions()
        {
            string filePath = Path.Combine(_tempDirectory, "custom.json");
            TestMessage msg = new() { Id = 1, Name = "Custom" };

            JsonSerializerOptions options = new() { WriteIndented = true };

            Serializer.WriteToJsonFile(msg, filePath, options);

            Assert.IsTrue(File.Exists(filePath));
            string content = File.ReadAllText(filePath);
            Assert.IsNotEmpty(content);
        }

        [Test]
        public void ReadFromJsonFileNonExistentFileThrowsException()
        {
            string filePath = Path.Combine(_tempDirectory, "nonexistent.json");

            Assert.Throws<FileNotFoundException>(() =>
                Serializer.ReadFromJsonFile<TestMessage>(filePath)
            );
        }

        [Test]
        public void WriteToJsonFileNullObjectWritesEmptyObject()
        {
            string filePath = Path.Combine(_tempDirectory, "null.json");

            Serializer.WriteToJsonFile<TestMessage>(null, filePath);

            Assert.IsTrue(File.Exists(filePath));
            string content = File.ReadAllText(filePath);
            Assert.IsNotEmpty(content);
        }

        [Test]
        public void WriteToJsonFileComplexObjectPreservesAllData()
        {
            string filePath = Path.Combine(_tempDirectory, "complex.json");
            ComplexMessage msg = new()
            {
                Integer = 999,
                Double = 3.14159,
                Text = "Complex with unicode: 世界 🌍",
                Data = new byte[] { 1, 2, 3, 4, 5 },
                StringList = new List<string> { "a", "b", "c" },
                Dictionary = new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 },
            };

            Serializer.WriteToJsonFile(msg, filePath);
            ComplexMessage loaded = Serializer.ReadFromJsonFile<ComplexMessage>(filePath);

            Assert.AreEqual(msg.Integer, loaded.Integer);
            Assert.AreEqual(msg.Double, loaded.Double);
            Assert.AreEqual(msg.Text, loaded.Text);
            CollectionAssert.AreEqual(msg.Data, loaded.Data);
            CollectionAssert.AreEqual(msg.StringList, loaded.StringList);
            CollectionAssert.AreEqual(msg.Dictionary, loaded.Dictionary);
        }

        [UnityTest]
        public IEnumerator WriteToJsonFileAsyncSimpleObjectCreatesFile()
        {
            string filePath = Path.Combine(_tempDirectory, "async_test.json");
            TestMessage msg = new()
            {
                Id = 1,
                Name = "AsyncTest",
                Values = new List<int> { 1, 2 },
            };

            Task serializeTask = Serializer.WriteToJsonFileAsync(msg, filePath);
            while (!serializeTask.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(File.Exists(filePath));
            Task<string> readTask = File.ReadAllTextAsync(filePath);
            while (!readTask.IsCompleted)
            {
                yield return null;
            }

            string content = readTask.Result;
            Assert.IsNotEmpty(content);
        }

        [UnityTest]
        public IEnumerator ReadFromJsonFileAsyncExistingFileReturnsCorrectObject()
        {
            string filePath = Path.Combine(_tempDirectory, "async_read_test.json");
            TestMessage original = new()
            {
                Id = 42,
                Name = "AsyncReadTest",
                Values = new List<int> { 10, 20, 30 },
            };

            Task writerTask = Serializer.WriteToJsonFileAsync(original, filePath);
            while (!writerTask.IsCompleted)
            {
                yield return null;
            }

            Task<TestMessage> readerTask = Serializer.ReadFromJsonFileAsync<TestMessage>(filePath);
            while (!readerTask.IsCompleted)
            {
                yield return null;
            }

            TestMessage loaded = readerTask.Result;

            Assert.NotNull(loaded);
            Assert.AreEqual(original.Id, loaded.Id);
            Assert.AreEqual(original.Name, loaded.Name);
            CollectionAssert.AreEqual(original.Values, loaded.Values);
        }

        [UnityTest]
        public IEnumerator WriteToJsonFileAsyncPrettyTrueFormatsWithIndentation()
        {
            string filePath = Path.Combine(_tempDirectory, "async_pretty.json");
            TestMessage msg = new() { Id = 1, Name = "AsyncPretty" };

            Task writerTask = Serializer.WriteToJsonFileAsync(msg, filePath, pretty: true);
            while (!writerTask.IsCompleted)
            {
                yield return null;
            }

            Task<string> readerTask = File.ReadAllTextAsync(filePath);
            while (!readerTask.IsCompleted)
            {
                yield return null;
            }

            string content = readerTask.Result;
            Assert.IsTrue(
                content.Contains("\n") || content.Contains("\r"),
                "Pretty printed JSON should contain newlines"
            );
        }

        [UnityTest]
        public IEnumerator WriteToJsonFileAsyncWithCustomOptionsUsesProvidedOptions()
        {
            string filePath = Path.Combine(_tempDirectory, "async_custom.json");
            TestMessage msg = new() { Id = 1, Name = "AsyncCustom" };

            JsonSerializerOptions options = new() { WriteIndented = true };

            Task writerTask = Serializer.WriteToJsonFileAsync(msg, filePath, options);
            while (!writerTask.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(File.Exists(filePath));
            Task<string> readerTask = File.ReadAllTextAsync(filePath);
            while (!readerTask.IsCompleted)
            {
                yield return null;
            }

            string content = readerTask.Result;
            Assert.IsNotEmpty(content);
        }

        [UnityTest]
        public IEnumerator ReadFromJsonFileAsyncNonExistentFileThrowsException()
        {
            string filePath = Path.Combine(_tempDirectory, "async_nonexistent.json");

            Task<TestMessage> readerTask = Serializer.ReadFromJsonFileAsync<TestMessage>(filePath);
            while (!readerTask.IsCompleted)
            {
                yield return null;
            }

            Assert.IsTrue(readerTask.IsFaulted);

            try
            {
                _ = readerTask.Result;
            }
            catch (FileNotFoundException)
            {
                // pass
            }
            catch (AggregateException e)
            {
                List<Exception> innerExceptions = e.Flatten().InnerExceptions.ToList();
                Assert.IsTrue(
                    innerExceptions.Any(inner => inner is FileNotFoundException),
                    string.Join(",", innerExceptions.Select(inner => inner.GetType().Name))
                );
            }
        }

        [UnityTest]
        public IEnumerator FileIORoundTripMultipleOperationsNoInterference()
        {
            string file1 = Path.Combine(_tempDirectory, "file1.json");
            string file2 = Path.Combine(_tempDirectory, "file2.json");

            TestMessage msg1 = new() { Id = 111, Name = "File1" };
            TestMessage msg2 = new() { Id = 222, Name = "File2" };

            Task writerTask = Serializer.WriteToJsonFileAsync(msg1, file1);
            while (!writerTask.IsCompleted)
            {
                yield return null;
            }

            writerTask = Serializer.WriteToJsonFileAsync(msg2, file2);
            while (!writerTask.IsCompleted)
            {
                yield return null;
            }

            Task<TestMessage> readerTask1 = Serializer.ReadFromJsonFileAsync<TestMessage>(file1);
            while (!readerTask1.IsCompleted)
            {
                yield return null;
            }
            TestMessage loaded1 = readerTask1.Result;
            Task<TestMessage> readerTask2 = Serializer.ReadFromJsonFileAsync<TestMessage>(file2);
            while (!readerTask2.IsCompleted)
            {
                yield return null;
            }
            TestMessage loaded2 = readerTask2.Result;

            Assert.AreEqual(msg1.Id, loaded1.Id);
            Assert.AreEqual(msg1.Name, loaded1.Name);
            Assert.AreEqual(msg2.Id, loaded2.Id);
            Assert.AreEqual(msg2.Name, loaded2.Name);
        }

        [Test]
        public void BinaryDeserializeEmptyArrayThrowsException()
        {
            byte[] emptyData = Array.Empty<byte>();

            // Empty input is an input-contract violation (InputValidation stage).
            Assert.Throws<SerializationInputException>(() =>
                Serializer.BinaryDeserialize<TestMessage>(emptyData)
            );
        }

        [Test]
        public void BinaryDeserializeCorruptedDataThrowsException()
        {
            byte[] corruptedData = { 0xFF, 0xFF, 0xFF, 0xFF };

            // Corrupt (non-null, non-empty) payload surfaces as CorruptDataException with the
            // underlying codec failure preserved as InnerException.
            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.BinaryDeserialize<TestMessage>(corruptedData)
            );
        }

        [Test]
        public void ProtoDeserializeNullDataThrowsException()
        {
            Assert.Throws<SerializationInputException>(() =>
                Serializer.ProtoDeserialize<TestMessage>(null)
            );
        }

        [Test]
        public void ProtoDeserializeEmptyArrayThrowsInputException()
        {
            byte[] emptyData = Array.Empty<byte>();

            Assert.Throws<SerializationInputException>(() =>
                Serializer.ProtoDeserialize<TestMessage>(emptyData)
            );
        }

        [Test]
        public void ProtoDeserializeWithTypeNullDataThrowsException()
        {
            // Null payload is an input-contract violation (InputValidation stage).
            Assert.Throws<SerializationInputException>(() =>
                Serializer.ProtoDeserialize<object>(null, typeof(TestMessage))
            );
        }

        [Test]
        public void ProtoDeserializeWithTypeNullTypeThrowsException()
        {
            byte[] data = { 1, 2, 3 };

            // A null target Type is a configuration error (the dispatch cannot proceed), surfaced
            // as SerializationConfigurationException — not swallowed by Try*.
            Assert.Throws<SerializationConfigurationException>(() =>
                Serializer.ProtoDeserialize<object>(data, null)
            );
        }

        [Test]
        public void JsonStringifyWithOptionsNullOptionsThrowsException()
        {
            TestMessage msg = new() { Id = 1 };

            Assert.Throws<ArgumentNullException>(() => Serializer.JsonStringify(msg, null));
        }

        [Test]
        public void BinarySerializeVeryLargeObjectHandlesGracefully()
        {
            // Test with a large collection
            ComplexMessage msg = new()
            {
                Integer = 1,
                StringList = Enumerable.Range(0, 10_000).Select(i => $"Item_{i}").ToList(),
            };

            byte[] serialized = Serializer.BinarySerialize(msg);

            Assert.NotNull(serialized);
            Assert.Greater(serialized.Length, 0);

            ComplexMessage deserialized = Serializer.BinaryDeserialize<ComplexMessage>(serialized);
            Assert.AreEqual(msg.StringList.Count, deserialized.StringList.Count);
        }

        [Test]
        public void GenericSerializeWithAllTypesEdgeCaseData()
        {
            // Test with edge case values
            ComplexMessage msg = new()
            {
                Integer = int.MinValue,
                Double = double.MaxValue,
                Text = string.Empty,
                Data = new byte[] { 0, 255 },
                StringList = new List<string> { "", "test" },
                Dictionary = new Dictionary<string, int> { [""] = 0, ["test"] = -1 },
            };

            foreach (
                SerializationType type in new[]
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    SerializationType.SystemBinary,
#pragma warning restore CS0618 // Type or member is obsolete
                    SerializationType.Protobuf,
                }
            )
            {
                byte[] serialized = Serializer.Serialize(msg, type);
                ComplexMessage deserialized = Serializer.Deserialize<ComplexMessage>(
                    serialized,
                    type
                );

                Assert.AreEqual(msg.Integer, deserialized.Integer, $"Failed for {type}");
                Assert.AreEqual(msg.Double, deserialized.Double, $"Failed for {type}");
            }
        }

        [Test]
        public void FileIOReadFromInvalidJsonThrowsException()
        {
            string filePath = Path.Combine(_tempDirectory, "invalid.json");
            File.WriteAllText(filePath, "{ invalid json content }");

            Assert.Throws<SerializationCorruptDataException>(() =>
                Serializer.ReadFromJsonFile<TestMessage>(filePath)
            );
        }
    }
}

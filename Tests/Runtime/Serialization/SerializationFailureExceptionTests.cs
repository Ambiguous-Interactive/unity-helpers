// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    /// <summary>
    /// Unit tests for the <see cref="SerializationFailureException"/> hierarchy itself:
    /// property immutability, lazy Message composition, ToString clarity, InnerException
    /// preservation, and <see cref="SerializableAttribute"/> round-trip.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializationFailureExceptionTests
    {
        [Test]
        public void PropertiesAreImmutableAfterConstruction()
        {
            SerializationInputException ex = new(
                SerializationFormat.Protobuf,
                SerializationOperation.Deserialize,
                typeof(int),
                "null",
                "data is null."
            );

            Assert.AreEqual(SerializationFormat.Protobuf, ex.Format);
            Assert.AreEqual(SerializationOperation.Deserialize, ex.Operation);
            Assert.AreEqual(typeof(int), ex.DeclaredType);
            Assert.AreEqual(SerializationStage.InputValidation, ex.Stage);
            Assert.AreEqual("null", ex.InputDescriptor);
            Assert.AreEqual("data is null.", ex.Reason);
            Assert.IsTrue(ex.InnerException == null);
        }

        [Test]
        public void MessageIsLazilyComposedAndStableAcrossCalls()
        {
            SerializationInputException ex = new(
                SerializationFormat.Json,
                SerializationOperation.Deserialize,
                typeof(string),
                "null",
                "data is null."
            );

            string first = ex.Message;
            string second = ex.Message;
            Assert.AreSame(first, second, "Message must be cached on first access.");
            StringAssert.Contains("Json", first);
            StringAssert.Contains("Deserialize", first);
            StringAssert.Contains("System.String", first);
            StringAssert.Contains("null", first);
            StringAssert.Contains("data is null.", first);
        }

        [Test]
        public void CorruptDataExceptionPreservesInnerException()
        {
            InvalidOperationException inner = new("codec rejected");
            SerializationCorruptDataException ex = new(
                SerializationFormat.Protobuf,
                SerializationOperation.Deserialize,
                typeof(object),
                "byte[16]",
                SerializationStage.Decode,
                "protobuf-net rejected the payload.",
                inner
            );
            Assert.AreSame(inner, ex.InnerException);
            Assert.AreEqual(SerializationStage.Decode, ex.Stage);
            StringAssert.Contains("protobuf-net rejected the payload.", ex.Message);
        }

        [Test]
        public void ThrowNullInputPopulatesProperties()
        {
            SerializationInputException ex = Assert.Throws<SerializationInputException>(() =>
                SerializationFailureException.ThrowNullInput<int>(
                    SerializationFormat.Binary,
                    SerializationOperation.Deserialize
                )
            );
            Assert.AreEqual(SerializationFormat.Binary, ex.Format);
            Assert.AreEqual(typeof(int), ex.DeclaredType);
            Assert.IsTrue(ex.InnerException == null);
        }

        [Test]
        public void ThrowCorruptWrapsInnerExceptionAndDescribesLength()
        {
            Exception inner = new InvalidDataException("bad");
            SerializationCorruptDataException ex = Assert.Throws<SerializationCorruptDataException>(
                () =>
                    SerializationFailureException.ThrowCorrupt<string>(
                        SerializationFormat.Json,
                        SerializationOperation.Deserialize,
                        inputLength: 256,
                        SerializationStage.Decode,
                        inner
                    )
            );
            Assert.AreSame(inner, ex.InnerException);
            StringAssert.Contains("byte[256]", ex.Message);
        }

        [Test]
        public void SubclassesAreDistinguishableByType()
        {
            SerializationFailureException inputEx = new SerializationInputException(
                SerializationFormat.Protobuf,
                SerializationOperation.Deserialize,
                typeof(int),
                "null",
                "x"
            );
            SerializationFailureException corruptEx = new SerializationCorruptDataException(
                SerializationFormat.Protobuf,
                SerializationOperation.Deserialize,
                typeof(int),
                "byte[3]",
                SerializationStage.Decode,
                "x",
                new InvalidOperationException()
            );
            SerializationFailureException typeEx = new SerializationTypeException(
                SerializationFormat.Protobuf,
                SerializationOperation.Deserialize,
                typeof(int),
                "<unresolved>",
                "x"
            );
            SerializationFailureException configEx = new SerializationConfigurationException(
                SerializationFormat.Dispatcher,
                SerializationOperation.Deserialize,
                typeof(int),
                "<n/a>",
                "x"
            );

            Assert.IsInstanceOf<SerializationFailureException>(inputEx);
            Assert.IsInstanceOf<SerializationFailureException>(corruptEx);
            Assert.IsInstanceOf<SerializationFailureException>(typeEx);
            Assert.IsInstanceOf<SerializationFailureException>(configEx);
            Assert.IsNotInstanceOf<SerializationCorruptDataException>(inputEx);
            Assert.IsNotInstanceOf<SerializationInputException>(corruptEx);
        }

        [Test]
        public void InputDescriptorNeverContainsPayloadBytes()
        {
            // Defense-in-depth: regardless of how we describe the input, the descriptor must not
            // contain the raw payload bytes (sensitive-data guarantee).
            byte[] secret = { 0x53, 0x65, 0x63, 0x72, 0x65, 0x74 }; // "Secret"
            SerializationCorruptDataException ex = null;
            try
            {
                SerializationFailureException.ThrowCorrupt<int>(
                    SerializationFormat.Protobuf,
                    SerializationOperation.Deserialize,
                    secret.Length,
                    SerializationStage.Decode,
                    new InvalidOperationException("simulated codec failure")
                );
            }
            catch (SerializationCorruptDataException caught)
            {
                ex = caught;
            }
            Assert.IsTrue(ex != null);
            StringAssert.DoesNotContain("Secret", ex.Message);
            StringAssert.DoesNotContain("53", ex.InputDescriptor); // hex of 'S'
            // Length is fine — that's intentional.
            StringAssert.Contains("byte[6]", ex.InputDescriptor);
        }

#pragma warning disable SYSLIB0011 // Type or member is obsolete (BinaryFormatter)
        [Test]
        public void ExceptionRoundTripsThroughBinarySerialization()
        {
            // Sanity-check that the [Serializable] attribute + GetObjectData/ctor pair work.
            // BinaryFormatter is obsolete and unsafe — used here purely as a serialization probe.
            SerializationInputException original = new(
                SerializationFormat.Protobuf,
                SerializationOperation.Deserialize,
                typeof(int),
                "null",
                "data is null."
            );
            string originalMessage = original.Message; // force composition

            using MemoryStream ms = new();
            BinaryFormatter formatter = new();
            formatter.Serialize(ms, original);
            ms.Position = 0;
            SerializationInputException copy = (SerializationInputException)
                formatter.Deserialize(ms);

            Assert.AreEqual(original.Format, copy.Format);
            Assert.AreEqual(original.Operation, copy.Operation);
            Assert.AreEqual(original.DeclaredType, copy.DeclaredType);
            Assert.AreEqual(original.Stage, copy.Stage);
            Assert.AreEqual(original.InputDescriptor, copy.InputDescriptor);
            Assert.AreEqual(original.Reason, copy.Reason);
            Assert.AreEqual(originalMessage, copy.Message);
        }

        [Serializable]
        public sealed class PublicNestedSample { }

        [Test]
        public void ExceptionRoundTripsWithNestedTypeReference()
        {
            // Verifies that a non-trivial Type (a nested class in this test assembly) survives the
            // AssemblyQualifiedName round-trip. If the assembly is trimmed at runtime, DeclaredType
            // would resolve to null — that's the documented contract.
            SerializationInputException original = new(
                SerializationFormat.Protobuf,
                SerializationOperation.Deserialize,
                typeof(PublicNestedSample),
                "null",
                "data is null."
            );

            using MemoryStream ms = new();
            BinaryFormatter formatter = new();
            formatter.Serialize(ms, original);
            ms.Position = 0;
            SerializationInputException copy = (SerializationInputException)
                formatter.Deserialize(ms);

            // In the test process the assembly is always loaded — round-trip should preserve the Type.
            Assert.AreEqual(typeof(PublicNestedSample), copy.DeclaredType);
        }
#pragma warning restore SYSLIB0011
    }
}

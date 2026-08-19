// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters;

    /// <summary>
    /// Proves a JSON converter exists for a closure only this assembly writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure this guards is invisible in the editor and invisible in CI: a
    /// <c>JsonConverterFactory</c> builds its converter with <c>MakeGenericType</c> and
    /// <c>Activator.CreateInstance</c>, IL2CPP compiles only the closures it can see statically, and
    /// so the constructor throws <c>ExecutionEngineException</c> the first time a player saves. The
    /// closures the package's own tests exercise are compiled precisely because the package names
    /// them, which is why the crash reaches a consumer's game rather than this suite.
    /// </para>
    /// <para>
    /// <see cref="ProbeStruct"/> and its containers exist nowhere else, so the registrations below
    /// can only have come from the generator's scan of this assembly's syntax -- the same mechanism
    /// running in a consumer's build over their own types.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class JsonConverterRegistrationTests
    {
        [Test]
        public void AClosureThisAssemblyWritesHasAnAlreadyConstructedConverter()
        {
            Assert.IsTrue(
                WJsonConverterRegistry.TryGet(
                    typeof(ProbeBox<ProbeStruct>),
                    out JsonConverter converter
                ),
                "The generator registered no converter for ProbeBox<ProbeStruct>"
            );
            Assert.IsInstanceOf<ProbeBoxConverter<ProbeStruct>>(converter);

            // Arity beyond one, because closing a two-parameter converter over the arguments in the
            // right order is exactly what a transposed declaration would get wrong.
            Assert.IsTrue(
                WJsonConverterRegistry.TryGet(
                    typeof(ProbePair<ProbeStruct, string>),
                    out JsonConverter pair
                )
            );
            Assert.IsInstanceOf<ProbePairConverter<ProbeStruct, string>>(pair);
        }

        [Test]
        public void AClosureNoAssemblyWritesIsNotRegistered()
        {
            // The negative control, and the whole reason the scan is worth having: registering every
            // conceivable closure is impossible, so a registry that answered yes here would mean the
            // test above proved nothing.
            //
            // Built with MakeGenericType rather than written as `typeof(ProbeBox<decimal>)`, because
            // writing it IS the thing that causes a registration -- the first draft of this test
            // spelled the closure out and failed, which is the scan working. That call is also the
            // one IL2CPP cannot compile, so it belongs in a test and never in shipped code.
            Assert.IsFalse(
                WJsonConverterRegistry.TryGet(
                    typeof(ProbeBox<>).MakeGenericType(typeof(decimal)),
                    out _
                )
            );
        }

        [Test]
        public void TheRegisteredConverterIsWhatSystemTextJsonUses()
        {
            // Registration is only half the claim. This asks System.Text.Json to serialize through
            // the factory path a consumer actually takes, and compares against the shape the
            // converter writes -- a bare number rather than the `{"Value":7}` the reflective object
            // converter would produce.
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                Converters = { new ProbeBoxFactory() },
                IncludeFields = true,
            };

            string json = JsonSerializer.Serialize(
                new ProbeBox<ProbeStruct> { Value = new ProbeStruct { Value = 7 } },
                options
            );
            Assert.AreEqual("{\"Value\":7}", json);

            ProbeBox<ProbeStruct> read = JsonSerializer.Deserialize<ProbeBox<ProbeStruct>>(
                json,
                options
            );
            Assert.AreEqual(7, read.Value.Value);
        }

        [Test]
        public void RegisteringTheSameTypeTwiceKeepsTheFirstConverter()
        {
            // Generated registrars run unordered, and a consumer's assembly may write a closure this
            // package's assembly also writes. Last-write-wins would make assembly load order decide
            // which converter serves a type, which is the failure WProtoRootMarshalProvider already
            // records for formatters.
            // Named through MakeGenericType so the generator has not already claimed it; writing
            // the closure here would register it before this test ran.
            System.Type boxOfLong = typeof(ProbeBox<>).MakeGenericType(typeof(long));

            ProbeBoxConverter<long> first = new ProbeBoxConverter<long>();
            Assert.IsTrue(WJsonConverterRegistry.TryRegister(boxOfLong, first));
            Assert.IsFalse(
                WJsonConverterRegistry.TryRegister(boxOfLong, new ProbeBoxConverter<long>())
            );

            Assert.IsTrue(WJsonConverterRegistry.TryGet(boxOfLong, out JsonConverter registered));
            Assert.AreSame(first, registered);
        }

        [Test]
        public void ARegistrationThatCouldNotServeTheTypeIsRefused()
        {
            // Generated code cannot produce this pair; a hand-written registration can, and the
            // failure it causes surfaces inside System.Text.Json naming neither the type nor the
            // converter.
            Assert.IsFalse(
                WJsonConverterRegistry.TryRegister(
                    typeof(ProbeBox<>).MakeGenericType(typeof(short)),
                    new ProbeBoxConverter<ProbeStruct>()
                )
            );
            Assert.IsFalse(WJsonConverterRegistry.TryRegister(null, new ProbeBoxConverter<int>()));
            Assert.IsFalse(
                WJsonConverterRegistry.TryRegister(
                    typeof(ProbeBox<>).MakeGenericType(typeof(int)),
                    null
                )
            );
        }

        /// <summary>A factory shaped like the package's, asking the registry before reflecting.</summary>
        private sealed class ProbeBoxFactory : JsonConverterFactory
        {
            public override bool CanConvert(System.Type typeToConvert)
            {
                return typeToConvert.IsGenericType
                    && typeToConvert.GetGenericTypeDefinition() == typeof(ProbeBox<>);
            }

            public override JsonConverter CreateConverter(
                System.Type typeToConvert,
                JsonSerializerOptions options
            )
            {
                if (WJsonConverterRegistry.TryGet(typeToConvert, out JsonConverter generated))
                {
                    return generated;
                }

                Assert.Fail("Fell through to the reflective path for " + typeToConvert);
                return null;
            }
        }
    }
}

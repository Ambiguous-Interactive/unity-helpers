// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    public static class ConsumerCases
    {
        public static readonly Guid FirstKey = Guid.Parse("00000000-0000-0000-0000-000000000001");
        public static readonly Guid OtherKey = Guid.Parse("00000000-0000-0000-0000-000000000002");

        public static Doc Create(string shape, bool changed = false)
        {
            Doc doc = new Doc
            {
                Revision =
                    changed && !string.Equals(shape, "populated", StringComparison.Ordinal)
                        ? 18
                        : 17,
            };
            if (string.Equals(shape, "null", StringComparison.Ordinal))
            {
                return doc;
            }
            doc.Plain = new SerializableDictionary<Guid, OwnedMessage>();
            doc.Sorted = new SerializableSortedDictionary<Guid, OwnedMessage>();
            doc.Cached =
                new SerializableDictionary<int, int?, SerializableDictionary.Cache<int?>>();
            doc.SortedCached =
                new SerializableSortedDictionary<int, int?, SerializableDictionary.Cache<int?>>();
            if (string.Equals(shape, "empty", StringComparison.Ordinal))
            {
                return doc;
            }
            Require(string.Equals(shape, "populated", StringComparison.Ordinal), "Unknown shape");
            doc.Plain.Add(
                FirstKey,
                new OwnedMessage
                {
                    Number = changed ? 43 : 42,
                    Text = " padded ",
                    Checkpoint = 0,
                }
            );
            doc.Sorted.Add(
                FirstKey,
                new OwnedMessage
                {
                    Number = changed ? 43 : 42,
                    Text = " padded ",
                    Checkpoint = 0,
                }
            );
            doc.Cached.Add(2, null);
            doc.Cached.Add(3, 0);
            doc.Cached.Add(4, changed ? -8 : -7);
            doc.SortedCached.Add(2, null);
            doc.SortedCached.Add(3, 0);
            doc.SortedCached.Add(4, changed ? -8 : -7);
            return doc;
        }

        public static int Fnv(byte[] bytes)
        {
            unchecked
            {
                uint value = 2166136261;
                foreach (byte item in bytes)
                {
                    value = (value ^ item) * 16777619;
                }
                return (int)value;
            }
        }

        public static void Require(bool valid, string message)
        {
            if (!valid)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void CheckMessage(IDictionary<Guid, OwnedMessage> map)
        {
            Require(map != null && map.Count == 1, "Message map count");
            Require(
                map.TryGetValue(FirstKey, out OwnedMessage value) && value != null,
                "Message missing"
            );
            Require(
                value.Number == 42
                    && string.Equals(value.Text, " padded ", StringComparison.Ordinal)
                    && value.Checkpoint.HasValue
                    && value.Checkpoint.Value == 0,
                "Message fields changed"
            );
        }

        public static void CheckNullable(IDictionary<int, int?> map)
        {
            Require(map != null && map.Count == 3, "Nullable map count");
            Require(
                map.TryGetValue(2, out int? missing) && !missing.HasValue,
                "Null value changed"
            );
            Require(
                map.TryGetValue(3, out int? zero) && zero.HasValue && zero.Value == 0,
                "Present zero changed"
            );
            Require(
                map.TryGetValue(4, out int? negative) && negative == -7,
                "Negative value changed"
            );
        }

        public static void CheckLogical(Doc doc, string shape)
        {
            Require(doc != null && doc.Revision == 17, "Revision changed");
            if (string.Equals(shape, "populated", StringComparison.Ordinal))
            {
                CheckMessage(doc.Plain);
                CheckMessage(doc.Sorted);
                CheckNullable(doc.Cached);
                CheckNullable(doc.SortedCached);
            }
            else
            {
                Require(
                    (doc.Plain == null || doc.Plain.Count == 0)
                        && (doc.Sorted == null || doc.Sorted.Count == 0)
                        && (doc.Cached == null || doc.Cached.Count == 0)
                        && (doc.SortedCached == null || doc.SortedCached.Count == 0),
                    "Null/empty maps acquired entries"
                );
            }
        }

        public static void Run(string mode, string shape, byte[] expected)
        {
            if (string.Equals(mode, "serialize-read", StringComparison.Ordinal))
            {
                Doc cold = Serializer.ProtoDeserialize<Doc>(expected);
                CheckLogical(cold, shape);
                Require(
                    Serializer.ProtoSerialize(cold).AsSpan().SequenceEqual(expected),
                    "Cold read/write bytes changed"
                );
            }
            Doc first = Create(shape);
            Doc equivalent = Create(shape);
            Doc different = Create(shape, true);
            switch (mode)
            {
                case "typed-hash":
                    Require(
                        ProtoEqualityExtensions.GetProtoComparer<Doc>().GetHashCode(first)
                            == Fnv(expected),
                        "First typed hash changed"
                    );
                    break;
                case "object-hash":
                    Require(
                        ProtoEqualityExtensions.GetProtoComparer<object>().GetHashCode(first)
                            == Fnv(expected),
                        "First object hash changed"
                    );
                    break;
                case "typed-equality":
                    Require(
                        first.ProtoEquals(equivalent) && !first.ProtoEquals(different),
                        "Typed equality lost values"
                    );
                    break;
                case "object-equality":
                    Require(
                        ProtoEqualityExtensions.GetProtoComparer<object>().Equals(first, equivalent)
                            && !ProtoEqualityExtensions
                                .GetProtoComparer<object>()
                                .Equals(first, different),
                        "Object equality lost values"
                    );
                    break;
                case "serialize-read":
                    break;
                case "merge":
                    CheckMerge(expected, shape);
                    break;
                default:
                    throw new ArgumentException("Unknown operation", nameof(mode));
            }
            byte[] bytes = Serializer.ProtoSerialize(first);
            Require(bytes.AsSpan().SequenceEqual(expected), "Historical bytes changed");
            Doc restored = Serializer.ProtoDeserialize<Doc>(expected);
            CheckLogical(restored, shape);
            Require(first.ProtoEquals(restored), "Restored object differs");
            CheckPerMapChanges(first, shape);
        }

        private static void CheckPerMapChanges(Doc first, string shape)
        {
            if (!string.Equals(shape, "populated", StringComparison.Ordinal))
            {
                return;
            }
            for (int family = 0; family < 4; ++family)
            {
                Doc different = Create(shape);
                switch (family)
                {
                    case 0:
                        Require(
                            different.Plain.TryGetValue(FirstKey, out OwnedMessage plain),
                            "Plain fixture key missing"
                        );
                        plain.Number = 43;
                        break;
                    case 1:
                        Require(
                            different.Sorted.TryGetValue(FirstKey, out OwnedMessage sorted),
                            "Sorted fixture key missing"
                        );
                        sorted.Number = 43;
                        break;
                    case 2:
                        different.Cached[4] = -8;
                        break;
                    default:
                        different.SortedCached[4] = -8;
                        break;
                }
                Require(
                    !first.ProtoEquals(different),
                    "Declared equality ignored map family " + family
                );
                Require(
                    !ProtoEqualityExtensions.GetProtoComparer<object>().Equals(first, different),
                    "Object equality ignored map family " + family
                );
                Require(
                    ProtoEqualityExtensions.GetProtoComparer<Doc>().GetHashCode(first)
                        != ProtoEqualityExtensions.GetProtoComparer<Doc>().GetHashCode(different),
                    "Hash ignored map family " + family
                );
            }
        }

        private static void CheckMerge(byte[] incoming, string shape)
        {
            Require(
                string.Equals(shape, "populated", StringComparison.Ordinal),
                "Merge requires populated shape"
            );
            ReverseGuidComparer policy = new ReverseGuidComparer();
            SortedDictionary<Guid, OwnedMessage> backing = new SortedDictionary<Guid, OwnedMessage>(
                policy
            );
            backing.Add(OtherKey, new OwnedMessage { Number = 99 });
            Doc target = new Doc
            {
                Sorted = new SerializableSortedDictionary<Guid, OwnedMessage>(backing),
            };
            SerializableSortedDictionary<Guid, OwnedMessage> original = target.Sorted;
            using (MemoryStream stream = new MemoryStream(incoming))
            {
                Doc result = ProtoBuf.Serializer.Merge(stream, target);
                Require(
                    ReferenceEquals(result, target) && ReferenceEquals(result.Sorted, original),
                    "Populated merge replaced identity"
                );
                Require(
                    result.Sorted.Count == 2
                        && result.Sorted.TryGetValue(FirstKey, out OwnedMessage replaced)
                        && replaced.Number == 42
                        && result.Sorted.TryGetValue(OtherKey, out OwnedMessage retained)
                        && retained.Number == 99,
                    "Merge content changed"
                );
                using (IEnumerator<Guid> order = result.Sorted.Keys.GetEnumerator())
                {
                    Require(
                        order.MoveNext()
                            && order.Current == OtherKey
                            && order.MoveNext()
                            && order.Current == FirstKey
                            && !order.MoveNext(),
                        "Custom comparator order lost"
                    );
                }
            }
            CheckMergeRejectsOverlappingKey(incoming);
        }

        private static void CheckMergeRejectsOverlappingKey(byte[] incoming)
        {
            SortedDictionary<Guid, OwnedMessage> backing = new SortedDictionary<Guid, OwnedMessage>(
                new ReverseGuidComparer()
            );
            OwnedMessage existing = new OwnedMessage { Number = -1 };
            OwnedMessage retained = new OwnedMessage { Number = 99 };
            backing.Add(FirstKey, existing);
            backing.Add(OtherKey, retained);
            Doc target = new Doc
            {
                Sorted = new SerializableSortedDictionary<Guid, OwnedMessage>(backing),
            };
            SerializableSortedDictionary<Guid, OwnedMessage> original = target.Sorted;
            bool rejected = false;
            using (MemoryStream stream = new MemoryStream(incoming))
            {
                try
                {
                    ProtoBuf.Serializer.Merge(stream, target);
                }
                catch (ArgumentException failure)
                {
                    Require(
                        failure.GetType() == typeof(ArgumentException),
                        "Overlapping merge exception changed"
                    );
                    rejected = true;
                }
            }
            Require(rejected, "Overlapping merge accepted a duplicate key");
            Require(ReferenceEquals(target.Sorted, original), "Rejected merge replaced identity");
            Require(
                original.Count == 2
                    && original.TryGetValue(FirstKey, out OwnedMessage unchanged)
                    && ReferenceEquals(unchanged, existing)
                    && unchanged.Number == -1
                    && original.TryGetValue(OtherKey, out OwnedMessage other)
                    && ReferenceEquals(other, retained)
                    && other.Number == 99,
                "Rejected merge changed existing sorted entries"
            );
            using (IEnumerator<Guid> order = original.Keys.GetEnumerator())
            {
                Require(
                    order.MoveNext()
                        && order.Current == OtherKey
                        && order.MoveNext()
                        && order.Current == FirstKey
                        && !order.MoveNext(),
                    "Rejected merge lost comparator order"
                );
            }
        }
    }
}

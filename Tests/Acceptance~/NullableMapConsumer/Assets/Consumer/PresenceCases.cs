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

    public static class PresenceCases
    {
        public static PresenceDoc Create(bool changed = false)
        {
            PresenceDoc value = new PresenceDoc
            {
                PlainKeyInt = new SerializableDictionary<int?, int>(),
                PlainKeyEnum = new SerializableDictionary<PresenceEnum?, int>(),
                PlainKeyStruct = new SerializableDictionary<PresenceValue?, int>(),
                CachedKeyInt =
                    new SerializableDictionary<int?, int, SerializableDictionary.Cache<int>>(),
                CachedKeyEnum =
                    new SerializableDictionary<
                        PresenceEnum?,
                        int,
                        SerializableDictionary.Cache<int>
                    >(),
                CachedKeyStruct =
                    new SerializableDictionary<
                        PresenceValue?,
                        int,
                        SerializableDictionary.Cache<int>
                    >(),
                ClrKeyInt = new Dictionary<int?, int>(),
                ClrKeyEnum = new Dictionary<PresenceEnum?, int>(),
                ClrKeyStruct = new Dictionary<PresenceValue?, int>(),
                PlainInt = new SerializableDictionary<int, int?>(),
                PlainEnum = new SerializableDictionary<int, PresenceEnum?>(),
                PlainStruct = new SerializableDictionary<int, PresenceValue?>(),
                SortedInt = new SerializableSortedDictionary<int, int?>(),
                SortedEnum = new SerializableSortedDictionary<int, PresenceEnum?>(),
                SortedStruct = new SerializableSortedDictionary<int, PresenceValue?>(),
                CachedInt =
                    new SerializableDictionary<int, int?, SerializableDictionary.Cache<int?>>(),
                CachedEnum =
                    new SerializableDictionary<
                        int,
                        PresenceEnum?,
                        SerializableDictionary.Cache<PresenceEnum?>
                    >(),
                CachedStruct =
                    new SerializableDictionary<
                        int,
                        PresenceValue?,
                        SerializableDictionary.Cache<PresenceValue?>
                    >(),
                SortedCachedInt =
                    new SerializableSortedDictionary<
                        int,
                        int?,
                        SerializableDictionary.Cache<int?>
                    >(),
                SortedCachedEnum =
                    new SerializableSortedDictionary<
                        int,
                        PresenceEnum?,
                        SerializableDictionary.Cache<PresenceEnum?>
                    >(),
                SortedCachedStruct =
                    new SerializableSortedDictionary<
                        int,
                        PresenceValue?,
                        SerializableDictionary.Cache<PresenceValue?>
                    >(),
                ClrInt = new Dictionary<int, int?>(),
                ClrEnum = new Dictionary<int, PresenceEnum?>(),
                ClrStruct = new Dictionary<int, PresenceValue?>(),
            };
            value.PlainInt.Add(0, null);
            value.PlainInt.Add(3, default(int));
            value.PlainInt.Add(4, 7);
            value.PlainEnum.Add(0, null);
            value.PlainEnum.Add(3, default(PresenceEnum));
            value.PlainEnum.Add(4, PresenceEnum.Nondefault);
            value.PlainStruct.Add(0, null);
            value.PlainStruct.Add(3, default(PresenceValue));
            value.PlainStruct.Add(4, new PresenceValue { Number = 7 });
            value.SortedInt.Add(0, null);
            value.SortedInt.Add(3, default(int));
            value.SortedInt.Add(4, 7);
            value.SortedEnum.Add(0, null);
            value.SortedEnum.Add(3, default(PresenceEnum));
            value.SortedEnum.Add(4, PresenceEnum.Nondefault);
            value.SortedStruct.Add(0, null);
            value.SortedStruct.Add(3, default(PresenceValue));
            value.SortedStruct.Add(4, new PresenceValue { Number = 7 });
            value.CachedInt.Add(0, null);
            value.CachedInt.Add(3, default(int));
            value.CachedInt.Add(4, 7);
            value.CachedEnum.Add(0, null);
            value.CachedEnum.Add(3, default(PresenceEnum));
            value.CachedEnum.Add(4, PresenceEnum.Nondefault);
            value.CachedStruct.Add(0, null);
            value.CachedStruct.Add(3, default(PresenceValue));
            value.CachedStruct.Add(4, new PresenceValue { Number = 7 });
            value.SortedCachedInt.Add(0, null);
            value.SortedCachedInt.Add(3, default(int));
            value.SortedCachedInt.Add(4, 7);
            value.SortedCachedEnum.Add(0, null);
            value.SortedCachedEnum.Add(3, default(PresenceEnum));
            value.SortedCachedEnum.Add(4, PresenceEnum.Nondefault);
            value.SortedCachedStruct.Add(0, null);
            value.SortedCachedStruct.Add(3, default(PresenceValue));
            value.SortedCachedStruct.Add(4, new PresenceValue { Number = 7 });
            value.ClrInt.Add(0, null);
            value.ClrInt.Add(3, default(int));
            value.ClrInt.Add(4, 7);
            value.ClrEnum.Add(0, null);
            value.ClrEnum.Add(3, default(PresenceEnum));
            value.ClrEnum.Add(4, PresenceEnum.Nondefault);
            value.ClrStruct.Add(0, null);
            value.ClrStruct.Add(3, default(PresenceValue));
            value.ClrStruct.Add(4, new PresenceValue { Number = 7 });
            value.PlainKeyInt.Add(default(int), 11);
            value.PlainKeyInt.Add(7, 19);
            value.PlainKeyEnum.Add(default(PresenceEnum), 11);
            value.PlainKeyEnum.Add(PresenceEnum.Nondefault, 19);
            value.PlainKeyStruct.Add(default(PresenceValue), 11);
            value.PlainKeyStruct.Add(new PresenceValue { Number = 7 }, 19);
            value.CachedKeyInt.Add(default(int), 11);
            value.CachedKeyInt.Add(7, 19);
            value.CachedKeyEnum.Add(default(PresenceEnum), 11);
            value.CachedKeyEnum.Add(PresenceEnum.Nondefault, 19);
            value.CachedKeyStruct.Add(default(PresenceValue), 11);
            value.CachedKeyStruct.Add(new PresenceValue { Number = 7 }, 19);
            value.ClrKeyInt.Add(default(int), 11);
            value.ClrKeyInt.Add(7, 19);
            value.ClrKeyEnum.Add(default(PresenceEnum), 11);
            value.ClrKeyEnum.Add(PresenceEnum.Nondefault, 19);
            value.ClrKeyStruct.Add(default(PresenceValue), 11);
            value.ClrKeyStruct.Add(new PresenceValue { Number = 7 }, 19);
            if (changed)
            {
                value.PlainInt[4] = 8;
            }
            return value;
        }

        public static void Check(PresenceDoc value)
        {
            ConsumerCases.Require(value != null, "Presence document missing");
            CheckKeys(value.PlainKeyInt, 7);
            CheckKeys(value.PlainKeyEnum, PresenceEnum.Nondefault);
            CheckKeys(value.PlainKeyStruct, new PresenceValue { Number = 7 });
            CheckKeys(value.CachedKeyInt, 7);
            CheckKeys(value.CachedKeyEnum, PresenceEnum.Nondefault);
            CheckKeys(value.CachedKeyStruct, new PresenceValue { Number = 7 });
            CheckKeys(value.ClrKeyInt, 7);
            CheckKeys(value.ClrKeyEnum, PresenceEnum.Nondefault);
            CheckKeys(value.ClrKeyStruct, new PresenceValue { Number = 7 });
            CheckMap(value.PlainInt, 7);
            CheckMap(value.PlainEnum, PresenceEnum.Nondefault);
            CheckMap(value.PlainStruct, new PresenceValue { Number = 7 });
            CheckMap(value.SortedInt, 7);
            CheckMap(value.SortedEnum, PresenceEnum.Nondefault);
            CheckMap(value.SortedStruct, new PresenceValue { Number = 7 });
            CheckMap(value.CachedInt, 7);
            CheckMap(value.CachedEnum, PresenceEnum.Nondefault);
            CheckMap(value.CachedStruct, new PresenceValue { Number = 7 });
            CheckMap(value.SortedCachedInt, 7);
            CheckMap(value.SortedCachedEnum, PresenceEnum.Nondefault);
            CheckMap(value.SortedCachedStruct, new PresenceValue { Number = 7 });
            CheckMap(value.ClrInt, 7);
            CheckMap(value.ClrEnum, PresenceEnum.Nondefault);
            CheckMap(value.ClrStruct, new PresenceValue { Number = 7 });
        }

        public static void Run(string mode, byte[] expected)
        {
            PresenceDoc first = Create();
            PresenceDoc equivalent = Create();
            PresenceDoc different = Create(true);
            switch (mode)
            {
                case "presence-typed-hash":
                    ConsumerCases.Require(
                        ProtoEqualityExtensions.GetProtoComparer<PresenceDoc>().GetHashCode(first)
                            == ConsumerCases.Fnv(expected),
                        "Cold typed hash bytes"
                    );
                    break;
                case "presence-object-hash":
                    ConsumerCases.Require(
                        ProtoEqualityExtensions.GetProtoComparer<object>().GetHashCode(first)
                            == ConsumerCases.Fnv(expected),
                        "Cold object hash bytes"
                    );
                    break;
                case "presence-typed-equality":
                    ConsumerCases.Require(
                        first.ProtoEquals(equivalent) && !first.ProtoEquals(different),
                        "Cold typed equality values"
                    );
                    break;
                case "presence-object-equality":
                    ConsumerCases.Require(
                        ProtoEqualityExtensions.GetProtoComparer<object>().Equals(first, equivalent)
                            && !ProtoEqualityExtensions
                                .GetProtoComparer<object>()
                                .Equals(first, different),
                        "Cold object equality values"
                    );
                    break;
                case "presence-foreign":
                    using (MemoryStream encoded = new MemoryStream())
                    {
                        ProtoBuf.Serializer.NonGeneric.Serialize(encoded, first);
                        ConsumerCases.Require(
                            encoded.ToArray().AsSpan().SequenceEqual(expected),
                            "Foreign write bytes"
                        );
                    }
                    using (MemoryStream incoming = new MemoryStream(expected))
                    {
                        Check(
                            (PresenceDoc)
                                ProtoBuf.Serializer.NonGeneric.Deserialize(
                                    typeof(PresenceDoc),
                                    incoming
                                )
                        );
                    }
                    break;
                case "presence-serialize-read":
                    break;
                default:
                    throw new ArgumentException("Unknown presence operation", nameof(mode));
            }
            byte[] bytes = Serializer.ProtoSerialize(first);
            ConsumerCases.Require(
                bytes.AsSpan().SequenceEqual(expected),
                "Presence literal bytes changed"
            );
            Check(Serializer.ProtoDeserialize<PresenceDoc>(expected));
            CheckSensitivity(first);
        }

        private static void CheckSensitivity(PresenceDoc first)
        {
            for (int member = 0; member < 15; ++member)
            {
                foreach (bool missing in new[] { false, true })
                {
                    PresenceDoc changed = Create();
                    switch (member)
                    {
                        case 0:
                            if (missing)
                            {
                                changed.PlainInt.Remove(3);
                            }
                            else
                            {
                                changed.PlainInt[3] = null;
                            }
                            break;
                        case 1:
                            if (missing)
                            {
                                changed.PlainEnum.Remove(3);
                            }
                            else
                            {
                                changed.PlainEnum[3] = null;
                            }
                            break;
                        case 2:
                            if (missing)
                            {
                                changed.PlainStruct.Remove(3);
                            }
                            else
                            {
                                changed.PlainStruct[3] = null;
                            }
                            break;
                        case 3:
                            if (missing)
                            {
                                changed.SortedInt.Remove(3);
                            }
                            else
                            {
                                changed.SortedInt[3] = null;
                            }
                            break;
                        case 4:
                            if (missing)
                            {
                                changed.SortedEnum.Remove(3);
                            }
                            else
                            {
                                changed.SortedEnum[3] = null;
                            }
                            break;
                        case 5:
                            if (missing)
                            {
                                changed.SortedStruct.Remove(3);
                            }
                            else
                            {
                                changed.SortedStruct[3] = null;
                            }
                            break;
                        case 6:
                            if (missing)
                            {
                                changed.CachedInt.Remove(3);
                            }
                            else
                            {
                                changed.CachedInt[3] = null;
                            }
                            break;
                        case 7:
                            if (missing)
                            {
                                changed.CachedEnum.Remove(3);
                            }
                            else
                            {
                                changed.CachedEnum[3] = null;
                            }
                            break;
                        case 8:
                            if (missing)
                            {
                                changed.CachedStruct.Remove(3);
                            }
                            else
                            {
                                changed.CachedStruct[3] = null;
                            }
                            break;
                        case 9:
                            if (missing)
                            {
                                changed.SortedCachedInt.Remove(3);
                            }
                            else
                            {
                                changed.SortedCachedInt[3] = null;
                            }
                            break;
                        case 10:
                            if (missing)
                            {
                                changed.SortedCachedEnum.Remove(3);
                            }
                            else
                            {
                                changed.SortedCachedEnum[3] = null;
                            }
                            break;
                        case 11:
                            if (missing)
                            {
                                changed.SortedCachedStruct.Remove(3);
                            }
                            else
                            {
                                changed.SortedCachedStruct[3] = null;
                            }
                            break;
                        case 12:
                            if (missing)
                            {
                                changed.ClrInt.Remove(3);
                            }
                            else
                            {
                                changed.ClrInt[3] = null;
                            }
                            break;
                        case 13:
                            if (missing)
                            {
                                changed.ClrEnum.Remove(3);
                            }
                            else
                            {
                                changed.ClrEnum[3] = null;
                            }
                            break;
                        case 14:
                            if (missing)
                            {
                                changed.ClrStruct.Remove(3);
                            }
                            else
                            {
                                changed.ClrStruct[3] = null;
                            }
                            break;
                    }
                    ConsumerCases.Require(
                        !first.ProtoEquals(changed),
                        "Present default/null/missing typed equality"
                    );
                    ConsumerCases.Require(
                        !ProtoEqualityExtensions.GetProtoComparer<object>().Equals(first, changed),
                        "Present default/null/missing object equality"
                    );
                    ConsumerCases.Require(
                        ProtoEqualityExtensions.GetProtoComparer<PresenceDoc>().GetHashCode(first)
                            != ProtoEqualityExtensions
                                .GetProtoComparer<PresenceDoc>()
                                .GetHashCode(changed),
                        "Present default/null/missing typed hash"
                    );
                    ConsumerCases.Require(
                        ProtoEqualityExtensions.GetProtoComparer<object>().GetHashCode(first)
                            != ProtoEqualityExtensions
                                .GetProtoComparer<object>()
                                .GetHashCode(changed),
                        "Present default/null/missing object hash"
                    );
                }
            }
        }

        private static void CheckKeys<T>(IDictionary<T?, int> map, T nondefault)
            where T : struct
        {
            ConsumerCases.Require(map != null && map.Count == 2, "Nullable key count");
            ConsumerCases.Require(
                map.TryGetValue(default(T), out int defaultValue) && defaultValue == 11,
                "Nullable default key changed"
            );
            ConsumerCases.Require(
                map.TryGetValue(nondefault, out int nondefaultValue) && nondefaultValue == 19,
                "Nullable nondefault key changed"
            );
        }

        private static void CheckMap<T>(IDictionary<int, T?> map, T nondefault)
            where T : struct
        {
            ConsumerCases.Require(map != null && map.Count == 3, "Presence map count");
            ConsumerCases.Require(
                map.TryGetValue(0, out T? absent) && !absent.HasValue,
                "Absent value changed"
            );
            ConsumerCases.Require(
                map.TryGetValue(3, out T? zero)
                    && zero.HasValue
                    && EqualityComparer<T>.Default.Equals(zero.Value, default(T)),
                "Present default changed"
            );
            ConsumerCases.Require(
                map.TryGetValue(4, out T? actual)
                    && actual.HasValue
                    && EqualityComparer<T>.Default.Equals(actual.Value, nondefault),
                "Present nondefault changed"
            );
        }
    }
}

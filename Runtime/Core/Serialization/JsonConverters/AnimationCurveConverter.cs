// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    public sealed class AnimationCurveConverter : JsonConverter<AnimationCurve>
    {
        public static readonly AnimationCurveConverter Instance = new();

        private static readonly JsonEncodedText KeysProp = JsonEncodedText.Encode("keys");
        private static readonly JsonEncodedText PreWrapModeProp = JsonEncodedText.Encode(
            "preWrapMode"
        );
        private static readonly JsonEncodedText PostWrapModeProp = JsonEncodedText.Encode(
            "postWrapMode"
        );

        private static readonly JsonEncodedText TimeProp = JsonEncodedText.Encode("time");
        private static readonly JsonEncodedText ValueProp = JsonEncodedText.Encode("value");
        private static readonly JsonEncodedText InTangentProp = JsonEncodedText.Encode("inTangent");
        private static readonly JsonEncodedText OutTangentProp = JsonEncodedText.Encode(
            "outTangent"
        );
        private static readonly JsonEncodedText InWeightProp = JsonEncodedText.Encode("inWeight");
        private static readonly JsonEncodedText OutWeightProp = JsonEncodedText.Encode("outWeight");
        private static readonly JsonEncodedText WeightedModeProp = JsonEncodedText.Encode(
            "weightedMode"
        );

        private AnimationCurveConverter() { }

        public override AnimationCurve Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"Invalid token type {reader.TokenType}");
            }

            Keyframe[] keys = null;
            WrapMode pre = WrapMode.ClampForever;
            WrapMode post = WrapMode.ClampForever;
            bool havePre = false;
            bool havePost = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    AnimationCurve curve = new(keys ?? Array.Empty<Keyframe>())
                    {
                        preWrapMode = havePre ? pre : WrapMode.ClampForever,
                        postWrapMode = havePost ? post : WrapMode.ClampForever,
                    };
                    return curve;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("keys"))
                    {
                        reader.Read();
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            throw new JsonException("keys must be an array");
                        }
                        JsonArrayAccumulator<Keyframe> accumulator = default;
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndArray)
                            {
                                break;
                            }
                            accumulator.Add(ReadKeyframe(ref reader));
                        }
                        keys = accumulator.Finish();
                    }
                    else if (reader.ValueTextEquals("preWrapMode"))
                    {
                        reader.Read();
                        pre = JsonSerializer.Deserialize<WrapMode>(ref reader, options);
                        havePre = true;
                    }
                    else if (reader.ValueTextEquals("postWrapMode"))
                    {
                        reader.Read();
                        post = JsonSerializer.Deserialize<WrapMode>(ref reader, options);
                        havePost = true;
                    }
                    else
                    {
                        throw new JsonException("Unknown property for AnimationCurve");
                    }
                }
            }

            throw new JsonException("Incomplete JSON for AnimationCurve");
        }

        private static Keyframe ReadKeyframe(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Invalid keyframe token");
            }

            float time = 0f;
            float value = 0f;
            float inTan = 0f;
            float outTan = 0f;
            float inW = 0f;
            float outW = 0f;
            int weightedMode = 0;
            bool hasInW = false;
            bool hasOutW = false;
            bool hasWeighted = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    Keyframe k = new(time, value, inTan, outTan);
#if UNITY_2018_1_OR_NEWER
                    if (hasWeighted)
                    {
                        k.weightedMode = (WeightedMode)weightedMode;
                    }
                    if (hasInW)
                    {
                        k.inWeight = inW;
                    }
                    if (hasOutW)
                    {
                        k.outWeight = outW;
                    }
#endif
                    return k;
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("time"))
                    {
                        reader.Read();
                        time = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals("value"))
                    {
                        reader.Read();
                        value = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals("inTangent"))
                    {
                        reader.Read();
                        inTan = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals("outTangent"))
                    {
                        reader.Read();
                        outTan = reader.GetSingle();
                    }
                    else if (reader.ValueTextEquals("inWeight"))
                    {
                        reader.Read();
                        inW = reader.GetSingle();
                        hasInW = true;
                    }
                    else if (reader.ValueTextEquals("outWeight"))
                    {
                        reader.Read();
                        outW = reader.GetSingle();
                        hasOutW = true;
                    }
                    else if (reader.ValueTextEquals("weightedMode"))
                    {
                        reader.Read();
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            /*
                                The non-generic overload boxes the parsed value into an object that
                                the cast below immediately unboxes, once per keyframe.
                            */
                            string s = reader.GetString();
                            if (!Enum.TryParse(s, true, out WeightedMode wm))
                            {
                                throw new JsonException("Invalid weightedMode");
                            }
                            weightedMode = (int)wm;
                        }
                        else
                        {
                            weightedMode = reader.GetInt32();
                        }
                        hasWeighted = true;
                    }
                    else
                    {
                        throw new JsonException("Unknown property for Keyframe");
                    }
                }
            }
            throw new JsonException("Incomplete JSON for Keyframe");
        }

        public override void Write(
            Utf8JsonWriter writer,
            AnimationCurve value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WritePropertyName(KeysProp);
            writer.WriteStartArray();
            foreach (Keyframe k in value.keys)
            {
                writer.WriteStartObject();
                writer.WriteNumber(TimeProp, k.time);
                writer.WriteNumber(ValueProp, k.value);
                writer.WriteNumber(InTangentProp, k.inTangent);
                writer.WriteNumber(OutTangentProp, k.outTangent);
#if UNITY_2018_1_OR_NEWER
                writer.WriteNumber(WeightedModeProp, (int)k.weightedMode);
                writer.WriteNumber(InWeightProp, k.inWeight);
                writer.WriteNumber(OutWeightProp, k.outWeight);
#endif
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName(PreWrapModeProp);
            JsonSerializer.Serialize(writer, value.preWrapMode, options);
            writer.WritePropertyName(PostWrapModeProp);
            JsonSerializer.Serialize(writer, value.postWrapMode, options);

            writer.WriteEndObject();
        }
    }
}

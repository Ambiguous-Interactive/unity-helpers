// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Names the package type to reach for when Unity declines to serialize a field's type.
    /// </summary>
    /// <remarks>
    /// The half of the report that is worth reading. "Unity will not serialize this" tells a
    /// developer they have a problem; naming <c>SerializableDictionary&lt;string, int&gt;</c> tells
    /// them what to write instead, and the package already ships a stand-in for most of the types
    /// anyone reaches for by accident.
    /// </remarks>
    public static class UnitySerializationStandIns
    {
        private static readonly Dictionary<Type, string> ByDefinition = new()
        {
            [typeof(Dictionary<,>)] = "SerializableDictionary<{0}, {1}>",
            [typeof(SortedDictionary<,>)] = "SerializableSortedDictionary<{0}, {1}>",
            [typeof(IDictionary<,>)] = "SerializableDictionary<{0}, {1}>",
            [typeof(HashSet<>)] = "SerializableHashSet<{0}>",
            [typeof(SortedSet<>)] = "SerializableSortedSet<{0}>",
            [typeof(ISet<>)] = "SerializableHashSet<{0}>",
            [typeof(Nullable<>)] = "SerializableNullable<{0}>",
            [typeof(ValueTuple<,>)] = "SerializableValueTuple<{0}, {1}>",
            [typeof(ValueTuple<,,>)] = "SerializableValueTuple<{0}, {1}, {2}>",
            [typeof(Tuple<,>)] = "SerializableValueTuple<{0}, {1}>",
            [typeof(Tuple<,,>)] = "SerializableValueTuple<{0}, {1}, {2}>",
            [typeof(KeyValuePair<,>)] = "SerializableValueTuple<{0}, {1}>",
            [typeof(Queue<>)] = "List<{0}>",
            [typeof(Stack<>)] = "List<{0}>",
            [typeof(LinkedList<>)] = "List<{0}>",
        };

        /// <summary>
        /// Finds the type to use instead of <paramref name="declared"/>.
        /// </summary>
        /// <param name="declared">The field's declared type.</param>
        /// <param name="standIn">Receives the replacement's readable name, or <c>null</c>.</param>
        /// <returns><c>true</c> when this package ships a stand-in for that type.</returns>
        /// <remarks>
        /// Answers for the generic <b>definition</b>, so a stand-in is found whatever the type
        /// arguments are, and the arguments are carried into the suggestion rather than dropped --
        /// <c>SerializableDictionary&lt;string, int&gt;</c> is a line a developer can paste, where
        /// <c>SerializableDictionary</c> is a hint they still have to finish.
        /// </remarks>
        public static bool TryGetStandIn(Type declared, out string standIn)
        {
            if (declared == null)
            {
                standIn = null;
                return false;
            }

            // An array or List<T> of an unserializable element is the element's problem, and naming
            // the element is what the developer can act on. Unity refuses to nest either one inside
            // the other at all, which SerializableList<T> is the answer to.
            if (declared.IsArray)
            {
                return TryGetStandIn(declared.GetElementType(), out standIn);
            }

            if (!declared.IsGenericType)
            {
                standIn = null;
                return false;
            }

            Type definition = declared.GetGenericTypeDefinition();
            Type[] arguments = declared.GetGenericArguments();

            if (definition == typeof(List<>))
            {
                return TryGetStandIn(arguments[0], out standIn);
            }

            if (!ByDefinition.TryGetValue(definition, out string template))
            {
                standIn = null;
                return false;
            }

            string[] names = new string[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                names[index] = Readable(arguments[index]);
            }

            standIn = string.Format(template, names);
            return true;
        }

        /// <summary>
        /// Writes a type the way a developer would write it in source.
        /// </summary>
        /// <param name="type">The type to name.</param>
        /// <returns>A readable name.</returns>
        public static string Readable(Type type)
        {
            if (type == null)
            {
                return "<null>";
            }

            if (type.IsArray)
            {
                return Readable(type.GetElementType()) + "[]";
            }

            if (!type.IsGenericType)
            {
                return type.Name;
            }

            StringBuilder builder = new();
            string name = type.Name;
            int tick = name.IndexOf('`');
            builder.Append(0 <= tick ? name.Substring(0, tick) : name);
            builder.Append('<');

            Type[] arguments = type.GetGenericArguments();
            for (int index = 0; index < arguments.Length; index++)
            {
                if (0 < index)
                {
                    builder.Append(", ");
                }

                builder.Append(Readable(arguments[index]));
            }

            builder.Append('>');
            return builder.ToString();
        }
    }
#endif
}

// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Core.Extension;
    using UnityEngine;

    /// <summary>
    /// Maps an enum value to a set of <see cref="Animator"/> boolean parameters so that only one
    /// state flag is enabled at a time. Helps drive complex state machines with strongly typed enums.
    /// </summary>
    /// <typeparam name="T">Specific enum type used to drive the animator parameters.</typeparam>
    [DataContract]
    public sealed class AnimatorEnumStateMachine<T>
        where T : struct, IConvertible, IComparable, IFormattable
    {
        private static readonly T[] Values = InitializeValues();

        /// <summary>
        /// Gets or sets the currently active enum value. Setting the value toggles the underlying
        /// boolean parameters so that only the matching state remains true.
        /// </summary>
        [DataMember]
        [JsonInclude]
        public T Value
        {
            get => _value;
            set
            {
                foreach (T possibleValue in Values)
                {
                    string valueName = possibleValue.ToString(CultureInfo.InvariantCulture);
                    if (_availableBools.Contains(valueName))
                    {
                        Animator.SetBool(valueName, Equals(value, possibleValue));
                    }
                }

                _value = value;
            }
        }

        /// <summary>
        /// Serialized helper used to record the enum name for persistence.
        /// </summary>
        [DataMember]
        [JsonInclude]
        private string Type => typeof(T).Name;

        /// <summary>
        /// Backing animator that exposes the boolean parameters backing the enum state.
        /// </summary>
        [JsonIgnore]
        [IgnoreDataMember]
        public readonly Animator Animator;

        [JsonIgnore]
        [IgnoreDataMember]
        private readonly HashSet<string> _availableBools = new();

        [JsonIgnore]
        [IgnoreDataMember]
        private T _value;

        /// <summary>
        /// Creates a state machine wrapper around the provided <see cref="Animator"/> and optionally
        /// initializes it with a default enum value.
        /// </summary>
        /// <param name="animator">Animator whose boolean parameters correspond to the enum entries.</param>
        /// <param name="defaultValue">Initial enum value to apply to the animator.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="animator"/> is null.</exception>
        public AnimatorEnumStateMachine(Animator animator, T defaultValue = default)
        {
            if (animator == null)
            {
                throw new ArgumentNullException(nameof(animator));
            }

            Animator = animator;

            foreach (AnimatorControllerParameter parameter in Animator.parameters)
            {
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger:
                        _availableBools.Add(parameter.name);
                        continue;
                    default:
                        continue;
                }
            }

            _value = defaultValue;
        }

        private static T[] InitializeValues()
        {
            Array enumValues = Enum.GetValues(typeof(T));
            T[] result = new T[enumValues.Length];
            for (int i = 0; i < enumValues.Length; i++)
            {
                result[i] = (T)enumValues.GetValue(i);
            }
            return result;
        }

        /// <summary>
        /// Serializes the state machine into JSON format for debugging and tooling scenarios.
        /// </summary>
        /// <returns>JSON representation of the current state.</returns>
        public override string ToString()
        {
            return this.ToJson();
        }
    }
}

// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// Counts how often a logging call site evaluates its receiver and its interpolated arguments.
    /// A <see cref="System.Diagnostics.ConditionalAttribute"/> method has its entire call site
    /// removed by the compiler, so both counts stay at zero in a file that defines none of the
    /// enabling symbols -- which is the only way to observe stripping from inside a test run.
    /// </summary>
    public static class LoggingCallSiteProbe
    {
        /// <summary>
        /// How many times <see cref="Receiver"/> has been read since the last <see cref="Reset"/>.
        /// </summary>
        public static int ReceiverEvaluations { get; private set; }

        /// <summary>
        /// How many times <see cref="Argument"/> has been read since the last <see cref="Reset"/>.
        /// </summary>
        public static int ArgumentEvaluations { get; private set; }

        private static Object Target;

        /// <summary>
        /// Points the probe at <paramref name="target"/> and zeroes both counters.
        /// </summary>
        /// <param name="target">The Unity Object <see cref="Receiver"/> yields.</param>
        public static void Reset(Object target)
        {
            Target = target;
            ReceiverEvaluations = 0;
            ArgumentEvaluations = 0;
        }

        /// <summary>
        /// A logging receiver whose evaluation is observable. Reading it is the side effect that a
        /// stripped call site must never perform.
        /// </summary>
        public static Object Receiver
        {
            get
            {
                ReceiverEvaluations++;
                return Target;
            }
        }

        /// <summary>
        /// A logging argument whose evaluation is observable. Reading it is the side effect that a
        /// stripped call site must never perform.
        /// </summary>
        public static int Argument
        {
            get
            {
                ArgumentEvaluations++;
                return ArgumentEvaluations;
            }
        }
    }
}

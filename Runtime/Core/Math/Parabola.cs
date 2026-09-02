// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Math
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Helper;
    using ProtoBuf;

    /// <summary>
    /// Represents a parabola defined by y = A*x^2 + B*x, with x-intercepts at 0 and Length.
    /// The parabola opens downward with its vertex at (Length/2, MaxHeight).
    /// </summary>
    /// <example>
    /// <code>
    /// var p = new Parabola(maxHeight: 5f, length: 10f);
    /// p.TryGetValueAtNormalized(0.5f, out float peak); // peak == 5
    /// </code>
    /// </example>
    [DataContract]
    [Serializable]
#pragma warning disable WPROTO030 // Served through ParabolaSurrogate by both serializers.
    [ProtoContract]
#pragma warning restore WPROTO030
    public readonly struct Parabola : IEquatable<Parabola>
    {
        /// <summary>
        /// The distance between the two x-intercepts (at x=0 and x=Length).
        /// </summary>
        [DataMember]
        [ProtoMember(1)]
        public readonly float Length;

        /// <summary>
        /// The coefficient of x^2 in the parabola equation y = A*x^2 + B*x.
        /// </summary>
        [DataMember]
        [ProtoMember(2)]
        public readonly float A;

        /// <summary>
        /// The coefficient of x in the parabola equation y = A*x^2 + B*x.
        /// </summary>
        [DataMember]
        [ProtoMember(3)]
        public readonly float B;

        /// <summary>
        /// The maximum height of the parabola (y-value at the vertex).
        /// </summary>
        [DataMember]
        [ProtoMember(4)]
        public readonly float MaxHeight;

        /// <summary>
        /// The x-coordinate of the vertex (always at Length/2).
        /// </summary>
        public float VertexX => Length * 0.5f;

        /// <summary>
        /// The vertex position of the parabola.
        /// </summary>
        public (float x, float y) Vertex => (VertexX, MaxHeight);

        /// <summary>
        /// The valid x-range for this parabola [0, Length].
        /// </summary>
        public (float min, float max) XRange => (0f, Length);

        /// <summary>
        /// Creates a Parabola that reaches a max height and has a specified length.
        /// </summary>
        /// <param name="maxHeight">Max height of parabola (must be greater than 0).</param>
        /// <param name="length">Length of parabola between x intercepts (must be greater than 0).</param>
        /// <exception cref="ArgumentException">Thrown when maxHeight or length are not positive.</exception>
        [JsonConstructor]
        public Parabola(float maxHeight, float length)
        {
            if (length <= 0f)
            {
                throw new ArgumentException(
                    $"Expected a length greater than 0, but found: {length:0.00}."
                );
            }

            if (maxHeight <= 0f)
            {
                throw new ArgumentException(
                    $"Expected a max height greater than 0, but found: {maxHeight:0.00}."
                );
            }

            Length = length;
            MaxHeight = maxHeight;

            /*
                For a parabola with intercepts at 0 and Length, and max height at Length/2:
                y = A*x^2 + B*x
                At x=0: y=0 (satisfied by having no constant term)
                At x=Length: y=0, so A*Length^2 + B*Length = 0, thus B = -A*Length
                At x=Length/2: y=maxHeight
                Substituting: maxHeight = A*(Length/2)^2 + B*(Length/2)
                             maxHeight = A*Length^2/4 - A*Length^2/2
                             maxHeight = -A*Length^2/4
                             A = -4*maxHeight/Length^2
            */
            A = -4f * maxHeight / (length * length);
            B = -A * length;
        }

        internal Parabola(float maxHeight, float length, float a, float b)
        {
            Length = length;
            MaxHeight = maxHeight;
            A = a;
            B = b;
        }

        /// <summary>
        /// Creates a Parabola from explicit coefficients.
        /// </summary>
        /// <param name="a">Coefficient of x^2.</param>
        /// <param name="b">Coefficient of x.</param>
        /// <param name="length">Length of parabola (must be greater than 0).</param>
        /// <exception cref="ArgumentException">Thrown when parameters would create an invalid parabola.</exception>
        public static Parabola FromCoefficients(float a, float b, float length)
        {
            if (length <= 0f)
            {
                throw new ArgumentException(
                    $"Expected a length greater than 0, but found: {length:0.00}."
                );
            }

            if (0f <= a)
            {
                throw new ArgumentException(
                    $"Expected a negative coefficient A (downward parabola), but found: {a:0.00}."
                );
            }

            // Verify that x=Length is an intercept: A*Length^2 + B*Length = 0
            float valueAtLength = a * length * length + b * length;
            if (1e-5f < Math.Abs(valueAtLength))
            {
                throw new ArgumentException(
                    $"Coefficients do not produce a parabola with intercept at x={length:0.00}. "
                        + $"Value at x=Length is {valueAtLength:0.00}, expected ~0."
                );
            }

            /*
                Calculate max height from coefficients
                Vertex x-coordinate: -B/(2A)
                For our parabola with intercepts at 0 and Length, vertex should be at Length/2
            */
            float vertexX = -b / (2f * a);
            float maxHeight = a * vertexX * vertexX + b * vertexX;

            if (maxHeight <= 0f)
            {
                throw new ArgumentException(
                    $"Calculated max height is not positive: {maxHeight:0.00}."
                );
            }

            return new Parabola(maxHeight, length, a, b);
        }

        /// <summary>
        /// Evaluates the parabola at a given x-coordinate.
        /// </summary>
        /// <param name="x">The x-coordinate (must be in range [0, Length]).</param>
        /// <param name="y">The resulting y-value, or NaN if x is out of range.</param>
        /// <returns>True if x is within valid range, false otherwise.</returns>
        public bool TryGetValueAt(float x, out float y)
        {
            if (x < 0f || Length < x)
            {
                y = float.NaN;
                return false;
            }

            y = A * (x * x) + B * x;
            return true;
        }

        /// <summary>
        /// Evaluates the parabola at a normalized position.
        /// </summary>
        /// <param name="t">Normalized position along the parabola [0, 1].</param>
        /// <param name="y">The resulting y-value, or NaN if t is out of range.</param>
        /// <returns>True if t is within valid range, false otherwise.</returns>
        public bool TryGetValueAtNormalized(float t, out float y)
        {
            if (t < 0f || 1f < t)
            {
                y = float.NaN;
                return false;
            }

            return TryGetValueAt(t * Length, out y);
        }

        /// <summary>
        /// Gets the value at a given x-coordinate without bounds checking.
        /// </summary>
        /// <param name="x">The x-coordinate.</param>
        /// <returns>The y-value at x.</returns>
        public float GetValueAtUnchecked(float x)
        {
            return A * (x * x) + B * x;
        }

        public bool Equals(Parabola other)
        {
            return Length.Equals(other.Length)
                && A.Equals(other.A)
                && B.Equals(other.B)
                && MaxHeight.Equals(other.MaxHeight);
        }

        public override bool Equals(object obj)
        {
            return obj is Parabola other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(Length, A, B, MaxHeight);
        }

        public static bool operator ==(Parabola left, Parabola right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Parabola left, Parabola right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"Parabola(maxHeight={MaxHeight:0.00}, length={Length:0.00}, vertex=({VertexX:0.00}, {MaxHeight:0.00}))";
        }
    }
}

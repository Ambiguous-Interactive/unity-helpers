// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// The pieces of rectangular-array handling that would otherwise be duplicated into every
    /// generated wrapper message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rectangular array is the one collection shape that cannot be reconstructed from its
    /// elements. <c>int[][]</c> carries its own structure -- each row is a separate run whose length
    /// is on the wire -- while <c>int[2,3]</c> and <c>int[3,2]</c> deliver the same six values in the
    /// same order, so the dimensions have to travel with them. The wrapper message is therefore
    /// <c>message Rect { repeated int32 dims = 1; repeated T values = 2; }</c>, which maps onto a
    /// concrete proto3 definition rather than onto a private convention.
    /// </para>
    /// <para>
    /// <b>The dimensions are a claim, and this is where it is refused.</b> A length prefix is safe to
    /// allocate from because the reader will not accept one longer than the bytes it holds; a
    /// dimension header has no such backing, and six bytes can state <c>[46341, 46341]</c> and ask
    /// for 8 GB. <see cref="TryAcceptShape"/> answers that by requiring the product of the
    /// dimensions -- computed in <see cref="long"/> by the caller so it cannot overflow into
    /// something plausible -- to equal the number of elements the payload actually delivered. That is
    /// stronger than clamping it to <see cref="SerializationCapacityLimits.MaximumRestoredCapacity"/>
    /// and needs no ceiling of its own: the allocation is backed one-for-one by elements the sender
    /// already paid for in bytes, so a claim that outruns its data is refused outright rather than
    /// quietly honored up to a limit. It also catches the inverse -- a header that under-states its
    /// data -- which a ceiling would let through and which would silently drop elements.
    /// </para>
    /// </remarks>
    public static class WProtoRectangular
    {
        /// <summary>
        /// The value <see cref="MultiplyDimensions"/> saturates to, one past the largest element
        /// count any array can have.
        /// </summary>
        /// <remarks>
        /// A delivered element count is an <see cref="int"/>, so a product at or above this can
        /// never equal one and saturating here loses nothing a caller could have used.
        /// </remarks>
        public const long TooManyElements = (long)int.MaxValue + 1;

        /// <summary>
        /// Multiplies a running element count by one more dimension, saturating instead of wrapping.
        /// </summary>
        /// <param name="total">The product of the dimensions so far; never negative.</param>
        /// <param name="dimension">The next dimension; never negative.</param>
        /// <returns>The product, or <see cref="TooManyElements"/> when it would exceed that.</returns>
        /// <remarks>
        /// <para>
        /// <b>Widening to <see cref="long"/> is not enough on its own, and that is the whole reason
        /// this exists.</b> Three <see cref="int"/> dimensions multiply to as much as 2^93, C#
        /// arithmetic wraps silently, and an attacker picks the wrap: <c>[2^22, 2^21, 2^21]</c> is
        /// exactly 2^64, so the wrapped product is <b>zero</b> -- which matches an empty element run,
        /// passes <see cref="TryAcceptShape"/>, and allocates an eight-exabyte array from sixteen
        /// bytes.
        /// </para>
        /// <para>
        /// Zero is preserved exactly rather than saturated, because a zero-length dimension is a real
        /// shape: <c>new int[0, 5]</c> has no elements and must still round-trip.
        /// </para>
        /// </remarks>
        public static long MultiplyDimensions(long total, int dimension)
        {
            if (total < 0 || dimension < 0)
            {
                return TooManyElements;
            }

            if (total == 0 || dimension == 0)
            {
                return 0;
            }

            if (TooManyElements / dimension < total)
            {
                return TooManyElements;
            }

            return total * dimension;
        }

        /// <summary>
        /// Indicates whether a payload's dimension header may be honored.
        /// </summary>
        /// <param name="rank">The member's declared rank, known at compile time.</param>
        /// <param name="dimensionCount">How many dimensions the payload stated.</param>
        /// <param name="declaredElements">
        /// The product of those dimensions, accumulated by the caller through
        /// <see cref="MultiplyDimensions"/> so that it saturates rather than wrapping. Every
        /// dimension has already been refused if negative, so this is never negative.
        /// </param>
        /// <param name="deliveredElements">How many elements the payload actually carried.</param>
        /// <param name="largestDimension">
        /// The largest single dimension the payload stated, folded by the caller alongside the
        /// product.
        /// </param>
        /// <returns><c>true</c> when the array may be allocated and filled.</returns>
        /// <remarks>
        /// <para>
        /// The first two halves are refusals of a malformed payload rather than of a large one, so
        /// neither has a configurable bound. A rank the member does not have cannot be reshaped into
        /// one that it does, and a product that disagrees with the delivered count describes an array
        /// this payload does not contain.
        /// </para>
        /// <para>
        /// <b>The third exists because the product stops bounding the axes as soon as one of them is
        /// zero.</b> <c>[int.MaxValue, 0]</c> has a product of zero, matches an empty element run
        /// exactly, and is a shape the payload paid ten bytes for -- and <c>new int[2147483647, 0]</c>
        /// then throws <see cref="OutOfMemoryException"/> out of a method whose whole contract is to
        /// report failure by returning <c>false</c>. So a single axis is a capacity claim in its own
        /// right and is refused as one.
        /// </para>
        /// <para>
        /// It cannot refuse a shape whose elements exist: with every axis at one or more, each axis
        /// divides the product, so each is at most the delivered count and
        /// <see cref="SerializationCapacityLimits.TryAccept"/> raises its ceiling to exactly that.
        /// This check is therefore <b>unreachable for any array that carries data</b>, and applies
        /// only to the empty shapes whose axes nothing pays for. That holds however low
        /// <see cref="SerializationCapacityLimits.MaximumRestoredCapacity"/> is set, because the
        /// delivered count is the floor rather than the cap.
        /// </para>
        /// <para>
        /// The consequence, stated rather than hidden: an <b>empty</b> array whose other axis
        /// exceeds <see cref="SerializationCapacityLimits.MaximumRestoredCapacity"/> -- a
        /// <c>new int[2000000, 0]</c> at the default of 1,048,576 -- no longer round-trips, and a
        /// game that genuinely persists one raises that limit, which is a decision its own code
        /// makes rather than one a payload makes.
        /// </para>
        /// </remarks>
        public static bool TryAcceptShape(
            int rank,
            int dimensionCount,
            long declaredElements,
            int deliveredElements,
            int largestDimension
        )
        {
            if (dimensionCount != rank || declaredElements != deliveredElements)
            {
                return false;
            }

            return SerializationCapacityLimits.TryAccept(
                largestDimension,
                deliveredElements,
                out int _
            );
        }

        /// <summary>
        /// Folds one more dimension into the largest seen so far.
        /// </summary>
        /// <param name="largest">The largest dimension so far.</param>
        /// <param name="dimension">The next dimension.</param>
        /// <returns>The larger of the two.</returns>
        /// <remarks>
        /// Emitted rather than <c>Math.Max</c> so the generated fold reads the same way the product's
        /// does, and so the two are changed together.
        /// </remarks>
        public static int LargerDimension(int largest, int dimension)
        {
            return largest < dimension ? dimension : largest;
        }

        /// <summary>
        /// Builds the exception thrown when a rectangular array does not start at index zero.
        /// </summary>
        /// <param name="contract">The declaring contract's name, for the message.</param>
        /// <param name="arrayType">The rectangular array's type name, for the message.</param>
        /// <param name="dimension">The dimension whose lower bound is not zero.</param>
        /// <param name="lowerBound">The lower bound that dimension actually has.</param>
        /// <returns>The exception to throw.</returns>
        /// <remarks>
        /// <para>
        /// Returned rather than thrown so the call site reads
        /// <c>throw WProtoRectangular.NonZeroLowerBound(…)</c> and the compiler treats the following
        /// code as unreachable.
        /// </para>
        /// <para>
        /// Only <see cref="Array.CreateInstance(Type, int[], int[])"/> produces one of these, and
        /// nothing on the wire can express it: the header carries lengths, and reading rebuilds the
        /// array with <c>new T[a, b]</c>, whose indices start at zero. Writing it anyway would hand
        /// back every element under different indices, which is a silent change to what the caller
        /// stored. protobuf-net refuses the whole shape on both supported majors, so this refuses
        /// strictly less than the oracle does.
        /// </para>
        /// </remarks>
        public static Exception NonZeroLowerBound(
            string contract,
            string arrayType,
            int dimension,
            int lowerBound
        )
        {
            return new InvalidOperationException(
                $"A '{arrayType}' inside '{contract}' starts dimension {dimension} at index "
                    + $"{lowerBound} rather than 0. The encoding carries the length of each dimension, "
                    + "and reading rebuilds the array with 'new T[...]', whose indices start at zero "
                    + "-- so writing this one would hand every element back under a different index. "
                    + "Copy it into an array created with 'new' rather than with "
                    + "'Array.CreateInstance(type, lengths, lowerBounds)'."
            );
        }
    }
}

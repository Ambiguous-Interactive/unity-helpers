// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System.Collections.Generic;

    /// <summary>
    /// Helpers for iterating over multidimensional arrays with tuples or buffered lists.
    /// </summary>
    public static class IterationHelpers
    {
        /// <summary>
        /// Enumerates all (i, j) indices of a 2D array.
        /// </summary>
        public static IEnumerable<(int, int)> IndexOver<T>(this T[,] array)
        {
            int firstDimensionLength = array.GetLength(0);
            int secondDimensionLength = array.GetLength(1);
            for (int i = 0; i < firstDimensionLength; i++)
            {
                for (int j = 0; j < secondDimensionLength; j++)
                {
                    yield return (i, j);
                }
            }
        }

        /// <summary>
        /// Fills a buffer with all (i, j) indices of a 2D array and returns the same buffer.
        /// </summary>
        public static List<(int, int)> IndexOver<T>(this T[,] array, List<(int, int)> buffer)
        {
            buffer.Clear();
            int firstDimensionLength = array.GetLength(0);
            int secondDimensionLength = array.GetLength(1);
            for (int i = 0; i < firstDimensionLength; i++)
            {
                for (int j = 0; j < secondDimensionLength; j++)
                {
                    (int i, int j) tuple = (i, j);
                    buffer.Add(tuple);
                }
            }

            return buffer;
        }

        /// <summary>
        /// Enumerates all (i, j, k) indices of a 3D array.
        /// </summary>
        public static IEnumerable<(int, int, int)> IndexOver<T>(this T[,,] array)
        {
            int firstDimensionLength = array.GetLength(0);
            int secondDimensionLength = array.GetLength(1);
            int thirdDimensionLength = array.GetLength(2);
            for (int i = 0; i < firstDimensionLength; i++)
            {
                for (int j = 0; j < secondDimensionLength; j++)
                {
                    for (int k = 0; k < thirdDimensionLength; k++)
                    {
                        yield return (i, j, k);
                    }
                }
            }
        }

        /// <summary>
        /// Fills a buffer with all (i, j, k) indices of a 3D array and returns the same buffer.
        /// </summary>
        public static List<(int, int, int)> IndexOver<T>(
            this T[,,] array,
            List<(int, int, int)> buffer
        )
        {
            buffer.Clear();
            int firstDimensionLength = array.GetLength(0);
            int secondDimensionLength = array.GetLength(1);
            int thirdDimensionLength = array.GetLength(2);
            for (int i = 0; i < firstDimensionLength; i++)
            {
                for (int j = 0; j < secondDimensionLength; j++)
                {
                    for (int k = 0; k < thirdDimensionLength; k++)
                    {
                        (int i, int j, int k) tuple = (i, j, k);
                        buffer.Add(tuple);
                    }
                }
            }

            return buffer;
        }
    }
}

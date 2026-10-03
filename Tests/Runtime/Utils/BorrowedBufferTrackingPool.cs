// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;

    internal sealed class BorrowedBufferTrackingPool<T> : ArrayPool<T>
    {
        public int RentCount { get; private set; }
        public int ReturnCount { get; private set; }
        public int ActiveCount => _active.Count;
        public bool LastClearOnReturn { get; private set; }
        public T[] LastRented { get; private set; }
        public T[] LastReturned { get; private set; }
        public Exception RentFailure { get; set; }
        public Exception ReturnFailure { get; set; }
        public bool FailBeforeRelease { get; set; }
        public Action<T[]> InitializeRental { get; set; }

        private readonly HashSet<T[]> _active = new HashSet<T[]>();
        private readonly T _initialValue;

        public BorrowedBufferTrackingPool(T initialValue)
        {
            _initialValue = initialValue;
        }

        public override T[] Rent(int minimumLength)
        {
            ++RentCount;
            if (RentFailure != null)
            {
                throw RentFailure;
            }
            T[] array = new T[minimumLength + 5];
            for (int index = 0; index < array.Length; ++index)
            {
                array[index] = _initialValue;
            }
            InitializeRental?.Invoke(array);
            _active.Add(array);
            LastRented = array;
            return array;
        }

        public override void Return(T[] array, bool clearArray = false)
        {
            ++ReturnCount;
            LastReturned = array;
            LastClearOnReturn = clearArray;
            if (FailBeforeRelease && ReturnFailure != null)
            {
                throw ReturnFailure;
            }
            if (!_active.Remove(array))
            {
                throw new InvalidOperationException("Foreign or duplicate return.");
            }
            if (clearArray)
            {
                Array.Clear(array, 0, array.Length);
            }
            if (ReturnFailure != null)
            {
                throw ReturnFailure;
            }
        }
    }
}

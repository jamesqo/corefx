// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Collections.Generic
{
    /// <summary>
    /// A circular buffer with a bounded capacity.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    internal struct CircularBuffer<T> : IDisposable
    {
        /// <summary>
        /// The initial capacity of this buffer after one item is added.
        /// </summary>
        private const int InitialCapacity = 4;

        /// <summary>
        /// The maximum number of items that can fit in one array.
        /// </summary>
        private const int MaxCoreClrArrayLength = 0x7fefffff;

        /// <summary>
        /// The maximum capacity this buffer can have.
        /// </summary>
        private readonly int _maxCapacity;

        /// <summary>
        /// The underlying array.
        /// </summary>
        private T[] _array;

        /// <summary>
        /// The next index to be overwritten.
        /// </summary>
        private int _index;

        /// <summary>
        /// Initializes a new instance of the <see cref="CircularBuffer{T}"/> struct.
        /// </summary>
        /// <param name="maxCapacity">The maximum capacity this buffer can have.</param>
        private CircularBuffer(int maxCapacity)
        {
            Debug.Assert(maxCapacity > 0);

            _maxCapacity = maxCapacity;
            _array = Array.Empty<T>();
            _index = 0;
        }

        /// <summary>
        /// Gets the capacity of this buffer.
        /// </summary>
        private int Capacity => _array.Length;

        /// <summary>
        /// Adds an item to this buffer.
        /// </summary>
        /// <param name="item">The item.</param>
        public void Add(T item)
        {
            Debug.Assert(_index < _maxCapacity);

            if (_index == Capacity)
            {
                Resize();
            }

            _array[_index++] = item;
        }

        /// <summary>
        /// Frees the resources associated with this buffer.
        /// </summary>
        /// <remarks>
        /// This method will allow the GC to reclaim the underlying array, even if the
        /// <see cref="CircularBuffer{T}"/> is still GC-reachable.
        /// </remarks>
        public void Dispose()
        {
            _array = null;
        }

        /// <summary>
        /// Replaces the oldest item in this buffer with a new one.
        /// </summary>
        /// <param name="newItem">The new item.</param>
        /// <param name="oldItem">The old item that was replaced.</param>
        public void Exchange(T newItem, out T oldItem)
        {
            Debug.Assert(Capacity == _maxCapacity);

            if (_index == Capacity)
            {
                _index = 0;
            }

            oldItem = _array[_index];
            _array[_index++] = newItem;
        }

        /// <summary>
        /// Resizes the buffer so there is room for at least one more item.
        /// </summary>
        private void Resize()
        {
            Debug.Assert(_index == Capacity);
            Debug.Assert(Capacity < _maxCapacity);

            int capacity = Capacity;
            int nextCapacity = capacity == 0 ? InitialCapacity : capacity * 2;

            if ((uint)nextCapacity > (uint)MaxCoreClrArrayLength)
            {
                nextCapacity = Math.Max(capacity + 1, MaxCoreClrArrayLength);
            }

            nextCapacity = Math.Min(nextCapacity, _maxCapacity);

            T[] next = new T[nextCapacity];
            if (_index > 0)
            {
                Array.Copy(_array, 0, next, 0, _index);
            }
            _array = next;
        }
    }
}

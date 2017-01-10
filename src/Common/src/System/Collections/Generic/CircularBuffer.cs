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
        /// The maximum number of items that can fit in one array on CoreCLR.
        /// </summary>
        /// <remarks>
        /// For byte arrays, the limit is slightly larger.
        /// </remarks>
        private const int MaxCoreClrArrayLength = 0x7fefffff;

        /// <summary>
        /// The underlying array.
        /// </summary>
        private T[] _array;

        /// <summary>
        /// The next index to be overwritten.
        /// </summary>
        private int _index;

#if DEBUG
        /// <summary>
        /// Ensures callers do not use certain methods after <see cref="Replace"/> is called.
        /// </summary>
        private bool _hasReplaced;
#endif

        /// <summary>
        /// Initializes a new instance of the <see cref="CircularBuffer{T}"/> struct.
        /// </summary>
        /// <param name="maxCapacity">The maximum capacity this buffer can have.</param>
        public CircularBuffer(int maxCapacity) : this()
        {
            Debug.Assert(maxCapacity > 0);

            MaxCapacity = maxCapacity;
            _array = Array.Empty<T>();
        }

        /// <summary>
        /// Gets the capacity of this buffer.
        /// </summary>
        private int Capacity => _array.Length;

        /// <summary>
        /// Gets the number of items added to this buffer.
        /// This is only valid before <see cref="Replace"/> is called.
        /// </summary>
        public int Count
        {
            get
            {
                Debug.Assert(!HasReplaced);
                return _index;
            }
        }

        /// <summary>
        /// Gets or sets whether <see cref="Replace"/> has been called. This can only be set in Debug builds.
        /// </summary>
#if DEBUG
        private bool HasReplaced
        {
            get { return _hasReplaced; }
            set
            {
                Debug.Assert(value);
                _hasReplaced = true;
            }
        }
#else
        private bool HasReplaced
        {
            get { throw new InvalidOperationException(); }
            set { }
        }
#endif

        /// <summary>
        /// The maximum capacity this buffer can have.
        /// </summary>
        private int MaxCapacity { get; }

        /// <summary>
        /// Adds an item to this buffer.
        /// </summary>
        /// <param name="item">The item.</param>
        public void Add(T item)
        {
            Debug.Assert(_index < MaxCapacity);
            Debug.Assert(!HasReplaced);

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
            Debug.Assert(HasReplaced);

            _array = null;
        }

        /// <summary>
        /// Replaces the oldest item in this buffer with a new one.
        /// </summary>
        /// <param name="newItem">The new item.</param>
        /// <returns>The old item that was replaced.</returns>
        public T Replace(T newItem)
        {
            Debug.Assert(_index > 0);
            Debug.Assert(Capacity == MaxCapacity);
            HasReplaced = true;

            if (_index == Capacity)
            {
                _index = 0;
            }

            T oldItem = _array[_index];
            _array[_index++] = newItem;
            return oldItem;
        }

        /// <summary>
        /// Creates an array from this buffer.
        /// </summary>
        /// <returns>An array with the contents of this buffer.</returns>
        public T[] ToArray()
        {
            Debug.Assert(_index > 0);
            Debug.Assert(Capacity == MaxCapacity);
            Debug.Assert(HasReplaced);

            int capacity = Capacity;
            var array = new T[capacity];

            int firstPart = capacity - _index;
            if (firstPart > 0)
            {
                Array.Copy(_array, _index, array, 0, firstPart);
            }

            Array.Copy(_array, 0, array, firstPart, _index);
            return array;
        }

        /// <summary>
        /// Resizes the buffer so there is room for at least one more item.
        /// </summary>
        private void Resize()
        {
            Debug.Assert(_index == Capacity);
            Debug.Assert(Capacity < MaxCapacity);
            Debug.Assert(!HasReplaced);

            int capacity = Capacity;
            int nextCapacity = capacity == 0 ? InitialCapacity : capacity * 2;

            if ((uint)nextCapacity > (uint)MaxCoreClrArrayLength)
            {
                nextCapacity = Math.Max(capacity + 1, MaxCoreClrArrayLength);
            }

            nextCapacity = Math.Min(nextCapacity, MaxCapacity);

            T[] next = new T[nextCapacity];
            if (_index > 0)
            {
                Array.Copy(_array, 0, next, 0, _index);
            }
            _array = next;
        }
    }
}

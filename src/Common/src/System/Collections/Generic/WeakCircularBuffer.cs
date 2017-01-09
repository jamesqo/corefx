// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Collections.Generic
{
    /// <summary>
    /// A circular buffer in which the oldest items are automatically overwritten.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    internal struct WeakCircularBuffer<T> : IDisposable
    {
        /// <summary>
        /// The underlying array for this buffer.
        /// </summary>
        private T[] _array;

        /// <summary>
        /// The next index that will be overwritten.
        /// </summary>
        private int _index;

        /// <summary>
        /// Constructs a circular buffer that is positioned at index 0.
        /// </summary>
        /// <param name="array">The underlying array for this buffer.</param>
        public WeakCircularBuffer(T[] array)
        {
            Debug.Assert(array?.Length > 0);

            _array = array;
            _index = 0;
        }

        /// <summary>
        /// Frees the resources associated with this buffer.
        /// </summary>
        /// <remarks>
        /// This method will allow the GC to reclaim the underlying array, even if the
        /// <see cref="WeakCircularBuffer{T}"/> is still GC-reachable.
        /// </remarks>
        public void Dispose()
        {
            _array = null;
        }

        /// <summary>
        /// Exchanges an item with the oldest item in this buffer.
        /// </summary>
        /// <param name="item">The item, to place in this buffer.</param>
        /// <param name="oldest">The oldest item, to evict from this buffer.</param>
        public void Exchange(T item, out T oldest)
        {
            if (_index == _array.Length)
            {
                _index = 0;
            }

            oldest = _array[_index];
            _array[_index++] = item;
        }
    }
}

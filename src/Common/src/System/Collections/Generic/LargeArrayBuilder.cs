// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Collections.Generic
{
    internal struct LargeArrayBuilder<T>
    {
        private const int StartingCapacity = 4;
        private const int ResizeLimit = 32;
        private const int Log2ResizeLimit = 5;

        private T[] _first;                // The first buffer we store items in. Resized until ResizeLimit.
        private ArrayBuilder<T[]> _others; // After ResizeLimit, we store subsequent items in buffers here.
        private T[] _current;              // Current buffer we're reading into.
        private int _index;                // Index into the current buffer.
        private int _count;                // Count of all of the items in this builder.

        public LargeArrayBuilder(bool initialize) : this()
        {
            // This is a workaround for C# not having parameterless struct constructors yet.
            // Once it gets them, replace this with a parameterless constructor.
            Debug.Assert(initialize);

            _first = _current = Array.Empty<T>();
        }

        public int Count => _count;

        public void Add(T item)
        {
            if (_index == _current.Length)
            {
                AllocateBuffer();
            }

            _current[_index++] = item;
            _count++;
        }

        public void AddRange(IEnumerable<T> items)
        {
            Debug.Assert(items != null);

            using (IEnumerator<T> enumerator = items.GetEnumerator())
            {
                AddRange(enumerator);
            }
        }

        public void AddRange(IEnumerator<T> enumerator)
        {
            T[] destination = _current;
            int index = _index;

            // Continuously read in items from the enumerator, updating _count
            // and _index when we run out of space.

            while (enumerator.MoveNext())
            {
                if (index == destination.Length)
                {
                    // No more space in this buffer. Resize.
                    _count += index - _index;
                    _index = index;
                    destination = AllocateBuffer();
                    index = _index; // May have been reset to 0
                }

                destination[index++] = enumerator.Current;
            }

            // Final update to _count and _index.
            _count += index - _index;
            _index = index;
        }

        public void CopyTo(T[] array, int arrayIndex, int count)
        {
            Debug.Assert(array != null);
            Debug.Assert(arrayIndex >= 0);
            Debug.Assert(count >= 0 && count <= Count);
            Debug.Assert(array.Length - arrayIndex >= count);
            
            for (int i = -1; count > 0; i++)
            {
                // Find the buffer we're copying from.
                T[] buffer = i < 0 ? _first : _others[i];
                
                // Copy until we satisfy count, or we reach the end of the buffer.
                int toCopy = Math.Min(count, buffer.Length);
                Array.Copy(buffer, 0, array, arrayIndex, toCopy);

                // Increment variables to that position.
                count -= toCopy;
                arrayIndex += toCopy;
            }
        }

        public T[] ToArray()
        {
            if (_count == 0)
            {
                return Array.Empty<T>();
            }

            var array = new T[_count];
            CopyTo(array, 0, _count);
            return array;
        }
        
        private T[] AllocateBuffer()
        {
            // - On the first few adds, simply resize _first.
            // - When we pass ResizeLimit, read in subsequent items to buffers in _others
            //   instead of resizing further. When we allocate a new buffer, add it to _others
            //   and reset _index to 0.
            // - Store the result of this in _current and return it.

            Debug.Assert(_index == _current.Length, $"{nameof(AllocateBuffer)} was called, but there's more space.");

            T[] result;

            if (_count < ResizeLimit)
            {
                // We haven't passed ResizeLimit. Resize _first, copying over the previous items.
                Debug.Assert(_current == _first);

                int nextCapacity = _count == 0 ? StartingCapacity : _first.Length * 2;

                result = new T[nextCapacity];
                Array.Copy(_first, 0, result, 0, _first.Length);
                _first = result;
            }
            else
            {
                // We're adding to a buffer in _others.
                // If we just transitioned from resizing, allocate a buffer with the same capacity
                // as _first. Otherwise, allocate a buffer with twice the capacity as the last one.
                int nextCapacity = _count == ResizeLimit ? ResizeLimit : _current.Length * 2;

                result = new T[nextCapacity];
                _others.Add(result);
                _index = 0;
            }

            _current = result;
            return result;
        }
    }
}

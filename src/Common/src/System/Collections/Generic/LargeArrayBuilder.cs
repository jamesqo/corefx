// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Collections.Generic
{
    internal struct Hop
    {
        internal Hop(int count, int index)
        {
            Debug.Assert(count >= 0 && index >= 0);
            Count = count;
            Index = index;
        }

        public int Count { get; } // How many slots to hop over.
        public int Index { get; } // Index into the builder's buffers (excl. other hops) we're into.
    }

    internal struct LargeArrayBuilder<T>
    {
        private const int StartingCapacity = 4;
        private const int ResizeLimit = 32;
        private const int Log2ResizeLimit = 5;

        private T[] _first;                // The first buffer we store items in. Resized until ResizeLimit.
        private ArrayBuilder<T[]> _others; // After ResizeLimit, we store subsequent items in buffers here.
        private ArrayBuilder<Hop> _hops;   // 'Hops' we have to make in the array produced in ToArray.
        private T[] _current;              // Current buffer we're reading into.
        private int _index;                // Index into the current buffer.
        private int _count;                // Count of all of the items in this builder, excluding hops.

        public LargeArrayBuilder(bool initialize) : this()
        {
            // This is a workaround for C# not having parameterless struct constructors yet.
            // Once it gets them, replace this with a parameterless constructor.
            Debug.Assert(initialize);

            _first = _current = Array.Empty<T>();
        }

        public int Count => _count;

        public ArrayBuilder<Hop>.View Hops => _hops.AsView();

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
            Debug.Assert(!(items is ICollection<T>), $"For collections, use the {nameof(Hop)} api instead.");

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

        public void CopyAdded(int sourceIndex, T[] destination, int destinationIndex, int count)
        {
            Debug.Assert(sourceIndex >= 0 && destination != null);
            Debug.Assert(destinationIndex >= 0 && count >= 0);
            Debug.Assert(Count - sourceIndex >= count);
            Debug.Assert(destination.Length - destinationIndex >= count);

            while (count > 0)
            {
                // Find the buffer and actual index associated with the index.
                int realIndex;
                T[] buffer = GetBufferFromIndex(sourceIndex, out realIndex);
                
                // Copy until we satisfy count, or we reach the end of the buffer.
                int toCopy = Math.Min(count, buffer.Length);
                Array.Copy(buffer, realIndex, destination, destinationIndex, toCopy);

                // Increment variables to that position.
                count -= toCopy;
                sourceIndex += toCopy;
                destinationIndex += toCopy;
            }
        }

        public void Hop(int count)
        {
            _hops.Add(new Hop(count: count, index: _count));
        }

        public T[] ToArray()
        {
            int count = GetCountIncludingHops();

            if (count == 0)
            {
                return Array.Empty<T>();
            }

            var array = new T[count];
            int thisIndex = 0;
            int arrayIndex = 0;

            for (int i = 0; i < _hops.Count; i++)
            {
                Hop hop = _hops[i];

                // Copy up to the index represented by the hop.
                int toCopy = hop.Index - thisIndex;
                if (toCopy > 0) // Can be 0 when 2 hops are added in a row, so they have the same indices and the segment between them is 0.
                {
                    CopyAdded(thisIndex, array, arrayIndex, toCopy);
                    thisIndex += toCopy;
                    arrayIndex += toCopy;
                }

                // Skip the hop.
                arrayIndex += hop.Count;
                // Since hops are not represented in our buffers, we don't
                // have to touch thisIndex here.
            }

            // Copy the segment after the final hop.
            int finalCopy = _count - thisIndex;
            if (finalCopy > 0) // Again, can happen if a hop was added last.
            {
                CopyAdded(thisIndex, array, arrayIndex, finalCopy);
            }

            Debug.Assert(arrayIndex + finalCopy == count, "We should have finished copying to all the slots in the array.");

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

        private int GetCountIncludingHops()
        {
            int result = _count;

            for (int i = 0; i < _hops.Count; i++)
            {
                result += _hops[i].Count;
            }

            return result;
        }

        private T[] GetBufferFromIndex(int index, out int realIndex)
        {
            Debug.Assert(index >= 0 && index < Count);
            Debug.Assert(_first != null); // Otherwise, Count == 0 and the first 2 conditions are disjoint

            if (index < 32)
            {
                realIndex = index;
                return _first;
            }

            // index rounded to the previous pow of 2, log2, - log2(ResizeLimit) determines what buffer we'll go to.
            // The distance it is from this power is the actual index.

            /*  Example cases:
                index == 32  -> [0], 0
                index == 63  -> [0], 31
                index == 64  -> [1], 0
                index == 127 -> [1], 63
                index == 128 -> [2], 0
                index == 255 -> [2], 127
            */

            int exponent = FloorLog2(index);
            int powerOfTwo = 1 << exponent;
            Debug.Assert(exponent >= Log2ResizeLimit && powerOfTwo <= index);

            realIndex = index - powerOfTwo;
            return _others[exponent - Log2ResizeLimit];
        }

        private static int FloorLog2(int value)
        {
            Debug.Assert(value > 0);
            
            // TODO: It may be possible to improve perf here using something
            // from http://graphics.stanford.edu/~seander/bithacks.html

            // We want the jit to generate code for unsigned, not arithmetic, right shifts.
            uint ui = (uint)value;
            int result = -1;

            do
            {
                ui >>= 1;
                result++;
            }
            while (ui > 0);

            return result;
        }
    }
}

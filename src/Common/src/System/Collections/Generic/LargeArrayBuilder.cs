// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Collections.Generic
{
    internal struct Hop
    {
        internal int _index; // Index into the builder's buffers (not incl. other hops) we're into.
        internal int _count; // How many slots to hop over.

        public int Count => _count;
        public int Index => _index;
    }

    internal struct LargeArrayBuilder<T>
    {
        private const int StartingCapacity = 4;
        private const int ResizeLimit = 32;
        private const int Log2ResizeLimit = 5;

        private T[] _first;                // The first buffer we store items in. Resized until ResizeLimit.
        private ArrayBuilder<T[]> _others; // After ResizeLimit, we store subsequent items in buffers here.
        private ArrayBuilder<Hop> _hops;   // 'Hops' we have to make in the array produced in ToArray.
        private int _index;                // Index into the current buffer we're reading into.
        private int _count;                // Count of all of the items in this builder, excluding hops.

        public int Count => _count;

        public ArrayBuilder<Hop>.View Hops => _hops.AsView();

        public void Add(T item)
        {
            T[] destination = GetAddBuffer();

            destination[_index++] = item;
            _count++;
        }

        public void CopyAdded(int sourceIndex, T[] destination, int destinationIndex, int count)
        {
            Debug.Assert(sourceIndex >= 0 && destination != null);
            Debug.Assert(destinationIndex >= 0 && count >= 0);
            Debug.Assert(Count - sourceIndex >= count);
            Debug.Assert(destination.Length - destinationIndex >= count);

            while (count > 0)
            {
                int realIndex;
                T[] buffer = GetLocationFromIndex(sourceIndex, out realIndex);
                
                // Copy until we satisfy count, or we reach the end of the buffer.
                int toCopy = Math.Min(count, buffer.Length);
                Debug.Assert(toCopy > 0);
                Array.Copy(buffer, realIndex, destination, destinationIndex, toCopy);

                count -= toCopy;
                sourceIndex += toCopy;
                destinationIndex += toCopy;
            }
        }

        public void Hop(int count)
        {
            Debug.Assert(count >= 0);

            _hops.Add(new Hop { _index = _count, _count = count });
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
                int toCopy = hop._index - thisIndex;
                if (toCopy > 0) // Can be 0 when 2 hops are added in a row, so they have the same indices and the segment between them is 0.
                {
                    CopyAdded(thisIndex, array, arrayIndex, toCopy);
                    thisIndex += toCopy;
                    arrayIndex += toCopy;
                }

                // Skip the hop.
                arrayIndex += hop._count;
                // Since hops are not represented in our buffers, we don't
                // have to touch thisIndex here.
            }

            // Copy the segment after the final hop.
            int finalCopy = _count - thisIndex;
            if (finalCopy > 0) // Again, can happen if a hop was added last.
            {
                CopyAdded(thisIndex, array, arrayIndex, finalCopy);
            }

            Debug.Assert(arrayIndex + toCopy == count, "We should have finished copying to all the slots in the array.");

            return array;
        }
        
        private T[] GetAddBuffer()
        {
            // Return the buffer we're reading into, or allocate a new one.
            // The returned buffer must have room for at least 1 more item @ _index.

            // - On the very first Add, initialize _first from null.
            // - On subsequent Adds, either return _first or resize if _first has no more space.
            // - When we pass ResizeLimit, read in subsequent items to buffers in _others
            //   instead of resizing further. When we allocate a new buffer, add it to _others
            //   and reset _index to 0.

            T[] result;
            
            if (count > ResizeLimit)
            {
                // We're adding to a buffer in _others.
                Debug.Assert(_others.Count > 0);

                result = _others[_others.Count - 1];
                if (_index == result.Length)
                {
                    // No more space in this buffer.
                    // Add a new buffer twice the size to _others.
                    result = new T[result.Length * 2];
                    _others.Add(result);
                    _index = 0;
                }
            }
            else
            {
                // We haven't passed ResizeLimit. All of the items so far have been added to _first.
                result = _first;

                if (_count == 0 || _index == _first.Length)
                {
                    // No more space in _first!
                    if (_count == ResizeLimit)
                    {
                        // Instead of resizing _first more, start adding subsequent items to buffers in _others.
                        result = new T[ResizeLimit];
                        _others.Add(result);
                        _index = 0;
                    }
                    else
                    {
                        // Resize _first, copying over the previous items.
                        int nextCapacity = _count == 0 ? StartingCapacity : _first.Length * 2;

                        result = new T[nextCapacity];
                        if (_count > 0)
                        {
                            Array.Copy(_first, 0, result, 0, _first.Length);
                        }
                        _first = result;
                    }
                }
            }

            return result;
        }

        private int GetCountIncludingHops()
        {
            int result = _count;

            for (int i = 0; i < _hops.Count; i++)
            {
                result += _hops[i]._count;
            }

            return result;
        }

        private T[] GetLocationFromIndex(int index, out int realIndex)
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

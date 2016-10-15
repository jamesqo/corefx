// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Collections.Generic
{
    internal struct Gap
    {
        internal int _index;
        internal int _count;
    }

    internal struct LargeArrayBuilder<T>
    {
        private const int StartingCapacity = 4;
        private const int FirstLimit = 32;

        private T[] _first;
        private ArrayBuilder<T[]> _others;
        private ArrayBuilder<Gap> _gaps;
        private int _index;
        private int _count;

        public int Count => _count;

        public void Add(T item)
        {
            T[] destination = GetAddDestination();
            destination[_index++] = item;
            _count++;
        }

        public void AddGap(int count)
        {
            Debug.Assert(count >= 0);

            _gaps.Add(new Gap { _index = _count, _count = count });
            _count += count;
        }

        public T[] ToArray()
        {
            if (_count == 0)
            {
                return Array.Empty<T>();
            }

            var result = new T[_count];
            
            int copied = 0;
            int bufferNum = -1;

            int sourceIndex = 0;
            int destinationIndex = 0;

            foreach (Gap gap in _gaps)
            {

                // Skip over the gap.
                int gapCount = gap._count;
                copied += gapCount;
                destinationIndex += gapCount;
            }

            // Finish up the segment after the final gap.
        }

        private void CopyTo(int sourceIndex, T[] destination, int destinationIndex, int count)
        {
            Debug.Assert(sourceIndex >= 0);
            Debug.Assert(destination != null);
            Debug.Assert(count >= 0);
            Debug.Assert()

            // Copy up to the gap.
            for ( ; ; bufferNum++)
            {
                T[] buffer = bufferNum < 0 ? _first : _others[bufferNum];

                int remaining = indexAfterCopy - copied;
                if (beforeGap > 0)
                {
                    int toCopy = Math.Min(buffer.Length, beforeGap);

                    Array.Copy(buffer, sourceIndex, result, destinationIndex, toCopy);
                    copied += toCopy;
                    destinationIndex += toCopy;
                }
                
                // - If the gap was before the end of the current buffer, break.
                // - If the gap was past or at the end of the current buffer, move on.
                
                if (beforeGap < buffer.Length)
                {
                    sourceIndex = beforeGap;
                    break;
                }
            }
        }
        
        private T[] GetAddDestination()
        {
            // - On the very first Add, initialize _first from null.
            // - On subsequent Adds, either do nothing or resize if _first has no more space.
            // - When we pass FirstLimit, read in subsequent items to buffers in _others
            //   instead of resizing further. When we allocate a new buffer, add it to _others
            //   and reset _index to 0.

            T[] result;
            
            if (_others.Count > 0)
            {
                // We're adding to a buffer in _others.
                Debug.Assert(count > FirstLimit);

                result = _others[_others.Count - 1];
                if (_index == result.Length)
                {
                    // No more space in this buffer.
                    // Add a new buffer twice the size to _others.
                    result = new T[result.Length * 2];
                    _others.Add(result);
                }
            }
            else if (_count < FirstLimit)
            {
                // We haven't passed FirstLimit, so we're adding to _first.
                result = _first;

                if (_count == 0 || _index == _first.Length)
                {
                    // No more space in _first! We need to resize.
                    int nextCapacity = _count == 0 ? StartingCapacity : _first.Length * 2;

                    result = new T[nextCapacity];
                    if (_count > 0)
                    {
                        Array.Copy(_first, 0, result, 0, _first.Length);
                    }
                    _first = result;
                }
            }
            else
            {
                // Crossover from copying to _first to copying to buffers in _others
                Debug.Assert(_count == FirstLimit);

                result = new T[FirstLimit];
                _others.Add(result);
            }

            return result;
        }
    }
}

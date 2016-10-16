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
            T[] buffer = GetAddBuffer();

            buffer[_index++] = item;
            _count++;
        }

        public void SkipCopy(int count)
        {
            Debug.Assert(count >= 0);

            _gaps.Add(new Gap { _index = _count, _count = count });
            _count += count;
        }

        public void CopyAdded(int sourceIndex, T[] destination, int destinationIndex, int count)
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

        public T[] ToArray()
        {
            if (_count == 0)
            {
                return Array.Empty<T>();
            }

            var result = new T[_count];
            
            for (int i = 0; i < _gaps.Count; i++)
            {
                Gap gap = gaps[i];
            }
        }
        
        private T[] GetAddBuffer()
        {
            // - On the very first Add, initialize _first from null.
            // - On subsequent Adds, either return _first or resize if _first has no more space.
            // - When we pass FirstLimit, read in subsequent items to buffers in _others
            //   instead of resizing further. When we allocate a new buffer, add it to _others
            //   and reset _index to 0.

            T[] result;
            
            if (count > FirstLimit)
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
                // We haven't passed FirstLimit. All of the items so far have been added to _first.
                result = _first;

                if (_count == 0 || _index == _first.Length)
                {
                    // No more space in _first!
                    if (_count == FirstLimit)
                    {
                        // Instead of resizing _first more, start adding subsequent items to buffers in _others.
                        result = new T[FirstLimit];
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
    }
}

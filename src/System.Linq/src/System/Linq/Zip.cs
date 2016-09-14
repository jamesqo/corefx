﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;

namespace System.Linq
{
    public static partial class Enumerable
    {
        public static IEnumerable<TResult> Zip<TFirst, TSecond, TResult>(this IEnumerable<TFirst> first, IEnumerable<TSecond> second, Func<TFirst, TSecond, TResult> resultSelector)
        {
            if (first == null)
            {
                throw Error.ArgumentNull(nameof(first));
            }

            if (second == null)
            {
                throw Error.ArgumentNull(nameof(second));
            }

            if (resultSelector == null)
            {
                throw Error.ArgumentNull(nameof(resultSelector));
            }
            
            var firstList = first as IList<TFirst>;
            var secondList = second as IList<TSecond>;

            if (firstList != null && secondList != null)
            {
                return ZipTwoLists(firstList, secondList, resultSelector);
            }
            else if (firstList != null)
            {
                var secondPart = second as IPartition<TSecond>;
                if (secondPart != null)
                {
                    return ZipListAndPartition(firstList, secondPart, resultSelector);
                }
            }
            else if (secondList != null)
            {
                var firstPart = first as IPartition<TFirst>;
                if (firstPart != null)
                {
                    return ZipPartitionAndList(firstPart, secondList, resultSelector);
                }
            }
            else
            {
                var firstPart = first as IPartition<TFirst>;
                if (firstPart != null)
                {
                    var secondPart = second as IPartition<TSecond>;
                    if (secondPart != null)
                    {
                        return ZipTwoPartitions(firstPart, secondPart, resultSelector);
                    }
                }
            }

            return ZipTwoEnumerables(first, second, resultSelector);
        }

        private static IEnumerable<TResult> ZipTwoLists<TFirst, TSecond, TResult>(IList<TFirst> first, IList<TSecond> second, Func<TFirst, TSecond, TResult> selector)
        {
            Debug.Assert(first != null && second != null);

            int count = Math.Min(first.Count, second.Count);

            // If either of the Counts are negative then something is wrong
            // with the IList implementation. We'll stick with the old behavior
            // of iterating through the list with its enumerator.

            return
                count < 0 ? ZipTwoEnumerables(first, second, selector) :
                count == 0 ? EmptyPartition<TResult>.Instance :
                new ZipListIterator<TFirst, TSecond, TResult>(first, second, selector, count);
        }

        private static IEnumerable<TResult> ZipListAndPartition<TFirst, TSecond, TResult>(IList<TFirst> first, IPartition<TSecond> second, Func<TFirst, TSecond, TResult> selector)
        {
            Debug.Assert(first != null && second != null);
            
            int count = Math.Min(first.Count, second.GetCount(true));

            // If the IPartition couldn't get its count cheaply, it will return -1 and
            // we'll fallback to treating both inputs as lazy enumerables.

            return
                count < 0 ? ZipTwoEnumerables(first, second, selector) :
                count == 0 ? EmptyPartition<TResult>.Instance :
                new ZipListAndPartitionIterator<TFirst, TSecond, TResult>(first, second, selector, count);
        }

        private static IEnumerable<TResult> ZipPartitionAndList<TFirst, TSecond, TResult>(IPartition<TFirst> first, IList<TSecond> second, Func<TFirst, TSecond, TResult> selector)
        {
            Debug.Assert(first != null && second != null);

            int count = Math.Min(first.GetCount(true), second.Count);

            // Unfortunately, we have to create a new delegate here to share code, since the constructor
            // accepts a Func<TSecond, TFirst, TResult> and we have a Func<TFirst, TSecond, TResult>.

            return
                count < 0 ? ZipTwoEnumerables(first, second, selector) :
                count == 0 ? EmptyPartition<TResult>.Instance :
                new ZipListAndPartitionIterator<TSecond, TFirst, TResult>(second, first, SwapInputOrder(selector), count);
        }

        private static IEnumerable<TResult> ZipTwoPartitions<TFirst, TSecond, TResult>(IPartition<TFirst> first, IPartition<TSecond> second, Func<TFirst, TSecond, TResult> selector)
        {
            return new ZipPartitionIterator<TFirst, TSecond, TResult>(first, second, selector);
        }

        private static IEnumerable<TResult> ZipTwoEnumerables<TFirst, TSecond, TResult>(IEnumerable<TFirst> first, IEnumerable<TSecond> second, Func<TFirst, TSecond, TResult> selector)
        {
            return new ZipEnumerableIterator<TFirst, TSecond, TResult>(first, second, selector);
        }

        private static Func<TFirst, TSecond, TResult> CombineSelectors<TFirst, TSecond, TMiddle, TResult>(Func<TFirst, TSecond, TMiddle> selector1, Func<TMiddle, TResult> selector2)
        {
            return (first, second) => selector2(selector1(first, second));
        }

        private static Func<TSecond, TFirst, TResult> SwapInputOrder<TFirst, TSecond, TResult>(Func<TFirst, TSecond, TResult> selector)
        {
            return (second, first) => selector(first, second);
        }

        private sealed class ZipListIterator<TFirst, TSecond, TResult> : Iterator<TResult>, IPartition<TResult>
        {
            private readonly IList<TFirst> _first;
            private readonly IList<TSecond> _second;
            private readonly Func<TFirst, TSecond, TResult> _selector;
            private readonly int _offset;
            private readonly int _count;

            internal ZipListIterator(IList<TFirst> first, IList<TSecond> second, Func<TFirst, TSecond, TResult> selector, int count)
                : this(first, second, selector, 0, count)
            {
            }

            private ZipListIterator(IList<TFirst> first, IList<TSecond> second, Func<TFirst, TSecond, TResult> selector, int offset, int count)
            {
                Debug.Assert(first != null);
                Debug.Assert(second != null);
                Debug.Assert(selector != null);
                Debug.Assert(offset >= 0 && offset < count);
                Debug.Assert(count > 0); // The caller should check beforehand for count == 0 and if so return EmptyPartition<TResult>
                Debug.Assert(offset + count <= Math.Min(first.Count, second.Count));
                Debug.Assert(offset + count > 0); // offset + count should never overflow as this class is currently written.
                                                  // The entry point to ZipListIterator in .Zip() passes in 0 for offset, and
                                                  // .Skip() and .Take() should never increase the value of offset + count.
                _first = first;
                _second = second;
                _selector = selector;
                _offset = offset;
                _count = count;
            }

            public override Iterator<TResult> Clone()
            {
                return new ZipListIterator<TFirst, TSecond, TResult>(_first, _second, _selector, _offset, _count);
            }

            public int GetCount(bool onlyIfCheap) => _count;

            public override bool MoveNext()
            {
                if (_state > _count)
                {
                    Dispose();
                    return false;
                }

                // _state - 1 represents the zero-based index into the lists.
                // IMPORTANT: We need to increment _state before _selector is called,
                // in case that function calls MoveNext on this iterator.
                int index = _offset + (_state++ - 1);
                _current = _selector(_first[index], _second[index]);
                return true;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector)
            {
                return new ZipListIterator<TFirst, TSecond, TResult2>(_first, _second, CombineSelectors(_selector, selector), _offset, _count);
            }

            public IPartition<TResult> Skip(int count)
            {
                if (count >= _count)
                {
                    return EmptyPartition<TResult>.Instance;
                }

                return new ZipListIterator<TFirst, TSecond, TResult>(_first, _second, _selector, _offset + count, _count - count);
            }

            public IPartition<TResult> Take(int count)
            {
                if (count >= _count)
                {
                    return this;
                }

                return new ZipListIterator<TFirst, TSecond, TResult>(_first, _second, _selector, _offset, count);
            }

            public TResult[] ToArray()
            {
                Debug.Assert(_count != 0); // See notes in constructor

                var array = new TResult[_count];

                for (int i = 0; i < array.Length; i++)
                {
                    int index = _offset + i;
                    array[i] = _selector(_first[index], _second[index]);
                }

                return array;
            }

            public List<TResult> ToList()
            {
                var list = new List<TResult>(_count);

                for (int i = 0; i < _count; i++)
                {
                    int index = _offset + i;
                    list.Add(_selector(_first[index], _second[index]));
                }

                return list;
            }

            public TResult TryGetElementAt(int index, out bool found)
            {
                if ((uint)index < (uint)_count)
                {
                    found = true;
                    int i = _offset + index;
                    return _selector(_first[i], _second[i]);
                }

                found = false;
                return default(TResult);
            }

            public TResult TryGetFirst(out bool found)
            {
                Debug.Assert(_count != 0); // See notes in constructor

                found = true;
                int index = _offset;
                return _selector(_first[index], _second[index]);
            }

            public TResult TryGetLast(out bool found)
            {
                Debug.Assert(_count != 0); // See notes in constructor

                found = true;
                int index = _offset + _count - 1; // _offset + _count should not overflow, see constructor
                return _selector(_first[index], _second[index]);
            }
        }

        private sealed class ZipPartitionIterator<TFirst, TSecond, TResult> : Iterator<TResult>, IPartition<TResult>
        {
            private readonly IPartition<TFirst> _first;
            private readonly IPartition<TSecond> _second;
            private readonly Func<TFirst, TSecond, TResult> _selector;
            private IEnumerator<TFirst> _firstEnumerator;
            private IEnumerator<TSecond> _secondEnumerator;

            internal ZipPartitionIterator(IPartition<TFirst> first, IPartition<TSecond> second, Func<TFirst, TSecond, TResult> selector)
            {
                Debug.Assert(first != null);
                Debug.Assert(second != null);
                Debug.Assert(selector != null);

                _first = first;
                _second = second;
                _selector = selector;
            }

            public override Iterator<TResult> Clone()
            {
                return new ZipPartitionIterator<TFirst, TSecond, TResult>(_first, _second, _selector);
            }

            public override void Dispose()
            {
                _firstEnumerator?.Dispose();
                _firstEnumerator = null;
                
                _secondEnumerator?.Dispose();
                _secondEnumerator = null;
                
                base.Dispose();
            }

            public int GetCount(bool onlyIfCheap) => Math.Min(_first.GetCount(onlyIfCheap), _second.GetCount(onlyIfCheap));

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _firstEnumerator = _first.GetEnumerator();
                        _secondEnumerator = _second.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        if (_firstEnumerator.MoveNext() && _secondEnumerator.MoveNext())
                        {
                            _current = _selector(_firstEnumerator.Current, _secondEnumerator.Current);
                            return true;
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector)
            {
                return new ZipPartitionIterator<TFirst, TSecond, TResult2>(_first, _second, CombineSelectors(_selector, selector));
            }

            public IPartition<TResult> Skip(int count)
            {
                return new ZipPartitionIterator<TFirst, TSecond, TResult>(_first.Skip(count), _second.Skip(count), _selector);
            }

            public IPartition<TResult> Take(int count)
            {
                return new ZipPartitionIterator<TFirst, TSecond, TResult>(_first.Take(count), _second.Take(count), _selector);
            }

            public TResult[] ToArray()
            {
                int count = GetCount(true);
                switch (count)
                {
                    case -1:
                        return EnumerableHelpers.ToArray(this);
                    case 0:
                        return Array.Empty<TResult>();
                    default:
                        int index = 0;
                        var array = new TResult[count];

                        using (var firstEnumerator = _first.GetEnumerator())
                        using (var secondEnumerator = _second.GetEnumerator())
                        {
                            while (firstEnumerator.MoveNext() && secondEnumerator.MoveNext())
                            {
                                array[index++] = _selector(firstEnumerator.Current, secondEnumerator.Current);
                            }
                        }

                        Debug.Assert(index == count); // We should have filled all of the slots in the array.
                        return array;
                }
            }

            public List<TResult> ToList()
            {
                int count = GetCount(true);
                List<TResult> list;
                switch (count)
                {
                    case -1:
                        list = new List<TResult>();
                        break;
                    case 0:
                        return new List<TResult>();
                    default:
                        list = new List<TResult>(count);
                        break;
                }

                using (var firstEnumerator = _first.GetEnumerator())
                using (var secondEnumerator = _second.GetEnumerator())
                {
                    while (firstEnumerator.MoveNext() && secondEnumerator.MoveNext())
                    {
                        list.Add(_selector(firstEnumerator.Current, secondEnumerator.Current));
                    }
                }

                Debug.Assert(list.Count == count); // We should have filled the list to the brim.
                return list;
            }

            public TResult TryGetElementAt(int index, out bool found)
            {
                TFirst firstElement = _first.TryGetElementAt(index, out found);
                if (found)
                {
                    TSecond secondElement = _second.TryGetElementAt(index, out found);
                    if (found)
                    {
                        return _selector(firstElement, secondElement);
                    }
                }

                return default(TResult);
            }

            public TResult TryGetFirst(out bool found)
            {
                TFirst firstElement = _first.TryGetFirst(out found);
                if (found)
                {
                    TSecond secondElement = _second.TryGetFirst(out found);
                    if (found)
                    {
                        return _selector(firstElement, secondElement);
                    }
                }

                return default(TResult);
            }

            public TResult TryGetLast(out bool found)
            {
                TFirst firstElement = _first.TryGetLast(out found);
                if (found)
                {
                    TSecond secondElement = _second.TryGetLast(out found);
                    if (found)
                    {
                        return _selector(firstElement, secondElement);
                    }
                }

                return default(TResult);
            }
        }

        private sealed class ZipListAndPartitionIterator<TFirst, TSecond, TResult> : Iterator<TResult>, IPartition<TResult>
        {
            private readonly IList<TFirst> _list;
            private readonly IPartition<TSecond> _partition;
            private readonly Func<TFirst, TSecond, TResult> _selector;
            private readonly int _offset; // into the IList. Not relevant for the IPartition, since it has .Skip for skipping items.
            private readonly int _count;
            private IEnumerator<TSecond> _partitionEnumerator;

            internal ZipListAndPartitionIterator(IList<TFirst> list, IPartition<TSecond> partition, Func<TFirst, TSecond, TResult> selector, int count)
                : this(list, partition, selector, 0, count)
            {
            }

            private ZipListAndPartitionIterator(IList<TFirst> list, IPartition<TSecond> partition, Func<TFirst, TSecond, TResult> selector, int offset, int count)
            {
                Debug.Assert(list != null);
                Debug.Assert(partition != null);
                Debug.Assert(selector != null);
                Debug.Assert(offset >= 0 && offset < count);
                Debug.Assert(count > 0);
                Debug.Assert(count <= Math.Min(list.Count, partition.GetCount(true))); // This constructor should only be called if the IPartition can get its count cheaply.
                Debug.Assert(offset + count <= list.Count);
                Debug.Assert(offset + count > 0); // offset + count should never overflow as this class is currently written.
                                                  // The entry point to ZipListIterator in .Zip() passes in 0 for offset, and
                                                  // .Skip() and .Take() should never increase the value of offset + count.
                _list = list;
                _partition = partition;
                _selector = selector;
                _offset = offset;
                _count = count;
            }

            public override Iterator<TResult> Clone()
            {
                return new ZipListAndPartitionIterator<TFirst, TSecond, TResult>(_list, _partition, _selector, _offset, _count);
            }

            public override void Dispose()
            {
                _partitionEnumerator?.Dispose();
                _partitionEnumerator = null;

                base.Dispose();
            }

            public int GetCount(bool onlyIfCheap) => _count;

            public override bool MoveNext()
            {
                if (_state > _count)
                {
                    Dispose();
                    return false;
                }

                if (_state == 1)
                {
                    _partitionEnumerator = _partition.GetEnumerator();
                }

                // _state - 1 represents the zero-based index into _list.
                // IMPORTANT: We need to increment _state before any other virtual functions
                // are called, in case they function call MoveNext on this iterator.
                int index = _offset + (_state++ - 1);
                TFirst firstElement = _list[index];

                if (!_partitionEnumerator.MoveNext())
                {
                    Debug.Assert(false, "Unexpectedly reached the end of the partition.");
                }

                TSecond secondElement = _partitionEnumerator.Current;

                _current = _selector(firstElement, secondElement);
                return true;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector)
            {
                return new ZipListAndPartitionIterator<TFirst, TSecond, TResult2>(_list, _partition, CombineSelectors(_selector, selector), _offset, _count);
            }

            public IPartition<TResult> Skip(int count)
            {
                if (count >= _count)
                {
                    return EmptyPartition<TResult>.Instance;
                }

                return new ZipListAndPartitionIterator<TFirst, TSecond, TResult>(_list, _partition.Skip(count), _selector, _offset + count, _count - count);
            }

            public IPartition<TResult> Take(int count)
            {
                if (count >= _count)
                {
                    return this;
                }

                return new ZipListAndPartitionIterator<TFirst, TSecond, TResult>(_list, _partition.Take(count), _selector, _offset, count);
            }

            public TResult[] ToArray()
            {
                Debug.Assert(_count > 0); // See notes in constructor

                var array = new TResult[_count];
                
                using (var partitionEnumerator = _partition.GetEnumerator())
                {
                    for (int i = 0; i < array.Length; i++)
                    {
                        if (!partitionEnumerator.MoveNext())
                        {
                            Debug.Assert(false, "Unexpectedly reached the end of the partition.");
                        }

                        array[i] = _selector(_list[_offset + i], partitionEnumerator.Current);
                    }
                }

                return array;
            }

            public List<TResult> ToList()
            {
                var list = new List<TResult>(_count);
                
                using (var partitionEnumerator = _partition.GetEnumerator())
                {
                    for (int i = 0; i < _count; i++)
                    {
                        if (!partitionEnumerator.MoveNext())
                        {
                            Debug.Assert(false, "Unexpectedly reached the end of the partition.");
                        }

                        list.Add(_selector(_list[_offset + i], partitionEnumerator.Current));
                    }
                }

                return list;
            }

            public TResult TryGetElementAt(int index, out bool found)
            {
                if ((uint)index < (uint)_count)
                {
                    TSecond secondElement = _partition.TryGetElementAt(index, out found);
                    if (found)
                    {
                        return _selector(_list[_offset + index], secondElement);
                    }
                }

                found = false;
                return default(TResult);
            }

            public TResult TryGetFirst(out bool found)
            {
                Debug.Assert(_count > 0); // The index into the IList should always succeed- see notes in constructor

                TSecond secondElement = _partition.TryGetFirst(out found);
                return found ? _selector(_list[_offset], secondElement) : default(TResult);
            }

            public TResult TryGetLast(out bool found)
            {
                Debug.Assert(_count > 0); // The index into the IList should always succeed- see notes in constructor

                TSecond secondElement = _partition.TryGetLast(out found);
                return found ? _selector(_list[_offset + _count - 1], secondElement) : default(TResult);
            }
        }

        private sealed class ZipEnumerableIterator<TFirst, TSecond, TResult> : Iterator<TResult>
        {
            private readonly IEnumerable<TFirst> _first;
            private readonly IEnumerable<TSecond> _second;
            private readonly Func<TFirst, TSecond, TResult> _selector;
            private IEnumerator<TFirst> _firstEnumerator;
            private IEnumerator<TSecond> _secondEnumerator;

            internal ZipEnumerableIterator(IEnumerable<TFirst> first, IEnumerable<TSecond> second, Func<TFirst, TSecond, TResult> selector)
            {
                Debug.Assert(first != null);
                Debug.Assert(second != null);
                Debug.Assert(selector != null);

                _first = first;
                _second = second;
                _selector = selector;
            }

            public override Iterator<TResult> Clone()
            {
                return new ZipEnumerableIterator<TFirst, TSecond, TResult>(_first, _second, _selector);
            }

            public override void Dispose()
            {
                _firstEnumerator?.Dispose();
                _firstEnumerator = null;

                _secondEnumerator?.Dispose();
                _secondEnumerator = null;

                base.Dispose();
            }

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        _firstEnumerator = _first.GetEnumerator();
                        _secondEnumerator = _second.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        if (_firstEnumerator.MoveNext() && _secondEnumerator.MoveNext())
                        {
                            _current = _selector(_firstEnumerator.Current, _secondEnumerator.Current);
                            return true;
                        }

                        Dispose();
                        break;
                }

                return false;
            }

            public override IEnumerable<TResult2> Select<TResult2>(Func<TResult, TResult2> selector)
            {
                return new ZipEnumerableIterator<TFirst, TSecond, TResult2>(_first, _second, CombineSelectors(_selector, selector));
            }
        }
    }
}

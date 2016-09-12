// Licensed to the .NET Foundation under one or more agreements.
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
            if (firstList != null)
            {
                var secondList = second as IList<TSecond>;
                if (secondList != null)
                {
                    int resultCount = Math.Min(firstList.Count, secondList.Count);
                    if (resultCount == 0)
                    {
                        return EmptyPartition<TResult>.Instance;
                    }

                    // If either of the Counts are negative then something is wrong
                    // with the IList implementation. We'll stick with the old behavior
                    // of iterating through the list with its enumerator.
                    if (resultCount > 0)
                    {
                        return new ZipListIterator<TFirst, TSecond, TResult>(firstList, secondList, resultSelector, 0, resultCount);
                    }
                }
            }

            return new ZipEnumerableIterator<TFirst, TSecond, TResult>(first, second, resultSelector);
        }

        private static Func<TFirst, TSecond, TResult> CombineSelectors<TFirst, TSecond, TMiddle, TResult>(Func<TFirst, TSecond, TMiddle> selector1, Func<TMiddle, TResult> selector2)
        {
            return (first, second) => selector2(selector1(first, second));
        }

        private sealed class ZipListIterator<TFirst, TSecond, TResult> : Iterator<TResult>, IPartition<TResult>
        {
            private readonly IList<TFirst> _first;
            private readonly IList<TSecond> _second;
            private readonly Func<TFirst, TSecond, TResult> _selector;
            private readonly int _offset;
            private readonly int _count;

            internal ZipListIterator(IList<TFirst> first, IList<TSecond> second, Func<TFirst, TSecond, TResult> selector, int offset, int count)
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
                if (_firstEnumerator != null)
                {
                    _firstEnumerator.Dispose();
                    _firstEnumerator = null;
                }

                if (_secondEnumerator != null)
                {
                    _secondEnumerator.Dispose();
                    _secondEnumerator = null;
                }

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

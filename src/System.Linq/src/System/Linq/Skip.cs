// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;

namespace System.Linq
{
    public static partial class Enumerable
    {
        public static IEnumerable<TSource> Skip<TSource>(this IEnumerable<TSource> source, int count)
        {
            if (source == null)
            {
                throw Error.ArgumentNull(nameof(source));
            }

            if (count <= 0)
            {
                // Return source if not actually skipping, but only if it's a type from here, to avoid
                // issues if collections are used as keys or otherwise must not be aliased.
                if (source is Iterator<TSource> || source is IPartition<TSource>)
                {
                    return source;
                }

                count = 0;
            }
            else
            {
                IPartition<TSource> partition = source as IPartition<TSource>;
                if (partition != null)
                {
                    return partition.Skip(count);
                }
            }

            IList<TSource> sourceList = source as IList<TSource>;
            if (sourceList != null)
            {
                return new ListPartition<TSource>(sourceList, count, int.MaxValue);
            }

            return new EnumerablePartition<TSource>(source, count, -1);
        }

        public static IEnumerable<TSource> SkipWhile<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            if (source == null)
            {
                throw Error.ArgumentNull(nameof(source));
            }

            if (predicate == null)
            {
                throw Error.ArgumentNull(nameof(predicate));
            }

            return SkipWhileIterator(source, predicate);
        }

        private static IEnumerable<TSource> SkipWhileIterator<TSource>(IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                while (e.MoveNext())
                {
                    TSource element = e.Current;
                    if (!predicate(element))
                    {
                        yield return element;
                        while (e.MoveNext())
                        {
                            yield return e.Current;
                        }

                        yield break;
                    }
                }
            }
        }

        public static IEnumerable<TSource> SkipWhile<TSource>(this IEnumerable<TSource> source, Func<TSource, int, bool> predicate)
        {
            if (source == null)
            {
                throw Error.ArgumentNull(nameof(source));
            }

            if (predicate == null)
            {
                throw Error.ArgumentNull(nameof(predicate));
            }

            return SkipWhileIterator(source, predicate);
        }

        private static IEnumerable<TSource> SkipWhileIterator<TSource>(IEnumerable<TSource> source, Func<TSource, int, bool> predicate)
        {
            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                int index = -1;
                while (e.MoveNext())
                {
                    checked
                    {
                        index++;
                    }

                    TSource element = e.Current;
                    if (!predicate(element, index))
                    {
                        yield return element;
                        while (e.MoveNext())
                        {
                            yield return e.Current;
                        }

                        yield break;
                    }
                }
            }
        }

#if netcoreapp11
        public static IEnumerable<TSource> SkipLast<TSource>(this IEnumerable<TSource> source, int count)
        {
            if (source == null)
            {
                throw Error.ArgumentNull(nameof(source));
            }

            if (count <= 0)
            {
                return source.Skip(0);
            }

            return SkipLastIterator(source, count);
        }

        private sealed class SkipLastIterator<TSource> : Iterator<TSource>, IIListProvider<TSource>
        {
            private readonly IEnumerable<TSource> _source;
            private readonly int _count;
            private IEnumerator<TSource> _enumerator;
            private CircularBuffer<TSource> _buffer;

            internal SkipLastIterator(IEnumerable<TSource> source, int count)
            {
                Debug.Assert(source != null);
                Debug.Assert(count > 0);

                _source = source;
                _count = count;
            }

            public override Iterator<TSource> Clone()
            {
                return new SkipLastIterator<TSource>(_source, _count);
            }

            public override void Dispose()
            {
                if (_enumerator != null)
                {
                    _enumerator.Dispose();
                    _enumerator = null;
                }

                _buffer.Dispose();

                base.Dispose();
            }

            public int GetCount(bool onlyIfCheap)
            {
                int sourceCount;
                return EnumerableHelpers.TryGetCount(_source, out sourceCount) ? Math.Max(0, sourceCount - _count) : -1;
            }

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        // Retrieve the enumerator from the source.
                        _enumerator = _source.GetEnumerator();
                        _state = 2;
                        goto case 2;
                    case 2:
                        // Accumulate the first N items into a buffer.
                        var buffer = new CircularBuffer<TSource>(_count);
                        FillBuffer(buffer, _enumerator);

                        // If the enumeration ended early, we have nothing to yield.
                        if (buffer.Count < _count)
                        {
                            break;
                        }

                        _buffer = buffer;
                        _state = 3;
                        goto case 3;
                    case 3:
                        // Interleave between reading in an item and yielding the oldest item.
                        // Using a circular buffer makes these operations O(1).

                        if (_enumerator.MoveNext())
                        {
                            _current = _buffer.Replace(_enumerator.Current);
                            return true;
                        }

                        break;
                }

                Dispose();
                return false;
            }

            public TSource[] ToArray()
            {
                var buffer = new CircularBuffer<TSource>(_count);
                var builder = new LargeArrayBuilder<TSource>(initialize: true);

                using (IEnumerator<TSource> e = _source.GetEnumerator())
                {
                    FillBuffer(buffer, e);

                    if (buffer.Count < _count)
                    {
                        return Array.Empty<TSource>();
                    }

                    while (e.MoveNext())
                    {
                        builder.Add(buffer.Replace(e.Current));
                    }

                    return builder.ToArray();
                }
            }

            public List<TSource> ToList()
            {
                var buffer = new CircularBuffer<TSource>(_count);
                var list = new List<TSource>();

                using (IEnumerator<TSource> e = _source.GetEnumerator())
                {
                    FillBuffer(buffer, e);

                    if (buffer.Count < _count)
                    {
                        return new List<TSource>();
                    }

                    while (e.MoveNext())
                    {
                        list.Add(buffer.Replace(e.Current));
                    }

                    return list;
                }
            }

            private void FillBuffer(CircularBuffer<TSource> buffer, IEnumerator<TSource> enumerator)
            {
                while (enumerator.MoveNext())
                {
                    buffer.Add(enumerator.Current);
                    if (buffer.Count == _count)
                    {
                        break;
                    }
                }
            }
        }

        private static IEnumerable<TSource> SkipLastIterator<TSource>(IEnumerable<TSource> source, int count)
        {
            Debug.Assert(source != null);
            Debug.Assert(count > 0);

            var queue = new Queue<TSource>();

            using (IEnumerator<TSource> e = source.GetEnumerator())
            {
                while (e.MoveNext())
                {
                    if (queue.Count == count)
                    {
                        do
                        {
                            yield return queue.Dequeue();
                            queue.Enqueue(e.Current);
                        }
                        while (e.MoveNext());
                    }
                    else
                    {
                        queue.Enqueue(e.Current);
                    }
                }
            }
        }
#endif
    }
}

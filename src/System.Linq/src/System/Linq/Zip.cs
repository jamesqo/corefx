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

            return new ZipEnumerableIterator<TFirst, TSecond, TResult>(first, second, resultSelector);
        }

        private static Func<TFirst, TSecond, TResult> CombineSelectors<TFirst, TSecond, TMiddle, TResult>(Func<TFirst, TSecond, TMiddle> selector1, Func<TMiddle, TResult> selector2)
        {
            return (first, second) => selector2(selector1(first, second));
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

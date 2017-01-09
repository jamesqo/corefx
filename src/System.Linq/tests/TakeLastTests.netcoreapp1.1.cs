// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Xunit;
using static System.Linq.Tests.SkipTakeData;

namespace System.Linq.Tests
{
    public class TakeLastTests : EnumerableTests
    {
        // TODO: RunOnce tests. Same for SkipLast.

        [Theory]
        [ClassData(typeof(SkipTakeData))]
        public void TakeLast(IEnumerable<int> source, int count)
        {
            Assert.All(IdentityTransforms<int>(), transform =>
            {
                IEnumerable<int> equivalent = transform(source);

                IEnumerable<int> expected = equivalent.Reverse().Take(count).Reverse();
                IEnumerable<int> actual = equivalent.TakeLast(count);

                Assert.Equal(expected, actual);
                Assert.Equal(expected.Count(), actual.Count());
                Assert.Equal(expected, actual.ToArray());
                Assert.Equal(expected, actual.ToList());

                Assert.Equal(expected.FirstOrDefault(), actual.FirstOrDefault());
                Assert.Equal(expected.LastOrDefault(), actual.LastOrDefault());

                Assert.All(Enumerable.Range(0, expected.Count()), index =>
                {
                    Assert.Equal(expected.ElementAt(index), actual.ElementAt(index));
                });

                Assert.Equal(0, actual.ElementAtOrDefault(-1));
                Assert.Equal(0, actual.ElementAtOrDefault(actual.Count()));
            });
        }

        [Theory]
        [MemberData(nameof(EvaluationBehaviorData), MemberType = typeof(SkipTakeData))]
        public void EvaluationBehavior(int count)
        {
            int index = 0;
            int limit = count * 2;

            DelegateIterator<int> source = null;
            source = new DelegateIterator<int>(
                getEnumerator: () => source,
                moveNext: () => ++index <= limit, // Stop once we go past the limit.
                current: () => index, // Yield from 1 up to the limit, inclusive.
                dispose: () => index = -1);

            IEnumerator<int> iterator = source.TakeLast(count).GetEnumerator();
            Assert.Equal(0, index); // Nothing should be done before MoveNext is called.

            for (int i = 1; i <= count; i++)
            {
                Assert.True(iterator.MoveNext());
                Assert.Equal(count + i, iterator.Current);
                Assert.Equal(limit, index); // After the first MoveNext call, everything should be evaluated.
            }

            Assert.False(iterator.MoveNext());
            Assert.Equal(-1, index);
        }
    }
}
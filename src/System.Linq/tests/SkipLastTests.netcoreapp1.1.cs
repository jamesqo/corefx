// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Linq.Tests
{
    public class SkipLastTests : EnumerableTests
    {
        [Theory]
        [MemberData(nameof(SkipLastData))]
        public void SkipLast(IEnumerable<int> source, int count)
        {
            foreach (IEnumerable<int> equivalent in IdentityTransforms<int>().Select(t => t(source)))
            {
                IEnumerable<int> expected = equivalent.Reverse().Skip(count).Reverse();
                IEnumerable<int> actual = equivalent.SkipLast(count);

                Assert.Equal(expected, actual);
                Assert.Equal(expected.Count(), actual.Count()); // Count may be optimized. The above assert does not imply this will pass.
                Assert.Equal(expected, actual.ToArray());
                Assert.Equal(expected, actual.ToList());

                Assert.Equal(expected.FirstOrDefault(), actual.FirstOrDefault());
                Assert.Equal(expected.LastOrDefault(), actual.LastOrDefault());

                // Likewise, ElementAt may be optimized.
                int count = expected.Count();
                for (int i = 0; i < count; i++)
                {
                    Assert.Equal(expected.ElementAt(i), actual.ElementAt(i));
                }

                Assert.Equal(default(T), expected.ElementAtOrDefault(-1));
            }
        }

        public static IEnumerable<object[]> SkipLastData()
        {
            IEnumerable<int> counts = new[] { int.MinValue, -1, 0, 1, 2, 3, 4, 5, int.MaxValue };
            IEnumerable<IEnumerable<int>> enumerables = from count in counts
                                                        select Enumerable.Range(count, Math.Min(100, Math.Abs(count)));
            
            return from count in counts
                   from enumerable in enumerables
                   select new object[] { count, enumerable };
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        public void EvaluationBehavior(int count)
        {
            // We want to make sure no more than `count` items are ever evaluated
            // ahead of the current position.
            // As an example, if [0, 1, 2, 3, 4, 5].SkipLast(2) is called, then we
            // should read in [0, 1, 2], yield 0, read in 3, yield 1, read in 4,
            // yield 2, etc.

            int index = 0;

            // Represents the range [1..2 * count].
            var enumerable = EphemeralSequence(new DelegateBasedEnumerator<int>
            {
                MoveNextWorker = () => ++index <= 2 * count,
                CurrentWorker = () => index
            });

            IEnumerator<int> iterator = enumerable.SkipLast(count).GetEnumerator();
            Assert.Equal(0, index);

            for (int i = 0; i < count; i++)
            {
                Assert.True(iterator.MoveNext());
                Assert.Equal(i, iterator.Current);
                Assert.Equal(count + i, index);
            }

            Assert.False(iterator.MoveNext());
        }
    }
}
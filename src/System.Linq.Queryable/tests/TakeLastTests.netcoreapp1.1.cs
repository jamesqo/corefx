// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Xunit;

namespace System.Linq.Tests
{
    public class TakeLastTests : EnumerableBasedTests
    {
        [Theory, MemberData(nameof(TakeLastData))]
        public void TakeLast(IQueryable<int> equivalent, int count)
        {
            IQueryable<int> expected = equivalent.Reverse().Take(count).Reverse();
            IQueryable<int> actual = equivalent.TakeLast(count);

            Assert.Equal(expected, actual);
        }

        public static IEnumerable<object[]> TakeLastData()
        {
            IEnumerable<int> counts = new[] { int.MinValue + 1, -1, 0, 1, 2, 3, 4, 5, int.MaxValue };
            for (int i = 0; i < 100; i += 11)
            {
                IEnumerable<int> enumerable = Enumerable.Range(i, i);
                foreach (int count in counts)
                {
                    yield return new object[] { enumerable.AsQueryable(), count };
                }
            }
        }
    }
}
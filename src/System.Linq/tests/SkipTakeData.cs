// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Generic;

namespace System.Linq.Tests
{
    public class SkipTakeData : IEnumerable<object[]>
    {
        private static IEnumerable<object[]> Data { get; } = CreateData();

        private static IEnumerable<object[]> CreateData()
        {
            IEnumerable<int> sourceCounts = new[] { 1, 2, 3, 5, 8, 13, 55, 100, 250, 1000, 2500 };

            IEnumerable<int> counts = new[] { 1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 100, 250, 500, 1000, 250000, int.MaxValue };
            counts = counts.Concat(counts.Select(c => -c)).Append(0);

            return from sourceCount in sourceCounts
                   let source = Enumerable.Range(0, sourceCount)
                   from count in counts
                   select new object[] { source, count };
        }

        public IEnumerator<object[]> GetEnumerator() => Data.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public static IEnumerable<object[]> EvaluationBehaviorData()
        {
            return Enumerable.Range(-1, 15).Select(count => new object[] { count });
        }
    }
}
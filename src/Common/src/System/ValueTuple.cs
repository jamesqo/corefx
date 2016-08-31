// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// "Low-level" implementation of ValueTuple usable by assemblies that
// don't want to depend on the System.ValueTuple package.

namespace System
{
    // Convenience class so the compiler can infer the type of the tuple.
    internal static class ValueTuple
    {
        public static ValueTuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2)
        {
            return new ValueTuple<T1, T2>(item1, item2);
        }

        public static ValueTuple<T1, T2, T3> Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3)
        {
            return new ValueTuple<T1, T2, T3>(item1, item2, item3);
        }

        public static ValueTuple<T1, T2, T3, T4> Create<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            return new ValueTuple<T1, T2, T3, T4>(item1, item2, item3, item4);
        }
    }

    // NOTE: The following types are intentionally mutable structs.
    // This matches what is done in the System.ValueTuple assembly, and
    // it's good for performance since it can be expensive/verbose to
    // create a copy of the struct when you only need to change 1 item.

    internal struct ValueTuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
    }

    internal struct ValueTuple<T1, T2, T3>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }
    }

    internal struct ValueTuple<T1, T2, T3, T4>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }
    }
}

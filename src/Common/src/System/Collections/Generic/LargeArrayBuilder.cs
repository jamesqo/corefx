// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Collections.Generic
{
    internal struct LargeArrayBuilder<T>
    {
        private struct Insertion
        {
            internal ICollection<T> _collection;
            internal int _index;
        }

        private const int FirstLimit = 32;
        private const int InitialFirstCapacity = 4;

        private T[] _first;
        private ArrayBuilder<T[]> _others;
        private ArrayBuilder<Insertion> _insertions;
        private int _index;

        public void Add(T item)
        {
            T[] destination = _others.Count == 0 ? _first : _others[_others.Count - 1];
            if (destination == null || _index == destination.Length)
            {
                MakeRoomForOneMoreItem(out destination);
            }

            destination[_index++] = item;
        }

        public void AddRange(IEnumerable<T> items)
        {
            Debug.Assert(items != null);

            var collection = items as ICollection<T>;
            if (collection != null)
            {
                EnqueueCollectionForInsertion(collection);
            }
            else
            {
                using (IEnumerator<T> en = items.GetEnumerator())
                {
                    AddRange(en);
                }
            }
        }

        public void AddRange(IEnumerator<T> en) => AddRange(en, int.MaxValue);

        public void AddRange(IEnumerator<T> en, int count)
        {
            Debug.Assert(en != null);
            Debug.Assert(count >= 0);
            
            if (count-- == 0 || !en.MoveNext())
            {
                return;
            }

            T[] destination = _others.Count == 0 ? _first : _others[_others.Count - 1];
            if (destination == null || _index == destination.Length)
            {
                MakeRoomForOneMoreItem(out destination);
            }

            int space = destination.Length - _index;
            Debug.Assert(space > 0);
            
            do
            {
                T current = en.Current;
                destination[_index++] = current;

                if (--space == 0)
                {
                    MakeRoomForOneMoreItem(out destination);
                }
            }
            while (count-- > 0 && en.MoveNext());
        }

        public int GetCount()
        {
            int count = GetCountExcludingCollections();

            for (int i = 0; i < _insertions.Count; i++)
            {
                count += _insertions[i]._collection.Count;
            }

            return count;
        }

        public T[] ToArray()
        {
            int count = GetCount();
            if (count == 0)
            {
                return Array.Empty<T>();
            }

            T[] source = _first;
            Debug.Assert(source != null);

            int copied = 0;
            int builderIndex = -1; // Into _others

            var array = new T[count];
            for (int i = 0; i < _insertions.Count; i++)
            {
                Insertion insertion = _insertions[i];

                // Copy up to the inserted index
                int index = insertion._index;

                while (copied != index)
                {
                    int remaining = index - copied;
                    int toCopy = Math.Min(source.Length, remaining);

                    Array.Copy(source, 0, array, 0, toCopy);
                    copied += toCopy;

                    // @TODO: Switch sources.
                }

                // Copy the items from the insertee
                ICollection<T> insertee = insertion._collection;
                insertee.CopyTo(array, 0);
                copied += insertee.Count;

                // @TODO: Need to adjust index of other insertees after one insertee is copied.
                // For example, if 2 collections are inserted in tandem then currently we will
                // have copied > index after the first insertee.
            }
        }

        private void EnqueueCollectionForInsertion(ICollection<T> collection)
        {
            Debug.Assert(collection != null);

            _insertions.Add(new Insertion { _collection = collection, _index = GetCountExcludingCollections() });
        }

        private int GetCountExcludingCollections()
        {
            int count = 0;

            if (_first != null)
            {
                count = _first.Length;

                for (int i = 0; i < _others.Count - 1; i++)
                {
                    count += _others[i].Length;
                }

                count += _index;
            }

            return count;
        }

        private void MakeRoomForOneMoreItem(out T[] destination)
        {
            // We should only call this method if we've run out of space.
            Debug.Assert(destination == null || _index == destination.Length);

            if (_first == null | _first.Length < FirstLimit)
            {
                int nextCapacity = _first == null ? InitialFirstCapacity : _first.Length * 2;

                destination = new T[nextCapacity];
                Array.Copy(_first, 0, destination, 0, _first.Length);
                _first = destination;
            }
            else
            {
                T[] source = _others.Count == 0 ? _first : _others[_others.Count - 1];
                Debug.Assert(source != null);

                destination = new T[_source.Length];
                _others.Add(destination);
            }

            _index = 0;
        }
    }
}

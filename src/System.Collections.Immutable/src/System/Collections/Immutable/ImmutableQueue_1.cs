// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

namespace System.Collections.Immutable
{
    /// <summary>
    /// An immutable queue.
    /// </summary>
    /// <typeparam name="T">The type of elements stored in the queue.</typeparam>
    [DebuggerDisplay("IsEmpty = {IsEmpty}")]
    [DebuggerTypeProxy(typeof(ImmutableQueueDebuggerProxy<>))]
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "Ignored")]
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix", Justification = "Ignored")]
    public sealed partial class ImmutableQueue<T> : IImmutableQueue<T>
    {
        /// <summary>
        /// The singleton empty queue.
        /// </summary>
        /// <remarks>
        /// Additional instances representing the empty queue may exist on deserialized instances.
        /// Actually since this queue is a struct, instances don't even apply and there are no singletons.
        /// </remarks>
        private static readonly ImmutableQueue<T> s_EmptyField = new ImmutableQueue<T>(ImmutableStack<T>.Empty, ImmutableStack<T>.Empty);

        /// <summary>
        /// The end of the queue that enqueued elements are pushed onto.
        /// </summary>
        private readonly ImmutableStack<T> _backwards;

        /// <summary>
        /// The end of the queue from which elements are dequeued.
        /// </summary>
        private readonly ImmutableStack<T> _forwards;

        /// <summary>
        /// The cached result of <see cref="Dequeue()"/>, or <c>null</c> if it was not called yet.
        /// </summary>
        private ImmutableQueue<T> _dequeue;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImmutableQueue{T}"/> class.
        /// </summary>
        /// <param name="forwards">The forward stack.</param>
        /// <param name="backwards">The backward stack.</param>
        private ImmutableQueue(ImmutableStack<T> forwards, ImmutableStack<T> backwards)
        {
            Debug.Assert(forwards != null);
            Debug.Assert(backwards != null);

            _forwards = forwards;
            _backwards = backwards;
        }

        /// <summary>
        /// Gets the empty queue.
        /// </summary>
        public ImmutableQueue<T> Clear()
        {
            Contract.Ensures(Contract.Result<ImmutableQueue<T>>().IsEmpty);
            Contract.Assume(s_EmptyField.IsEmpty);
            return Empty;
        }

        /// <summary>
        /// Gets a value indicating whether this instance is empty.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is empty; otherwise, <c>false</c>.
        /// </value>
        public bool IsEmpty
        {
            get { return _forwards.IsEmpty && _backwards.IsEmpty; }
        }

        /// <summary>
        /// Gets the empty queue.
        /// </summary>
        public static ImmutableQueue<T> Empty
        {
            get
            {
                Contract.Ensures(Contract.Result<ImmutableQueue<T>>().IsEmpty);
                Contract.Assume(s_EmptyField.IsEmpty);
                return s_EmptyField;
            }
        }

        /// <summary>
        /// Gets an empty queue.
        /// </summary>
        IImmutableQueue<T> IImmutableQueue<T>.Clear()
        {
            Contract.Assume(s_EmptyField.IsEmpty);
            return this.Clear();
        }

        /// <summary>
        /// Gets the element at the front of the queue.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the queue is empty.</exception>
        [Pure]
        public T Peek()
        {
            if (this.IsEmpty)
            {
                ThrowInvalidEmptyOperation();
            }

            return _forwards.Peek();
        }

        /// <summary>
        /// Adds an element to the back of the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>
        /// The new queue.
        /// </returns>
        [Pure]
        public ImmutableQueue<T> Enqueue(T value)
        {
            Contract.Ensures(!Contract.Result<ImmutableQueue<T>>().IsEmpty);

            if (this.IsEmpty)
            {
                return new ImmutableQueue<T>(ImmutableStack.Create(value), ImmutableStack<T>.Empty);
            }
            else
            {
                return new ImmutableQueue<T>(_forwards, _backwards.Push(value));
            }
        }

        /// <summary>
        /// Adds an element to the back of the queue.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>
        /// The new queue.
        /// </returns>
        [Pure]
        IImmutableQueue<T> IImmutableQueue<T>.Enqueue(T value)
        {
            return this.Enqueue(value);
        }

        /// <summary>
        /// Returns a queue that is missing the front element.
        /// </summary>
        /// <returns>A queue; never <c>null</c>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the queue is empty.</exception>
        [Pure]
        public ImmutableQueue<T> Dequeue()
        {
            if (this.IsEmpty)
            {
                ThrowInvalidEmptyOperation();
            }

            return _dequeue ?? this.CreateDequeue();
        }

        /// <summary>
        /// Creates and caches the result of <see cref="Dequeue()"/>.
        /// </summary>
        private ImmutableQueue<T> CreateDequeue()
        {
            Debug.Assert(!this.IsEmpty);
            Debug.Assert(_dequeue == null);

            ImmutableStack<T> f = _forwards.Pop();
            if (!f.IsEmpty)
            {
                _dequeue = new ImmutableQueue<T>(f, _backwards);
            }
            else if (_backwards.IsEmpty)
            {
                _dequeue = ImmutableQueue<T>.Empty;
            }
            else
            {
                _dequeue = new ImmutableQueue<T>(_backwards.Reverse(), ImmutableStack<T>.Empty);
            }

            Debug.Assert(_dequeue != null);
            return _dequeue;
        }

        /// <summary>
        /// Retrieves the item at the head of the queue, and returns a queue with the head element removed.
        /// </summary>
        /// <param name="value">Receives the value from the head of the queue.</param>
        /// <returns>The new queue with the head element removed.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the queue is empty.</exception>
        [SuppressMessage("Microsoft.Design", "CA1021:AvoidOutParameters", MessageId = "0#")]
        [Pure]
        public ImmutableQueue<T> Dequeue(out T value)
        {
            value = this.Peek();
            return _dequeue ?? this.CreateDequeue();
        }

        /// <summary>
        /// Returns a queue that is missing the front element.
        /// </summary>
        /// <returns>A queue; never <c>null</c>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the queue is empty.</exception>
        [Pure]
        IImmutableQueue<T> IImmutableQueue<T>.Dequeue()
        {
            return this.Dequeue();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// An <see cref="Enumerator"/> that can be used to iterate through the collection.
        /// </returns>
        [Pure]
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// A <see cref="IEnumerator{T}"/> that can be used to iterate through the collection.
        /// </returns>
        [Pure]
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new EnumeratorObject(this);
        }

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="IEnumerator"/> object that can be used to iterate through the collection.
        /// </returns>
        [Pure]
        IEnumerator IEnumerable.GetEnumerator()
        {
            return new EnumeratorObject(this);
        }

        /// <summary>
        /// Throws an <see cref="InvalidOperationException"/> due to an empty queue.
        /// </summary>
        private static void ThrowInvalidEmptyOperation()
        {
            throw new InvalidOperationException(SR.InvalidEmptyOperation);
        }
    }
}

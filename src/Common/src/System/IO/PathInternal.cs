// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;

namespace System.IO
{
    /// <summary>Contains internal path helpers that are shared between many projects.</summary>
    internal static partial class PathInternal
    {
        /// <summary>
        /// Callback type with which <see cref="BorrowCharBuffer{T, TState}"/> needs to be invoked.
        /// </summary>
        internal unsafe delegate T RentedCallback<out T, in TState>(char* buffer, int length, TState state);

        // We don't want to cause a stack overflow, so only allocate
        // up to this many characters via stackalloc before we switch
        // to using heap-allocated buffers.
        private static int CharacterStackLimit = 512; // Same as 1024 bytes

        /// <summary>
        /// Obtains a character buffer, avoiding heap allocations if possible,
        /// and invokes the specified callback with the buffer.
        /// If <paramref name="length"/> is below a certain limit, the buffer
        /// will be stack allocated. Otherwise, the buffer will be obtained via
        /// a regular heap allocation.
        /// </summary>
        /// <param name="length">The minimum length of the character buffer needed.</param>
        /// <param name="callback">The callback with which to invoke the rented buffer.</param>
        /// <param name="state">Any state that needs to get passed into the callback to avoid closures.</param>
        /// <returns>The result of the callback's invocation.</returns>
        /// <remarks>
        /// The buffer <paramref name="callback"/> is invoked with is NOT guaranteed to be zero-initialized.
        /// stackalloc is allowed to return 'dirty' buffers that are not completely zeroed out. Please do not
        /// depend on this fact in your code, or otherwise it may introdce subtle bugs.
        /// Additionally, please note that the buffer passed to <paramref name="callback"/> will be <see cref="null"/>
        /// if the value of <paramref name="length"/> is 0.
        /// </remarks>
        internal unsafe static T BorrowCharBuffer<T, TState>(int length, RentedCallback<T, TState> callback, TState state)
        {
            Debug.Assert(length >= 0);
            Debug.Assert(callback != null);

            bool allocateOnStack = length <= CharacterStackLimit;

            if (allocateOnStack)
            {
                char* pBuffer = stackalloc char[length];
                return callback(pBuffer, length, state);
            }
            else
            {
                // TODO: Investigate if it would be worth using ArrayPool here.
                char[] buffer = new char[length];
                fixed (char* pBuffer = buffer)
                {
                    return callback(pBuffer, length, state);
                }
            }
        }

        /// <summary>
        /// Checks for invalid path characters in the given path.
        /// </summary>
        /// <exception cref="System.ArgumentNullException">Thrown if the path is null.</exception>
        /// <exception cref="System.ArgumentException">Thrown if the path has invalid characters.</exception>
        /// <param name="path">The path to check for invalid characters.</param>
        internal static void CheckInvalidPathChars(string path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            if (PathInternal.HasIllegalCharacters(path))
                throw new ArgumentException(SR.Argument_InvalidPathChars, nameof(path));
        }

        /// <summary>
        /// Copies the contents of a string to a pre-allocated character buffer represented by a pointer and a length.
        /// </summary>
        /// <param name="source">The string to copy from.</param>
        /// <param name="destination">A pointer to the buffer where the string will be written to.</param>
        internal unsafe static void CopyTo(this string source, char* destination)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            source.CopyTo(0, destination, source.Length);
        }

        /// <summary>
        /// Copies the contents of a string to a pre-allocated character buffer represented by a pointer and a length.
        /// </summary>
        /// <param name="source">The string to copy from.</param>
        /// <param name="sourceIndex">The index within <paramref name="source"/> at which to start copying.</param>
        /// <param name="destination">A pointer to the buffer where the string will be written to.</param>
        /// <param name="count">The number of characters within <paramref name="source"/> to copy.</param>
        internal unsafe static void CopyTo(this string source, int sourceIndex, char* destination, int count)
        {
            // Since we're dealing with unsafe code, let's be extra careful and perform
            // argument validation even in Release mode.
            if (source == null || destination == null)
            {
                throw new ArgumentNullException(source == null ? nameof(source) : nameof(destination));
            }
            if ((sourceIndex | count) < 0)
            {
                throw new ArgumentOutOfRangeException(sourceIndex < 0 ? nameof(sourceIndex) : nameof(count));
            }
            if (count > source.Length - sourceIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            }

            fixed (char* pSource = source)
            {
                // Just call Buffer.MemoryCopy, which is the same thing as many String methods do internally.
                // Micro-optimization: We want to cast to ulong beforehand since the overload taking longs
                // does a checked cast.
                ulong byteCount = (ulong)count * 2;
                Buffer.MemoryCopy(pSource, destination, byteCount, byteCount);
            }
        }

        /// <summary>
        /// Returns true if the given StringBuilder starts with the given value.
        /// </summary>
        /// <param name="value">The string to compare against the start of the StringBuilder.</param>
        internal static bool StartsWithOrdinal(this StringBuilder builder, string value)
        {
            if (value == null || builder.Length < value.Length)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (builder[i] != value[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if the given string starts with the given value.
        /// </summary>
        /// <param name="value">The string to compare against the start of the source string.</param>
        internal static bool StartsWithOrdinal(this string source, string value)
        {
            if (value == null || source.Length < value.Length)
                return false;

            return source.StartsWith(value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Trims the specified characters from the end of the StringBuilder.
        /// </summary>
        internal static StringBuilder TrimEnd(this StringBuilder builder, params char[] trimChars)
        {
            if (trimChars == null || trimChars.Length == 0)
                return builder;

            int end = builder.Length - 1;

            for (; end >= 0; end--)
            {
                int i = 0;
                char ch = builder[end];
                for (; i < trimChars.Length; i++)
                {
                    if (trimChars[i] == ch) break;
                }
                if (i == trimChars.Length)
                {
                    // Not a trim char
                    break;
                }
            }

            builder.Length = end + 1;
            return builder;
        }
        
        /// <summary>
        /// Returns the start index of the filename
        /// in the given path, or 0 if no directory
        /// or volume separator is found.
        /// </summary>
        /// <param name="path">The path in which to find the index of the filename.</param>
        /// <remarks>
        /// This method returns path.Length for
        /// inputs like "/usr/foo/" on Unix. As such,
        /// it is not safe for being used to index
        /// the string without additional verification.
        /// </remarks>
        internal static int FindFileNameIndex(string path)
        {
            Debug.Assert(path != null);
            PathInternal.CheckInvalidPathChars(path);
            
            for (int i = path.Length - 1; i >= 0; i--)
            {
                char ch = path[i];
                if (IsDirectoryOrVolumeSeparator(ch))
                    return i + 1;
            }
            
            return 0; // the whole path is the filename
        }
    }
}

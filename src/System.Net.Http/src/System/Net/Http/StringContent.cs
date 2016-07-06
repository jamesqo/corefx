// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace System.Net.Http
{
    public class StringContent : ByteArrayContent
    {
        private static class RentState
        {
            public const int Present = 0;
            public const int Taken = 1;
        }

        private static int s_rentState = RentState.Present;

        private const string defaultMediaType = "text/plain";

        private byte[] _rented;

        public StringContent(string content)
            : this(content, null, null)
        {
        }

        public StringContent(string content, Encoding encoding)
            : this(content, encoding, null)
        {
        }

        public StringContent(string content, Encoding encoding, string mediaType)
            : this(GetContentBuffer(content, encoding), encoding, mediaType)
        {
        }

        private StringContent(ArraySegment<byte> content, Encoding encoding, string mediaType)
            : base(content.Array, content.Offset, content.Count)
        {
            // Initialize the 'Content-Type' header with information provided by parameters. 
            MediaTypeHeaderValue headerValue = new MediaTypeHeaderValue((mediaType == null) ? defaultMediaType : mediaType);
            headerValue.CharSet = (encoding == null) ? HttpContent.DefaultStringEncoding.WebName : encoding.WebName;

            Headers.ContentType = headerValue;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    var rented = _rented;
                    if (rented != null && ReturnIfNotPresent(rented))
                    {
                        _rented = null;
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        // A StringContent is essentially a ByteArrayContent. We serialize the string into a byte-array in the 
        // constructor using encoding information provided by the caller (if any). When this content is sent, the
        // Content-Length can be retrieved easily (length of the array).
        private static ArraySegment<byte> GetContentBuffer(string content, Encoding encoding)
        {
            // In this case we treat 'null' strings different from string.Empty in order to be consistent with our 
            // other *Content constructors: 'null' throws, empty values are allowed.
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (encoding == null)
            {
                encoding = HttpContent.DefaultStringEncoding;
            }

            var previousState = Interlocked.CompareExchange(ref s_rentState, RentState.Taken, RentState.Present);
            if (previousState == RentState.Present)
            {
                int byteCount = encoding.GetByteCount(content);
                var rented = ArrayPool<byte>.Shared.Rent(minimumLength: byteCount);

                try
                {
                    int written = encoding.GetBytes(content, 0, content.Length, rented, 0);
                    Debug.Assert(written == byteCount);
                    return new ArraySegment<byte>(rented, 0, written);
                }
                catch
                {
                    ReturnIfNotPresent(rented);
                    throw;
                }
            }
            
            return new ArraySegment<byte>(encoding.GetBytes(content));
        }

        private static bool ReturnIfNotPresent(byte[] buffer)
        {
            Debug.Assert(buffer != null);

            var previousState = Interlocked.CompareExchange(ref s_rentState, RentState.Present, RentState.Taken);
            if (previousState == RentState.Taken)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                return true;
            }
            
            return false;
        }
    }
}

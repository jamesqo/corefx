// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Don't entity encode high chars (160 to 256)
#define ENTITY_ENCODE_HIGH_ASCII_CHARS

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;

namespace System.Net
{
    public static class WebUtility
    {
        // some consts copied from Char / CharUnicodeInfo since we don't have friend access to those types
        private const char HIGH_SURROGATE_START = '\uD800';
        private const char LOW_SURROGATE_START = '\uDC00';
        private const char LOW_SURROGATE_END = '\uDFFF';
        private const int UNICODE_PLANE00_END = 0x00FFFF;
        private const int UNICODE_PLANE01_START = 0x10000;
        private const int UNICODE_PLANE16_END = 0x10FFFF;

        private const int UnicodeReplacementChar = '\uFFFD';

        #region HtmlEncode / HtmlDecode methods

        private static readonly char[] s_htmlEntityEndingChars = new char[] { ';', '&' };

        public static string HtmlEncode(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return value;
            }

            // Don't create StringBuilder if we don't have anything to encode
            int index = IndexOfHtmlEncodingChars(value, 0);
            if (index == -1)
            {
                return value;
            }

            StringBuilder sb = StringBuilderCache.Acquire(value.Length);
            HtmlEncode(value, index, sb);
            return StringBuilderCache.GetStringAndRelease(sb);
        }

        private static unsafe void HtmlEncode(string value, int index, StringBuilder output)
        {
            Debug.Assert(output != null);
            Debug.Assert(0 <= index && index <= value.Length, "0 <= index && index <= value.Length");

            int cch = value.Length - index;
            fixed (char* str = value)
            {
                char* pch = str;
                while (index-- > 0)
                {
                    output.Append(*pch++);
                }

                for (; cch > 0; cch--, pch++)
                {
                    char ch = *pch;
                    if (ch <= '>')
                    {
                        switch (ch)
                        {
                            case '<':
                                output.Append("&lt;");
                                break;
                            case '>':
                                output.Append("&gt;");
                                break;
                            case '"':
                                output.Append("&quot;");
                                break;
                            case '\'':
                                output.Append("&#39;");
                                break;
                            case '&':
                                output.Append("&amp;");
                                break;
                            default:
                                output.Append(ch);
                                break;
                        }
                    }
                    else
                    {
                        int valueToEncode = -1; // set to >= 0 if needs to be encoded

#if ENTITY_ENCODE_HIGH_ASCII_CHARS
                        if (ch >= 160 && ch < 256)
                        {
                            // The seemingly arbitrary 160 comes from RFC
                            valueToEncode = ch;
                        }
                        else
#endif // ENTITY_ENCODE_HIGH_ASCII_CHARS
                        if (Char.IsSurrogate(ch))
                        {
                            int scalarValue = GetNextUnicodeScalarValueFromUtf16Surrogate(ref pch, ref cch);
                            if (scalarValue >= UNICODE_PLANE01_START)
                            {
                                valueToEncode = scalarValue;
                            }
                            else
                            {
                                // Don't encode BMP characters (like U+FFFD) since they wouldn't have
                                // been encoded if explicitly present in the string anyway.
                                ch = (char)scalarValue;
                            }
                        }

                        if (valueToEncode >= 0)
                        {
                            // value needs to be encoded
                            output.Append("&#");
                            output.Append(valueToEncode.ToString(CultureInfo.InvariantCulture));
                            output.Append(';');
                        }
                        else
                        {
                            // write out the character directly
                            output.Append(ch);
                        }
                    }
                }
            }
        }

        public static string HtmlDecode(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return value;
            }

            // Don't create StringBuilder if we don't have anything to encode
            if (!StringRequiresHtmlDecoding(value))
            {
                return value;
            }

            StringBuilder sb = StringBuilderCache.Acquire(value.Length);
            HtmlDecode(value, sb);
            return StringBuilderCache.GetStringAndRelease(sb);
        }

        [SuppressMessage("Microsoft.Usage", "CA1806:DoNotIgnoreMethodResults", MessageId = "System.UInt16.TryParse(System.String,System.Globalization.NumberStyles,System.IFormatProvider,System.UInt16@)", Justification = "UInt16.TryParse guarantees that result is zero if the parse fails.")]
        private static void HtmlDecode(string value, StringBuilder output)
        {
            Debug.Assert(output != null);

            int l = value.Length;
            for (int i = 0; i < l; i++)
            {
                char ch = value[i];

                if (ch == '&')
                {
                    // We found a '&'. Now look for the next ';' or '&'. The idea is that
                    // if we find another '&' before finding a ';', then this is not an entity,
                    // and the next '&' might start a real entity (VSWhidbey 275184)
                    int index = value.IndexOfAny(s_htmlEntityEndingChars, i + 1);
                    if (index > 0 && value[index] == ';')
                    {
                        int entityOffset = i + 1;
                        int entityLength = index - entityOffset;

                        if (entityLength > 1 && value[entityOffset] == '#')
                        {
                            // The # syntax can be in decimal or hex, e.g.
                            //      &#229;  --> decimal
                            //      &#xE5;  --> same char in hex
                            // See http://www.w3.org/TR/REC-html40/charset.html#entities

                            bool parsedSuccessfully;
                            uint parsedValue;
                            if (value[entityOffset + 1] == 'x' || value[entityOffset + 1] == 'X')
                            {
                                parsedSuccessfully = uint.TryParse(value.Substring(entityOffset + 2, entityLength - 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsedValue);
                            }
                            else
                            {
                                parsedSuccessfully = uint.TryParse(value.Substring(entityOffset + 1, entityLength - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue);
                            }

                            if (parsedSuccessfully)
                            {
                                // decoded character must be U+0000 .. U+10FFFF, excluding surrogates
                                parsedSuccessfully = ((parsedValue < HIGH_SURROGATE_START) || (LOW_SURROGATE_END < parsedValue && parsedValue <= UNICODE_PLANE16_END));
                            }

                            if (parsedSuccessfully)
                            {
                                if (parsedValue <= UNICODE_PLANE00_END)
                                {
                                    // single character
                                    output.Append((char)parsedValue);
                                }
                                else
                                {
                                    // multi-character
                                    char leadingSurrogate, trailingSurrogate;
                                    ConvertSmpToUtf16(parsedValue, out leadingSurrogate, out trailingSurrogate);
                                    output.Append(leadingSurrogate);
                                    output.Append(trailingSurrogate);
                                }

                                i = index; // already looked at everything until semicolon
                                continue;
                            }
                        }
                        else
                        {
                            string entity = value.Substring(entityOffset, entityLength);
                            i = index; // already looked at everything until semicolon

                            char entityChar = HtmlEntities.Lookup(entity);
                            if (entityChar != (char)0)
                            {
                                ch = entityChar;
                            }
                            else
                            {
                                output.Append('&');
                                output.Append(entity);
                                output.Append(';');
                                continue;
                            }
                        }
                    }
                }

                output.Append(ch);
            }
        }

        private static unsafe int IndexOfHtmlEncodingChars(string s, int startPos)
        {
            Debug.Assert(0 <= startPos && startPos <= s.Length, "0 <= startPos && startPos <= s.Length");

            int cch = s.Length - startPos;
            fixed (char* str = s)
            {
                for (char* pch = &str[startPos]; cch > 0; pch++, cch--)
                {
                    char ch = *pch;
                    if (ch <= '>')
                    {
                        switch (ch)
                        {
                            case '<':
                            case '>':
                            case '"':
                            case '\'':
                            case '&':
                                return s.Length - cch;
                        }
                    }
#if ENTITY_ENCODE_HIGH_ASCII_CHARS
                    else if (ch >= 160 && ch < 256)
                    {
                        return s.Length - cch;
                    }
#endif // ENTITY_ENCODE_HIGH_ASCII_CHARS
                    else if (Char.IsSurrogate(ch))
                    {
                        return s.Length - cch;
                    }
                }
            }

            return -1;
        }

        #endregion

        #region UrlEncode implementation

        private static byte[] UrlEncode(byte[] bytes, int offset, int count, bool alwaysCreateNewReturnValue)
        {
            byte[] encoded = UrlEncode(bytes, offset, count);

            return (alwaysCreateNewReturnValue && (encoded != null) && (encoded == bytes))
                ? (byte[])encoded.Clone()
                : encoded;
        }

        private static byte[] UrlEncode(byte[] bytes, int offset, int count)
        {
            if (!ValidateUrlEncodingParameters(bytes, offset, count))
            {
                return null;
            }

            int cSpaces = 0;
            int cUnsafe = 0;

            // count them first
            for (int i = 0; i < count; i++)
            {
                char ch = (char)bytes[offset + i];

                if (ch == ' ')
                    cSpaces++;
                else if (!IsUrlSafeChar(ch))
                    cUnsafe++;
            }

            // nothing to expand?
            if (cSpaces == 0 && cUnsafe == 0)
            {
                if (0 == offset && bytes.Length == count)
                {
                    return bytes;
                }
                else
                {
                    var subarray = new byte[count];
                    Buffer.BlockCopy(bytes, offset, subarray, 0, count);
                    return subarray;
                }
            }

            // expand not 'safe' characters into %XX, spaces to +s
            byte[] expandedBytes = new byte[count + cUnsafe * 2];
            int pos = 0;

            for (int i = 0; i < count; i++)
            {
                byte b = bytes[offset + i];
                char ch = (char)b;

                if (IsUrlSafeChar(ch))
                {
                    expandedBytes[pos++] = b;
                }
                else if (ch == ' ')
                {
                    expandedBytes[pos++] = (byte)'+';
                }
                else
                {
                    expandedBytes[pos++] = (byte)'%';
                    expandedBytes[pos++] = (byte)IntToHex((b >> 4) & 0xf);
                    expandedBytes[pos++] = (byte)IntToHex(b & 0x0f);
                }
            }

            return expandedBytes;
        }

        #endregion

        #region UrlEncode public methods

        [SuppressMessage("Microsoft.Design", "CA1055:UriReturnValuesShouldNotBeStrings", Justification = "Already shipped public API; code moved here as part of API consolidation")]
        public static string UrlEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] encodedBytes = UrlEncode(bytes, 0, bytes.Length, false /* alwaysCreateNewReturnValue */);
            return Encoding.UTF8.GetString(encodedBytes, 0, encodedBytes.Length);
        }

        public static byte[] UrlEncodeToBytes(byte[] value, int offset, int count)
        {
            return UrlEncode(value, offset, count, true /* alwaysCreateNewReturnValue */);
        }

        #endregion

        #region UrlDecode implementation

        private static string UrlDecodeInternal(string value, Encoding encoding)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            int count = value.Length;
            UrlDecoder helper = new UrlDecoder(count, encoding);

            // go through the string's chars collapsing %XX and
            // appending each char as char, with exception of %XX constructs
            // that are appended as bytes

            for (int pos = 0; pos < count; pos++)
            {
                char ch = value[pos];

                if (ch == '+')
                {
                    ch = ' ';
                }
                else if (ch == '%' && pos < count - 2)
                {
                    int h1 = HexToInt(value[pos + 1]);
                    int h2 = HexToInt(value[pos + 2]);

                    if (h1 >= 0 && h2 >= 0)
                    {     // valid 2 hex chars
                        byte b = (byte)((h1 << 4) | h2);
                        pos += 2;

                        // don't add as char
                        helper.AddByte(b);
                        continue;
                    }
                }

                if ((ch & 0xFF80) == 0)
                    helper.AddByte((byte)ch); // 7 bit have to go as bytes because of Unicode
                else
                    helper.AddChar(ch);
            }

            return helper.GetString();
        }

        private static byte[] UrlDecodeInternal(byte[] bytes, int offset, int count)
        {
            if (!ValidateUrlEncodingParameters(bytes, offset, count))
            {
                return null;
            }

            int decodedBytesCount = 0;
            byte[] decodedBytes = new byte[count];

            for (int i = 0; i < count; i++)
            {
                int pos = offset + i;
                byte b = bytes[pos];

                if (b == '+')
                {
                    b = (byte)' ';
                }
                else if (b == '%' && i < count - 2)
                {
                    int h1 = HexToInt((char)bytes[pos + 1]);
                    int h2 = HexToInt((char)bytes[pos + 2]);

                    if (h1 >= 0 && h2 >= 0)
                    {     // valid 2 hex chars
                        b = (byte)((h1 << 4) | h2);
                        i += 2;
                    }
                }

                decodedBytes[decodedBytesCount++] = b;
            }

            if (decodedBytesCount < decodedBytes.Length)
            {
                Array.Resize(ref decodedBytes, decodedBytesCount);
            }

            return decodedBytes;
        }

        #endregion

        #region UrlDecode public methods


        [SuppressMessage("Microsoft.Design", "CA1055:UriReturnValuesShouldNotBeStrings", Justification = "Already shipped public API; code moved here as part of API consolidation")]
        public static string UrlDecode(string encodedValue)
        {
            return UrlDecodeInternal(encodedValue, Encoding.UTF8);
        }

        public static byte[] UrlDecodeToBytes(byte[] encodedValue, int offset, int count)
        {
            return UrlDecodeInternal(encodedValue, offset, count);
        }

        #endregion

        #region Helper methods

        // similar to Char.ConvertFromUtf32, but doesn't check arguments or generate strings
        // input is assumed to be an SMP character
        private static void ConvertSmpToUtf16(uint smpChar, out char leadingSurrogate, out char trailingSurrogate)
        {
            Debug.Assert(UNICODE_PLANE01_START <= smpChar && smpChar <= UNICODE_PLANE16_END);

            int utf32 = (int)(smpChar - UNICODE_PLANE01_START);
            leadingSurrogate = (char)((utf32 / 0x400) + HIGH_SURROGATE_START);
            trailingSurrogate = (char)((utf32 % 0x400) + LOW_SURROGATE_START);
        }

        private static unsafe int GetNextUnicodeScalarValueFromUtf16Surrogate(ref char* pch, ref int charsRemaining)
        {
            // invariants
            Debug.Assert(charsRemaining >= 1);
            Debug.Assert(Char.IsSurrogate(*pch));

            if (charsRemaining <= 1)
            {
                // not enough characters remaining to resurrect the original scalar value
                return UnicodeReplacementChar;
            }

            char leadingSurrogate = pch[0];
            char trailingSurrogate = pch[1];

            if (Char.IsSurrogatePair(leadingSurrogate, trailingSurrogate))
            {
                // we're going to consume an extra char
                pch++;
                charsRemaining--;

                // below code is from Char.ConvertToUtf32, but without the checks (since we just performed them)
                return (((leadingSurrogate - HIGH_SURROGATE_START) * 0x400) + (trailingSurrogate - LOW_SURROGATE_START) + UNICODE_PLANE01_START);
            }
            else
            {
                // unmatched surrogate
                return UnicodeReplacementChar;
            }
        }

        private static int HexToInt(char h)
        {
            return (h >= '0' && h <= '9') ? h - '0' :
            (h >= 'a' && h <= 'f') ? h - 'a' + 10 :
            (h >= 'A' && h <= 'F') ? h - 'A' + 10 :
            -1;
        }

        private static char IntToHex(int n)
        {
            Debug.Assert(n < 0x10);

            if (n <= 9)
                return (char)(n + (int)'0');
            else
                return (char)(n - 10 + (int)'A');
        }

        // Set of safe chars, from RFC 1738.4 minus '+'
        private static bool IsUrlSafeChar(char ch)
        {
            if (ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9')
                return true;

            switch (ch)
            {
                case '-':
                case '_':
                case '.':
                case '!':
                case '*':
                case '(':
                case ')':
                    return true;
            }

            return false;
        }

        private static bool ValidateUrlEncodingParameters(byte[] bytes, int offset, int count)
        {
            if (bytes == null && count == 0)
                return false;
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            if (offset < 0 || offset > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0 || offset + count > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return true;
        }

        private static bool StringRequiresHtmlDecoding(string s)
        {
            // this string requires html decoding if it contains '&' or a surrogate character
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '&' || Char.IsSurrogate(c))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        // Internal struct to facilitate URL decoding -- keeps char buffer and byte buffer, allows appending of either chars or bytes
        private struct UrlDecoder
        {
            private int _bufferSize;

            // Accumulate characters in a special array
            private int _numChars;
            private char[] _charBuffer;

            // Accumulate bytes for decoding into characters in a special array
            private int _numBytes;
            private byte[] _byteBuffer;

            // Encoding to convert chars to bytes
            private Encoding _encoding;

            private void FlushBytes()
            {
                Debug.Assert(_numBytes > 0);
                _numChars += _encoding.GetChars(_byteBuffer, 0, _numBytes, _charBuffer, _numChars);
                _numBytes = 0;
            }

            internal UrlDecoder(int bufferSize, Encoding encoding)
            {
                _bufferSize = bufferSize;
                _encoding = encoding;

                _charBuffer = new char[bufferSize];
                
                _numChars = 0;
                _numBytes = 0;
                _byteBuffer = null; // byte buffer created on demand
            }

            internal void AddChar(char ch)
            {
                if (_numBytes > 0)
                    FlushBytes();

                _charBuffer[_numChars++] = ch;
            }

            internal void AddByte(byte b)
            {
                if (_byteBuffer == null)
                    _byteBuffer = new byte[_bufferSize];

                _byteBuffer[_numBytes++] = b;
            }

            internal String GetString()
            {
                if (_numBytes > 0)
                    FlushBytes();

                if (_numChars > 0)
                    return new String(_charBuffer, 0, _numChars);
                else
                    return String.Empty;
            }
        }

        // helper class for lookup of HTML encoding entities
        private static class HtmlEntities
        {
            // Maps entity strings => unicode chars
            public static char Lookup(string entity)
            {
                switch (entity)
                {
                    // The list is from http://www.w3.org/TR/REC-html40/sgml/entities.html, except for &apos;, which
                    // is defined in http://www.w3.org/TR/2008/REC-xml-20081126/#sec-predefined-ent.
                    case "quot": return '\x0022';
                    case "amp": return '\x0026';
                    case "apos": return '\x0027';
                    case "lt": return '\x003c';
                    case "gt": return '\x003e';
                    case "nbsp": return '\x00a0';
                    case "iexcl": return '\x00a1';
                    case "cent": return '\x00a2';
                    case "pound": return '\x00a3';
                    case "curren": return '\x00a4';
                    case "yen": return '\x00a5';
                    case "brvbar": return '\x00a6';
                    case "sect": return '\x00a7';
                    case "uml": return '\x00a8';
                    case "copy": return '\x00a9';
                    case "ordf": return '\x00aa';
                    case "laquo": return '\x00ab';
                    case "not": return '\x00ac';
                    case "shy": return '\x00ad';
                    case "reg": return '\x00ae';
                    case "macr": return '\x00af';
                    case "deg": return '\x00b0';
                    case "plusmn": return '\x00b1';
                    case "sup2": return '\x00b2';
                    case "sup3": return '\x00b3';
                    case "acute": return '\x00b4';
                    case "micro": return '\x00b5';
                    case "para": return '\x00b6';
                    case "middot": return '\x00b7';
                    case "cedil": return '\x00b8';
                    case "sup1": return '\x00b9';
                    case "ordm": return '\x00ba';
                    case "raquo": return '\x00bb';
                    case "frac14": return '\x00bc';
                    case "frac12": return '\x00bd';
                    case "frac34": return '\x00be';
                    case "iquest": return '\x00bf';
                    case "Agrave": return '\x00c0';
                    case "Aacute": return '\x00c1';
                    case "Acirc": return '\x00c2';
                    case "Atilde": return '\x00c3';
                    case "Auml": return '\x00c4';
                    case "Aring": return '\x00c5';
                    case "AElig": return '\x00c6';
                    case "Ccedil": return '\x00c7';
                    case "Egrave": return '\x00c8';
                    case "Eacute": return '\x00c9';
                    case "Ecirc": return '\x00ca';
                    case "Euml": return '\x00cb';
                    case "Igrave": return '\x00cc';
                    case "Iacute": return '\x00cd';
                    case "Icirc": return '\x00ce';
                    case "Iuml": return '\x00cf';
                    case "ETH": return '\x00d0';
                    case "Ntilde": return '\x00d1';
                    case "Ograve": return '\x00d2';
                    case "Oacute": return '\x00d3';
                    case "Ocirc": return '\x00d4';
                    case "Otilde": return '\x00d5';
                    case "Ouml": return '\x00d6';
                    case "times": return '\x00d7';
                    case "Oslash": return '\x00d8';
                    case "Ugrave": return '\x00d9';
                    case "Uacute": return '\x00da';
                    case "Ucirc": return '\x00db';
                    case "Uuml": return '\x00dc';
                    case "Yacute": return '\x00dd';
                    case "THORN": return '\x00de';
                    case "szlig": return '\x00df';
                    case "agrave": return '\x00e0';
                    case "aacute": return '\x00e1';
                    case "acirc": return '\x00e2';
                    case "atilde": return '\x00e3';
                    case "auml": return '\x00e4';
                    case "aring": return '\x00e5';
                    case "aelig": return '\x00e6';
                    case "ccedil": return '\x00e7';
                    case "egrave": return '\x00e8';
                    case "eacute": return '\x00e9';
                    case "ecirc": return '\x00ea';
                    case "euml": return '\x00eb';
                    case "igrave": return '\x00ec';
                    case "iacute": return '\x00ed';
                    case "icirc": return '\x00ee';
                    case "iuml": return '\x00ef';
                    case "eth": return '\x00f0';
                    case "ntilde": return '\x00f1';
                    case "ograve": return '\x00f2';
                    case "oacute": return '\x00f3';
                    case "ocirc": return '\x00f4';
                    case "otilde": return '\x00f5';
                    case "ouml": return '\x00f6';
                    case "divide": return '\x00f7';
                    case "oslash": return '\x00f8';
                    case "ugrave": return '\x00f9';
                    case "uacute": return '\x00fa';
                    case "ucirc": return '\x00fb';
                    case "uuml": return '\x00fc';
                    case "yacute": return '\x00fd';
                    case "thorn": return '\x00fe';
                    case "yuml": return '\x00ff';
                    case "OElig": return '\x0152';
                    case "oelig": return '\x0153';
                    case "Scaron": return '\x0160';
                    case "scaron": return '\x0161';
                    case "Yuml": return '\x0178';
                    case "fnof": return '\x0192';
                    case "circ": return '\x02c6';
                    case "tilde": return '\x02dc';
                    case "Alpha": return '\x0391';
                    case "Beta": return '\x0392';
                    case "Gamma": return '\x0393';
                    case "Delta": return '\x0394';
                    case "Epsilon": return '\x0395';
                    case "Zeta": return '\x0396';
                    case "Eta": return '\x0397';
                    case "Theta": return '\x0398';
                    case "Iota": return '\x0399';
                    case "Kappa": return '\x039a';
                    case "Lambda": return '\x039b';
                    case "Mu": return '\x039c';
                    case "Nu": return '\x039d';
                    case "Xi": return '\x039e';
                    case "Omicron": return '\x039f';
                    case "Pi": return '\x03a0';
                    case "Rho": return '\x03a1';
                    case "Sigma": return '\x03a3';
                    case "Tau": return '\x03a4';
                    case "Upsilon": return '\x03a5';
                    case "Phi": return '\x03a6';
                    case "Chi": return '\x03a7';
                    case "Psi": return '\x03a8';
                    case "Omega": return '\x03a9';
                    case "alpha": return '\x03b1';
                    case "beta": return '\x03b2';
                    case "gamma": return '\x03b3';
                    case "delta": return '\x03b4';
                    case "epsilon": return '\x03b5';
                    case "zeta": return '\x03b6';
                    case "eta": return '\x03b7';
                    case "theta": return '\x03b8';
                    case "iota": return '\x03b9';
                    case "kappa": return '\x03ba';
                    case "lambda": return '\x03bb';
                    case "mu": return '\x03bc';
                    case "nu": return '\x03bd';
                    case "xi": return '\x03be';
                    case "omicron": return '\x03bf';
                    case "pi": return '\x03c0';
                    case "rho": return '\x03c1';
                    case "sigmaf": return '\x03c2';
                    case "sigma": return '\x03c3';
                    case "tau": return '\x03c4';
                    case "upsilon": return '\x03c5';
                    case "phi": return '\x03c6';
                    case "chi": return '\x03c7';
                    case "psi": return '\x03c8';
                    case "omega": return '\x03c9';
                    case "thetasym": return '\x03d1';
                    case "upsih": return '\x03d2';
                    case "piv": return '\x03d6';
                    case "ensp": return '\x2002';
                    case "emsp": return '\x2003';
                    case "thinsp": return '\x2009';
                    case "zwnj": return '\x200c';
                    case "zwj": return '\x200d';
                    case "lrm": return '\x200e';
                    case "rlm": return '\x200f';
                    case "ndash": return '\x2013';
                    case "mdash": return '\x2014';
                    case "lsquo": return '\x2018';
                    case "rsquo": return '\x2019';
                    case "sbquo": return '\x201a';
                    case "ldquo": return '\x201c';
                    case "rdquo": return '\x201d';
                    case "bdquo": return '\x201e';
                    case "dagger": return '\x2020';
                    case "Dagger": return '\x2021';
                    case "bull": return '\x2022';
                    case "hellip": return '\x2026';
                    case "permil": return '\x2030';
                    case "prime": return '\x2032';
                    case "Prime": return '\x2033';
                    case "lsaquo": return '\x2039';
                    case "rsaquo": return '\x203a';
                    case "oline": return '\x203e';
                    case "frasl": return '\x2044';
                    case "euro": return '\x20ac';
                    case "image": return '\x2111';
                    case "weierp": return '\x2118';
                    case "real": return '\x211c';
                    case "trade": return '\x2122';
                    case "alefsym": return '\x2135';
                    case "larr": return '\x2190';
                    case "uarr": return '\x2191';
                    case "rarr": return '\x2192';
                    case "darr": return '\x2193';
                    case "harr": return '\x2194';
                    case "crarr": return '\x21b5';
                    case "lArr": return '\x21d0';
                    case "uArr": return '\x21d1';
                    case "rArr": return '\x21d2';
                    case "dArr": return '\x21d3';
                    case "hArr": return '\x21d4';
                    case "forall": return '\x2200';
                    case "part": return '\x2202';
                    case "exist": return '\x2203';
                    case "empty": return '\x2205';
                    case "nabla": return '\x2207';
                    case "isin": return '\x2208';
                    case "notin": return '\x2209';
                    case "ni": return '\x220b';
                    case "prod": return '\x220f';
                    case "sum": return '\x2211';
                    case "minus": return '\x2212';
                    case "lowast": return '\x2217';
                    case "radic": return '\x221a';
                    case "prop": return '\x221d';
                    case "infin": return '\x221e';
                    case "ang": return '\x2220';
                    case "and": return '\x2227';
                    case "or": return '\x2228';
                    case "cap": return '\x2229';
                    case "cup": return '\x222a';
                    case "int": return '\x222b';
                    case "there4": return '\x2234';
                    case "sim": return '\x223c';
                    case "cong": return '\x2245';
                    case "asymp": return '\x2248';
                    case "ne": return '\x2260';
                    case "equiv": return '\x2261';
                    case "le": return '\x2264';
                    case "ge": return '\x2265';
                    case "sub": return '\x2282';
                    case "sup": return '\x2283';
                    case "nsub": return '\x2284';
                    case "sube": return '\x2286';
                    case "supe": return '\x2287';
                    case "oplus": return '\x2295';
                    case "otimes": return '\x2297';
                    case "perp": return '\x22a5';
                    case "sdot": return '\x22c5';
                    case "lceil": return '\x2308';
                    case "rceil": return '\x2309';
                    case "lfloor": return '\x230a';
                    case "rfloor": return '\x230b';
                    case "lang": return '\x2329';
                    case "rang": return '\x232a';
                    case "loz": return '\x25ca';
                    case "spades": return '\x2660';
                    case "clubs": return '\x2663';
                    case "hearts": return '\x2665';
                    case "diams": return '\x2666';
                }
                
                return default(char);
            }
        }
    }
}

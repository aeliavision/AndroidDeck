using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VcfEditor.Helpers
{
    public static partial class QuotedPrintableUtility
    {
        // compile-time source-generated matcher — zero runtime compilation overhead.
        [GeneratedRegex("^[0-9A-Fa-f]{2}$")]
        private static partial Regex HexRegex();
        /// <summary>
        /// Decodes a Quoted-Printable string to a standard string.
        /// Handles UTF-8 encoded text commonly found in vCards (CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE).
        /// </summary>
        public static string Decode(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Remove "soft line breaks" (=\r\n) before decoding
            var cleanInput = input.Replace("=\r\n", "").Replace("=\n", "");

            var bytes = new List<byte>();
            for (int i = 0; i < cleanInput.Length; i++)
            {
                char c = cleanInput[i];
                if (c == '=')
                {
                    // Trailing '=' is a soft line break marker (or truncated data). Ignore it.
                    if (i == cleanInput.Length - 1)
                    {
                        break;
                    }
                    if (i + 2 < cleanInput.Length)
                    {
                        var hex = cleanInput.AsSpan(i + 1, 2);
                        // If it's a soft break that wasn't caught (e.g. at end of string), ignore.
                        if (hex.SequenceEqual("\r\n".AsSpan()) || hex.SequenceEqual("\n".AsSpan()))
                        {
                            i += hex.Length;
                            continue;
                        }

                        if (IsHex(hex))
                        {
                            try
                            {
                                bytes.Add(byte.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                i += 2;
                            }
                            catch
                            {
                                // Fallback: treat as literal equals sign
                                bytes.Add((byte)'=');
                            }
                        }
                        else
                        {
                            bytes.Add((byte)'=');
                        }
                    }
                    else
                    {
                        // '=' followed by a single char at EOF is most likely a soft break/truncation.
                        break;
                    }
                }
                else
                {
                    // character whose code point is > 255, and misinterprets code points
                    // 128-255 as raw bytes rather than UTF-8 sequences.
                    // QP-encoded files are ASCII-safe: any literal char in a properly
                    // formed QP stream must be printable ASCII (0x21-0x7E) or a tab/space.
                    // If a non-ASCII char sneaks in (e.g. file was decoded as UTF-8 before
                    // being passed here), re-encode it to UTF-8 bytes so the final
                    // Encoding.UTF8.GetString() call reconstructs the correct codepoint.
                    if (c <= 0x7F)
                    {
                        bytes.Add((byte)c);
                    }
                    else
                    {
                        // Encode the char as UTF-8 and add all resulting bytes.
                        foreach (var b in System.Text.Encoding.UTF8.GetBytes(c.ToString()))
                            bytes.Add(b);
                    }
                }
            }

            // vCard 2.1 is often mixed, but for modern usage we assume UTF-8 for decoded bytes
            // If the original file was read as "Text" by the caller, they might have messed up extended chars,
            // but Quoted-Printable is usually ASCII-safe chars representing bytes.
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Encodes a string to Quoted-Printable format per RFC 2045.
        /// Windows CRLF (\r\n) in QP soft-line-breaks is technically correct per RFC 2045,
        /// but many mobile vCard parsers only handle =\n (LF-only soft breaks).
        /// We use LF-only to maximise cross-platform compatibility.
        /// </summary>
        public static string Encode(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var bytes = Encoding.UTF8.GetBytes(input);
            var sb = new StringBuilder();
            int lineLength = 0;
            const int maxLineLength = 75;

            foreach (var b in bytes)
            {
                string encoded;
                if (b == '\t' || (b >= 33 && b <= 126 && b != '='))
                {
                    encoded = ((char)b).ToString();
                }
                else if (b == ' ')
                {
                    encoded = " ";
                }
                else
                {
                    encoded = $"={b:X2}";
                }
                if (lineLength + encoded.Length > maxLineLength)
                {
                    sb.Append("=\n");
                    lineLength = 0;
                }

                sb.Append(encoded);
                lineLength += encoded.Length;
            }

            return sb.ToString();
        }

        private static bool IsHex(ReadOnlySpan<char> text)
        {
            return HexRegex().IsMatch(text);
        }
    }
}

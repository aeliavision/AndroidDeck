using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VcfEditor.Models;
using VcfEditor.Helpers;

namespace VcfEditor.Core
{
    public class VcfParser
    {
        private readonly ILogger<VcfParser> _logger;

        public VcfParser(ILogger<VcfParser>? logger = null)
        {
            _logger = logger ?? AppLoggerFactory.CreateLogger<VcfParser>();
        }

        public async IAsyncEnumerable<Contact> ParseVcfAsync(
            TextReader reader,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string? line;
            Contact? currentContact = null;
            string? currentProperty = null;
            var propertyValueBuilder = new StringBuilder();
            long totalCharacters = 0;
            var contactCount = 0;

            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateInputLine(line, ref totalCharacters);

                // RFC 6350 §3.2 / vCard 3.0: folded continuation lines start with space/tab.
                bool isFoldedContinuation = currentProperty != null && line.Length > 0 && (line[0] == ' ' || line[0] == '\t');

                // vCard 2.1 / Quoted-Printable continuation: previous line ends with '='.
                bool isQpContinuation = currentProperty != null &&
                                       propertyValueBuilder.Length > 0 &&
                                       propertyValueBuilder[propertyValueBuilder.Length - 1] == '=' &&
                                       currentProperty.Contains("ENCODING=QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);

                if (isFoldedContinuation)
                {
                    propertyValueBuilder.Append(line.AsSpan(1));
                    continue;
                }

                if (isQpContinuation)
                {
                    // Remove the soft-line-break '=' before appending the next line.
                    propertyValueBuilder.Length--;
                    propertyValueBuilder.Append(line);
                    continue;
                }

                // Skip truly blank lines (empty separators between vCards).
                if (string.IsNullOrWhiteSpace(line)) continue;

                // If we were building a property, process it now before starting new one
                if (currentProperty != null)
                {
                    ProcessProperty(currentContact!, currentProperty, propertyValueBuilder.ToString());
                    currentProperty = null;
                    propertyValueBuilder.Clear();
                }

                if (string.Equals(line, "BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    currentContact = new Contact();
                }
                else if (string.Equals(line, "END:VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentContact != null)
                    {
                        contactCount++;
                        ValidateContactCount(contactCount);
                        yield return currentContact;
                        currentContact = null;
                    }
                }
                else if (currentContact != null)
                {
                    // Parse "KEY;PARAM=VAL:VALUE"
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        currentProperty = line[..colonIndex];
                        propertyValueBuilder.Append(line.AsSpan(colonIndex + 1));
                    }
                }
            }
            // if END:VCARD was absent (truncated / malformed files from some Android exporters).
            if (currentContact != null)
            {
                if (currentProperty != null)
                    ProcessProperty(currentContact, currentProperty, propertyValueBuilder.ToString());

                if (!IsContactEmpty(currentContact))
                {
                    LogMessages.TruncatedVcf(_logger);
                    contactCount++;
                    ValidateContactCount(contactCount);
                    yield return currentContact;
                }
            }
        }

        public IEnumerable<Contact> ParseVcf(TextReader reader)
        {
            string? line;
            Contact? currentContact = null;
            string? currentProperty = null;
            var propertyValueBuilder = new StringBuilder();
            long totalCharacters = 0;
            var contactCount = 0;

            while ((line = reader.ReadLine()) != null)
            {
                ValidateInputLine(line, ref totalCharacters);
                // RFC 6350 §3.2 / vCard 3.0: folded continuation lines start with space/tab.
                bool isFoldedContinuation = currentProperty != null && line.Length > 0 && (line[0] == ' ' || line[0] == '\t');

                // vCard 2.1 / Quoted-Printable continuation: previous line ends with '='.
                bool isQpContinuation = currentProperty != null &&
                                       propertyValueBuilder.Length > 0 &&
                                       propertyValueBuilder[propertyValueBuilder.Length - 1] == '=' &&
                                       currentProperty.Contains("ENCODING=QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase);

                if (isFoldedContinuation)
                {
                    propertyValueBuilder.Append(line.AsSpan(1));
                    continue;
                }

                if (isQpContinuation)
                {
                    // Remove the soft-line-break '=' before appending the next line.
                    propertyValueBuilder.Length--;
                    propertyValueBuilder.Append(line);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line)) continue;

                // Flush the buffered property before starting the next one.
                if (currentProperty != null)
                {
                    ProcessProperty(currentContact!, currentProperty, propertyValueBuilder.ToString());
                    currentProperty = null;
                    propertyValueBuilder.Clear();
                }

                if (string.Equals(line, "BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    currentContact = new Contact();
                }
                else if (string.Equals(line, "END:VCARD", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentContact != null)
                    {
                        contactCount++;
                        ValidateContactCount(contactCount);
                        yield return currentContact;
                        currentContact = null;
                    }
                }
                else if (currentContact != null)
                {
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        currentProperty = line[..colonIndex];
                        propertyValueBuilder.Append(line.AsSpan(colonIndex + 1));
                    }
                }
            }

            // Handle truncated files that lack END:VCARD.
            if (currentContact != null)
            {
                if (currentProperty != null)
                    ProcessProperty(currentContact, currentProperty, propertyValueBuilder.ToString());
                if (!IsContactEmpty(currentContact))
                {
                    LogMessages.TruncatedVcf(_logger);
                    contactCount++;
                    ValidateContactCount(contactCount);
                    yield return currentContact;
                }
            }
        }

        public async Task<List<Contact>> ParseVcfFileAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"file not found: {filePath}");
            ValidateFileSize(filePath);

            var contacts = new List<Contact>();

            // Allow file sharing for read
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            await foreach (var contact in ParseVcfAsync(reader, cancellationToken).ConfigureAwait(false))
                contacts.Add(contact);

            return contacts;
        }

        /// <summary>
        /// <c>Task.Run(() =&gt; ParseVcfFileAsync()).GetAwaiter().GetResult()</c>.
        /// When invoked from the WPF UI thread, <c>ReadLineAsync</c> can capture the
        /// <c>DispatcherSynchronizationContext</c> and try to marshal its continuation back
        /// to the UI thread — which is permanently blocked waiting for GetResult(), causing
        /// a deadlock. The fix is a true synchronous implementation that uses the blocking
        /// <c>StreamReader.ReadLine()</c> so no async state machine or context capture is
        /// involved. Callers that can already be on a background thread (e.g. inside
        /// <c>Task.Run</c>) should prefer <see cref="ParseVcfFileAsync"/> directly.
        /// </summary>
        public List<Contact> ParseVcfFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"file not found: {filePath}");
            ValidateFileSize(filePath);

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            return ParseVcf(reader).ToList();
        }


        private static void ValidateInputLine(string line, ref long totalCharacters)
        {
            if (line.Length > VcfParsingLimits.MaxLineCharacters)
                throw new InvalidDataException("VCF line exceeds the configured character limit.");

            totalCharacters = checked(totalCharacters + line.Length + 1L);
            if (totalCharacters > VcfParsingLimits.MaxInputCharacters)
                throw new InvalidDataException("VCF input exceeds the configured character limit.");
        }

        private static void ValidateContactCount(int contactCount)
        {
            if (contactCount > VcfParsingLimits.MaxContactCount)
                throw new InvalidDataException("VCF contact count exceeds the configured limit.");
        }

        private static void ValidateFileSize(string filePath)
        {
            var length = new FileInfo(filePath).Length;
            if (length > VcfParsingLimits.MaxFileBytes)
                throw new InvalidDataException("VCF file exceeds the configured size limit.");
        }

        private static void ProcessProperty(Contact contact, string keyPart, string valuePart)
        {
            if (contact == null) return;

            // keyPart might contain parameters, e.g. "N;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE"
            var keyParts = keyPart.Split(';');
            var propertyName = keyParts[0].ToUpperInvariant();
            var parameters = keyParts.Skip(1).ToList();

            bool isQuotedPrintable = parameters.Any(parameter =>
                parameter.Contains("ENCODING=QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase) ||
                parameter.Contains("ENCODING=QP", StringComparison.OrdinalIgnoreCase));
            
            // Decode value if needed
            string finalValue = isQuotedPrintable ? QuotedPrintableUtility.Decode(valuePart) : valuePart;
            finalValue = finalValue.Trim();

            switch (propertyName)
            {
                case "N":
                    ParseNameLine(contact, finalValue);
                    break;
                case "FN":
                    contact.FullName = finalValue;
                    break;
                case "TEL":
                    ParsePhoneLine(contact, parameters, finalValue);
                    break;
                case "ORG":
                    contact.Organization = finalValue;
                    break;
                case "TITLE":
                    contact.Title = finalValue;
                    break;
                case "EMAIL":
                    contact.Email = finalValue;
                    break;
            }
        }

        private static void ParseNameLine(Contact contact, string value)
        {
            var parts = SplitUnescaped(value, ';');
            if (parts.Count > 0) contact.LastName = UnescapeVcfText(parts[0]).Trim();
            if (parts.Count > 1) contact.FirstName = UnescapeVcfText(parts[1]).Trim();
            if (parts.Count > 2) contact.MiddleName = UnescapeVcfText(parts[2]).Trim();
            if (parts.Count > 3) contact.Prefix = UnescapeVcfText(parts[3]).Trim();
            if (parts.Count > 4) contact.Suffix = UnescapeVcfText(parts[4]).Trim();
        }

        private static bool IsContactEmpty(Contact contact)
        {
            return string.IsNullOrWhiteSpace(contact.FullName)
                   && string.IsNullOrWhiteSpace(contact.FirstName)
                   && string.IsNullOrWhiteSpace(contact.MiddleName)
                   && string.IsNullOrWhiteSpace(contact.LastName)
                   && string.IsNullOrWhiteSpace(contact.Prefix)
                   && string.IsNullOrWhiteSpace(contact.Suffix)
                   && string.IsNullOrWhiteSpace(contact.Organization)
                   && string.IsNullOrWhiteSpace(contact.Title)
                   && string.IsNullOrWhiteSpace(contact.Email)
                   && (contact.PhoneNumbers == null || contact.PhoneNumbers.Count == 0);
        }

        private static List<string> SplitUnescaped(string input, char delimiter)
        {
            var result = new List<string>();
            if (input == null)
            {
                result.Add(string.Empty);
                return result;
            }

            var sb = new StringBuilder();
            bool escape = false;

            foreach (var c in input)
            {
                if (c == '\\')
                {
                    // Toggle escape on each backslash; when we see an escaped backslash (\\),
                    // we emit a single literal backslash and return to non-escaped state.
                    escape = !escape;
                    if (!escape)
                        sb.Append('\\');
                    continue;
                }

                if (c == delimiter)
                {
                    if (escape)
                    {
                        // Delimiter was escaped; treat it as a literal.
                        sb.Append(delimiter);
                        escape = false;
                        continue;
                    }
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                if (escape)
                {
                    // Escaped non-delimiter character: keep the backslash so downstream
                    // UnescapeVcfText can interpret sequences like \n, \;, \, etc.
                    sb.Append('\\');
                    escape = false;
                }

                sb.Append(c);
            }

            if (escape)
            {
                // Trailing backslash: preserve it literally.
                sb.Append('\\');
            }

            result.Add(sb.ToString());
            return result;
        }

        private static string UnescapeVcfText(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // vCard 3.0 escape sequences.
            return value
                .Replace("\\;", ";")
                .Replace("\\,", ",")
                .Replace("\\n", "\n")
                .Replace("\\N", "\n")
                .Replace("\\\\", "\\");
        }

        private static void ParsePhoneLine(Contact contact, List<string> parameters, string value)
        {
            var type = PhoneNumberType.CELL;
            
            // Extract type from parameters (e.g. TEL;CELL;VOICE)
            // vCard 2.1 style: TEL;WORK;VOICE:123
            foreach (var param in parameters)
            {
                var normalized = param.Trim();
                if (normalized.Equals("CELL", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("TYPE=CELL", StringComparison.OrdinalIgnoreCase))
                    type = PhoneNumberType.CELL;
                else if (normalized.Equals("HOME", StringComparison.OrdinalIgnoreCase) ||
                         normalized.Contains("TYPE=HOME", StringComparison.OrdinalIgnoreCase))
                    type = PhoneNumberType.HOME;
                else if (normalized.Equals("WORK", StringComparison.OrdinalIgnoreCase) ||
                         normalized.Contains("TYPE=WORK", StringComparison.OrdinalIgnoreCase))
                    type = PhoneNumberType.WORK;
                else if (normalized.Equals("X-MOBILE", StringComparison.OrdinalIgnoreCase)) type = PhoneNumberType.XMobile;
                else if (normalized.Equals("X-WORK", StringComparison.OrdinalIgnoreCase)) type = PhoneNumberType.XWork;
                else if (normalized.Equals("X-HOME", StringComparison.OrdinalIgnoreCase)) type = PhoneNumberType.XHome;
            }

            contact.PhoneNumbers.Add(new PhoneNumber(value, type));
        }

        public async Task<string> ExportToVcfAsync(List<Contact> contacts)
        {
            return await Task.Run(() => ExportToVcf(contacts));
        }

        /// <summary>
        /// Writes vCards incrementally so large exports do not allocate a second complete
        /// copy of the output before the destination file is written.
        /// </summary>
        public async Task WriteVcfAsync(
            TextWriter writer,
            IEnumerable<Contact> contacts,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(contacts);

            var count = 0;
            foreach (var contact in contacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                count++;
                ValidateContactCount(count);

                var contactBuilder = new StringBuilder(512);
                AppendContact(contactBuilder, contact);
                await writer.WriteAsync(contactBuilder.ToString().AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            LogMessages.VcfExportCompleted(_logger, count);
        }

        public string ExportToVcf(List<Contact> contacts)
        {
            ArgumentNullException.ThrowIfNull(contacts);
            ValidateContactCount(contacts.Count);

            var estimatedSize = contacts.Count * 256;
            var vcfBuilder = new StringBuilder(estimatedSize > 0 ? estimatedSize : 256);
            foreach (var contact in contacts)
                AppendContact(vcfBuilder, contact);

            LogMessages.VcfExportCompleted(_logger, contacts.Count);
            return vcfBuilder.ToString();
        }

        private static void AppendContact(StringBuilder vcfBuilder, Contact contact)
        {
            vcfBuilder.AppendLine(Constants.VcfBeginCard);
            vcfBuilder.AppendLine(Constants.VcfVersion);

            var nameParts = new[]
            {
                contact.LastName ?? "",
                contact.FirstName ?? "",
                contact.MiddleName ?? "",
                contact.Prefix ?? "",
                contact.Suffix ?? ""
            };
            // than 75 octets MUST be folded by inserting CRLF + single WSP at the fold
            // point. Without folding, some parsers (iOS Contacts, macOS AddressBook)
            // reject or truncate vCards with long NOTE / PHOTO / ORG lines.
            AppendFolded(vcfBuilder, $"N:{string.Join(";", nameParts.Select(EscapeVcfValue))}");

            if (!string.IsNullOrWhiteSpace(contact.FullName))
                AppendFolded(vcfBuilder, $"FN:{EscapeVcfValue(contact.FullName)}");

            foreach (var phone in contact.PhoneNumbers)
            {
                var typeString = GetVcfPhoneType(phone.Type);
                AppendFolded(vcfBuilder, $"TEL;{typeString}:{phone.Number}");
            }

            if (!string.IsNullOrWhiteSpace(contact.Organization))
                AppendFolded(vcfBuilder, $"ORG:{EscapeVcfValue(contact.Organization)}");

            if (!string.IsNullOrWhiteSpace(contact.Title))
                AppendFolded(vcfBuilder, $"TITLE:{EscapeVcfValue(contact.Title)}");

            if (!string.IsNullOrWhiteSpace(contact.Email))
                AppendFolded(vcfBuilder, $"EMAIL;INTERNET:{contact.Email}");

            vcfBuilder.AppendLine(Constants.VcfEndCard);
            vcfBuilder.AppendLine();
        }

        /// <summary>
        /// per RFC 6350 §3.2. Continuation lines are prefixed with a single space (LWSP).
        /// Uses UTF-8 byte count for the fold boundary to handle multi-byte characters.
        /// </summary>
        private static void AppendFolded(StringBuilder sb, string line)
        {
            const int maxOctets = 75;
            var bytes = Encoding.UTF8.GetBytes(line);

            if (bytes.Length <= maxOctets)
            {
                sb.AppendLine(line);
                return;
            }

            // Walk the string char-by-char, accumulating UTF-8 byte counts.
            // We fold at character boundaries so we never split a multi-byte sequence.
            int octetCount = 0;
            int lineStart = 0;
            bool firstSegment = true;

            for (int i = 0; i <= line.Length; i++)
            {
                int charOctets = (i < line.Length)
                    ? Encoding.UTF8.GetByteCount(line, i, 1)
                    : int.MaxValue; // sentinel — flush remainder

                bool mustFold = (octetCount + charOctets > maxOctets) || (i == line.Length);

                if (mustFold && i > lineStart)
                {
                    if (!firstSegment) sb.Append(' '); // LWSP continuation prefix
                    sb.Append(line, lineStart, i - lineStart);
                    sb.Append("\r\n");
                    lineStart = i;
                    octetCount = 1; // the leading LWSP space counts as 1 octet on next line
                    firstSegment = false;
                }

                if (i < line.Length)
                    octetCount += charOctets;
            }
        }

        /// <summary>
        /// Escapes a vCard text value per RFC 6350 §3.4 (backslash, semicolon, comma, newline).
        /// </summary>
        private static string EscapeVcfValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\n", "\\n")
                .Replace("\r", "");
        }

        private static string GetVcfPhoneType(PhoneNumberType type)
        {
            return type switch
            {
                PhoneNumberType.CELL => Constants.PhoneTypeCell,
                PhoneNumberType.HOME => Constants.PhoneTypeHome,
                PhoneNumberType.WORK => Constants.PhoneTypeWork,
                PhoneNumberType.XMobile => Constants.PhoneTypeMobile,
                PhoneNumberType.XWork => "X-WORK",
                PhoneNumberType.XHome => "X-HOME",
                PhoneNumberType.XOther => Constants.PhoneTypeOther,
                PhoneNumberType.XCustom => "X-CUSTOM",
                _ => Constants.PhoneTypeCell
            };
        }
    }

    public class VcfParsingException : Exception
    {
        public VcfParsingException(string message) : base(message) { }
        public VcfParsingException(string message, Exception innerException) : base(message, innerException) { }
    }
}
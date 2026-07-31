using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace VcfEditor.Models.DTOs
{
    /// <summary>
    /// Maps between <see cref="ContactDto"/> (API) and <see cref="Contact"/> (domain).
    /// </summary>
    public static class DtoMapper
    {
        // Android Contact Provider phone type constants
        private const int AndroidTypeCustom = 0;
        private const int AndroidTypeHome = 1;
        private const int AndroidTypeMobile = 2;
        private const int AndroidTypeWork = 3;
        private const int AndroidTypeFaxWork = 4;
        private const int AndroidTypeFaxHome = 5;
        private const int AndroidTypePager = 6;
        private const int AndroidTypeOther = 7;
        private const int AndroidTypeCallback = 8;
        private const int AndroidTypeCar = 9;
        private const int AndroidTypeCompanyMain = 10;
        private const int AndroidTypeIsdn = 11;
        private const int AndroidTypeMain = 12;
        private const int AndroidTypeOtherFax = 13;
        private const int AndroidTypeRadio = 14;
        private const int AndroidTypeTelex = 15;
        private const int AndroidTypeTtyTdd = 16;
        private const int AndroidTypeWorkMobile = 17;
        private const int AndroidTypeWorkPager = 18;
        private const int AndroidTypeAssistant = 19;
        private const int AndroidTypeMms = 20;

        #region Contact mapping

        /// <summary>
        /// Convert an API DTO to a domain <see cref="Contact"/>.
        /// </summary>
        public static Contact? ToContact(ContactDto? dto)
        {
            if (dto == null) return null;

            var contact = new Contact
            {
                AndroidId = dto.Id,
                FirstName = dto.FirstName,
                MiddleName = dto.MiddleName,
                LastName = dto.LastName,
                Prefix = dto.Prefix,
                Suffix = dto.Suffix,
                Organization = dto.Organization,
                Title = dto.Title,
                AccountName = dto.AccountName,
                AccountType = dto.AccountType,
                IsReadOnly = dto.ReadOnly,
                Source = ContactSource.AndroidPhone,
                Etag = dto.Etag
            };
            // Contact.UpdateFullName(), which auto-computes FullName from those parts.
            // By the time we reach this line, contact.FullName is already non-empty whenever
            // any name part was non-empty — so the old guard
            //   `if (string.IsNullOrWhiteSpace(contact.FullName) && …)`
            // would silently skip dto.FullName for almost every real contact.
            //
            // Instead, always prefer the explicit FullName from the DTO when it is provided,
            // because the server may supply a richer display name (nickname, custom ordering,
            // Kanji/Romaji mixed, etc.) that differs from the auto-reconstructed value.
            if (!string.IsNullOrWhiteSpace(dto.FullName))
            {
                contact.FullName = dto.FullName;
            }

            // SER-02 FIX: Map ALL email addresses into the Emails collection instead of
            // only preserving the first one. The legacy Email property shim keeps
            // existing code that reads contact.Email working via Emails[0].
            if (dto.Emails != null)
            {
                foreach (var e in dto.Emails)
                {
                    if (!string.IsNullOrWhiteSpace(e.Value))
                        contact.Emails.Add(e.Value!);
                }
            }

            // Map phones
            if (dto.Phones != null)
            {
                foreach (var phone in dto.Phones)
                {
                    var pn = ToPhoneNumber(phone);
                    if (pn != null) contact.PhoneNumbers.Add(pn);
                }
            }

            return contact;
        }

        /// <summary>
        /// Convert a domain <see cref="Contact"/> to an API DTO.
        /// </summary>
        public static ContactDto? ToDto(Contact? contact)
        {
            if (contact == null) return null;

            var dto = new ContactDto
            {
                Id = contact.AndroidId,
                FirstName = contact.FirstName ?? string.Empty,
                MiddleName = contact.MiddleName ?? string.Empty,
                LastName = contact.LastName ?? string.Empty,
                Prefix = contact.Prefix ?? string.Empty,
                Suffix = contact.Suffix ?? string.Empty,
                FullName = contact.FullName ?? string.Empty,
                Organization = contact.Organization ?? string.Empty,
                Title = contact.Title ?? string.Empty,
                AccountName = contact.AccountName ?? string.Empty,
                AccountType = contact.AccountType ?? string.Empty,
                ReadOnly = contact.IsReadOnly,
                Etag = contact.Etag
            };

            // SER-02 FIX: Map ALL emails from the Emails collection, falling back to the
            // legacy Email shim for contacts that were created before the multi-email fix.
            var emailSource = contact.Emails?.Count > 0
                ? contact.Emails.Where(e => !string.IsNullOrWhiteSpace(e)).ToList()
                : (!string.IsNullOrWhiteSpace(contact.Email)
                    ? new List<string> { contact.Email! }
                    : new List<string>());

            if (emailSource.Count > 0)
            {
                dto.Emails = emailSource.Select(addr => new EmailDto
                {
                    Value = addr,
                    Type  = "1" // TYPE_HOME — Android default for contacts with no explicit type
                }).ToList();
            }

            // Map phones
            dto.Phones = contact.PhoneNumbers
                .Select(ToPhoneDto)
                .Where(p => p != null)
                .Select(p => p!)
                .ToList();

            return dto;
        }

        /// <summary>
        /// Convert a list of DTOs to domain contacts.
        /// </summary>
        public static List<Contact> ToContacts(IEnumerable<ContactDto>? dtos)
        {
            return dtos?.Select(ToContact).Where(c => c != null).Select(c => c!).ToList()
                   ?? new List<Contact>();
        }

        #endregion

        #region Phone number mapping

        public static PhoneNumber? ToPhoneNumber(PhoneDto? dto)
        {
            if (dto == null) return null;
            var mappedType = int.TryParse(dto.Type, out var typeInt)
                ? MapAndroidPhoneType(typeInt)
                : PhoneNumberType.XOther;
            var phone = new PhoneNumber(dto.Value ?? string.Empty, mappedType);
            if (mappedType == PhoneNumberType.XCustom)
            {
                phone.CustomLabel = dto.Label;
                phone.AndroidRawType = 0; // Android TYPE_CUSTOM = 0
            }
            else if (int.TryParse(dto.Type, out var raw))
            {
                phone.AndroidRawType = raw;
            }
            return phone;
        }

        public static PhoneDto? ToPhoneDto(PhoneNumber? phoneNumber)
        {
            if (phoneNumber == null) return null;
            // a phone number that came from the device. The old code fell through to
            // MapToAndroidPhoneType() for any type not in the small known set, collapsing
            // e.g. TYPE_FAX_WORK (4), TYPE_PAGER (6), TYPE_ASSISTANT (19) etc. all to
            // TYPE_OTHER (7). Now we store AndroidRawType on the PhoneNumber when it is
            // received from the server and always send it back unchanged.
            // AndroidRawType is int?, Type (string) needs int→string conversion
            string typeStr = phoneNumber.AndroidRawType.HasValue
                ? phoneNumber.AndroidRawType.Value.ToString(CultureInfo.InvariantCulture)
                : MapToAndroidPhoneType(phoneNumber.Type).ToString(CultureInfo.InvariantCulture);

            return new PhoneDto
            {
                Value = phoneNumber.Number,
                Type = typeStr,
                Label = phoneNumber.AndroidRawType == 0 ? phoneNumber.CustomLabel : null
            };
        }

        /// <summary>
        /// Map an Android phone type integer to the desktop enum.
        /// </summary>
        public static PhoneNumberType MapAndroidPhoneType(int androidType)
        {
            return androidType switch
            {
                AndroidTypeCustom => PhoneNumberType.XCustom,

                AndroidTypeHome => PhoneNumberType.HOME,
                AndroidTypeMobile => PhoneNumberType.CELL,
                AndroidTypeWork => PhoneNumberType.WORK,

                AndroidTypeWorkMobile => PhoneNumberType.CELL,

                AndroidTypeOther => PhoneNumberType.XOther,
                AndroidTypeFaxWork => PhoneNumberType.XOther,
                AndroidTypeFaxHome => PhoneNumberType.XOther,
                AndroidTypePager => PhoneNumberType.XOther,
                AndroidTypeCallback => PhoneNumberType.XOther,
                AndroidTypeCar => PhoneNumberType.XOther,
                AndroidTypeCompanyMain => PhoneNumberType.WORK,
                AndroidTypeIsdn => PhoneNumberType.XOther,
                AndroidTypeMain => PhoneNumberType.WORK,
                AndroidTypeOtherFax => PhoneNumberType.XOther,
                AndroidTypeRadio => PhoneNumberType.XOther,
                AndroidTypeTelex => PhoneNumberType.XOther,
                AndroidTypeTtyTdd => PhoneNumberType.XOther,
                AndroidTypeWorkPager => PhoneNumberType.WORK,
                AndroidTypeAssistant => PhoneNumberType.WORK,
                AndroidTypeMms => PhoneNumberType.CELL,

                _ => PhoneNumberType.XOther
            };
        }

        /// <summary>
        /// Map a desktop phone type enum to the Android integer.
        /// </summary>
        public static int MapToAndroidPhoneType(PhoneNumberType type)
        {
            return type switch
            {
                PhoneNumberType.HOME or PhoneNumberType.XHome => AndroidTypeHome,
                PhoneNumberType.CELL or PhoneNumberType.XMobile => AndroidTypeMobile,
                PhoneNumberType.WORK or PhoneNumberType.XWork => AndroidTypeWork,
                _ => AndroidTypeOther
            };
        }

        #endregion
    }
}

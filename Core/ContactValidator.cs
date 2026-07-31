using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VcfEditor.Models;

namespace VcfEditor.Core
{
    public static partial class ContactValidator
    {
        [GeneratedRegex(@"[\s\-\(\)\+]")]
        private static partial Regex PhoneFormattingRegex();

        [GeneratedRegex(@"^\d+$")]
        private static partial Regex DigitsOnlyRegex();

        [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
        private static partial Regex EmailRegex();
        public static ValidationResult ValidateContact(Contact? contact)
        {
            var errors = new List<string>();

            if (contact == null)
            {
                errors.Add("Contact cannot be null.");
                return new ValidationResult { IsValid = false, Errors = errors };
            }

            // Validate name - at least one name component should be present
            if (string.IsNullOrWhiteSpace(contact.FirstName) &&
                string.IsNullOrWhiteSpace(contact.LastName) &&
                string.IsNullOrWhiteSpace(contact.FullName))
            {
                errors.Add("At least a first name, last name, or full name is required.");
            }

            // Validate phone numbers
            // have no phone numbers. Only require a phone for local VCF contacts.
            if (contact.PhoneNumbers.Count == 0 && contact.Source == VcfEditor.Models.ContactSource.LocalVcf)
            {
                errors.Add("At least one phone number is required.");
            }

            foreach (var phone in contact.PhoneNumbers)
            {
                // indicator instead of an empty string when Number is null.
                var displayNumber = phone.Number ?? "(empty)";
                var phoneValidation = ValidatePhoneNumber(phone.Number ?? string.Empty);
                if (!phoneValidation.IsValid)
                {
                    errors.Add($"Invalid phone number '{displayNumber}': {phoneValidation.ErrorMessage}");
                }
            }

            // Validate email if provided
            if (!string.IsNullOrWhiteSpace(contact.Email))
            {
                var emailValidation = ValidateEmail(contact.Email);
                if (!emailValidation.IsValid)
                {
                    errors.Add($"Invalid email '{contact.Email}': {emailValidation.ErrorMessage}");
                }
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }

        public static PhoneValidationResult ValidatePhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                return new PhoneValidationResult { IsValid = false, ErrorMessage = "Phone number cannot be empty." };
            }

            var isInternational = phoneNumber.TrimStart().StartsWith('+');
            var cleanNumber = PhoneFormattingRegex().Replace(phoneNumber, "");
            if (!DigitsOnlyRegex().IsMatch(cleanNumber))
            {
                return new PhoneValidationResult { IsValid = false, ErrorMessage = "Phone number can only contain digits, spaces, hyphens, parentheses, and plus signs." };
            }
            if (cleanNumber.Length < 7)
            {
                return new PhoneValidationResult { IsValid = false, ErrorMessage = "Phone number must have at least 7 digits." };
            }
            if (cleanNumber.Length > 15)
            {
                return new PhoneValidationResult { IsValid = false, ErrorMessage = "Phone number is too long (maximum 15 digits)." };
            }

            // E.164 numbers cannot have a leading 0 after '+' (country codes don't start with 0).
            if (isInternational && cleanNumber.Length > 0 && cleanNumber[0] == '0')
            {
                return new PhoneValidationResult { IsValid = false, ErrorMessage = "International phone numbers cannot start with 0." };
            }

            return new PhoneValidationResult { IsValid = true };
        }


        public static EmailValidationResult ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return new EmailValidationResult { IsValid = true }; // Email is optional
            }

            if (!EmailRegex().IsMatch(email))
            {
                return new EmailValidationResult { IsValid = false, ErrorMessage = "Invalid email format." };
            }

            return new EmailValidationResult { IsValid = true };
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public string ErrorMessage => string.Join(Environment.NewLine, Errors);
    }

    public class PhoneValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class EmailValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
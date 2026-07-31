using FluentAssertions;
using VcfEditor.Core;
using VcfEditor.Models;

namespace VcfEditor.Core.Tests;

public sealed class ContactValidatorTests
{
    [Fact]
    public void ValidateContactAcceptsNamedLocalContactWithValidPhone()
    {
        var contact = new Contact { FullName = "John Doe" };
        contact.PhoneNumbers.Add(new PhoneNumber("+1 555 010 0237", PhoneNumberType.CELL));

        var result = ContactValidator.ValidateContact(contact);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateContactRejectsInvalidEmail()
    {
        var contact = new Contact { FullName = "John Doe", Email = "invalid-email" };
        contact.PhoneNumbers.Add(new PhoneNumber("+15550100237", PhoneNumberType.CELL));

        var result = ContactValidator.ValidateContact(contact);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Contains("Invalid email", StringComparison.Ordinal));
    }
}

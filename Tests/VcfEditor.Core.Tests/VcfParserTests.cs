using FluentAssertions;
using VcfEditor.Core;

namespace VcfEditor.Core.Tests;

public sealed class VcfParserTests
{
    [Fact]
    public void ParseVcfParsesBasicContact()
    {
        const string vcf = """
            BEGIN:VCARD
            VERSION:3.0
            N:Doe;John;;;
            FN:John Doe
            TEL;TYPE=CELL:+15550100237
            EMAIL:john.doe@example.com
            END:VCARD
            """;

        var contacts = new VcfParser().ParseVcf(new StringReader(vcf)).ToList();

        contacts.Should().ContainSingle();
        contacts[0].FullName.Should().Be("John Doe");
        contacts[0].PrimaryPhoneNumber.Should().Be("+15550100237");
        contacts[0].Email.Should().Be("john.doe@example.com");
    }

    [Fact]
    public void ParseVcfEmitsNonEmptyTruncatedContact()
    {
        const string vcf = """
            BEGIN:VCARD
            VERSION:3.0
            FN:Truncated Contact
            TEL:+15550100477
            """;

        var contacts = new VcfParser().ParseVcf(new StringReader(vcf)).ToList();

        contacts.Should().ContainSingle();
        contacts[0].FullName.Should().Be("Truncated Contact");
    }
}

public sealed class VcfParserLimitTests
{
    [Fact]
    public void ParseVcfRejectsLineBeyondConfiguredCharacterLimit()
    {
        var oversizedName = new string('x', VcfParsingLimits.MaxLineCharacters + 1);
        var input = $"BEGIN:VCARD\nVERSION:3.0\nFN:{oversizedName}\nEND:VCARD\n";

        var act = () => new VcfParser().ParseVcf(new StringReader(input)).ToList();

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*line*limit*");
    }


    [Fact]
    public async Task WriteVcfAsyncMatchesBufferedExportAndSupportsCancellation()
    {
        var contact = new VcfEditor.Models.Contact
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com"
        };
        contact.PhoneNumbers.Add(new VcfEditor.Models.PhoneNumber(
            "+15550100237",
            VcfEditor.Models.PhoneNumberType.CELL));
        var parser = new VcfParser();
        using var writer = new StringWriter();

        await parser.WriteVcfAsync(writer, [contact], CancellationToken.None);

        writer.ToString().Should().Be(parser.ExportToVcf([contact]));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledWriter = new StringWriter();
        var act = () => parser.WriteVcfAsync(cancelledWriter, [contact], cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ParseVcfAsyncObservesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var parser = new VcfParser();

        var act = async () =>
        {
            await foreach (var _ in parser.ParseVcfAsync(
                               new StringReader("BEGIN:VCARD\nEND:VCARD\n"),
                               cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

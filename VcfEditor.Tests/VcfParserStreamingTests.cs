using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using VcfEditor.Core;

namespace VcfEditor.Tests;

public sealed class VcfParserStreamingTests
{
    [Test]
    public async Task ParseVcfAsync_unfolds_folded_lines_before_processing_property()
    {
        var vcf = string.Join("\n", new[]
        {
            "BEGIN:VCARD",
            "FN:John ",
            " Doe",
            "END:VCARD",
            ""
        });

        var parser = new VcfParser();
        using var reader = new StringReader(vcf);

        var contacts = new List<Models.Contact>();
        await foreach (var c in parser.ParseVcfAsync(reader))
            contacts.Add(c);

        Assert.That(contacts, Has.Count.EqualTo(1));
        Assert.That(contacts[0].FullName, Is.EqualTo("John Doe"));
    }

    [Test]
    public async Task ParseVcfAsync_emits_last_contact_when_file_is_truncated_missing_end_vcard()
    {
        var vcf = string.Join("\n", new[]
        {
            "BEGIN:VCARD",
            "FN:Alice Example",
            "TEL;CELL:123",
            "" // No END:VCARD
        });

        var parser = new VcfParser();
        using var reader = new StringReader(vcf);

        var contacts = new List<Models.Contact>();
        await foreach (var c in parser.ParseVcfAsync(reader))
            contacts.Add(c);

        Assert.That(contacts, Has.Count.EqualTo(1));
        Assert.That(contacts[0].FullName, Is.EqualTo("Alice Example"));
        Assert.That(contacts[0].PhoneNumbers, Has.Count.EqualTo(1));
        Assert.That(contacts[0].PhoneNumbers[0].Number, Is.EqualTo("123"));
    }

    [Test]
    public void ParseVcf_sync_emits_last_contact_when_file_is_truncated_missing_end_vcard()
    {
        var vcf = string.Join("\n", new[]
        {
            "BEGIN:VCARD",
            "FN:Bob Example",
            "" // No END:VCARD
        });

        var parser = new VcfParser();
        using var reader = new StringReader(vcf);

        var contacts = new List<Models.Contact>(parser.ParseVcf(reader));

        Assert.That(contacts, Has.Count.EqualTo(1));
        Assert.That(contacts[0].FullName, Is.EqualTo("Bob Example"));
    }
}

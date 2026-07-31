using VcfEditor.Core;
using VcfEditor.Features.Contacts;
using VcfEditor.Models;

namespace VcfEditor.Core.Tests;

public sealed class ContactFileWorkflowTests
{
    [Fact]
    public async Task SaveThenLoadPreservesContactData()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "contacts.vcf");
            var contact = new Contact
            {
                FirstName = "John",
                LastName = "Doe",
                Organization = "AndroidDeck",
                Title = "Developer",
                Email = "john.doe@example.com"
            };
            contact.PhoneNumbers.Add(new PhoneNumber("+15550100237", PhoneNumberType.CELL));

            var workflow = new ContactFileWorkflow(new VcfParser());

            await workflow.SaveAsync(path, [contact]);
            var loaded = await workflow.LoadAsync(path);

            var saved = Assert.Single(loaded);
            Assert.Equal("John Doe", saved.FullName);
            Assert.Equal("AndroidDeck", saved.Organization);
            Assert.Equal("Developer", saved.Title);
            Assert.Equal("john.doe@example.com", saved.Email);
            Assert.Equal("+15550100237", saved.PrimaryPhoneNumber);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledSaveDoesNotReplaceExistingFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "contacts.vcf");
            const string original = "existing-content";
            await File.WriteAllTextAsync(path, original);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var workflow = new ContactFileWorkflow(new VcfParser());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                workflow.SaveAsync(path, [], cancellation.Token));

            Assert.Equal(original, await File.ReadAllTextAsync(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AndroidDeckTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

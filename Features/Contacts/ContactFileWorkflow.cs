using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VcfEditor.Core;
using VcfEditor.Models;

namespace VcfEditor.Features.Contacts;

public interface IContactFileWorkflow
{
    Task<IReadOnlyList<Contact>> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string filePath,
        IEnumerable<Contact> contacts,
        CancellationToken cancellationToken = default);
}

public sealed class ContactFileWorkflow : IContactFileWorkflow
{
    private readonly VcfParser _parser;

    public ContactFileWorkflow(VcfParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
    }

    public async Task<IReadOnlyList<Contact>> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();
        var contacts = await _parser.ParseVcfFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return contacts;
    }

    public async Task SaveAsync(
        string filePath,
        IEnumerable<Contact> contacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(contacts);

        cancellationToken.ThrowIfCancellationRequested();
        var tempPath = filePath + ".tmp";
        var committed = false;
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            await using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 64 * 1024,
                leaveOpen: false)
            {
                NewLine = "\r\n"
            })
            {
                await _parser.WriteVcfAsync(writer, contacts, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, filePath, overwrite: true);
            committed = true;
        }
        finally
        {
            if (!committed)
                TryDeleteTemporaryFile(tempPath);
        }
    }

    private static void TryDeleteTemporaryFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (IOException)
        {
            // Preserve the original save failure; a stale .tmp file is safe to overwrite later.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the original save failure; a stale .tmp file is safe to overwrite later.
        }
    }
}

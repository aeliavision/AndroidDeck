using System;
using VcfEditor.Core;
using VcfEditor.Navigation;

namespace VcfEditor.Features.PhoneSession;

public sealed class PhoneSessionContext
{
    private PhoneApiClient? _client;

    public PhoneApiClient Client => _client
        ?? throw new InvalidOperationException("The phone session context has not been initialized.");

    public ShellCapabilitySnapshot Capabilities { get; private set; }
        = ShellCapabilitySnapshot.Disconnected;

    internal void Initialize(PhoneApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (_client is not null)
            throw new InvalidOperationException("The phone session context is already initialized.");

        _client = client;
        Capabilities = new ShellCapabilitySnapshot(
            client.IsConnected,
            false,
            false,
            false,
            false,
            false,
            null);
    }

    public void UpdateCapabilities(ShellCapabilitySnapshot capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        Capabilities = capabilities;
    }
}

using System;
using System.Globalization;

namespace VcfEditor.Models;

public sealed record PairedDeviceRecord(
    string Endpoint,
    DateTimeOffset? LastUsedUtc,
    DateTimeOffset? ExpiresAtUtc)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Endpoint) ? "Paired phone" : Endpoint;
    public string LastUsedDisplay => LastUsedUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "Not recorded";
    public string ExpiresDisplay => ExpiresAtUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "Managed by phone session";
}

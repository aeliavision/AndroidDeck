using System;
using System.Linq;
using System.Text.Json;
using VcfEditor.Models.DTOs;

namespace VcfEditor.Core
{
    /// <summary>
    /// Immutable, UI-independent capability state derived from the phone status payload.
    /// </summary>
    public sealed record CapabilityState(
        bool SupportsFiles,
        bool SupportsGallery,
        bool SupportsBackup,
        bool RequiresAllFilesAccess,
        bool RequiresMediaPermissions)
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static CapabilityState FromStatusJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("A phone status payload is required.", nameof(json));

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("The phone status payload must be a JSON object.");

            var hasCapabilityFields = document.RootElement
                .EnumerateObject()
                .Any(property => property.Name.Equals("supportsFiles", StringComparison.OrdinalIgnoreCase));

            if (!hasCapabilityFields)
            {
                return new CapabilityState(
                    SupportsFiles: true,
                    SupportsGallery: true,
                    SupportsBackup: false,
                    RequiresAllFilesAccess: false,
                    RequiresMediaPermissions: false);
            }

            var status = JsonSerializer.Deserialize<DeviceStatusDto>(json, SerializerOptions)
                ?? throw new JsonException("The phone status payload could not be deserialized.");

            return new CapabilityState(
                status.SupportsFiles,
                status.SupportsGallery,
                status.SupportsBackup,
                status.RequiresAllFilesAccess,
                status.RequiresMediaPermissions);
        }
    }
}

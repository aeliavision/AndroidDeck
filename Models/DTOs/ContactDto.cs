using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace VcfEditor.Models.DTOs
{
    /// <summary>
    /// Contact data transfer object for the phone API.
    /// </summary>
    public class ContactDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("firstName")]
        public string? FirstName { get; set; }

        [JsonPropertyName("middleName")]
        public string? MiddleName { get; set; }

        [JsonPropertyName("lastName")]
        public string? LastName { get; set; }

        [JsonPropertyName("prefix")]
        public string? Prefix { get; set; }

        [JsonPropertyName("suffix")]
        public string? Suffix { get; set; }

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("organization")]
        public string? Organization { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("emails")]
        public List<EmailDto> Emails { get; set; } = new();

        [JsonPropertyName("phones")]
        public List<PhoneDto> Phones { get; set; } = new();

        [JsonPropertyName("accountName")]
        public string AccountName { get; set; } = string.Empty;

        [JsonPropertyName("accountType")]
        public string AccountType { get; set; } = string.Empty;

        [JsonPropertyName("readOnly")]
        public bool ReadOnly { get; set; }


        [JsonPropertyName("etag")]
        public string? Etag { get; set; }
    }

    public class PhoneDto
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }
        // and "custom:<label>" encoded custom labels in a lossless way.
        // FlexibleStringConverter handles both JSON number (2) and string ("2") gracefully
        // so the C# side works regardless of whether the Android server sends an int or string.
        [JsonPropertyName("type")]
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Type { get; set; }
        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }

    public class EmailDto
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("type")]
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Type { get; set; }
    }

    /// <summary>
    /// Deserializes both JSON number (2) and JSON string ("2") into a C# string.
    /// This makes <see cref="PhoneDto.Type"/> and <see cref="EmailDto.Type"/> resilient
    /// to Android servers that serialize the type as an integer rather than a string.
    /// </summary>
    public class FlexibleStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetInt64().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Null   => null,
                _                    => reader.GetString()
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }
    }

    public class DeviceStatusDto
    {
        public string? DeviceName { get; set; }
        public int AndroidVersion { get; set; }
        public List<string> Accounts { get; set; } = new();
        public bool WriteSupported { get; set; }
        public bool SupportsFiles { get; set; }
        public bool SupportsGallery { get; set; }
        public bool SupportsBackup { get; set; }
        public bool RequiresAllFilesAccess { get; set; }
        public bool RequiresMediaPermissions { get; set; }

        // Optional counts (companion app v3.0+). Null when not provided by the phone.
        public int? MediaCount { get; set; }
        public int? GroupCount { get; set; }
    }

    public class PairRequestDto
    {
        public string? PairingCode { get; set; }
        public string? ClientId { get; set; }
    }

    public class PairResponseDto
    {
        public string? SessionId { get; set; }
        public string? HmacSecret { get; set; }
        public long ExpiresAt { get; set; }
        public string? ServerPublicKey { get; set; }
        // Shown to the user for out-of-band verification against the phone screen.
        public string? CertFingerprint { get; set; }
    }

    /// <summary>
    /// Extends the v1 request with an ephemeral ECDH public key.
    /// </summary>
    public class PairRequestV2Dto
    {
        public string? PairingCode { get; set; }
        public string? ClientId { get; set; }
        /// <summary>Desktop's ephemeral ECDH public key (Base64 DER, P-256).</summary>
        public string? ClientPublicKey { get; set; }
    }


    public sealed class PairRequestV3Dto
    {
        public string PairingCode { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientPublicKey { get; set; } = string.Empty;
    }

    public sealed class PairResponseV3Dto
    {
        public string SessionId { get; set; } = string.Empty;
        public long ExpiresAt { get; set; }
        public string ServerPublicKey { get; set; } = string.Empty;
        public string CertFingerprint { get; set; } = string.Empty;
    }

    public class FileEntryDto
    {
        public string? Name { get; set; }
        public string? Path { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public long LastModified { get; set; }
        public string? MimeType { get; set; }

        /// <summary>Human-readable file size (e.g. "1.2 MB").</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string SizeDisplay => IsDirectory ? string.Empty : FormatSize(Size);

        /// <summary>Last modified as a local DateTime.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime LastModifiedLocal =>
            DateTimeOffset.FromUnixTimeMilliseconds(LastModified).LocalDateTime;

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }

    public class DirectoryListingDto
    {
        public string? Path { get; set; }
        public string? Parent { get; set; }
        public List<FileEntryDto> Items { get; set; } = new();
    }

    public class UploadResultDto
    {
        public string? Path { get; set; }
        public long Size { get; set; }
        public string? Checksum { get; set; }
    }

    public class DeleteResultDto
    {
        public bool Success { get; set; }
        public string? Path { get; set; }
    }

    public class MkdirResultDto
    {
        public string? Path { get; set; }
        public bool Created { get; set; }
    }

    public class StreamInitResponseDto
    {
        public string? TransferId { get; set; }
        public int ChunkSize { get; set; }
    }

    public class StreamStatusResponseDto
    {
        public string? TransferId { get; set; }
        public List<int>? ChunksReceived { get; set; }
        public int TotalChunks { get; set; }
        public long BytesReceived { get; set; }
    }

    public class StreamCompleteResponseDto
    {
        public bool Success { get; set; }
        public string? FinalPath { get; set; }
        public bool VerifiedChecksum { get; set; }
    }

    public class AlbumDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? CoverMediaId { get; set; }
        public string? CoverMediaType { get; set; }
        public int Count { get; set; }
    }

    public class AlbumsResponseDto
    {
        public List<AlbumDto> Albums { get; set; } = new();
    }

    public class GalleryMediaDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? MimeType { get; set; }
        public long Size { get; set; }
        public long DateTaken { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? MediaType { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime DateTakenLocal =>
            DateTimeOffset.FromUnixTimeMilliseconds(DateTaken).LocalDateTime;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsVideo => string.Equals(MediaType, "video", StringComparison.OrdinalIgnoreCase);

        [System.Text.Json.Serialization.JsonIgnore]
        public string SizeDisplay => Size switch
        {
            < 1024 => $"{Size} B",
            < 1024 * 1024 => $"{Size / 1024.0:F1} KB",
            _ => $"{Size / (1024.0 * 1024):F1} MB"
        };
    }

    public class MediaPageDto
    {
        public List<GalleryMediaDto> Items { get; set; } = new();
        public int? NextPage { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class GalleryActionResultDto
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? UpdatedName { get; set; }
        public string? UpdatedPath { get; set; }
    }

    public class GalleryDeleteRequestDto
    {
        public List<string> Ids { get; set; } = new();
        public string MediaType { get; set; } = "image";
    }

    public class GalleryRenameRequestDto
    {
        public string Id { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
        public string MediaType { get; set; } = "image";
    }

    public class GalleryMoveRequestDto
    {
        public List<string> Ids { get; set; } = new();
        public string TargetRelativePath { get; set; } = string.Empty;
        public string MediaType { get; set; } = "image";
    }

    public class GalleryMetadataRequestDto
    {
        public string Id { get; set; } = string.Empty;
        public string MediaType { get; set; } = "image";
        public bool? Favorite { get; set; }
        public string? Description { get; set; }
    }

    public class ContactsPageDto
    {
        public List<ContactDto> Items { get; set; } = new();
        public int? NextPage { get; set; }
    }

    public class ApiErrorDto
    {
        public string? Error { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// A contact group on the Android device.
    /// </summary>
    public class GroupDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("accountName")]
        public string? AccountName { get; set; }

        [JsonPropertyName("accountType")]
        public string? AccountType { get; set; }

        [JsonPropertyName("memberCount")]
        public int MemberCount { get; set; }
    }

    /// <summary>
    /// Page wrapper for a list of groups.
    /// </summary>
    public class GroupsPageDto
    {
        [JsonPropertyName("items")]
        public List<GroupDto> Items { get; set; } = new();
    }
}

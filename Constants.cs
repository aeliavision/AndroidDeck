using System.Reflection;

namespace VcfEditor
{
    public static class Constants
    {
        // File extensions and filters
        public const string VcfFileExtension = ".vcf";
        public const string VcfFileFilter = "VCF files (*.vcf)|*.vcf|All files (*.*)|*.*";
        public const string VcfFileDialogTitle = "VCF Contact File";

        // VCF format constants
        public const string VcfVersion = "VERSION:3.0";
        public const string VcfBeginCard = "BEGIN:VCARD";
        public const string VcfEndCard = "END:VCARD";

        // Validation constants
        public const int MinPhoneNumberLength = 7;
        public const int MaxPhoneNumberLength = 15;
        public const int MinContactNameLength = 2;
        public const int MaxContactNameLength = 100;

        // UI constants
        public const int DefaultDialogWidth = 480;
        public const int DefaultDialogHeight = 320;
        public const int DefaultListViewHeight = 140;
        public const int DefaultButtonMinWidth = 80;
        public const int DefaultButtonHeight = 36;

        // Message box titles
        public const string ErrorTitle = "Error";
        public const string WarningTitle = "Warning";
        public const string ConfirmationTitle = "Confirm";
        public const string ValidationErrorTitle = "Validation Error";
        public const string SuccessTitle = "Success";

        // Default messages
        public const string NoSelectionMessage = "Please select an item first.";
        public const string ConfirmDeleteMessage = "Are you sure you want to delete this item?";
        public const string FileLoadErrorMessage = "Error loading file. Please check the file format and try again.";
        public const string FileSaveErrorMessage = "Error saving file. Please check file permissions and try again.";
        public const string GenericErrorMessage = "An unexpected error occurred. Please try again.";

        // Search and filter constants
        public const string SearchAllFields = "All Fields";
        public const string SearchName = "Name";
        public const string SearchPhone = "Phone";
        public const string SearchOrganization = "Organization";

        // Phone number types
        public const string PhoneTypeCell = "CELL";
        public const string PhoneTypeHome = "HOME";
        public const string PhoneTypeWork = "WORK";
        public const string PhoneTypeMobile = "X-MOBILE";
        public const string PhoneTypeOther = "X-OTHER";

        // Application settings
        public const string AppName = "AndroidDeck";
        public static string AppVersion
        {
            get
            {
                var informationalVersion = typeof(Constants).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;
                if (string.IsNullOrWhiteSpace(informationalVersion))
                    return typeof(Constants).Assembly.GetName().Version?.ToString(3) ?? "Unknown";

                var metadataIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
                return metadataIndex >= 0 ? informationalVersion[..metadataIndex] : informationalVersion;
            }
        }
        public const string SettingsFileName = "settings.json";
        public const string RecentFilesKey = "RecentFiles";
        public const int MaxRecentFiles = 10;

        // Android connection constants
        public const int DefaultPhoneServerPort = 8732;
        public const string DefaultApiBasePath = "/api/v1";
        public const int PairingCodeLength = 6;
        public const int ConnectionTimeoutSeconds = 10;
        public const int RequestTimeoutSeconds = 300;
        public const int HeartbeatIntervalSeconds = 30;
        public const int MaxTimestampDriftMinutes = 5;

        // Android connection UI messages
        public const string PhoneConnectedMessage = "Connected to {0}";
        public const string PhoneDisconnectedMessage = "Disconnected from phone";
        public const string PairingFailedMessage = "Pairing failed. Check the code and try again.";
        public const string PhoneUnreachableMessage = "Phone is not reachable. Check your network or USB connection.";
        public const string WritePermissionDeniedMessage = "Write permission denied by the phone.";
        public const string ReadOnlyContactMessage = "This contact belongs to a read-only account.";
        public const string SessionExpiredMessage = "Session expired. Please reconnect.";
    }
}
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using VcfEditor.Core;
using VcfEditor.Core.Settings;
using VcfEditor.Helpers;
using VcfEditor.Services;

namespace VcfEditor.Views
{
    /// <summary>
    /// Dialog for establishing a connection to the Android companion app.
    /// On success, exposes the connected PhoneContactsClient instance.
    /// </summary>
    public partial class ConnectPhoneDialog : AppDialogWindow, IDisposable
    {
        private static readonly ILogger Logger = AppLoggerFactory.CreateLogger(nameof(ConnectPhoneDialog));

        private PhoneContactsClient? _phoneClient;
        private readonly AdbHelper _adbHelper;
        private readonly IAppSettingsStore _settingsStore;
        private bool _isConnecting;
        private string? _pendingCertFingerprint;
#pragma warning disable CS0414 // reserved for future: block window close if cert not verified
        private bool _certVerified;
#pragma warning restore CS0414
        private const string CompanionPackageName = "com.vcfeditor.companion";
        // Without this, closing the dialog mid-pairing left PairAsync running on a
        // background thread, still holding references to UI elements and potentially
        // completing after the window is gone (causing InvalidOperationException on Dispatcher).
        private System.Threading.CancellationTokenSource? _pairingCts;
        private bool _disposed;

        /// <summary>The connected PhoneContactsClient shim. Only valid when DialogResult == true.</summary>
        public PhoneContactsClient? PhoneClient => _phoneClient;

        /// <summary>
        /// P3/P4: The underlying PhoneApiClient exposed for FileBrowser and Gallery views.
        /// Accesses the inner modular client via the shim's internal field.
        /// </summary>
        public PhoneApiClient? PhoneApiClient
        {
            get
            {
                if (_phoneClient is PhoneContactsClient pcc)
                    return pcc.InnerApiClient;
                return null;
            }
        }

        public ConnectPhoneDialog(
            IAppSettingsStore settingsStore,
            IDialogService dialogService)
        {
            ArgumentNullException.ThrowIfNull(settingsStore);
            ArgumentNullException.ThrowIfNull(dialogService);
            _settingsStore = settingsStore;
            InitializeComponent();
            _adbHelper = new AdbHelper();
            PairingCodeTextBox.Focus();

            IpAddressTextBox.TextChanged += (_, _) => ClearValidation(IpAddressTextBox, IpValidationText);
            PortTextBox.TextChanged += (_, _) => ClearValidation(PortTextBox, PortValidationText);
            PairingCodeTextBox.TextChanged += (_, _) => ClearValidation(PairingCodeTextBox, PairingValidationText);
            Closing += (_, _) => _pairingCts?.Cancel();
            Closed += (_, _) => Dispose();

            // Initial device check if ADB is available
            if (_adbHelper.IsAdbAvailable)
            {
                _ = RefreshDevices();
            }
        }

        private void ConnectionMode_Changed(object sender, RoutedEventArgs e)
        {
            if (UsbRadio == null || WifiIpPanel == null || UsbDevicePanel == null) return;

            if (UsbRadio.IsChecked == true)
            {
                WifiIpPanel.Visibility = Visibility.Collapsed;
                UsbDevicePanel.Visibility = Visibility.Visible;
                IpAddressTextBox.Text = "127.0.0.1";
                IpAddressTextBox.IsEnabled = false;
                
                if (_adbHelper.IsAdbAvailable)
                {
                    _ = RefreshDevices();
                }
                else
                {
                    ShowError("ADB not found. USB mode requires Android SDK platform-tools.");
                }
            }
            else
            {
                WifiIpPanel.Visibility = Visibility.Visible;
                UsbDevicePanel.Visibility = Visibility.Collapsed;
                IpAddressTextBox.Text = "192.168.";
                IpAddressTextBox.IsEnabled = true;
                AppInstallPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await CheckAppStatusAsync();
        }

        private async Task CheckAppStatusAsync()
        {
            string? selectedDevice = DeviceComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedDevice))
            {
                UpdateAppStatus("Select a device", "");
                InstallAppButton.Visibility = Visibility.Collapsed;
                return;
            }

            UpdateAppStatus("Checking for companion app...", "", true);
            bool isInstalled = await _adbHelper.IsAppInstalledAsync(selectedDevice, CompanionPackageName);

            if (isInstalled)
            {
                UpdateAppStatus("Companion app is installed", "OK");
                InstallAppButton.Visibility = Visibility.Collapsed;
                ApkPathPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                UpdateAppStatus("Companion app not found on device", "");
                InstallAppButton.Visibility = Visibility.Visible;
                ApkPathPanel.Visibility = Visibility.Visible;
                
                // Try to find default APK path
                string defaultApk = GetDefaultApkPath();
                if (File.Exists(defaultApk))
                {
                    ApkPathTextBox.Text = defaultApk;
                }
            }
        }

        private void UpdateAppStatus(string message, string icon, bool isBusy = false)
        {
            AppStatusText.Text = message;
            AppStatusIcon.Text = icon;
            AppStatusProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string GetDefaultApkPath()
        {
            // Try to find the APK in the project's build output folder
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                // Navigate up from bin/Debug/net8.0-windows to project root
                string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
                return Path.Combine(projectRoot, "AndroidCompanion", "app", "build", "outputs", "apk", "debug", "app-debug.apk");
            }
            catch { return ""; }
        }

        private void BrowseApk_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Android Package (*.apk)|*.apk",
                Title = "Select AndroidDeck Companion APK"
            };

            if (dialog.ShowDialog() == true)
            {
                ApkPathTextBox.Text = dialog.FileName;
            }
        }

        private async void InstallApp_Click(object sender, RoutedEventArgs e)
        {
            string? device = DeviceComboBox.SelectedItem as string;
            string apkPath = ApkPathTextBox.Text;

            if (string.IsNullOrEmpty(device) || string.IsNullOrEmpty(apkPath) || !File.Exists(apkPath))
            {
                ShowError("Please select a device and a valid APK file.");
                return;
            }

            try
            {
                UpdateAppStatus("Installing companion app...", "", true);
                InstallAppButton.IsEnabled = false;

                bool success = await _adbHelper.InstallAppAsync(device, apkPath);
                if (success)
                {
                    UpdateAppStatus("Installation successful!", "OK");
                    InstallAppButton.Visibility = Visibility.Collapsed;
                    ApkPathPanel.Visibility = Visibility.Collapsed;
                    ShowStatus("App installed. Please open it on your phone and start the server.", false);
                }
                else
                {
                    UpdateAppStatus("Installation failed. Check USB connection.", "X");
                    ShowError("Failed to install APK. Ensure 'Install via USB' is enabled in Developer Options.");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Install error: {ex.Message}");
            }
            finally
            {
                InstallAppButton.IsEnabled = true;
            }
        }

        private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDevices();
        }

        private async System.Threading.Tasks.Task RefreshDevices()
        {
            if (!_adbHelper.IsAdbAvailable)
            {
                ShowError("ADB not found.");
                return;
            }

            try
            {
                var devices = await _adbHelper.ListDevicesAsync();
                DeviceComboBox.ItemsSource = devices;
                if (devices.Count > 0)
                {
                    DeviceComboBox.SelectedIndex = 0;
                    ShowStatus($"Found {devices.Count} device(s)", isError: false);
                }
                else
                {
                    ShowStatus("No devices found via USB. Check connection & debugging.", isError: false);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to list devices: {ex.Message}");
            }
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnecting) return;

            // Validate inputs
            var ip = IpAddressTextBox.Text.Trim();
            var portText = PortTextBox.Text.Trim();
            var pairingCode = PairingCodeTextBox.Text.Trim();

            ClearAllValidation();

            if (UsbRadio.IsChecked != true && string.IsNullOrEmpty(ip))
            {
                SetValidationError(IpAddressTextBox, IpValidationText, "Please enter the IP address shown on your phone.");
                IpAddressTextBox.Focus();
                return;
            }

            if (UsbRadio.IsChecked != true && !IsValidIpOrHost(ip))
            {
                SetValidationError(IpAddressTextBox, IpValidationText, "Enter a valid IP address or host name.");
                IpAddressTextBox.Focus();
                return;
            }

            if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
            {
                SetValidationError(PortTextBox, PortValidationText, "Enter a valid port number (1–65535).");
                PortTextBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(pairingCode) || pairingCode.Length != 6 || !Regex.IsMatch(pairingCode, "^[A-Za-z0-9]{6}$"))
            {
                SetValidationError(PairingCodeTextBox, PairingValidationText, "Enter the 6-character pairing code from your phone.");
                PairingCodeTextBox.Focus();
                return;
            }

            // Attempt connection
            _isConnecting = true;
            ConnectButton.IsEnabled = false;
            
            if (UsbRadio.IsChecked == true)
            {
                if (!_adbHelper.IsAdbAvailable)
                {
                    ShowError("ADB is not available for USB connection.");
                    _isConnecting = false;
                    ConnectButton.IsEnabled = true;
                    return;
                }

                ShowStatus("Setting up ADB forwarding...", isError: false);
                if (!await _adbHelper.ForwardPortAsync(port))
                {
                    var details = _adbHelper.LastError;
                    ShowError(string.IsNullOrWhiteSpace(details)
                        ? "Failed to setup ADB port forwarding."
                        : $"Failed to setup ADB port forwarding: {details}");
                    _isConnecting = false;
                    ConnectButton.IsEnabled = true;
                    return;
                }
            }

            ShowStatus("Connecting...", isError: false);
            _pairingCts?.Cancel();
            _pairingCts?.Dispose();
            _pairingCts = new System.Threading.CancellationTokenSource();

            bool usbMode = UsbRadio.IsChecked == true;
            bool pairingSucceeded = false;
            try
            {
                _phoneClient = new PhoneContactsClient(useHttps: false, _settingsStore);
                // If the window is closed mid-pairing, _pairingCts is cancelled in the
                // Closing handler, causing OperationCanceledException caught below.
                var success = await _phoneClient.PairAsync(ip, port, pairingCode, _pairingCts.Token);

                if (success)
                {
                    pairingSucceeded = true;
                    // verification before closing the dialog.
                    var fingerprint = _phoneClient.LastCertFingerprint;
                    if (!string.IsNullOrEmpty(fingerprint))
                    {
                        _pendingCertFingerprint = fingerprint;
                        ShowCertFingerprint(fingerprint);
                        // Don't close yet — wait for user to click "Fingerprint Matches".
                        ConnectButton.IsEnabled = false;
                    }
                    else
                    {
                        ShowStatus("Connected successfully!", isError: false);
                        await System.Threading.Tasks.Task.Delay(500);
                        DialogResult = true;
                        Close();
                    }
                }
                else
                {
                    ShowError(_phoneClient.ErrorMessage ?? "Pairing failed. Check the code and try again.");
                    _phoneClient?.Dispose();
                    _phoneClient = null;
                }
            }
            catch (PhoneConnectionException ex)
            {
                ShowError($"Connection error: {ex.Message}");
                _phoneClient?.Dispose();
                _phoneClient = null;
            }
            catch (Exception ex)
            {
                ShowError($"Unexpected error: {ex.Message}");
                _phoneClient?.Dispose();
                _phoneClient = null;
            }
            finally
            {
                // rule that was set up earlier. Without this the forwarding slot leaks
                // indefinitely and can interfere with other USB connections on the same port.
                if (usbMode && !pairingSucceeded && _adbHelper.IsAdbAvailable)
                {
                    try { await _adbHelper.RemoveForwardAsync(port); } catch { /* best-effort */ }
                }

                _isConnecting = false;
                ConnectButton.IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _phoneClient?.Dispose();
            _phoneClient = null;
            DialogResult = false;
            Close();
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusBorder.Visibility = Visibility.Visible;

            if (isError)
            {
                StatusBorder.SetResourceReference(Border.BackgroundProperty, "Brush.ErrorContainer");
                StatusBorder.SetResourceReference(Border.BorderBrushProperty, "Brush.Error");
                StatusIcon.Text = "X";
                StatusMessageText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
            }
            else
            {
                StatusBorder.SetResourceReference(Border.BackgroundProperty, "Brush.PrimaryContainer");
                StatusBorder.SetResourceReference(Border.BorderBrushProperty, "Brush.Primary");
                StatusIcon.Text = "...";
                StatusMessageText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Primary");
            }

            StatusMessageText.Text = message;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _pairingCts?.Cancel();
            _pairingCts?.Dispose();
            _pairingCts = null;

            if (DialogResult != true)
            {
                _phoneClient?.Dispose();
                _phoneClient = null;
            }

            GC.SuppressFinalize(this);
        }

        private void ShowError(string message)
        {
            ShowStatus(message, isError: true);
        }

        private static bool IsValidIpOrHost(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            if (System.Net.IPAddress.TryParse(value, out _)) return true;

            // Very small hostname validation: allow letters/numbers/dots/hyphens.
            // (We don't resolve DNS here; just avoid obviously invalid input.)
            return Regex.IsMatch(value, "^[A-Za-z0-9.-]+$");
        }

        private void ClearAllValidation()
        {
            ClearValidation(IpAddressTextBox, IpValidationText);
            ClearValidation(PortTextBox, PortValidationText);
            ClearValidation(PairingCodeTextBox, PairingValidationText);
        }

        private void ClearValidation(TextBox box, TextBlock message)
        {
            box.BorderBrush = (Brush)FindResource("Brush.Border");
            message.Visibility = Visibility.Collapsed;
            message.Text = string.Empty;
        }

        private void SetValidationError(TextBox box, TextBlock message, string text)
        {
            box.BorderBrush = (Brush)FindResource("Brush.Error");
            message.Text = text;
            message.Visibility = Visibility.Visible;
            ShowError(text);
        }

        private void ShowCertFingerprint(string fingerprint)
        {
            StatusBorder.Visibility = Visibility.Collapsed;
            CertFingerprintText.Text = fingerprint;
            CertFingerprintBorder.Visibility = Visibility.Visible;
        }

        private void CopyCert_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_pendingCertFingerprint))
            {
                Clipboard.SetText(_pendingCertFingerprint);
                CopyCertButton.Content = "Copied";
                _ = Task.Delay(2000).ContinueWith(_ =>
                    Dispatcher.BeginInvoke(() => CopyCertButton.Content = "Copy"));
            }
        }

        private async void CertVerified_Click(object sender, RoutedEventArgs e)
        {
            _certVerified = true;
            CertFingerprintBorder.Visibility = Visibility.Collapsed;
            LogMessages.CertificateFingerprintVerified(Logger, _pendingCertFingerprint);
            ShowStatus("Connected and verified!", isError: false);
            await Task.Delay(500);
            DialogResult = true;
            Close();
        }
    }
}

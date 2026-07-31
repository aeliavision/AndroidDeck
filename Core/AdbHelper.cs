using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VcfEditor.Core
{
    public class AdbHelper
    {
        private static readonly char[] LineSeparators = ['\r', '\n'];
        private static readonly char[] DeviceColumnSeparators = ['\t', ' '];

        private string? _adbPath;

        public string? LastError { get; private set; }

        public AdbHelper(string? customAdbPath = null)
        {
            _adbPath = customAdbPath ?? FindAdb();
        }

        public bool IsAdbAvailable => !string.IsNullOrEmpty(_adbPath) && File.Exists(_adbPath);
        // CancellationToken and once with. The token-less overloads called the token overloads
        // with no benefit, doubling the surface area of the class. Fix: use
        // `CancellationToken cancellationToken = default` as a default parameter on every
        // method so callers that don't need cancellation continue to compile unchanged, and
        // callers that do simply pass their token. All duplicate overloads are removed.

        /// <summary>Forwards <paramref name="port"/> on the single connected ADB device.</summary>
        public async Task<bool> ForwardPortAsync(int port, CancellationToken cancellationToken = default)
        {
            if (!IsAdbAvailable) return false;

            var deviceId = await GetSingleDeviceIdAsync(cancellationToken);
            if (string.IsNullOrEmpty(deviceId)) return false;

            // adb -s <device> forward tcp:PORT tcp:PORT
            var result = await RunAdbCommandAsync(deviceId, $"forward tcp:{port} tcp:{port}", cancellationToken);
            LastError = result.ExitCode == 0 ? null : result.Output;
            return result.ExitCode == 0;
        }

        /// <summary>Removes an ADB forward rule for <paramref name="port"/>.</summary>
        public async Task<bool> RemoveForwardAsync(int port, CancellationToken cancellationToken = default)
        {
            if (!IsAdbAvailable) return false;

            var deviceId = await GetSingleDeviceIdAsync(cancellationToken);
            if (string.IsNullOrEmpty(deviceId)) return false;

            var result = await RunAdbCommandAsync(deviceId, $"forward --remove tcp:{port}", cancellationToken);
            LastError = result.ExitCode == 0 ? null : result.Output;
            return result.ExitCode == 0;
        }

        private async Task<string?> GetSingleDeviceIdAsync(CancellationToken cancellationToken = default)
        {
            LastError = null;
            var devices = await ListDevicesAsync(cancellationToken);
            if (devices.Count == 0)
            {
                LastError = "No ADB devices found. Ensure USB debugging is enabled and the device is authorized (adb devices).";
                return null;
            }
            if (devices.Count > 1)
            {
                LastError = "Multiple ADB devices found. Disconnect extra devices or specify a device.";
                return null;
            }

            return devices[0];
        }

        /// <summary>Returns the serial numbers of all currently connected ADB devices.</summary>
        public async Task<List<string>> ListDevicesAsync(CancellationToken cancellationToken = default)
        {
            if (!IsAdbAvailable) return new List<string>();

            var result = await RunAdbCommandAsync(null, "devices", cancellationToken);
            if (result.ExitCode != 0) return new List<string>();

            var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
            var devices = new List<string>();

            // Skip first line ("List of devices attached")
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(DeviceColumnSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1] == "device")
                    devices.Add(parts[0]);
            }

            return devices;
        }

        /// <summary>Returns true if <paramref name="packageName"/> is installed on the device.</summary>
        public async Task<bool> IsAppInstalledAsync(string deviceId, string packageName,
            CancellationToken cancellationToken = default)
        {
            if (!IsAdbAvailable) return false;

            // adb -s deviceId shell pm list packages packageName
            var result = await RunAdbCommandAsync(deviceId, $"shell pm list packages {packageName}", cancellationToken);
            return result.ExitCode == 0 && result.Output.Contains($"package:{packageName}", StringComparison.Ordinal);
        }

        /// <summary>Installs the APK at <paramref name="apkPath"/> on the device, replacing any existing version.</summary>
        public async Task<bool> InstallAppAsync(string deviceId, string apkPath,
            CancellationToken cancellationToken = default)
        {
            if (!IsAdbAvailable || !File.Exists(apkPath)) return false;

            // adb -s deviceId install -r apkPath
            var result = await RunAdbCommandAsync(deviceId, $"install -r \"{apkPath}\"", cancellationToken);
            return result.ExitCode == 0;
        }

        private async Task<(int ExitCode, string Output)> RunAdbCommandAsync(
            string? deviceId, string arguments, CancellationToken cancellationToken = default)
        {
            try
            {
                var fullArguments = string.IsNullOrEmpty(deviceId)
                    ? arguments
                    : $"-s {deviceId} {arguments}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = fullArguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                // the process to exit. Using Task.Run(() => WaitForExit()) blocks a thread-
                // pool thread but — crucially — the ReadToEndAsync tasks are already running
                // so the pipe buffers can drain, preventing the deadlock where the child
                // blocks on a full pipe and the parent waits for the child to exit.
                // Use WaitForExitAsync (available in .NET 5+) for a fully async wait.
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask  = process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken);

                var output = await outputTask;
                var error  = await errorTask;

                return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
            }
            catch (OperationCanceledException)
            {
                return (-1, "Cancelled");
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        private static string? FindAdb()
        {
            // 1. Try PATH
            // finish and never redirected its output, leaving a dangling child process.
            // Use a short synchronous WaitForExit with a timeout so we confirm adb is
            // reachable without leaking the process handle.
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "adb",
                        Arguments = "--version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                // Drain output so the child doesn't block on a full pipe buffer.
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                bool exited = process.WaitForExit(3000); // 3-second timeout
                if (exited && process.ExitCode == 0)
                    return "adb";
            }
            catch { }

            // 2. Try common Android SDK locations
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] paths = {
                Path.Combine(localAppData, @"Android\Sdk\platform-tools\adb.exe"),
                @"C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe",
                @"C:\android-sdk\platform-tools\adb.exe"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }

            return null;  // adb not found on this system
        }
    }
}

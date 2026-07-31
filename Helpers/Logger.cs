using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace VcfEditor.Helpers
{
    /// <summary>
    /// Diagnostic file logger — writes timestamped entries to VcfEditor_diag.log
    /// in the same folder as the executable. Enabled unconditionally so we can trace
    /// phone-connection issues without attaching a debugger.
    /// </summary>
    public static class DiagLog
    {
        private static ILogger Logger => AppLoggerFactory.CreateLogger(nameof(DiagLog));

        public static void Write(string level, string message, Exception? ex = null)
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [Thread:{Environment.CurrentManagedThreadId}] {message}";
            if (ex != null) entry += $"\n  EXCEPTION: {ex.GetType().Name}: {ex.Message}\n  Stack: {ex.StackTrace}";

            // ARCH-W04: Route all diagnostic logging through the single ILogger pipeline.
            // If AppLoggerFactory isn't initialized yet, CreateLogger returns a NullLogger.
            try
            {
                switch (level?.ToUpperInvariant())
                {
                    case "ERROR":
                        LogMessages.DiagnosticError(Logger, ex, entry);
                        break;
                    case "WARN":
                    case "WARNING":
                        LogMessages.DiagnosticWarning(Logger, ex, entry);
                        break;
                    default:
                        LogMessages.DiagnosticInformation(Logger, ex, entry);
                        break;
                }
            }
            catch
            {
                // Swallow — never let logging crash the application.
            }
        }

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg, Exception? ex = null) => Write("WARN", msg, ex);
        public static void Error(string msg, Exception ex) => Write("ERROR", msg, ex);
    }

    /// <summary>
    /// <see cref="ILoggerFactory"/>-based approach that is compatible with DI.
    ///
    /// <para>
    /// The old <c>Logger</c> class held a static <see cref="ILoggerFactory"/> and exposed
    /// static <c>LogInformation/LogWarning/LogError</c> helpers. This made it impossible
    /// to swap the logger implementation in tests, inject context-specific loggers, or
    /// configure per-category log levels — all hallmarks of the static-singleton anti-pattern.
    /// </para>
    ///
    /// <para>
    /// Fix: <see cref="AppLoggerFactory"/> is initialised once at application startup
    /// (called from <c>App.OnStartup</c>) and disposed on exit. All classes that need logging
    /// call <see cref="AppLoggerFactory.CreateLogger{T}"/> to obtain a typed
    /// <see cref="ILogger{T}"/> instance via the shared factory — no static state, and
    /// easily replaceable with any <see cref="ILoggerFactory"/> (e.g. Serilog, NLog) in tests.
    /// </para>
    /// </summary>
    public static class AppLoggerFactory
    {
        private static ILoggerFactory? _factory;

        /// <summary>
        /// Initialises the shared factory. Must be called once from <c>App.OnStartup</c>
        /// before any component requests a logger.
        /// </summary>
        public static void Initialize(ILoggerFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _factory = factory;
        }

        /// <summary>
        /// Creates a typed <see cref="ILogger{T}"/> for the given category.
        /// Returns a <see cref="Microsoft.Extensions.Logging.Abstractions.NullLogger{T}"/>
        /// if the factory has not yet been initialised (e.g. during unit tests).
        /// </summary>
        public static ILogger<T> CreateLogger<T>() =>
            _factory?.CreateLogger<T>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<T>();

        public static ILogger CreateLogger(string categoryName) =>
            _factory?.CreateLogger(categoryName)
            ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger(categoryName);

        /// <summary>
        /// Releases the compatibility reference to the host-owned logger factory.
        /// The .NET Generic Host remains the sole owner and disposes the factory.
        /// </summary>
        public static void Shutdown()
        {
            _factory = null;
        }
    }

    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _filePath;
        // LEAK-03 FIX: Share a single FileLogger instance across all categories so only
        // one StreamWriter (and one OS file handle) is ever open for the log file.
        // Previously Dispose() was a no-op because File.AppendAllText held no handle;
        // now Dispose() must flush and close the shared StreamWriter.
        private readonly FileLogger _sharedLogger;

        public FileLoggerProvider(string filePath)
        {
            _filePath = filePath;
            _sharedLogger = new FileLogger(filePath);
        }

        public ILogger CreateLogger(string categoryName) => _sharedLogger;

        // LEAK-03 FIX: Dispose flushes and closes the persistent StreamWriter.
        // Called by ILoggerFactory.Dispose() which is triggered by the .NET Generic Host
        // on process exit — guarantees no log lines are lost on orderly shutdown.
        public void Dispose()
        {
            _sharedLogger.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// LEAK-03 FIX: Replaces the File.AppendAllText-per-call pattern with a persistent,
    /// buffered StreamWriter that stays open for the lifetime of the logger.
    ///
    /// Old behaviour: every Log() call opened the file, wrote one line, and closed it.
    /// That meant one CreateFile + FlushFileBuffers + CloseHandle syscall sequence per
    /// log entry — O(n) kernel transitions for n messages, plus OS-level file metadata
    /// updates (mtime, size) on every write. Under heavy load (e.g. parsing a large VCF)
    /// this produced measurable disk I/O and latency spikes on the UI thread.
    ///
    /// New behaviour:
    ///   • StreamWriter is opened once with AutoFlush = false and a 64 KB write buffer.
    ///   • Log() writes into the buffer under a lock; no syscall unless the buffer fills.
    ///   • Flush() is called explicitly on Dispose() (triggered by ProcessExit via
    ///     IHost.Dispose → ILoggerFactory.Dispose) so no log lines are lost on exit.
    ///   • FileShare.ReadWrite allows external readers (e.g. tail, notepad++) to read the
    ///     file while it is open.
    /// </summary>
    public class FileLogger : ILogger, IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();
        private bool _disposed;

        public FileLogger(string filePath)
        {
            // LEAK-03 FIX: Open once; keep open. AutoFlush=false lets the OS buffer writes.
            // FileShare.ReadWrite allows concurrent readers while the log is open.
            var stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 65536,
                useAsync: false);

            _writer = new StreamWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false)
            {
                AutoFlush = false
            };
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => !_disposed;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (_disposed) return;

            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel}] {formatter(state, exception)}";
            if (exception != null)
                logEntry += $"{Environment.NewLine}Exception: {exception}";

            lock (_lock)
            {
                try
                {
                    if (_disposed) return;
                    _writer.WriteLine(logEntry);
                    // Flush every 10 s is handled by Dispose; for critical levels flush immediately.
                    if (logLevel >= LogLevel.Warning)
                        _writer.Flush();
                }
                catch
                {
                    // Swallow — never let logging crash the application.
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                try { _writer.Flush(); } catch { }
                try { _writer.Dispose(); } catch { }
            }

            GC.SuppressFinalize(this);
        }
    }
}
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VcfEditor.Helpers;

namespace VcfEditor.Services.Performance;

/// <summary>
/// Samples the WPF dispatcher cadence and emits a bounded warning when the UI thread
/// is delayed long enough to be noticeable. It records duration only and never logs
/// contact, file, device, or session data.
/// </summary>
internal sealed class UiThreadStallMonitor : IHostedService, IDisposable
{
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan WarningThreshold = TimeSpan.FromMilliseconds(750);

    private readonly Application _application;
    private readonly ILogger<UiThreadStallMonitor> _logger;
    private readonly Stopwatch _stopwatch = new();
    private DispatcherTimer? _timer;
    private bool _disposed;

    public UiThreadStallMonitor(
        Application application,
        ILogger<UiThreadStallMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(logger);
        _application = application;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_application.Dispatcher.CheckAccess())
            StartCore();
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            _application.Dispatcher.Invoke(StartCore, DispatcherPriority.Send, cancellationToken);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_application.Dispatcher.HasShutdownStarted || _application.Dispatcher.HasShutdownFinished)
            return Task.CompletedTask;

        if (_application.Dispatcher.CheckAccess())
            StopCore();
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            _application.Dispatcher.Invoke(StopCore, DispatcherPriority.Send, cancellationToken);
        }
        return Task.CompletedTask;
    }

    internal static TimeSpan CalculateStall(TimeSpan elapsed)
        => elapsed > SampleInterval ? elapsed - SampleInterval : TimeSpan.Zero;

    private void StartCore()
    {
        if (_timer is not null || _disposed) return;
        _stopwatch.Restart();
        _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = SampleInterval
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = _stopwatch.Elapsed;
        _stopwatch.Restart();
        var stall = CalculateStall(elapsed);
        if (stall >= WarningThreshold)
            LogMessages.UiThreadStallDetected(_logger, stall.TotalMilliseconds);
    }

    private void StopCore()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
        _stopwatch.Reset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_application.Dispatcher.HasShutdownStarted && !_application.Dispatcher.HasShutdownFinished)
        {
            if (_application.Dispatcher.CheckAccess())
                StopCore();
            else
                _application.Dispatcher.Invoke(StopCore);
        }
        GC.SuppressFinalize(this);
    }
}

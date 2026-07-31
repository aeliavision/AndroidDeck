using System;
using Microsoft.Extensions.Logging;
using VcfEditor.Helpers;

namespace VcfEditor.Services;

public sealed class ApplicationExceptionHandler : IApplicationExceptionHandler
{
    private readonly ILogger<ApplicationExceptionHandler> _logger;
    private readonly IDialogService _dialogService;

    public ApplicationExceptionHandler(
        ILogger<ApplicationExceptionHandler> logger,
        IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dialogService);
        _logger = logger;
        _dialogService = dialogService;
    }

    public bool Handle(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var correlationId = FormatCorrelationId(DateTimeOffset.UtcNow, Guid.NewGuid());
        if (IsRecoverable(exception))
        {
            LogMessages.RecoverableUiException(_logger, exception, correlationId);
            return true;
        }

        LogMessages.FatalUiException(_logger, exception, correlationId);
        _dialogService.ShowError(
            $"AndroidDeck encountered an unexpected error and must close.\n\nReference: {correlationId}",
            "AndroidDeck error");
        return false;
    }

    internal static bool IsRecoverable(Exception exception) => exception is RecoverableUiException;

    internal static string FormatCorrelationId(DateTimeOffset timestamp, Guid identifier)
    {
        var suffix = identifier.ToString("N")[..8];
        return $"ERR-{timestamp.UtcDateTime:yyyyMMdd}-{suffix}";
    }
}

public sealed class RecoverableUiException : Exception
{
    public RecoverableUiException()
    {
    }

    public RecoverableUiException(string message)
        : base(message)
    {
    }

    public RecoverableUiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

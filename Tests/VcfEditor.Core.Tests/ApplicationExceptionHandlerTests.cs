using FluentAssertions;
using VcfEditor.Services;

namespace VcfEditor.Core.Tests;

public sealed class ApplicationExceptionHandlerTests
{
    [Fact]
    public void FormatCorrelationIdUsesUtcTimestampAndStableSuffix()
    {
        var timestamp = new DateTimeOffset(2026, 7, 28, 20, 15, 30, TimeSpan.Zero);
        var identifier = Guid.Parse("12345678-90ab-cdef-1234-567890abcdef");

        var result = ApplicationExceptionHandler.FormatCorrelationId(timestamp, identifier);

        result.Should().Be("ERR-20260728-12345678");
    }

    [Fact]
    public void IsRecoverableReturnsTrueOnlyForExplicitRecoverableUiException()
    {
        ApplicationExceptionHandler.IsRecoverable(new RecoverableUiException("Recoverable"))
            .Should().BeTrue();
        ApplicationExceptionHandler.IsRecoverable(new InvalidOperationException("Fatal"))
            .Should().BeFalse();
    }
}

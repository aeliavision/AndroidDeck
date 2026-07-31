using System;
using System.Threading;
using System.Threading.Tasks;

namespace VcfEditor.Services.Settings;

public interface IUserNotificationService
{
    Task WaitForDismissalAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class UserNotificationService : IUserNotificationService
{
    public Task WaitForDismissalAsync(TimeSpan duration, CancellationToken cancellationToken)
        => Task.Delay(duration, cancellationToken);
}

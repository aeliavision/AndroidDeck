using System;
using Microsoft.Extensions.DependencyInjection;
using VcfEditor.Core;
using VcfEditor.ViewModels;
using VcfEditor.Views;

namespace VcfEditor.Features.PhoneSession;

public sealed class PhoneSessionScopeFactory : IPhoneSessionScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PhoneSessionScopeFactory(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    public PhoneSessionScope Create(PhoneApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var scope = _scopeFactory.CreateScope();
        try
        {
            var provider = scope.ServiceProvider;
            var context = provider.GetRequiredService<PhoneSessionContext>();
            context.Initialize(client);

            return new PhoneSessionScope(
                scope,
                context,
                provider.GetRequiredService<FileBrowserView>(),
                provider.GetRequiredService<FileBrowserViewModel>(),
                provider.GetRequiredService<GalleryView>(),
                provider.GetRequiredService<GalleryViewModel>(),
                provider.GetRequiredService<BackupView>(),
                provider.GetRequiredService<BackupViewModel>());
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}

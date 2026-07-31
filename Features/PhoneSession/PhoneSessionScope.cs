using System;
using Microsoft.Extensions.DependencyInjection;
using VcfEditor.ViewModels;
using VcfEditor.Views;

namespace VcfEditor.Features.PhoneSession;

public sealed class PhoneSessionScope : IDisposable
{
    private readonly IServiceScope _scope;
    private bool _disposed;

    internal PhoneSessionScope(
        IServiceScope scope,
        PhoneSessionContext context,
        FileBrowserView fileBrowserView,
        FileBrowserViewModel fileBrowserViewModel,
        GalleryView galleryView,
        GalleryViewModel galleryViewModel,
        BackupView backupView,
        BackupViewModel backupViewModel)
    {
        _scope = scope;
        Context = context;
        FileBrowserView = fileBrowserView;
        FileBrowserViewModel = fileBrowserViewModel;
        GalleryView = galleryView;
        GalleryViewModel = galleryViewModel;
        BackupView = backupView;
        BackupViewModel = backupViewModel;
    }

    public PhoneSessionContext Context { get; }
    public FileBrowserView FileBrowserView { get; }
    public FileBrowserViewModel FileBrowserViewModel { get; }
    public GalleryView GalleryView { get; }
    public GalleryViewModel GalleryViewModel { get; }
    public BackupView BackupView { get; }
    public BackupViewModel BackupViewModel { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _scope.Dispose();
        GC.SuppressFinalize(this);
    }
}

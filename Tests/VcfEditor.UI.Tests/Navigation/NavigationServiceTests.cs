using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using VcfEditor.Core;
using VcfEditor.Navigation;
using VcfEditor.Services;
using VcfEditor.Views;
using Xunit;

namespace VcfEditor.UI.Tests.Navigation;

public sealed class NavigationServiceTests
{
    [Fact]
    public async Task NavigateAsyncInitializesPageAndPublishesNavigation()
    {
        var page = new object();
        using var factory = new FakePageFactory(page);
        var service = new NavigationService(factory);
        NavigationChangedEventArgs? observed = null;
        service.Navigated += (_, args) => observed = args;

        await service.NavigateAsync(ShellDestination.Contacts);

        factory.InitializedDestinations.Should().ContainSingle()
            .Which.Should().Be(ShellDestination.Contacts);
        service.Current.Should().Be(ShellDestination.Contacts);
        observed.Should().NotBeNull();
        observed!.Page.Should().BeSameAs(page);
        observed.IsReselection.Should().BeFalse();
    }

    [Fact]
    public async Task NavigateAsyncMarksReselectionWithoutCreatingAnotherPage()
    {
        var page = new object();
        using var factory = new FakePageFactory(page);
        var service = new NavigationService(factory);
        NavigationChangedEventArgs? observed = null;
        service.Navigated += (_, args) => observed = args;

        await service.NavigateAsync(ShellDestination.Dashboard);

        observed.Should().NotBeNull();
        observed!.IsReselection.Should().BeTrue();
        factory.GetPageCalls.Should().Be(1);
    }

    [Fact]
    public async Task NavigateAsyncDoesNotPublishWhenInitializationIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        using var factory = new FakePageFactory(new object()) { BlockInitialization = true };
        var service = new NavigationService(factory);
        var navigated = false;
        service.Navigated += (_, _) => navigated = true;

        var task = service.NavigateAsync(ShellDestination.Gallery, cancellation.Token);
        cancellation.Cancel();

        await FluentActions.Awaiting(() => task).Should().ThrowAsync<OperationCanceledException>();
        navigated.Should().BeFalse();
        service.Current.Should().Be(ShellDestination.Dashboard);
    }

    private sealed class FakePageFactory : IPageFactory
    {
        private readonly object _page;

        public FakePageFactory(object page)
        {
            _page = page;
        }

        public event EventHandler? MetricsChanged
        {
            add { }
            remove { }
        }

        public DashboardView DashboardView => null!;
        public ContactsView ContactsView => null!;
        public int GalleryItemCount => 0;
        public int BackupHistoryCount => 0;
        public bool IsGalleryMetricsLoaded => false;
        public int GetPageCalls { get; private set; }

        public void UpdateCapabilities(ShellCapabilitySnapshot snapshot) { }
        public bool BlockInitialization { get; init; }
        public List<ShellDestination> InitializedDestinations { get; } = [];

        public object? GetPage(ShellDestination destination)
        {
            GetPageCalls++;
            return _page;
        }

        public void SetPhoneClient(PhoneApiClient? client)
        {
        }

        public async Task InitializePageAsync(
            ShellDestination destination,
            CancellationToken cancellationToken = default)
        {
            InitializedDestinations.Add(destination);
            if (BlockInitialization)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}

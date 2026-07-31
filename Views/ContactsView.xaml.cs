using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VcfEditor.Core;
using VcfEditor.Features.Contacts;
using VcfEditor.Helpers;
using VcfEditor.Models;
using VcfEditor.Services;
using VcfEditor.ViewModels;
namespace VcfEditor.Views;
public partial class ContactsView : UserControl
{
    private readonly ContactsViewModel _viewModel;
    private readonly IContactEditorWorkflow _workflow;
    private readonly ContactsViewPresentation _presentation;
    private readonly DragDropHelper _dragDrop;
    public ContactsView(
        ContactsViewModel viewModel,
        IContactEditorWorkflow workflow,
        IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(dialogService);
        _viewModel = viewModel;
        _workflow = workflow;
        InitializeComponent();
        _presentation = new ContactsViewPresentation(this);
        _dragDrop = new DragDropHelper(_workflow.OpenFileAsync, dialogService);
        DataContext = viewModel;
        viewModel.PhoneErrorOccurred += (title, message) => dialogService.ShowError(message, title);
        workflow.PhoneClientChanged += OnWorkflowPhoneClientChanged;
        viewModel.RefreshSort();
        Loaded += ContactsView_Loaded;
        Unloaded += ContactsView_Unloaded;
        SizeChanged += (_, _) => _presentation.UpdateLayout();
        _presentation.UpdateSourceMode(viewModel.ActiveSource);
    }
    public IContactEditorWorkflow Actions => _workflow;
    public event Action<PhoneApiClient?>? PhoneClientChanged;
    public event EventHandler? RetryPhoneConnectionRequested;
    public void ShowPhoneConnectionBanner(string message, bool showRetry)
    {
        PhoneConnectionBannerText.Text = message;
        RetryPhoneConnectionButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        PhoneConnectionBanner.Visibility = Visibility.Visible;
    }
    public void HidePhoneConnectionBanner()
    {
        PhoneConnectionBanner.Visibility = Visibility.Collapsed;
        PhoneConnectionBannerText.Text = string.Empty;
    }
    public async Task ConnectFromDashboardAsync(PhoneContactsClient contactsClient, PhoneApiClient apiClient)
    {
        await _viewModel.ConnectPhoneAsync(contactsClient);
        _presentation.UpdateSourceMode(_viewModel.ActiveSource);
        PhoneClientChanged?.Invoke(apiClient);
    }
    public Task RefreshFromDashboardAsync() => _workflow.RefreshPhoneAsync();
    public void DisconnectFromDashboard()
    {
        _workflow.DisconnectPhone();
        _presentation.UpdateSourceMode(_viewModel.ActiveSource);
        PhoneClientChanged?.Invoke(null);
    }
    private List<Contact> SelectedContacts()
        => ContactsListView.SelectedItems.Cast<Contact>().ToList();
    private void UpdateWorkflowSelection()
        => _workflow.UpdateSelection(
            SelectedContacts(),
            ContactsListView.SelectedItem as Contact,
            PhoneNumbersListView.SelectedItem as PhoneNumber);
    private void ContactsView_Loaded(object sender, RoutedEventArgs e)
    {
        _dragDrop.EnableDragDrop(MainGrid);
        _presentation.UpdateLayout();
        UpdateWorkflowSelection();
    }
    private void ContactsView_Unloaded(object sender, RoutedEventArgs e)
        => _dragDrop.DisableDragDrop(MainGrid);

    private void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchTextBox.Clear();
    private async void ContactsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedContacts();
        _viewModel.SelectedContact = selected.Count == 1 ? selected[0] : null;
        _presentation.UpdateDetailsSelection(selected.Count);
        SelectionSummaryText.Text = selected.Count switch
        {
            0 => "No contacts selected · Ctrl+click selects multiple · Enter edits · Delete removes",
            1 => "1 contact selected · Enter edits · Delete removes",
            _ => $"{selected.Count} contacts selected · Delete removes the selection"
        };
        UpdateWorkflowSelection();
        await _workflow.LoadSelectedDetailsAsync(_viewModel.SelectedContact);
    }
    private async void ContactsListView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
            await _workflow.DeleteContactsCommand.ExecuteAsync(null);
        else if (e.Key == Key.Enter)
            await _workflow.EditContactsCommand.ExecuteAsync(null);
        else
            return;
        e.Handled = true;
    }
    private async void ContactsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => await _workflow.EditContactsCommand.ExecuteAsync(null);
    private void PhoneNumbersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _presentation.UpdatePhoneNumberSelection(PhoneNumbersListView.SelectedItem is PhoneNumber);
        UpdateWorkflowSelection();
    }
    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewColumnHeader { Tag: not null } header)
            _viewModel.ApplySort(header.Tag.ToString()!);
    }
    private void RtlToggleButton_Click(object sender, RoutedEventArgs e)
        => _presentation.SetRightToLeft(RtlToggleButton.IsChecked == true);
    private void VisibleRtlToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = VisibleRtlToggle.IsChecked == true;
        RtlToggleButton.IsChecked = enabled;
        RtlMenuItem.IsChecked = enabled;
        _presentation.SetRightToLeft(enabled);
    }
    private void RtlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var enabled = RtlMenuItem.IsChecked;
        RtlToggleButton.IsChecked = enabled;
        VisibleRtlToggle.IsChecked = enabled;
        _presentation.SetRightToLeft(enabled);
    }
    private void CompactBackButton_Click(object sender, RoutedEventArgs e)
        => _presentation.ShowContactList();
    private void RetryPhoneConnection_Click(object sender, RoutedEventArgs e)
        => RetryPhoneConnectionRequested?.Invoke(this, EventArgs.Empty);
    private void OnWorkflowPhoneClientChanged(PhoneApiClient? client)
    {
        _presentation.UpdateSourceMode(_viewModel.ActiveSource);
        PhoneClientChanged?.Invoke(client);
    }
}

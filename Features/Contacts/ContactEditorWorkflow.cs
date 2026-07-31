using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using VcfEditor.Core;
using VcfEditor.Models;
using VcfEditor.Services;
using VcfEditor.ViewModels;

namespace VcfEditor.Features.Contacts;

public interface IContactEditorWorkflow
{
    event Action<PhoneApiClient?>? PhoneClientChanged;

    IAsyncRelayCommand OpenFileCommand { get; }
    IAsyncRelayCommand SaveFileCommand { get; }
    IAsyncRelayCommand AddContactCommand { get; }
    IAsyncRelayCommand EditContactsCommand { get; }
    IAsyncRelayCommand DeleteContactsCommand { get; }
    IAsyncRelayCommand ConnectPhoneCommand { get; }
    IAsyncRelayCommand RefreshPhoneCommand { get; }
    IRelayCommand DisconnectPhoneCommand { get; }
    IAsyncRelayCommand AddPhoneNumberCommand { get; }
    IAsyncRelayCommand EditPhoneNumberCommand { get; }
    IAsyncRelayCommand DeletePhoneNumberCommand { get; }

    void UpdateSelection(
        IReadOnlyList<Contact> contacts,
        Contact? selectedContact,
        PhoneNumber? selectedPhoneNumber);

    Task OpenFileAsync();
    Task OpenFileAsync(string filePath);
    Task SaveFileAsync(bool saveAs = false);
    Task AddContactAsync();
    Task EditContactsAsync(IReadOnlyList<Contact> contacts);
    Task DeleteContactsAsync(IReadOnlyList<Contact> contacts);
    Task LoadSelectedDetailsAsync(Contact? contact);
    Task AddPhoneNumberAsync(Contact? contact);
    Task EditPhoneNumberAsync(Contact? contact, PhoneNumber? phoneNumber);
    Task DeletePhoneNumberAsync(Contact? contact, PhoneNumber? phoneNumber);
    Task<PhoneConnectionResult?> ConnectPhoneAsync();
    Task RefreshPhoneAsync();
    void DisconnectPhone();
}

public sealed class ContactEditorWorkflow : IContactEditorWorkflow, IDisposable
{
    private readonly ContactsViewModel _viewModel;
    private readonly IDialogService _dialogService;
    private Contact[] _selectedContacts = Array.Empty<Contact>();
    private Contact? _selectedContact;
    private PhoneNumber? _selectedPhoneNumber;
    private bool _disposed;

    public ContactEditorWorkflow(ContactsViewModel viewModel, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(dialogService);
        _viewModel = viewModel;
        _dialogService = dialogService;

        OpenFileCommand = new AsyncRelayCommand(OpenFileAsync, CanRun);
        SaveFileCommand = new AsyncRelayCommand(() => SaveFileAsync(), CanSave);
        AddContactCommand = new AsyncRelayCommand(AddContactAsync, CanRun);
        EditContactsCommand = new AsyncRelayCommand(
            () => EditContactsAsync(_selectedContacts),
            CanEdit);
        DeleteContactsCommand = new AsyncRelayCommand(
            () => DeleteContactsAsync(_selectedContacts),
            CanDelete);
        ConnectPhoneCommand = new AsyncRelayCommand(ConnectAndPublishAsync, CanRun);
        RefreshPhoneCommand = new AsyncRelayCommand(RefreshPhoneAsync, CanUsePhone);
        DisconnectPhoneCommand = new RelayCommand(DisconnectAndPublish, CanUsePhone);
        AddPhoneNumberCommand = new AsyncRelayCommand(
            () => AddPhoneNumberAsync(_selectedContact),
            CanAddPhoneNumber);
        EditPhoneNumberCommand = new AsyncRelayCommand(
            () => EditPhoneNumberAsync(_selectedContact, _selectedPhoneNumber),
            CanEditPhoneNumber);
        DeletePhoneNumberCommand = new AsyncRelayCommand(
            () => DeletePhoneNumberAsync(_selectedContact, _selectedPhoneNumber),
            CanEditPhoneNumber);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public event Action<PhoneApiClient?>? PhoneClientChanged;

    public IAsyncRelayCommand OpenFileCommand { get; }
    public IAsyncRelayCommand SaveFileCommand { get; }
    public IAsyncRelayCommand AddContactCommand { get; }
    public IAsyncRelayCommand EditContactsCommand { get; }
    public IAsyncRelayCommand DeleteContactsCommand { get; }
    public IAsyncRelayCommand ConnectPhoneCommand { get; }
    public IAsyncRelayCommand RefreshPhoneCommand { get; }
    public IRelayCommand DisconnectPhoneCommand { get; }
    public IAsyncRelayCommand AddPhoneNumberCommand { get; }
    public IAsyncRelayCommand EditPhoneNumberCommand { get; }
    public IAsyncRelayCommand DeletePhoneNumberCommand { get; }

    public void UpdateSelection(
        IReadOnlyList<Contact> contacts,
        Contact? selectedContact,
        PhoneNumber? selectedPhoneNumber)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        _selectedContacts = contacts.ToArray();
        _selectedContact = selectedContact;
        _selectedPhoneNumber = selectedPhoneNumber;
        NotifyCanExecuteChanged();
    }

    public async Task OpenFileAsync()
    {
        var path = _dialogService.ShowOpenVcfDialog();
        if (!string.IsNullOrWhiteSpace(path))
            await OpenFileAsync(path);
    }

    public Task OpenFileAsync(string filePath) => _viewModel.LoadFileAsync(filePath);

    public async Task SaveFileAsync(bool saveAs = false)
    {
        var path = _viewModel.CurrentFilePath;
        if (saveAs || string.IsNullOrWhiteSpace(path))
            path = _dialogService.ShowSaveVcfDialog(path);
        if (string.IsNullOrWhiteSpace(path)) return;

        _viewModel.CurrentFilePath = path;
        await _viewModel.SaveFileAsync();
    }

    public async Task AddContactAsync()
    {
        var contact = _dialogService.ShowCreateContactDialog(_viewModel.ActiveSource);
        if (contact is null) return;

        if (_viewModel.ActiveSource == ContactSource.AndroidPhone)
            await _viewModel.AddPhoneContactAsync(contact);
        else
        {
            _viewModel.AddContact(contact);
            if (_viewModel.HasSearchText)
            {
                _viewModel.SearchText = string.Empty;
                _viewModel.StatusMessage = "Contact added — search cleared to show it.";
            }
            else
            {
                _viewModel.StatusMessage = "Contact added.";
            }
        }
    }

    public async Task EditContactsAsync(IReadOnlyList<Contact> contacts)
    {
        if (contacts.Count == 0)
        {
            _dialogService.ShowWarning("Please select a contact to edit.", "No Selection");
            return;
        }
        if (contacts.Count > 1)
        {
            _dialogService.ShowInformation(
                "Please select only one contact to edit.",
                "Multiple Selection");
            return;
        }

        var contact = contacts[0];
        if (!_dialogService.ShowEditContactDialog(contact)) return;

        if (_viewModel.ActiveSource == ContactSource.AndroidPhone)
        {
            // The dialog's Ok_Click already called _originalContact.UpdateFrom(clone),
            // which fires INPC events — WPF bindings update the row text immediately.
            // Do NOT call RefreshView() here: it drains the dispatcher queue early and
            // races with any pending FetchMissingDetailsAsync BeginInvoke.
            // UpdatePhoneContactAsync increments _phoneFetchGeneration and calls
            // RefreshView() after the server confirms, which is the correct moment.
            await _viewModel.UpdatePhoneContactAsync(contact);
        }
        else
        {
            _viewModel.IsDirty = true;
            _viewModel.RefreshView();
            _viewModel.StatusMessage = "Contact updated.";
        }
    }

    public async Task DeleteContactsAsync(IReadOnlyList<Contact> contacts)
    {
        if (contacts.Count == 0)
        {
            _dialogService.ShowWarning("Please select one or more contacts to delete.", "No Selection");
            return;
        }

        var message = contacts.Count == 1
            ? $"Are you sure you want to delete '{contacts[0].FullName}'?"
            : $"Are you sure you want to delete {contacts.Count} contacts?";
        if (!_dialogService.Confirm(message, "Confirm Delete")) return;

        if (_viewModel.ActiveSource == ContactSource.AndroidPhone)
            await _viewModel.DeletePhoneContactsAsync(contacts);
        else
        {
            _viewModel.DeleteContacts(contacts);
            _viewModel.StatusMessage = $"Deleted {contacts.Count} contact(s).";
        }
    }

    public Task LoadSelectedDetailsAsync(Contact? contact)
        => _viewModel.LoadContactDetailsAsync(contact);

    public async Task AddPhoneNumberAsync(Contact? contact)
    {
        if (contact is null)
        {
            _dialogService.ShowWarning("Please select a contact first.", "No Selection");
            return;
        }

        var phone = _dialogService.ShowCreatePhoneNumberDialog();
        if (phone is not null)
            await _viewModel.AddPhoneNumberAsync(contact, phone);
    }

    public async Task EditPhoneNumberAsync(Contact? contact, PhoneNumber? phoneNumber)
    {
        if (contact is null || phoneNumber is null)
        {
            _dialogService.ShowWarning(
                "Please select a contact and phone number to edit.",
                "No Selection");
            return;
        }

        if (_dialogService.ShowEditPhoneNumberDialog(phoneNumber))
            await _viewModel.PhoneNumberEditedAsync(contact);
    }

    public async Task DeletePhoneNumberAsync(Contact? contact, PhoneNumber? phoneNumber)
    {
        if (contact is null || phoneNumber is null)
        {
            _dialogService.ShowWarning("Please select a phone number to delete.", "No Selection");
            return;
        }

        if (_dialogService.Confirm(
                $"Are you sure you want to delete the phone number '{phoneNumber.Number}'?",
                "Confirm Delete"))
        {
            await _viewModel.DeletePhoneNumberAsync(contact, phoneNumber);
        }
    }

    public async Task<PhoneConnectionResult?> ConnectPhoneAsync()
    {
        var connection = _dialogService.ShowConnectPhoneDialog();
        if (connection is null) return null;
        await _viewModel.ConnectPhoneAsync(connection.ContactsClient);
        return connection;
    }

    public Task RefreshPhoneAsync() => _viewModel.RefreshFromPhoneAsync();

    public void DisconnectPhone() => _viewModel.DisconnectPhone();

    private bool CanRun() => !_viewModel.IsBusy;
    private bool CanSave() => CanRun() && _viewModel.HasContacts;
    private bool CanEdit() => CanRun() && _selectedContacts.Length == 1;
    private bool CanDelete() => CanRun() && _selectedContacts.Length > 0;
    private bool CanUsePhone() => CanRun() && _viewModel.PhoneClient is not null;
    private bool CanAddPhoneNumber() => CanRun() && _selectedContact is not null;
    private bool CanEditPhoneNumber()
        => CanRun() && _selectedContact is not null && _selectedPhoneNumber is not null;

    private async Task ConnectAndPublishAsync()
    {
        var connection = await ConnectPhoneAsync();
        if (connection is not null)
            PhoneClientChanged?.Invoke(connection.ApiClient);
    }

    private void DisconnectAndPublish()
    {
        DisconnectPhone();
        PhoneClientChanged?.Invoke(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContactsViewModel.IsBusy)
            or nameof(ContactsViewModel.HasContacts)
            or nameof(ContactsViewModel.PhoneClient)
            or nameof(ContactsViewModel.ActiveSource))
        {
            NotifyCanExecuteChanged();
        }
    }

    private void NotifyCanExecuteChanged()
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        SaveFileCommand.NotifyCanExecuteChanged();
        AddContactCommand.NotifyCanExecuteChanged();
        EditContactsCommand.NotifyCanExecuteChanged();
        DeleteContactsCommand.NotifyCanExecuteChanged();
        ConnectPhoneCommand.NotifyCanExecuteChanged();
        RefreshPhoneCommand.NotifyCanExecuteChanged();
        DisconnectPhoneCommand.NotifyCanExecuteChanged();
        AddPhoneNumberCommand.NotifyCanExecuteChanged();
        EditPhoneNumberCommand.NotifyCanExecuteChanged();
        DeletePhoneNumberCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        GC.SuppressFinalize(this);
    }
}

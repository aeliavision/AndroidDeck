using System;
using System.Windows;
using System.Windows.Controls;
using VcfEditor.Models;
using VcfEditor.Services;
using VcfEditor.Core;
using VcfEditor.Views;

namespace VcfEditor.Views
{
    public partial class EditContactDialog : AppDialogWindow
    {
        private readonly Contact _originalContact;
        private readonly IDialogService _dialogService;

        public Contact Contact { get; private set; }
        public bool IsEditable { get; private set; }

            public EditContactDialog(Contact contact)
            : this(contact, NonInteractiveDialogService.Instance)
        {
        }

        public EditContactDialog(Contact contact, IDialogService dialogService)
        {
            InitializeComponent();
            ArgumentNullException.ThrowIfNull(contact);
            ArgumentNullException.ThrowIfNull(dialogService);
            _dialogService = dialogService;
            _originalContact = contact;
            Contact = _originalContact.Clone();
            IsEditable = !_originalContact.IsReadOnly;
            DataContext = this;

            Closing += EditContactDialog_Closing;
            PhoneNumbersListView.SelectionChanged += (_, _) =>
            {
                ClearInlineValidation(PhoneNumbersListView, ValidationSummaryText);
            };
        }

        private void EditContactDialog_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DialogResult == true) return;
            if (!IsEditable) return;
            if (!HasUnsavedChanges()) return;

            if (!_dialogService.Confirm(
                    "Discard your changes?",
                    "Unsaved changes"))
                e.Cancel = true;
        }

        private bool HasUnsavedChanges()
        {
            if (!string.Equals(_originalContact.Prefix, Contact.Prefix, StringComparison.Ordinal)) return true;
            if (!string.Equals(_originalContact.FirstName, Contact.FirstName, StringComparison.Ordinal)) return true;
            if (!string.Equals(_originalContact.MiddleName, Contact.MiddleName, StringComparison.Ordinal)) return true;
            if (!string.Equals(_originalContact.LastName, Contact.LastName, StringComparison.Ordinal)) return true;
            if (!string.Equals(_originalContact.Suffix, Contact.Suffix, StringComparison.Ordinal)) return true;
            if (!string.Equals(_originalContact.FullName, Contact.FullName, StringComparison.Ordinal)) return true;
            if (!string.Equals(_originalContact.Organization, Contact.Organization, StringComparison.Ordinal)) return true;
            if (!string.Equals(_originalContact.Title, Contact.Title, StringComparison.Ordinal)) return true;

            if (_originalContact.Emails.Count != Contact.Emails.Count) return true;
            for (int i = 0; i < _originalContact.Emails.Count; i++)
            {
                if (!string.Equals(_originalContact.Emails[i], Contact.Emails[i], StringComparison.Ordinal)) return true;
            }

            if (_originalContact.PhoneNumbers.Count != Contact.PhoneNumbers.Count) return true;
            for (int i = 0; i < _originalContact.PhoneNumbers.Count; i++)
            {
                var a = _originalContact.PhoneNumbers[i];
                var b = Contact.PhoneNumbers[i];
                if (!string.Equals(a.Number, b.Number, StringComparison.Ordinal)) return true;
                if (a.Type != b.Type) return true;
                if (a.AndroidRawType != b.AndroidRawType) return true;
            }

            return false;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var validationResult = ContactValidator.ValidateContact(Contact);

            if (!validationResult.IsValid)
            {
                _dialogService.ShowWarning(
                    $"Please fix the following errors:\n\n{validationResult.ErrorMessage}",
                    "Validation Error");
                return;
            }

            ClearInlineValidation(FirstNameTextBox, ValidationSummaryText);
            _originalContact.UpdateFrom(Contact);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AddPhone_Click(object sender, RoutedEventArgs e)
        {
            var phoneDialog = new Views.PhoneNumberDialog(_dialogService);
            if (phoneDialog.ShowDialog() == true)
            {
                Contact.PhoneNumbers.Add(phoneDialog.PhoneNumber);
            }
        }

        private void RemovePhone_Click(object sender, RoutedEventArgs e)
        {
            var phoneNumbersListView = FindName("PhoneNumbersListView") as ListView;
            if (phoneNumbersListView?.SelectedItem is PhoneNumber selectedPhone)
            {
                if (_dialogService.Confirm(
                        $"Are you sure you want to delete the phone number '{selectedPhone.Number}'?",
                        "Confirm Delete"))
                {
                    Contact.PhoneNumbers.Remove(selectedPhone);
                }
            }
            else
            {
                SetInlineValidation(
                    PhoneNumbersListView,
                    ValidationSummaryText,
                    "Select a phone number to remove.");
            }
        }

        private void EditPhone_Click(object sender, RoutedEventArgs e)
        {
            var phoneNumbersListView = FindName("PhoneNumbersListView") as ListView;
            if (phoneNumbersListView?.SelectedItem is PhoneNumber selectedPhone)
            {
                var phoneDialog = new Views.PhoneNumberDialog(selectedPhone, _dialogService);
                if (phoneDialog.ShowDialog() == true)
                {
                }
            }
            else
            {
                SetInlineValidation(
                    PhoneNumbersListView,
                    ValidationSummaryText,
                    "Select a phone number to edit.");
            }
        }
        // both duplicated Contact.Clone() and Contact.UpdateFrom() which are the
        // canonical implementations on the model. Using the model methods ensures
        // any new fields added to Contact are automatically handled everywhere.
    }
}
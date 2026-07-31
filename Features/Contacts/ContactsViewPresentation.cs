using System;
using System.Windows;
using System.Windows.Controls;
using VcfEditor.Models;
using VcfEditor.Views;

namespace VcfEditor.Features.Contacts;

public sealed class ContactsViewPresentation
{
    private const double CompactThreshold = 900;
    private const double ExpandedThreshold = 1200;
    private readonly ContactsView _view;
    private int _selectedCount;

    public ContactsViewPresentation(ContactsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
    }

    public void UpdateLayout()
    {
        UpdateColumns();
        UpdateDetailsLayout();
    }

    public void UpdateDetailsSelection(int selectedCount)
    {
        _selectedCount = selectedCount;
        var single = selectedCount == 1;
        _view.SelectedContactInfo.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        _view.ContactEditPanel.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetailsLayout();
    }

    public void UpdatePhoneNumberSelection(bool selected)
    {
        _view.EditPhoneButton.IsEnabled = selected;
        _view.DeletePhoneButton.IsEnabled = selected;
    }

    public void UpdateSourceMode(ContactSource source)
    {
        var phone = source == ContactSource.AndroidPhone;
        _view.SourceIndicatorBar.Visibility = Visibility.Visible;
        _view.DeviceInfoText.Text = phone ? "Connected phone" : "Local file";
        _view.RefreshPhoneButton.Visibility = phone ? Visibility.Visible : Visibility.Collapsed;
        _view.DisconnectButton.Visibility = phone ? Visibility.Visible : Visibility.Collapsed;
        _view.OpenFileButton.IsEnabled = !phone;
        _view.SaveFileButton.IsEnabled = !phone;
        _view.ConnectPhoneButton.Content = phone ? "Connected" : "Connect phone";
        _view.ConnectPhoneButton.IsEnabled = !phone;
    }

    public void SetRightToLeft(bool enabled)
        => _view.FlowDirection = enabled ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public void ShowContactList()
    {
        _view.ContactsListView.SelectedItems.Clear();
        _selectedCount = 0;
        UpdateDetailsSelection(0);
    }

    private void UpdateColumns()
    {
        if (_view.ContactsListView.View is not GridView)
            return;
        var available = _view.ContactsListView.ActualWidth - 30;
        if (!double.IsFinite(available) || available <= 0)
            return;
        const double phone = 160, organization = 190, email = 210;
        _view.NameColumn.Width = Math.Max(200, available - phone - organization - email);
        _view.PhoneColumn.Width = phone;
        _view.OrganizationColumn.Width = organization;
        _view.EmailColumn.Width = email;
    }

    private void UpdateDetailsLayout()
    {
        var width = _view.ActualWidth;
        if (!double.IsFinite(width) || width <= 0)
            return;

        if (width < CompactThreshold)
        {
            var showDetails = _selectedCount == 1;
            _view.ContactListPanel.Visibility = showDetails ? Visibility.Collapsed : Visibility.Visible;
            _view.DetailsPanelBorder.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
            _view.DetailsGridSplitter.Visibility = Visibility.Collapsed;
            _view.CompactBackButton.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(_view.DetailsPanelBorder, 0);
            Grid.SetColumnSpan(_view.DetailsPanelBorder, 3);
            _view.DetailsColumn.Width = new GridLength(0);
            return;
        }

        _view.ContactListPanel.Visibility = Visibility.Visible;
        _view.DetailsPanelBorder.Visibility = Visibility.Visible;
        _view.DetailsGridSplitter.Visibility = Visibility.Visible;
        _view.CompactBackButton.Visibility = Visibility.Collapsed;
        Grid.SetColumn(_view.DetailsPanelBorder, 2);
        Grid.SetColumnSpan(_view.DetailsPanelBorder, 1);
        _view.DetailsColumn.Width = new GridLength(width >= ExpandedThreshold ? 380 : 320);
    }
}

using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VcfEditor.Core;
using VcfEditor.Models;
using VcfEditor.Services;

namespace VcfEditor.Views;

public sealed class PhoneNumberDialog : AppDialogWindow
{
    private readonly TextBox _numberInput;
    private readonly ComboBox _typeInput;
    private readonly TextBlock _validation;
    private readonly PhoneNumber? _originalPhoneNumber;

    public PhoneNumber PhoneNumber { get; }

    public PhoneNumberDialog()
        : this(NonInteractiveDialogService.Instance)
    {
    }

    public PhoneNumberDialog(PhoneNumber existingPhone)
        : this(existingPhone, NonInteractiveDialogService.Instance)
    {
    }

    public PhoneNumberDialog(IDialogService dialogService)
        : this(new PhoneNumber(), null, dialogService, "Add phone number")
    {
    }

    public PhoneNumberDialog(PhoneNumber existingPhone, IDialogService dialogService)
        : this(
            ClonePhone(existingPhone),
            existingPhone,
            dialogService,
            "Edit phone number")
    {
    }

    private static PhoneNumber ClonePhone(PhoneNumber existingPhone)
    {
        ArgumentNullException.ThrowIfNull(existingPhone);
        return new PhoneNumber(existingPhone.Number ?? string.Empty, existingPhone.Type);
    }

    private PhoneNumberDialog(
        PhoneNumber workingCopy,
        PhoneNumber? original,
        IDialogService dialogService,
        string title)
    {
        ArgumentNullException.ThrowIfNull(dialogService);
        PhoneNumber = workingCopy;
        _originalPhoneNumber = original;
        Title = title;
        Width = 480;

        var panel = new StackPanel { Margin = new Thickness(20) };
        var titleBlock = new TextBlock { Text = title };
        ApplyDialogStyle(titleBlock, "Dialog.Title");
        panel.Children.Add(titleBlock);
        var body = new TextBlock { Text = "Enter the number and choose how it should be labeled." };
        ApplyDialogStyle(body, "Dialog.Body");
        panel.Children.Add(body);

        panel.Children.Add(new TextBlock { Text = "Phone number", Margin = new Thickness(0, 0, 0, 4) });
        _numberInput = new TextBox
        {
            Text = PhoneNumber.Number,
            MinHeight = 38,
            Margin = new Thickness(0, 0, 0, 10)
        };
        AutomationProperties.SetName(_numberInput, "Phone number");
        _numberInput.TextChanged += (_, _) =>
        {
            PhoneNumber.Number = _numberInput.Text;
            ClearInlineValidation(_numberInput, _validation);
        };
        _numberInput.KeyDown += (_, e) => { if (e.Key == Key.Enter && Accept()) e.Handled = true; };
        panel.Children.Add(_numberInput);

        panel.Children.Add(new TextBlock { Text = "Type", Margin = new Thickness(0, 0, 0, 4) });
        _typeInput = new ComboBox { MinHeight = 38, Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetName(_typeInput, "Phone number type");
        _typeInput.Items.Add(CreateTypeItem("Mobile", PhoneNumberType.CELL));
        _typeInput.Items.Add(CreateTypeItem("Home", PhoneNumberType.HOME));
        _typeInput.Items.Add(CreateTypeItem("Work", PhoneNumberType.WORK));
        _typeInput.Items.Add(CreateTypeItem("Other", PhoneNumberType.XOther));
        _typeInput.SelectedItem = FindTypeItem(PhoneNumber.Type) ?? _typeInput.Items[0];
        _typeInput.SelectionChanged += (_, _) =>
        {
            if (_typeInput.SelectedItem is ComboBoxItem { Tag: PhoneNumberType type })
                PhoneNumber.Type = type;
        };
        panel.Children.Add(_typeInput);

        _validation = CreateValidationSummary();
        panel.Children.Add(_validation);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Save", MinWidth = 100, IsDefault = true };
        save.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        panel.Children.Add(actions);
        Content = panel;
    }

    private bool Accept()
    {
        var result = ContactValidator.ValidatePhoneNumber(_numberInput.Text);
        if (!result.IsValid)
        {
            SetInlineValidation(_numberInput, _validation, result.ErrorMessage ?? "Enter a valid phone number.");
            return false;
        }

        PhoneNumber.Number = (_numberInput.Text ?? string.Empty).Trim();
        if (_originalPhoneNumber is not null)
        {
            _originalPhoneNumber.Number = PhoneNumber.Number;
            _originalPhoneNumber.Type = PhoneNumber.Type;
        }
        DialogResult = true;
        return true;
    }

    private ComboBoxItem? FindTypeItem(PhoneNumberType type)
    {
        foreach (var item in _typeInput.Items)
            if (item is ComboBoxItem { Tag: PhoneNumberType candidate } combo && candidate == type)
                return combo;
        return null;
    }

    private static ComboBoxItem CreateTypeItem(string label, PhoneNumberType type) =>
        new() { Content = label, Tag = type };
}

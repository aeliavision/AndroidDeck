using System.Windows.Controls;
using System.Windows.Input;

namespace VcfEditor.Views;

public partial class ShellSidebarView : UserControl
{
    public ShellSidebarView()
    {
        InitializeComponent();
    }

    public void FocusSelectedItem()
    {
        var target = NavigationList.SelectedItem
            ?? (NavigationList.Items.Count > 0 ? NavigationList.Items[0] : null);
        if (target is null)
            return;

        NavigationList.ScrollIntoView(target);
        NavigationList.UpdateLayout();

        if (NavigationList.ItemContainerGenerator.ContainerFromItem(target) is not ListBoxItem item)
            return;

        item.Focus();
        Keyboard.Focus(item);
    }
}

using System.Windows;
using System.Windows.Controls;

namespace VcfEditor.Views
{
    public sealed class GalleryMetadataDialog : AppDialogWindow
    {
        private readonly CheckBox _favorite;
        private readonly TextBox _description;

        public bool? Favorite { get; private set; }
        public string? Description { get; private set; }

        public GalleryMetadataDialog(string title, bool? favoriteInitial = null, string? descriptionInitial = null)
        {
            Title = title;
            Width = 480;
            Height = 280;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new DockPanel { Margin = new Thickness(14) };

            var favLabel = new TextBlock { Text = "Favorite", Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(favLabel, Dock.Top);
            root.Children.Add(favLabel);

            _favorite = new CheckBox { Content = "Mark as favorite", Margin = new Thickness(0, 0, 0, 12) };
            if (favoriteInitial.HasValue) _favorite.IsChecked = favoriteInitial.Value;
            DockPanel.SetDock(_favorite, Dock.Top);
            root.Children.Add(_favorite);

            var descLabel = new TextBlock { Text = "Description", Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(descLabel, Dock.Top);
            root.Children.Add(descLabel);

            _description = new TextBox
            {
                Text = descriptionInitial ?? string.Empty,
                AcceptsReturn = true,
                Height = 110,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 14)
            };
            DockPanel.SetDock(_description, Dock.Top);
            root.Children.Add(_description);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (_, _) => { DialogResult = false; };

            var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
            ok.Click += (_, _) =>
            {
                Favorite = _favorite.IsChecked;
                Description = _description.Text;
                DialogResult = true;
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}

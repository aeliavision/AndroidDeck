using System.Windows;
using System.Windows.Controls;

namespace VcfEditor.Views
{
    public sealed class ConflictResolutionDialog : AppDialogWindow
    {
        public enum Result
        {
            Replace,
            KeepBoth,
            Skip
        }

        public Result? Choice { get; private set; }

        public ConflictResolutionDialog(string title, string message)
        {
            Title = title;
            Width = 420;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new DockPanel { Margin = new Thickness(14) };

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(text, Dock.Top);
            root.Children.Add(text);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var skip = new Button { Content = "Skip", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true, IsDefault = true };
            skip.Click += (_, _) => { Choice = Result.Skip; DialogResult = true; };

            var keepBoth = new Button { Content = "Keep both", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
            keepBoth.Click += (_, _) => { Choice = Result.KeepBoth; DialogResult = true; };

            var replace = new Button { Content = "Replace", MinWidth = 90 };
            ApplyDialogStyle(replace, "Dialog.DestructiveAction");
            replace.Click += (_, _) => { Choice = Result.Replace; DialogResult = true; };

            buttons.Children.Add(skip);
            buttons.Children.Add(keepBoth);
            buttons.Children.Add(replace);

            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}

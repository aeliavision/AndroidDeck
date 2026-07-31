using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace VcfEditor.Views
{
    public sealed class BackupCompletionDialog : AppDialogWindow
    {
        public BackupCompletionDialog(string title, string message, IEnumerable<(string Label, string Value)> rows)
        {
            Title = title;
            Width = 600;
            Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new DockPanel { Margin = new Thickness(16) };

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var titleBlock = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold };
            titleBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSize.Title");
            header.Children.Add(titleBlock);
            var subtitleBlock = new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            subtitleBlock.SetResourceReference(TextBlock.FontSizeProperty, "FontSize.Body");
            header.Children.Add(subtitleBlock);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rowList = rows.ToList();
            for (int i = 0; i < rowList.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var (label, value) = rowList[i];

                var left = new TextBlock
                {
                    Text = label,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, i == 0 ? 0 : 10, 12, 0)
                };
                left.SetResourceReference(TextBlock.FontSizeProperty, "FontSize.Caption");
                Grid.SetRow(left, i);
                Grid.SetColumn(left, 0);
                grid.Children.Add(left);

                var right = new TextBlock
                {
                    Text = value,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 0)
                };
                right.SetResourceReference(TextBlock.FontSizeProperty, "FontSize.Caption");
                Grid.SetRow(right, i);
                Grid.SetColumn(right, 1);
                grid.Children.Add(right);
            }

            DockPanel.SetDock(grid, Dock.Top);
            root.Children.Add(grid);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var ok = new Button
            {
                Content = "Done",
                MinWidth = 120,
                IsDefault = true
            };
            ok.Click += (_, _) => { DialogResult = true; };

            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            Content = root;
        }
    }
}

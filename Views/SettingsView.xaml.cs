using System.Windows.Controls;
using VcfEditor.ViewModels;

namespace VcfEditor.Views
{
    /// <summary>
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView(SettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}

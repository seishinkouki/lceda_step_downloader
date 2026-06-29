using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace ModelDownloader.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        private void ThemeButton_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button && Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = button.IsChecked switch
                {
                    true => ThemeVariant.Dark,
                    false => ThemeVariant.Light,
                    _ => ThemeVariant.Default
                };
            }
        }
    }
}
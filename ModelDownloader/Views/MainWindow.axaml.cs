using System.Runtime.InteropServices;
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Title = "立创商城 STEP 下载器";
                ExtendClientAreaToDecorationsHint = false;
                CanMaximize = true;
                TitleBarRightPanel.Margin = new Thickness(0);
            }
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

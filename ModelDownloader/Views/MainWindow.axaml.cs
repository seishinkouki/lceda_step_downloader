using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using ModelDownloader.ViewModels;

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

        /// <summary>点击 设置->保存位置 菜单项：打开文件夹选择框，设置下载保存位置</summary>
        private async void SaveLocationMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var options = new FolderPickerOpenOptions { AllowMultiple = false, Title = "选择下载保存位置" };
            try
            {
                if (!string.IsNullOrWhiteSpace(vm.SaveLocation))
                {
                    options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(vm.SaveLocation);
                }
            }
            catch (Exception)
            {
                // 当前保存目录不存在时，从默认位置开始选择
            }

            var folders = await storage.OpenFolderPickerAsync(options);
            if (folders.Count > 0)
            {
                vm.SetSaveLocation(folders[0].Path.LocalPath);
            }
        }
    }
}

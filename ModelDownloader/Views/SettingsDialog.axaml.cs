using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ModelDownloader.Views
{
    /// <summary>
    /// 设置对话框：分别设置符号 / 封装 / 3D 模型的保存位置。
    /// 保存返回 true（属性携带新值），取消返回 false。
    /// </summary>
    public partial class SettingsDialog : Window
    {
        public string? SymbolLocation { get; private set; }

        public string? FootprintLocation { get; private set; }

        public string? Model3DLocation { get; private set; }

        public string? LibraryFormat { get; private set; }

        public SettingsDialog()
        {
            InitializeComponent();
        }

        public SettingsDialog(string symbolPath, string footprintPath, string model3DPath, string format) : this()
        {
            SymbolPathBox.Text = symbolPath;
            FootprintPathBox.Text = footprintPath;
            Model3DPathBox.Text = model3DPath;
            foreach (var item in FormatBox.Items)
            {
                if (item is ComboBoxItem { Tag: string tag } && tag == format)
                {
                    FormatBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void Save_OnClick(object? sender, RoutedEventArgs e)
        {
            SymbolLocation = SymbolPathBox.Text;
            FootprintLocation = FootprintPathBox.Text;
            Model3DLocation = Model3DPathBox.Text;
            LibraryFormat = (FormatBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "elibz2";
            Close(true);
        }

        private void Cancel_OnClick(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private async void BrowseSymbol_OnClick(object? sender, RoutedEventArgs e)
            => await BrowseAsync(SymbolPathBox);

        private async void BrowseFootprint_OnClick(object? sender, RoutedEventArgs e)
            => await BrowseAsync(FootprintPathBox);

        private async void BrowseModel3D_OnClick(object? sender, RoutedEventArgs e)
            => await BrowseAsync(Model3DPathBox);

        private async Task BrowseAsync(TextBox pathBox)
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = "选择保存位置"
            });
            if (folders.Count > 0)
            {
                pathBox.Text = folders[0].Path.LocalPath;
            }
        }
    }
}

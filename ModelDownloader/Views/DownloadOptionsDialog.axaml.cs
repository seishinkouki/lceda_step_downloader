using Avalonia.Controls;
using Avalonia.Interactivity;
using ModelDownloader.ViewModels;

namespace ModelDownloader.Views
{
    /// <summary>
    /// 下载内容选择对话框：勾选要下载哪些内容（符号/封装/3D 模型），可多选。
    /// 确定返回 DownloadSelection，取消返回 null。
    /// </summary>
    public partial class DownloadOptionsDialog : Window
    {
        public DownloadOptionsDialog()
        {
            InitializeComponent();
        }

        /// <param name="hasSymbol">当前元件是否含有符号</param>
        /// <param name="hasFootprint">当前元件是否含有封装</param>
        /// <param name="hasModel">当前元件是否含有 3D 模型</param>
        public DownloadOptionsDialog(bool hasSymbol, bool hasFootprint, bool hasModel) : this()
        {
            Configure(CbSymbol, hasSymbol);
            Configure(CbFootprint, hasFootprint);
            Configure(CbModel, hasModel);
            OkButton.IsEnabled = hasSymbol || hasFootprint || hasModel;

            static void Configure(CheckBox checkBox, bool available)
            {
                checkBox.IsEnabled = available;
                if (!available)
                {
                    checkBox.IsChecked = false;
                }
            }
        }

        private void OkButton_OnClick(object? sender, RoutedEventArgs e)
        {
            Close(new DownloadSelection
            {
                Symbol = CbSymbol.IsChecked == true,
                Footprint = CbFootprint.IsChecked == true,
                Model3D = CbModel.IsChecked == true
            });
        }

        private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}

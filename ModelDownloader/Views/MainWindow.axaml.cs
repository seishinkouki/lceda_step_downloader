using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using ModelDownloader.Platform;
using ModelDownloader.ViewModels;

namespace ModelDownloader.Views
{
    public partial class MainWindow : Window
    {
        /// <summary>全局实例,供 ViewModel 弹出对话框时作为 owner 使用</summary>
        public static MainWindow? Instance { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Title = "立创商城 STEP 下载器";
                ExtendClientAreaToDecorationsHint = false;
                CanMaximize = true;
                TitleBarRightPanel.Margin = new Thickness(0);
            }
        }

        /// <summary>
        /// 粘贴兜底：Linux 下 Avalonia 框架剪贴板（X11Clipboard）在部分桌面环境
        /// （尤其纯 Wayland 会话）读不到内容且 TextBox 静默失败，表现为 Ctrl+V /
        /// 右键粘贴均无反应。这里在 Tunnel 阶段接管 Ctrl+V 与 Shift+Insert，
        /// 改走 ClipboardHelper 的"框架 -> wl-paste/xclip/xsel"兜底链。
        /// </summary>
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDownTunnel, RoutingStrategies.Tunnel);
        }

        private async void OnPreviewKeyDownTunnel(object? sender, KeyEventArgs e)
        {
            var isPaste =
                (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0) ||
                (e.Key == Key.Insert && (e.KeyModifiers & KeyModifiers.Shift) != 0);
            if (!isPaste) return;

            if (FocusManager?.GetFocusedElement() is not TextBox textBox) return;

            e.Handled = true;
            var topLevel = TopLevel.GetTopLevel(this);
            var text = await ClipboardHelper.GetTextWithFallbackAsync(topLevel);
            if (string.IsNullOrEmpty(text))
            {
                Trace.WriteLine("[Clipboard] 粘贴兜底：未能读取到剪贴板文本");
                return;
            }
            PasteText(textBox, text);
        }

        /// <summary>在 TextBox 光标处插入文本（有选区时先替换选区）。</summary>
        private static void PasteText(TextBox textBox, string text)
        {
            var oldText = textBox.Text ?? string.Empty;
            var start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
            var end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
            textBox.Text = string.Concat(oldText.AsSpan(0, start), text, oldText.AsSpan(end));
            var caret = start + text.Length;
            textBox.SelectionStart = caret;
            textBox.SelectionEnd = caret;
            textBox.CaretIndex = caret;
        }

        /// <summary>右键菜单"粘贴"：走兜底链读取剪贴板。</summary>
        private async void PasteMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var text = await ClipboardHelper.GetTextWithFallbackAsync(topLevel);
            if (string.IsNullOrEmpty(text))
            {
                Trace.WriteLine("[Clipboard] 右键粘贴：未能读取到剪贴板文本");
                return;
            }
            PasteText(SearchBox, text);
        }

        /// <summary>右键菜单"复制"。</summary>
        private void CopyMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            var selected = SearchBox.SelectedText;
            if (string.IsNullOrEmpty(selected)) return;
            TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(selected);
        }

        /// <summary>右键菜单"全选"。</summary>
        private void SelectAllMenuItem_OnClick(object? sender, RoutedEventArgs e) => SearchBox.SelectAll();

        /// <summary>搜索结果行右键菜单"复制元件编号"(Supplier Part,缺省回退 ProductCode)。</summary>
        private async void CopyPartNumberMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: Grid { DataContext: ResultItemViewModel item } } })
                return;

            var partNumber = item.PartNumber;
            if (string.IsNullOrEmpty(partNumber)) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null) return;
            await topLevel.Clipboard.SetTextAsync(partNumber);
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

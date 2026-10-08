using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelDownloader.Controls;
using ModelDownloader.Models;
using ModelDownloader.Services;
using ModelDownloader.Views;

namespace ModelDownloader.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private static HttpClient Client => new();
        [ObservableProperty] public partial ObservableCollection<string> SearchSource { get; set; } = [];
        [ObservableProperty] public partial string SelectedSearchSource { get; set; } = "立创商城";
        [ObservableProperty] public partial ModelSource? SelectedModelSource { get; set; } = null;

        /// <summary>当前选中元件缺少 3D 模型时为 true(UI 显示"暂未绘制"提示)</summary>
        [ObservableProperty]
        public partial bool IsModelMissing { get; set; } = false;

        /// <summary>记录当前预览请求的目标元件,异步返回后校验,防止快速切换选中项时旧请求污染新状态</summary>
        private ResultItem? _previewRequest;
        [ObservableProperty] public partial ObservableCollection<ResultItemViewModel> SearchResult { get; set; } = [];
        [ObservableProperty] public partial ResultItemViewModel? SelectedSearchResult { get; set; } = null;
        [ObservableProperty] public partial Bitmap? CurrentImageSource { get; set; } = null;

        private static readonly AppSettings LoadedSettings = AppSettings.Load();

        /// <summary>3D 模型（STEP）文件的保存目录（可在 设置 对话框中修改）</summary>
        [ObservableProperty]
        public partial string SaveLocation { get; set; } =
            LoadedSettings.SaveLocation is { Length: > 0 } saved
                ? saved
                : Path.Combine(AppContext.BaseDirectory, "step");

        /// <summary>符号文件的保存目录（可在 设置 对话框中修改）</summary>
        [ObservableProperty]
        public partial string SymbolLocation { get; set; } =
            LoadedSettings.SymbolLocation is { Length: > 0 } saved
                ? saved
                : Path.Combine(AppContext.BaseDirectory, "symbol");

        /// <summary>封装文件的保存目录（可在 设置 对话框中修改）</summary>
        [ObservableProperty]
        public partial string FootprintLocation { get; set; } =
            LoadedSettings.FootprintLocation is { Length: > 0 } saved
                ? saved
                : Path.Combine(AppContext.BaseDirectory, "footprint");

        /// <summary>符号/封装导出格式:elibz2 | kicad | pads(可在 设置 对话框中选择)</summary>
        [ObservableProperty]
        public partial string LibraryFormat { get; set; } =
            LoadedSettings.LibraryFormat is { Length: > 0 } f ? f : "elibz2";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBusy))]
        public partial bool IsImageLoading { get; set; } = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBusy))]
        public partial bool IsModelLoading { get; set; } = false;

        public bool IsBusy => IsImageLoading || IsModelLoading;

        public LruCache<string, Bitmap> ImageCache { get; } = new(50); // Store up to 50 preview images
        public LruCache<string, (byte[] ObjBytes, byte[] MtlBytes)> ModelCache { get; } = new(50); // Store up to 50 models

        [ObservableProperty]
        public partial ObservableCollection<DownloadTask> DownloadQueue { get; set; } = [];

        [ObservableProperty]
        public partial ObservableCollection<ResultItemViewModel> StagedDownloads { get; set; } = [];

        partial void OnSelectedSearchResultChanged(ResultItemViewModel? value)
        {
            if (SelectedSearchResult != null)
            {
                string? url = null;
                if (SelectedSearchResult.Model.Images?.Count == 0)
                {
                    url = SelectedSearchResult.Model.Creator?.Avatar;
                }
                else if (SelectedSearchResult.Model.Images?.Count > 0)
                {
                    url = SelectedSearchResult.Model.Images[0];
                }

                _ = LoadImageAsync(url);
                _ = PreviewModelAsync(SelectedSearchResult.Model);
            }
            else
            {
                CurrentImageSource = null;
                IsModelMissing = false;
            }
        }

        private async Task LoadImageAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                CurrentImageSource = null;
                return;
            }

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = "https:" + url;
            }

            if (ImageCache.TryGetValue(url, out var cachedBitmap))
            {
                CurrentImageSource = cachedBitmap;
                return;
            }

            try
            {
                IsImageLoading = true;
                using var networkStream = await Client.GetStreamAsync(url);
                using var ms = new MemoryStream();
                await networkStream.CopyToAsync(ms);
                ms.Position = 0;

                var bitmap = new Bitmap(ms);
                ImageCache.Set(url, bitmap);

                // Ensure we are assigning to the currently selected result's image
                if (SelectedSearchResult != null && (SelectedSearchResult.Model.Images?.Contains(url) == true || url.EndsWith(SelectedSearchResult.Model.Creator?.Avatar ?? "")))
                {
                    CurrentImageSource = bitmap;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                CurrentImageSource = null;
            }
            finally
            {
                IsImageLoading = false;
            }
        }

        [RelayCommand]
        public async Task SearchAsync(string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }
            try
            {
                if (SelectedSearchSource == "立创商城")
                {
                    var res = await Client.GetFromJsonAsync<LCSCResult>("https://pro.lceda.cn/api/szlcsc/eda/product/list?wd=" + keyword, CustomJsonSerializerContext.Default.LCSCResult);
                    if (res != null && res.Result != null)
                    {
                        SearchResult.Clear();
                        foreach (var item in res.Result)
                        {
                            var vm = new ResultItemViewModel(item);
                            // Preserve checked state if it's already in StagedDownloads
                            if (StagedDownloads.Any(s => s.Model.Uuid == item.Uuid))
                            {
                                vm.IsChecked = true;
                            }
                            SearchResult.Add(vm);
                        }
                    }
                }
                //else if (SelectedSearchSource == "嘉立创EDA")
                //{
                //    var res = await Client.GetFromJsonAsync<LCSCResult>("https://pro.lceda.cn/api/jlc/eda/product/list?wd=" + keyword);
                //    if (res != null && res.Result != null)
                //    {
                //        SearchResult = new(res.Result);
                //    }
                //}
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading 3D preview: {ex}");
                Debug.WriteLine(ex);
            }
        }
        private async Task PreviewModelAsync(ResultItem? item)
        {
            // 记录本次请求的目标,异步返回后校验,避免快速切换选中项时旧请求污染新状态
            _previewRequest = item;

            // 默认按"有模型"处理;确认缺失或加载失败时再置位提示
            IsModelMissing = false;

            if (item == null || item.Attributes == null || !item.Attributes.TryGetValue("3D Model", out var model3DUUID))
            {
                // 元件没有 3D Model 属性:该元件暂未绘制 3D 模型
                IsModelMissing = true;
                return;
            }

            try
            {
                IsModelLoading = true;

                if (ModelCache.TryGetValue(model3DUUID, out var cachedData))
                {
                    SelectedModelSource = ModelSource.FromStreams(
                        new MemoryStream(cachedData.ObjBytes),
                        new MemoryStream(cachedData.MtlBytes)
                    );
                    return;
                }

                var SelectedComponent = await Client.GetFromJsonAsync<Models.Model3DComponent>("https://pro.lceda.cn/api/v2/components/" + model3DUUID, CustomJsonSerializerContext.Default.Model3DComponent);
                var url = "https://modules.lceda.cn/3dmodel/" + SelectedComponent?.Result?.Model3DUuid + "?path=" + SelectedComponent?.Result?.Path;
                Stream? streamObj;
                if (SelectedComponent?.Code != 404)
                {
                    streamObj = await Client.GetStreamAsync(url);
                }
                else
                {
                    streamObj = await Client.GetStreamAsync($"https://modules.lceda.cn/3dmodel/{model3DUUID}");
                }
                
                var (objBytes, mtlBytes) = await ObjMtlSplitToBytes(streamObj);

                if (objBytes != null && mtlBytes != null)
                {
                    // 请求已过期(用户已切换到其他元件):丢弃结果
                    if (_previewRequest != item) return;

                    ModelCache.Set(model3DUUID, (objBytes, mtlBytes));
                    SelectedModelSource = ModelSource.FromStreams(
                        new MemoryStream(objBytes),
                        new MemoryStream(mtlBytes)
                    );
                }
                else
                {
                    // 拿到了流但内容不是有效的 OBJ/MTL
                    if (_previewRequest == item) IsModelMissing = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                // 模型下载失败(如接口 404):同样视为暂未绘制
                if (_previewRequest == item) IsModelMissing = true;
            }
            finally
            {
                IsModelLoading = false;
            }
        }

        private async Task<(byte[]? ObjBytes, byte[]? MtlBytes)> ObjMtlSplitToBytes(Stream objstream)
        {
            var objMs = new MemoryStream();
            var mtlMs = new MemoryStream();

            try
            {
                using (var objWriter = new StreamWriter(objMs, new UTF8Encoding(false), 1024, true))
                using (var mtlWriter = new StreamWriter(mtlMs, new UTF8Encoding(false), 1024, true))
                using (var sr = new StreamReader(objstream))
                {
                    var readline = string.Empty;
                    while ((readline = await sr.ReadLineAsync()) != null)
                    {
                        await objWriter.WriteLineAsync(readline);
                        if (readline.Contains("newmtl"))
                        {
                            await mtlWriter.WriteLineAsync(readline);
                            for (var i = 0; i < 3; i++)
                            {
                                readline = await sr.ReadLineAsync();
                                if (readline != null) await mtlWriter.WriteLineAsync(readline);
                            }
                            readline = await sr.ReadLineAsync();
                            readline = await sr.ReadLineAsync();
                            if (readline != null) await mtlWriter.WriteLineAsync(readline);
                        }
                    }

                    await mtlWriter.FlushAsync();
                    await objWriter.FlushAsync();
                }

                return (objMs.ToArray(), mtlMs.ToArray());
            }
            catch (Exception)
            {
                return (null, null);
            }
            finally
            {
                objMs.Dispose();
                mtlMs.Dispose();
            }
        }

        [RelayCommand]
        public async Task DownloadStep(ResultItemViewModel? item)
        {
            if (item == null) return;

            // 弹出下载内容选择对话框(符号/封装/3D 模型,可多选)
            var selection = await PromptDownloadSelectionAsync(
                SymbolUuid(item.Model) != null,
                FootprintUuid(item.Model) != null,
                Model3DUuid(item.Model) != null);
            if (selection == null || !selection.Any) return;

            await ExecuteDownloadAsync(item, selection);
        }

        /// <summary>按选择下载一个条目的符号/封装/3D 模型(选项中该元件没有的项自动跳过)。</summary>
        private async Task ExecuteDownloadAsync(ResultItemViewModel item, DownloadSelection selection)
        {
            var results = new List<bool>();

            // 按设置中的导出格式确定扩展名:elibz2 / kicad / pads
            var symbolExt = LibraryFormat switch { "kicad" => ".kicad_sym", "pads" => ".asc", _ => ".elibz2" };
            var footprintExt = LibraryFormat switch { "kicad" => ".kicad_mod", "pads" => ".asc", _ => ".elibz2" };

            if (selection.Symbol && SymbolUuid(item.Model) is { } symbolUuid)
            {
                results.Add(await TryDownloadAsync(item, "符号", async () =>
                {
                    var title = GetSafeFileName(item.Model.DisplayTitle ?? item.Model.Symbol?.DisplayTitle ?? symbolUuid);
                    await LcedaDocumentService.DownloadSymbolAsync(
                        symbolUuid, item.Model, Path.Combine(SymbolLocation, title + symbolExt), LibraryFormat);
                }));
            }

            if (selection.Footprint && FootprintUuid(item.Model) is { } footprintUuid)
            {
                results.Add(await TryDownloadAsync(item, "封装", async () =>
                {
                    var title = GetSafeFileName(item.Model.Footprint?.DisplayTitle ?? item.Model.Footprint?.Title ?? footprintUuid);
                    await LcedaDocumentService.DownloadFootprintAsync(
                        footprintUuid, Path.Combine(FootprintLocation, title + footprintExt), LibraryFormat);
                }));
            }

            if (selection.Model3D && Model3DUuid(item.Model) is { } modelUuid)
            {
                results.Add(await TryDownloadAsync(item, "3D 模型", () => DownloadStepAsync(item.Model, SaveLocation)));
            }

            // 全部下载成功才从"待下载"列表移除(部分失败保留,便于重试)
            if (results.Count > 0 && results.All(success => success))
            {
                RemoveStaged(item);
            }
        }

        /// <summary>执行单个下载动作,创建队列条目并跟踪状态;返回是否成功。</summary>
        private async Task<bool> TryDownloadAsync(ResultItemViewModel item, string kind, Func<Task> action)
        {
            var task = new DownloadTask
            {
                Title = $"{item.Model.DisplayTitle ?? "未知"} ({kind})",
                StatusText = "下载中..."
            };
            DownloadQueue.Add(task);

            try
            {
                await action();
                task.Progress = 100;
                task.StatusText = "完成";
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                // 状态栏显示截断的原因,便于定位(如接口变更、解密失败等)
                var reason = ex.Message.ReplaceLineEndings(" ");
                task.StatusText = reason.Length > 40 ? "错误: " + reason[..40] + "…" : "错误: " + reason;
                return false;
            }
        }

        /// <summary>弹出下载内容选择对话框;用户取消返回 null。</summary>
        private static async Task<DownloadSelection?> PromptDownloadSelectionAsync(
            bool hasSymbol, bool hasFootprint, bool hasModel)
        {
            var owner = MainWindow.Instance;
            if (owner == null) return null;

            var dialog = new DownloadOptionsDialog(hasSymbol, hasFootprint, hasModel);
            return await dialog.ShowDialog<DownloadSelection?>(owner);
        }

        private static string? SymbolUuid(ResultItem model) =>
            model.Attributes?.TryGetValue("Symbol", out var s) == true ? s : model.Symbol?.Uuid;

        private static string? FootprintUuid(ResultItem model) =>
            model.Attributes?.TryGetValue("Footprint", out var f) == true ? f : model.Footprint?.Uuid;

        private static string? Model3DUuid(ResultItem model) =>
            model.Attributes?.TryGetValue("3D Model", out var m) == true ? m : null;

        [RelayCommand]
        public void ToggleStage(ResultItemViewModel item)
        {
            // 搜索结果每次都会重建 ViewModel 实例,StagedDownloads 中可能持有
            // 旧实例,必须按 Uuid 匹配,否则取消勾选时移除不掉
            if (item.IsChecked && !StagedDownloads.Any(s => s.Model.Uuid == item.Model.Uuid))
            {
                StagedDownloads.Add(item);
            }
            else if (!item.IsChecked)
            {
                var staged = StagedDownloads.FirstOrDefault(s => s.Model.Uuid == item.Model.Uuid);
                if (staged != null)
                {
                    StagedDownloads.Remove(staged);
                }
            }
        }

        [RelayCommand]
        public void RemoveStaged(ResultItemViewModel item)
        {
            item.IsChecked = false;
            var staged = StagedDownloads.FirstOrDefault(s => s.Model.Uuid == item.Model.Uuid);
            if (staged != null)
            {
                StagedDownloads.Remove(staged);
            }
        }

        /// <summary>打开设置对话框:分别设置符号/封装/3D 模型保存位置,保存后持久化</summary>
        [RelayCommand]
        public async Task OpenSettingsAsync()
        {
            var owner = MainWindow.Instance;
            if (owner == null) return;

            var dialog = new SettingsDialog(SymbolLocation, FootprintLocation, SaveLocation, LibraryFormat);
            if (await dialog.ShowDialog<bool>(owner))
            {
                if (!string.IsNullOrWhiteSpace(dialog.SymbolLocation)) SymbolLocation = dialog.SymbolLocation;
                if (!string.IsNullOrWhiteSpace(dialog.FootprintLocation)) FootprintLocation = dialog.FootprintLocation;
                if (!string.IsNullOrWhiteSpace(dialog.Model3DLocation)) SaveLocation = dialog.Model3DLocation;
                if (!string.IsNullOrWhiteSpace(dialog.LibraryFormat)) LibraryFormat = dialog.LibraryFormat;

                new AppSettings
                {
                    SaveLocation = SaveLocation,
                    SymbolLocation = SymbolLocation,
                    FootprintLocation = FootprintLocation,
                    LibraryFormat = LibraryFormat
                }.Save();
            }
        }

        [RelayCommand]
        public async Task DownloadBatchAsync()
        {
            var itemsToDownload = StagedDownloads.ToList();
            if (itemsToDownload.Count == 0) return;

            // 批量下载只弹一次选择对话框,应用到所有暂存条目
            var selection = await PromptDownloadSelectionAsync(
                itemsToDownload.Any(i => SymbolUuid(i.Model) != null),
                itemsToDownload.Any(i => FootprintUuid(i.Model) != null),
                itemsToDownload.Any(i => Model3DUuid(i.Model) != null));
            if (selection == null || !selection.Any) return;

            StagedDownloads.Clear();

            foreach (var item in itemsToDownload)
            {
                // Uncheck them from search results UI if visible
                item.IsChecked = false;
                _ = ExecuteDownloadAsync(item, selection);
            }
        }

        private async Task DownloadStepAsync(ResultItem item, string targetFolder)
        {
            if (item == null || item.Attributes == null || !item.Attributes.TryGetValue("3D Model", out var model3DUUID)) return;
            var SelectedComponent = await Client.GetFromJsonAsync<Models.Model3DComponent>("https://pro.lceda.cn/api/v2/components/" + model3DUUID, CustomJsonSerializerContext.Default.Model3DComponent);
            Stream? streamStep;
            if (SelectedComponent == null || SelectedComponent.Result == null) 
            {
                streamStep = await Client.GetStreamAsync("https://modules.lceda.cn/qAxj6KHrDKw4blvCG8QJPs7Y/" + model3DUUID);
                if(streamStep == null)
                {
                    return;
                }
            }
            else
            {
                streamStep = await Client.GetStreamAsync("https://modules.lceda.cn/qAxj6KHrDKw4blvCG8QJPs7Y/" + SelectedComponent.Result.Model3DUuid);
            }

            Directory.CreateDirectory(targetFolder);
            var tempTitle = GetSafeFileName(item?.Footprint?.DisplayTitle);
            string fileToWriteTo = Path.Combine(targetFolder, tempTitle + ".step");
            using Stream streamToWriteTo = File.Open(fileToWriteTo, FileMode.Create);
            await streamStep.CopyToAsync(streamToWriteTo);
        }

        private static string GetSafeFileName(string? fileName)
        {
            return string.Join("_", fileName?.ToString().Split(Path.GetInvalidFileNameChars()) ?? []);
        }

        public MainWindowViewModel()
        {
            SearchSource.Add("立创商城");
            //SearchSource.Add("嘉立创EDA");
        }
    }
}

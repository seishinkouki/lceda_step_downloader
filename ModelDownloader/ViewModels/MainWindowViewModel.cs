using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelDownloader.Controls;
using ModelDownloader.Models;

namespace ModelDownloader.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private static HttpClient Client => new();
        [ObservableProperty] public partial ObservableCollection<string> SearchSource { get; set; } = [];
        [ObservableProperty] public partial string SelectedSearchSource { get; set; } = "立创商城";
        [ObservableProperty] public partial ModelSource? SelectedModelSource { get; set; } = null;
        [ObservableProperty] public partial ObservableCollection<ResultItemViewModel> SearchResult { get; set; } = [];
        [ObservableProperty] public partial ResultItemViewModel? SelectedSearchResult { get; set; } = null;
        [ObservableProperty] public partial Bitmap? CurrentImageSource { get; set; } = null;

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
                    var res = await Client.GetFromJsonAsync<LCSCResult>("https://pro.lceda.cn/api/szlcsc/eda/product/list?wd=" + keyword);
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
                Debug.WriteLine(ex);
            }
        }
        private async Task PreviewModelAsync(ResultItem? item)
        {
            if (item == null || item.Attributes == null || !item.Attributes.TryGetValue("3D Model", out var model3DUUID))
                return;

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

                var SelectedComponent = await Client.GetFromJsonAsync<Models.Model3DComponent>("https://pro.lceda.cn/api/v2/components/" + model3DUUID);
                var url = "https://modules.lceda.cn/3dmodel/" + SelectedComponent?.Result?.Model3DUuid + "?path=" + SelectedComponent?.Result?.Path;
                var streamObj = await Client.GetStreamAsync(url);
                var (objBytes, mtlBytes) = await ObjMtlSplitToBytes(streamObj);

                if (objBytes != null && mtlBytes != null)
                {
                    ModelCache.Set(model3DUUID, (objBytes, mtlBytes));
                    SelectedModelSource = ModelSource.FromStreams(
                        new MemoryStream(objBytes),
                        new MemoryStream(mtlBytes)
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
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
            
            var task = new DownloadTask { Title = item.Model.DisplayTitle ?? "未知", StatusText = "下载中..." };
            DownloadQueue.Add(task);
            
            try
            {
                await DownloadStepAsync(item.Model, Path.Combine(AppContext.BaseDirectory, "step"));
                task.Progress = 100;
                task.StatusText = "完成";
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                task.StatusText = "错误";
            }
        }

        [RelayCommand]
        public void ToggleStage(ResultItemViewModel item)
        {
            if (item.IsChecked && !StagedDownloads.Contains(item))
            {
                StagedDownloads.Add(item);
            }
            else if (!item.IsChecked && StagedDownloads.Contains(item))
            {
                StagedDownloads.Remove(item);
            }
        }

        [RelayCommand]
        public void RemoveStaged(ResultItemViewModel item)
        {
            item.IsChecked = false;
            StagedDownloads.Remove(item);
        }

        [RelayCommand]
        public void DownloadBatch()
        {
            var itemsToDownload = StagedDownloads.ToList();
            StagedDownloads.Clear();
            
            foreach (var item in itemsToDownload)
            {
                // Uncheck them from search results UI if visible
                item.IsChecked = false;
                _ = DownloadStep(item);
            }
        }

        private async Task DownloadStepAsync(ResultItem item, string targetFolder)
        {
            if (item == null || item.Attributes == null || !item.Attributes.TryGetValue("3D Model", out var model3DUUID)) return;
            var SelectedComponent = await Client.GetFromJsonAsync<Models.Model3DComponent>("https://pro.lceda.cn/api/v2/components/" + model3DUUID);
            if (SelectedComponent == null || SelectedComponent.Result == null) return;
            var streamStep = await Client.GetStreamAsync("https://modules.lceda.cn/qAxj6KHrDKw4blvCG8QJPs7Y/" + SelectedComponent.Result.Model3DUuid);

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

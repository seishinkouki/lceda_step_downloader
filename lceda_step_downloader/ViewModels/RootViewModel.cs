using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using HelixToolkit.Wpf;
using lceda_step_downloader.Models.Component;
using lceda_step_downloader.Models.Root;

namespace lceda_step_downloader.ViewModels
{
    public partial class RootViewModel : ObservableObject
    {
        private int _imageLoadVersion;
        private int _objLoadVersion;
        [ObservableProperty] private bool _downloadAllowed;
        [ObservableProperty] private ResultItem _selectedItem;
        [ObservableProperty] private Model3DGroup _myModelGroup;
        [ObservableProperty] private System.Windows.Media.ImageSource _imageSource;
        [ObservableProperty] private bool _isImageLoading;
        [ObservableProperty] private bool _isObjLoading;
        [ObservableProperty] private Root _searchResult;
        [ObservableProperty] private Component _selectedComponent;
        [ObservableProperty] private ObservableCollection<SearchSite> _searchSites;
        [ObservableProperty] private SearchSite _sSite;
        [ObservableProperty] private bool _automaticLoadObj;

        partial void OnAutomaticLoadObjChanged(bool value)
        {
            if (value && SelectedItem != null)
            {
                _ = DownloadObj();
            }
        }

        partial void OnSelectedItemChanged(ResultItem value)
        {
            OnResultSelection();
        }

        private static readonly HttpClient client = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip
        });

        public RootViewModel()
        {
            SelectedItem = null;
            SearchSites = new ObservableCollection<SearchSite>()
            {
                //new SearchSite(){Site="LCEDA", Value = 0},
                new(){Site="LCSC", Value = 1},
            };
            SSite = SearchSites[0];
            AutomaticLoadObj = false;

            EnsureModelDirectories();
        }

        [RelayCommand]
        public async Task DoSearch(string argument)
        {
            Debug.WriteLine(String.Format("搜索关键字: {0}", argument));
            await SearchAsync(argument);
        }

        private async Task SearchAsync(string argument)
        {
            if (SSite == null)
            {
                return;
            }

            try
            {
                if (SSite.Site == "LCSC")
                {
                    var streamTask = client.GetStreamAsync("https://pro.lceda.cn/api/szlcsc/eda/product/list?wd=" + Uri.EscapeDataString(argument ?? string.Empty));
                    Debug.WriteLine(streamTask.ToString());
                    SearchResult = await JsonSerializer.DeserializeAsync<Root>(await streamTask);
                    Debug.WriteLine(SearchResult?.result?.Count);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Growl.Warning("搜索失败");
            }
        }

        public void OnResultSelection()
        {
            if (SelectedItem == null)
            {
                return;
            }
            DownloadAllowed = true;

            //部分器件没有图片, 替换成商城LOGO
            if (SelectedItem.images.Count == 0)
            {
                LoadImageSource("https:" + SelectedItem.creator.avatar);
                return;
            }
            LoadImageSource(SelectedItem.images[0]);
            if (AutomaticLoadObj)
            {
                _ = DownloadObj();
            }
        }

        [RelayCommand]
        public async Task DownloadObj()
        {
            if (SelectedItem == null)
            {
                return;
            }
            var selectedItem = SelectedItem;
            var objLoadVersion = ++_objLoadVersion;
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsObjLoading = true;
                MyModelGroup = null;
            });

            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

            Debug.WriteLine("准备下载obj:编号{0},标题{1}", SearchResult.result.IndexOf(selectedItem), selectedItem.display_title);
            Debug.WriteLine(selectedItem.attributes._3D_Model);

            await DownloadObjAsync(selectedItem, objLoadVersion);
        }

        [RelayCommand]
        public async Task DownloadStep()
        {
            //https://pro.lceda.cn/api/components/9059586b8e0c4e2ba21b2ac2c1eb066b?uuid=9059586b8e0c4e2ba21b2ac2c1eb066b&path=0819f05c4eef4c71ace90d822a990e87
            //https://pro.lceda.cn/api/components/105b388c0c03439aa7dbf35dd2b762a6?uuid=105b388c0c03439aa7dbf35dd2b762a6
            //{"success":true,"code":0,"result":{"uuid":"105b388c0c03439aa7dbf35dd2b762a6","modifier":{"uuid":"0819f05c4eef4c71ace90d822a990e87","username":"LCSC","nickname":"LCSC","avatar":"\/\/image.lceda.cn\/avatars\/2018\/6\/kFlrasi7W06gTdBLAqW3fkrqbDhbowynuSzkjqso.png"},"creator":{"uuid":"0819f05c4eef4c71ace90d822a990e87","username":"LCSC","nickname":"LCSC","avatar":"\/\/image.lceda.cn\/avatars\/2018\/6\/kFlrasi7W06gTdBLAqW3fkrqbDhbowynuSzkjqso.png"},"owner":{"uuid":"0819f05c4eef4c71ace90d822a990e87","username":"LCSC","nickname":"LCSC","avatar":"\/\/image.lceda.cn\/avatars\/2018\/6\/kFlrasi7W06gTdBLAqW3fkrqbDhbowynuSzkjqso.png"},"description":"","docType":16,"dataStr":"{\"model\":\"6d30b5a04660477fbdff168686b01590\",\"type\":\"wrl\",\"src\":\"qfn-56_l7.0-w7.0-p0.40-tl-ep4.0\",\"unit\":\"mm\"}","tags":{"parent_tag":[],"child_tag":[]},"public":true,"source":"","version":1653017104,"type":3,"title":"qfn-56_l7.0-w7.0-p0.40-tl-ep4.0","createTime":1653017104,"updateTime":1658962217,"created_at":"2022-05-20 11:25:04","display_title":"QFN-56_L7.0-W7.0-P0.40-TL-EP4.0","updated_at":"2022-07-28 06:55:05","ticket":1,"std_uuid":"ce2b808f96c74d7981784d534cecd1c0","3d_model_uuid":"6d30b5a04660477fbdff168686b01590","has_device":false,"path":"0819f05c4eef4c71ace90d822a990e87"}}
            //https://modules.lceda.cn/qAxj6KHrDKw4blvCG8QJPs7Y/6d30b5a04660477fbdff168686b01590
            if (SelectedItem == null)
            {
                return;
            }
            var selectedItem = SelectedItem;

            Debug.WriteLine("准备下载step:编号{0},标题{1}", SearchResult.result.IndexOf(selectedItem), selectedItem.display_title);
            Debug.WriteLine(selectedItem.attributes._3D_Model_Transform);

            Directory.CreateDirectory(StepDirectory);
            var stepTitle = GetSafeFileName(selectedItem.footprint.display_title);
            var stepFile = Path.Combine(StepDirectory, stepTitle + ".step");
            if (File.Exists(stepFile))
            {
                Debug.WriteLine("存在step缓存");
                Growl.Info("STEP文件已存在");
                return;
            }
            DownloadAllowed = false;
            IsObjLoading = true;
            try
            {
                await DownloadStepAsync(selectedItem);
                Growl.Success("下载成功");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Growl.Warning("下载失败");
            }
            finally
            {
                DownloadAllowed = true;
                IsObjLoading = false;
            }
        }

        //构造PCB数据, 以利用lceda专业版的PCB导出STEP接口
        private async Task DownloadStepAsync(ResultItem selectedItem)
        {
            var streamTask = client.GetStreamAsync("https://pro.lceda.cn/api/components/" + selectedItem.attributes._3D_Model + "?uuid=" + selectedItem.attributes._3D_Model);

            SelectedComponent = await JsonSerializer.DeserializeAsync<Component>(await streamTask);
            if (SelectedComponent.code != 0)
            {
                SelectedComponent = new Component
                {
                    result = new Result()
                };
                SelectedComponent.result._3d_model_uuid = selectedItem.attributes._3D_Model;
            }
            Debug.WriteLine(SelectedComponent.result._3d_model_uuid);

            Stream streamStep = await client.GetStreamAsync("https://modules.lceda.cn/qAxj6KHrDKw4blvCG8QJPs7Y/" + SelectedComponent.result._3d_model_uuid);
            //器件名称
            //var tempTitle = string.Join("_", selectedItem.title.ToString().Split(Path.GetInvalidFileNameChars()));
            //封装名称
            Directory.CreateDirectory(StepDirectory);
            var tempTitle = GetSafeFileName(selectedItem.footprint.display_title);
            string fileToWriteTo = Path.Combine(StepDirectory, tempTitle + ".step");
            using Stream streamToWriteTo = File.Open(fileToWriteTo, FileMode.Create);
            await streamStep.CopyToAsync(streamToWriteTo);
            //MediaEle sr = new(await streamStep);
            //using Stream streamToWriteTo = File.Open(@".\step\" + selectedItem.title.ToString().Replace("/", "") + @".step", FileMode.Create);
            //await sr.CopyToAsync(streamToWriteTo);

            //StreamWriter stepWriter = new(@".\step\" + selectedItem.title.ToString().Replace("/", "") + @".step");

            //构造的PCB数据
            //var stringPayload =
            //    "[\"DOCTYPE\",\"PREVIEW\",\"1.0\"]\r\n" +
            //    "[\"HEAD\",{\"scale\":0.0254}]\r\n" +
            //    "[\"HEAD\",{\"scale\":10}]\r\n" +
            //    "[\"LAYER\",11,\"OUTLINE\",\"Board Outline Layer\",3,\"#c2c200\",1,\"#c2c200\",1]\r\n" +
            //    "[\"POLY\",\"e60\",0,\"\",11,1,[40,-40,\"L\",42,-40,42,-38,40,-38,40,-40],0]\r\n" +
            //    "[\"COMPONENT\",\"e0\",0,9,0,0,0,{\"uuid\":\"" + SelectedComponent.result._3d_model_uuid +
            //    "\",\"dx\":" + model_dx.ToString() +
            //    ",\"dy\":" + model_dy.ToString() +
            //    ",\"dz\":" + model_dz.ToString() +
            //    ",\"rz\":" + model_rz.ToString() +
            //    ",\"rx\":" + model_rx.ToString() +
            //    ",\"ry\":" + model_ry.ToString() +
            //    ",\"x\":" + model_x.ToString() +
            //    ",\"y\":" + model_y.ToString() +
            //    ",\"z\":" + model_z.ToString() +
            //    ",\"Footprint\":\"USB-C-SMD_TYPEC-303-ACP16\",\"Designator\":\"USB1\",\"Device\":\"TYPEC-303-ACP16\"},0]";

            //var compressedContent = CompressRequestContent(stringPayload);
            //compressedContent.Headers.Add("Content-Encoding", "gzip");
            //compressedContent.Headers.Add("Content-Type", "x-application/x-gzip");

            //var resp = await client.PostAsync("https://pro.lceda.cn/occapi/api/convert/pcb2step", compressedContent);

            //var responseStream = await resp.Content.ReadAsStreamAsync();

            //var streamReader = new StreamReader(responseStream);

            //StreamWriter stepWriter = new(@".\step\" + selectedItem.title.ToString().Replace("/", "") + @".step");

            //如果你上传的PCB数据里只有器件没有PCB, 或者PCB面积过小(<0.5*0.5mm), lc后台都会给你加上PCB
            //所以这里用了个凑活能用的方法:在程序里自动删掉PCB对应实体节点, 可能在某些软件里仍然会显示一个小小的PCB
            //实测AD Fusion 360 SW显示正常, 有更好的方法欢迎PR
            //string readline;
            //while ((readline = streamReader.ReadLine()) != null)
            //{
            //    if(readline.Contains("#29 = ADVANCED_BREP_SHAPE_REPRESENTATION('',(#11,#30)"))
            //    {
            //        stepWriter.WriteLine(readline.Replace("#30", "'NONE'"));
            //    }
            //    else
            //    {
            //        stepWriter.WriteLine(readline);
            //    }
            //}

            //stepWriter.Flush();
            //stepWriter.Close();
            //stepWriter.Dispose();
        }

        public static HttpContent CompressRequestContent(string content)
        {
            var compressedStream = new MemoryStream();
            using (var contentStream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                using var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress);
                contentStream.CopyTo(gzipStream);
            }

            var httpContent = new ByteArrayContent(compressedStream.ToArray());
            return httpContent;
        }

        private async Task DownloadObjAsync(ResultItem selectedItem, int objLoadVersion)
        {
            try
            {
                var streamTask = client.GetStreamAsync("https://pro.lceda.cn/api/components/" + selectedItem.attributes._3D_Model + "?uuid=" + selectedItem.attributes._3D_Model);

                SelectedComponent = await JsonSerializer.DeserializeAsync<Component>(await streamTask);
                if (SelectedComponent.code != 0)
                {
                    SelectedComponent = new Component
                    {
                        result = new Result
                        {
                            _3d_model_uuid = selectedItem.attributes._3D_Model
                        }
                    };
                }
                Debug.WriteLine(SelectedComponent.result._3d_model_uuid);

                var streamObj = await client.GetStreamAsync("https://modules.lceda.cn/3dmodel/" + SelectedComponent.result._3d_model_uuid);
                await ObjMtlSplit(streamObj, selectedItem, objLoadVersion);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Growl.Warning("模型加载失败");
                });
                CompleteObjLoad(objLoadVersion);
            }
        }

        //lc前端从服务端获取的OBJ模型数据实际上是把mtl和obj写在了一个文件里面, 然后前端再做处理展示, 这里同样需要做分离
        private async Task ObjMtlSplit(Stream objstream, ResultItem selectedItem, int objLoadVersion)
        {
            var tempTitle = GetSafeFileName(selectedItem.title);

            using (MemoryStream objMs = new MemoryStream())
            using (MemoryStream mtlMs = new MemoryStream())
            {
                using (StreamWriter objWriter = new StreamWriter(objMs, new UTF8Encoding(false), 1024, true))
                using (StreamWriter mtlWriter = new StreamWriter(mtlMs, new UTF8Encoding(false), 1024, true))
                using (StreamReader sr = new StreamReader(objstream))
                {
                    string readline = string.Empty;
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
                            readline = await sr.ReadLineAsync(); // discard
                            readline = await sr.ReadLineAsync();
                            if (readline != null) await mtlWriter.WriteLineAsync(readline);
                        }
                    }
                    await mtlWriter.FlushAsync();
                    await objWriter.FlushAsync();
                }

                objMs.Position = 0;
                mtlMs.Position = 0;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        ObjReader CurrentHelixObjReader = new ObjReader();
                        MyModelGroup = CurrentHelixObjReader.Read(objMs, new Stream[] { mtlMs });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                        Growl.Warning("模型加载失败");
                    }
                });
            }

            CompleteObjLoad(objLoadVersion);
        }

        private void LoadImageSource(string source)
        {
            var imageLoadVersion = ++_imageLoadVersion;
            IsImageLoading = true;
            ImageSource = null;

            var normalizedSource = NormalizeImageSource(source);
            if (string.IsNullOrWhiteSpace(normalizedSource))
            {
                CompleteImageLoad(imageLoadVersion);
                return;
            }

            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.DownloadCompleted += (_, _) => CompleteImageLoad(imageLoadVersion);
                bitmapImage.DownloadFailed += (_, _) => CompleteImageLoad(imageLoadVersion);
                bitmapImage.DecodeFailed += (_, _) => CompleteImageLoad(imageLoadVersion);
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(normalizedSource, UriKind.RelativeOrAbsolute);
                bitmapImage.CacheOption = BitmapCacheOption.Default;
                bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmapImage.EndInit();
                ImageSource = bitmapImage;

                if (!bitmapImage.IsDownloading)
                {
                    CompleteImageLoad(imageLoadVersion);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                CompleteImageLoad(imageLoadVersion);
            }
        }

        private static string NormalizeImageSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return source;
            }
            return source.StartsWith("//") ? "https:" + source : source;
        }

        private static string GetSafeFileName(string fileName)
        {
            return string.Join("_", fileName.ToString().Split(Path.GetInvalidFileNameChars()));
        }

        private static string StepDirectory => Path.Combine(AppContext.BaseDirectory, "step");

        private static void EnsureModelDirectories()
        {
            Directory.CreateDirectory(StepDirectory);
        }

        private void CompleteImageLoad(int imageLoadVersion)
        {
            if (imageLoadVersion != _imageLoadVersion)
            {
                return;
            }
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (imageLoadVersion == _imageLoadVersion)
                {
                    IsImageLoading = false;
                }
            });
        }

        private void CompleteObjLoad(int objLoadVersion)
        {
            if (objLoadVersion != _objLoadVersion)
            {
                return;
            }
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (objLoadVersion == _objLoadVersion)
                {
                    IsObjLoading = false;
                }
            });
        }
    }

    [ValueConversion(typeof(int), typeof(ListViewItem))]
    public class IndexConverter : IValueConverter
    {
        public object Convert(object value, Type TargetType, object parameter, CultureInfo culture)
        {
            ListViewItem item = (ListViewItem)value;
            ListView listView = ItemsControl.ItemsControlFromItemContainer(item) as ListView;
            return listView.ItemContainerGenerator.IndexFromContainer(item) + 1;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

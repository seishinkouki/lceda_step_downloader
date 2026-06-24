using lceda_step_downloader.Models.Root;
using lceda_step_downloader.Models.Component;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Diagnostics;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HelixToolkit.Wpf;
using System.Windows.Media.Media3D;
using System.Windows.Data;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using System.Windows;
using System.Text;
using System.IO.Compression;
using System.Net;
using System.Linq;
using HandyControl.Controls;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace lceda_step_downloader.ViewModels
{
    public partial class RootViewModel : ObservableObject
    {
        private int _imageLoadVersion;
        private int _objLoadVersion;
        [ObservableProperty] private string _title = "立创EDA 3D模型下载器";
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
              new SearchSite(){Site="LCSC", Value = 1},
            };
            SSite = SearchSites[0];
            AutomaticLoadObj = false;

            //创建模型存储目录
            if (!Directory.Exists(@".\temp"))
            {
                Directory.CreateDirectory(@".\temp");
            }
            if (!Directory.Exists(@".\step"))
            {
                Directory.CreateDirectory(@".\step");
            }
        }

        [RelayCommand]
        public void DoSearch(string argument)
        {
            Debug.WriteLine(String.Format("搜索关键字: {0}", argument));
            Task task = new(() => SearchTask(argument));
            task.Start();
        }

        public async void SearchTask(string argument)
        {
            if (SSite == null)
            {
                return;
            }
            if (SSite.Site == "LCSC")
            {
                var streamTask = client.GetStreamAsync("https://pro.lceda.cn/api/szlcsc/eda/product/list?wd=" + argument.ToString());
                Debug.WriteLine(streamTask.ToString());
                SearchResult = await JsonSerializer.DeserializeAsync<Root>(await streamTask);
                Debug.WriteLine(SearchResult.result.Count);
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

            var tempTitle = GetSafeFileName(selectedItem.title);
            var objFile = Path.Combine(AppContext.BaseDirectory, "temp", tempTitle + ".obj");
            if (File.Exists(objFile))
            {
                Debug.WriteLine("存在缓存");
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ObjReader CurrentHelixObjReader = new();
                        MyModelGroup = CurrentHelixObjReader.Read(objFile);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    Growl.Warning("模型加载失败");
                }
                finally
                {
                    CompleteObjLoad(objLoadVersion);
                }
                return;
            }
            _ = Task.Run(() => DownloadObjAsync(selectedItem, objLoadVersion));
        }
        
        [RelayCommand]
        public void DownloadStep()
        {
            //https://pro.lceda.cn/api/components/9059586b8e0c4e2ba21b2ac2c1eb066b?uuid=9059586b8e0c4e2ba21b2ac2c1eb066b&path=0819f05c4eef4c71ace90d822a990e87
            //https://pro.lceda.cn/api/components/105b388c0c03439aa7dbf35dd2b762a6?uuid=105b388c0c03439aa7dbf35dd2b762a6
            //{"success":true,"code":0,"result":{"uuid":"105b388c0c03439aa7dbf35dd2b762a6","modifier":{"uuid":"0819f05c4eef4c71ace90d822a990e87","username":"LCSC","nickname":"LCSC","avatar":"\/\/image.lceda.cn\/avatars\/2018\/6\/kFlrasi7W06gTdBLAqW3fkrqbDhbowynuSzkjqso.png"},"creator":{"uuid":"0819f05c4eef4c71ace90d822a990e87","username":"LCSC","nickname":"LCSC","avatar":"\/\/image.lceda.cn\/avatars\/2018\/6\/kFlrasi7W06gTdBLAqW3fkrqbDhbowynuSzkjqso.png"},"owner":{"uuid":"0819f05c4eef4c71ace90d822a990e87","username":"LCSC","nickname":"LCSC","avatar":"\/\/image.lceda.cn\/avatars\/2018\/6\/kFlrasi7W06gTdBLAqW3fkrqbDhbowynuSzkjqso.png"},"description":"","docType":16,"dataStr":"{\"model\":\"6d30b5a04660477fbdff168686b01590\",\"type\":\"wrl\",\"src\":\"qfn-56_l7.0-w7.0-p0.40-tl-ep4.0\",\"unit\":\"mm\"}","tags":{"parent_tag":[],"child_tag":[]},"public":true,"source":"","version":1653017104,"type":3,"title":"qfn-56_l7.0-w7.0-p0.40-tl-ep4.0","createTime":1653017104,"updateTime":1658962217,"created_at":"2022-05-20 11:25:04","display_title":"QFN-56_L7.0-W7.0-P0.40-TL-EP4.0","updated_at":"2022-07-28 06:55:05","ticket":1,"std_uuid":"ce2b808f96c74d7981784d534cecd1c0","3d_model_uuid":"6d30b5a04660477fbdff168686b01590","has_device":false,"path":"0819f05c4eef4c71ace90d822a990e87"}}
            //https://modules.lceda.cn/qAxj6KHrDKw4blvCG8QJPs7Y/6d30b5a04660477fbdff168686b01590
            if (SelectedItem == null)
            {
                return;
            }

            Debug.WriteLine("准备下载step:编号{0},标题{1}", SearchResult.result.IndexOf(SelectedItem), SelectedItem.display_title);
            Debug.WriteLine(SelectedItem.attributes._3D_Model_Transform);

            //器件名称
            //if (File.Exists(@".\step\" + SelectedItem.title.ToString().Replace("/", "") + @".step"))
            //封装名称
            if (File.Exists(@".\step\" + SelectedItem.footprint.display_title.ToString().Replace("/", "") + @".step"))
            {
                Debug.WriteLine("存在step缓存");
                Growl.Info("STEP文件已存在");
                return;
            }
            DownloadAllowed = false;
            IsObjLoading = true;
            Task.Run(() => DownloadStepAsync());
        }

        //构造PCB数据, 以利用lceda专业版的PCB导出STEP接口
        public async void DownloadStepAsync()
        {
            var streamTask = client.GetStreamAsync("https://pro.lceda.cn/api/components/" + SelectedItem.attributes._3D_Model + "?uuid=" + SelectedItem.attributes._3D_Model);

            SelectedComponent = await JsonSerializer.DeserializeAsync<Component>(await streamTask);
            if (SelectedComponent.code != 0)
            {
                SelectedComponent = new Component
                {
                    result = new Result()
                };
                SelectedComponent.result._3d_model_uuid = SelectedItem.attributes._3D_Model;
            }
            Debug.WriteLine(SelectedComponent.result._3d_model_uuid);

            Stream streamStep = await client.GetStreamAsync("https://modules.lceda.cn/qAxj6KHrDKw4blvCG8QJPs7Y/" + SelectedComponent.result._3d_model_uuid);
            //器件名称
            //var tempTitle = string.Join("_", SelectedItem.title.ToString().Split(Path.GetInvalidFileNameChars()));
            //封装名称
            var tempTitle = string.Join("_", SelectedItem.footprint.display_title.ToString().ToString().Split(Path.GetInvalidFileNameChars()));
            string fileToWriteTo = Path.Combine(AppContext.BaseDirectory, "step", tempTitle + ".step");
            using Stream streamToWriteTo = File.Open(fileToWriteTo, FileMode.Create);
            await streamStep.CopyToAsync(streamToWriteTo);
            //MediaEle sr = new(await streamStep);
            //using Stream streamToWriteTo = File.Open(@".\step\" + SelectedItem.title.ToString().Replace("/", "") + @".step", FileMode.Create);
            //await sr.CopyToAsync(streamToWriteTo);

            //StreamWriter stepWriter = new(@".\step\" + SelectedItem.title.ToString().Replace("/", "") + @".step");

            //器件模型的变换数据, 以适应lceda的坐标系以及比例, 参数由lc后台维护, 可见lceda的模型也不全是自己画的
            var model_dx = Convert.ToDouble(SelectedItem.attributes._3D_Model_Transform.Split(',')[0]) / 10.0;
            var model_dy = Convert.ToDouble(SelectedItem.attributes._3D_Model_Transform.Split(',')[1]) / 10.0;
            var model_dz = Convert.ToDouble(SelectedItem.attributes._3D_Model_Transform.Split(',')[2]) / 10.0;
            var model_rz = Convert.ToInt32(SelectedItem.attributes._3D_Model_Transform.Split(',')[3]);
            var model_rx = Convert.ToInt32(SelectedItem.attributes._3D_Model_Transform.Split(',')[4]);
            var model_ry = Convert.ToInt32(SelectedItem.attributes._3D_Model_Transform.Split(',')[5]);
            var model_x = Convert.ToDouble(SelectedItem.attributes._3D_Model_Transform.Split(',')[6]) / 10.0;
            var model_y = Convert.ToDouble(SelectedItem.attributes._3D_Model_Transform.Split(',')[7]) / 10.0;
            var model_z = Convert.ToDouble(SelectedItem.attributes._3D_Model_Transform.Split(',')[8]) / 10.0 - 49;

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

            //StreamWriter stepWriter = new(@".\step\" + SelectedItem.title.ToString().Replace("/", "") + @".step");

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
            DownloadAllowed = true;
            IsObjLoading = false;
            Growl.Success("下载成功");
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
            var objFile = Path.Combine(AppContext.BaseDirectory, "temp", tempTitle + ".obj");
            var mtlFile = Path.Combine(AppContext.BaseDirectory, "temp", tempTitle + ".mtl");
            var objDownloadFile = objFile + ".download";
            var mtlDownloadFile = mtlFile + ".download";

            using (StreamWriter objWriter = new(objDownloadFile))
            using (StreamWriter mtlWriter = new(mtlDownloadFile))
            using (StreamReader sr = new(objstream))
            {
                await objWriter.WriteLineAsync("mtllib " + tempTitle + ".mtl");
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
                            await mtlWriter.WriteLineAsync(readline);
                        }
                        readline = await sr.ReadLineAsync();
                        readline = await sr.ReadLineAsync();
                        await mtlWriter.WriteLineAsync(readline);
                    }
                }
                await mtlWriter.FlushAsync();
                await objWriter.FlushAsync();
            }

            File.Move(mtlDownloadFile, mtlFile, true);
            File.Move(objDownloadFile, objFile, true);

            Application.Current.Dispatcher.Invoke(() =>
            {
                ObjReader CurrentHelixObjReader = new();
                MyModelGroup = CurrentHelixObjReader.Read(objFile);
            });
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

    [ValueConversion(typeof(Int32), typeof(ListViewItem))]
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

    public class BooleanOrConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return !values.OfType<bool>().Any((b => b == false));

        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

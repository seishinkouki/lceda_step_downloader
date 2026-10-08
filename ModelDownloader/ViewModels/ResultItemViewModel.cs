using CommunityToolkit.Mvvm.ComponentModel;
using ModelDownloader.Models;

namespace ModelDownloader.ViewModels;

public partial class ResultItemViewModel : ObservableObject
{
    public ResultItem Model { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = false;

    /// <summary>立创元件编号(Supplier Part,缺失时回退 ProductCode)。</summary>
    public string? PartNumber =>
        Model.Attributes?.TryGetValue("Supplier Part", out var p) == true && !string.IsNullOrEmpty(p)
            ? p
            : Model.ProductCode;

    public ResultItemViewModel(ResultItem model)
    {
        Model = model;
    }
}

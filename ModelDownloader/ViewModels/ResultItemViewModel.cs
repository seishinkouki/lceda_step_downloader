using CommunityToolkit.Mvvm.ComponentModel;
using ModelDownloader.Models;

namespace ModelDownloader.ViewModels;

public partial class ResultItemViewModel : ObservableObject
{
    public ResultItem Model { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = false;

    public ResultItemViewModel(ResultItem model)
    {
        Model = model;
    }
}

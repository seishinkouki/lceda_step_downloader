using CommunityToolkit.Mvvm.ComponentModel;

namespace ModelDownloader.ViewModels;

public partial class DownloadTask : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Progress { get; set; } = 0;

    // e.g. "Queued", "Downloading...", "Success", "Error"
    [ObservableProperty]
    public partial string StatusText { get; set; } = "Queued";
}

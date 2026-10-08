namespace ModelDownloader.ViewModels;

/// <summary>下载内容选择结果（由 DownloadOptionsDialog 返回）。</summary>
public sealed class DownloadSelection
{
    public bool Symbol { get; init; }

    public bool Footprint { get; init; }

    public bool Model3D { get; init; }

    /// <summary>是否至少选择了其中一项</summary>
    public bool Any => Symbol || Footprint || Model3D;
}

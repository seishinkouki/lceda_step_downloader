using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModelDownloader.Models;

/// <summary>
/// .elibz2 压缩包内 device2.json / footprint2.json 的数据模型。
/// 结构由官方导出样本逆向得到:
/// {
///   "devices":   { "<uuid>": DeviceMetaEntry },   // 符号包: 1 项(device);封装包: 空
///   "symbols":   { "<uuid>": LibMetaEntry },       // 符号包: 1 项;封装包: 空
///   "footprints":{ "<uuid>": LibMetaEntry },       // 符号包: 关联封装(可能为空);封装包: 1 项
///   "panelLibs": {}
/// }
/// </summary>
public sealed class Elibz2Meta
{
    [JsonPropertyName("devices")]
    public Dictionary<string, DeviceMetaEntry> Devices { get; set; } = [];

    [JsonPropertyName("symbols")]
    public Dictionary<string, LibMetaEntry> Symbols { get; set; } = [];

    [JsonPropertyName("footprints")]
    public Dictionary<string, LibMetaEntry> Footprints { get; set; } = [];

    [JsonPropertyName("panelLibs")]
    public Dictionary<string, object> PanelLibs { get; set; } = [];
}

/// <summary>device 元数据条目(device2.json 的 devices 节点)。</summary>
public sealed class DeviceMetaEntry
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; set; }

    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }

    [JsonPropertyName("ticket")]
    public int Ticket { get; set; }

    [JsonPropertyName("updateTime")]
    public long UpdateTime { get; set; }

    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; set; }

    [JsonPropertyName("creator")]
    public UserInfo? Creator { get; set; }

    [JsonPropertyName("modifier")]
    public UserInfo? Modifier { get; set; }

    [JsonPropertyName("owner")]
    public UserInfo? Owner { get; set; }

    [JsonPropertyName("symbol_type")]
    public int SymbolType { get; set; }

    /// <summary>由搜索结果条目构造 device 元数据(字段一一对应)。</summary>
    public static DeviceMetaEntry From(ResultItem device) => new()
    {
        Uuid = device.Uuid,
        Path = device.Creator?.Uuid,
        Attributes = device.Attributes,
        Images = device.Images,
        Ticket = device.Ticket,
        UpdateTime = device.UpdateTime,
        CreateTime = device.CreateTime,
        Title = device.Title,
        Description = device.Description,
        DisplayTitle = device.DisplayTitle,
        Creator = device.Creator,
        Modifier = device.Modifier,
        Owner = device.Owner,
        SymbolType = device.SymbolType
    };
}

/// <summary>符号/封装元数据条目(symbols / footprints 节点)。</summary>
public sealed class LibMetaEntry
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("ticket")]
    public int Ticket { get; set; }

    [JsonPropertyName("updateTime")]
    public long UpdateTime { get; set; }

    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; set; }

    [JsonPropertyName("creator")]
    public UserInfo? Creator { get; set; }

    [JsonPropertyName("modifier")]
    public UserInfo? Modifier { get; set; }

    [JsonPropertyName("owner")]
    public UserInfo? Owner { get; set; }

    [JsonPropertyName("docType")]
    public int DocType { get; set; }

    /// <summary>由库文档详情接口响应构造符号/封装元数据。</summary>
    public static LibMetaEntry From(Model3DDetail detail) => new()
    {
        Uuid = detail.Uuid,
        Path = detail.Path,
        Ticket = detail.Ticket,
        UpdateTime = detail.UpdateTime,
        CreateTime = detail.CreateTime,
        Title = detail.Title,
        Description = detail.Description,
        DisplayTitle = detail.DisplayTitle,
        Creator = detail.Creator,
        Modifier = detail.Modifier,
        Owner = detail.Owner,
        DocType = detail.DocType
    };
}

/// <summary>elibz2 元数据的 JSON 序列化上下文(source-gen,兼容 NativeAOT/trimming)。
/// 输出带缩进、忽略 null 字段,与官方导出样本一致。</summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Elibz2Meta))]
public sealed partial class Elibz2JsonContext : JsonSerializerContext
{
}

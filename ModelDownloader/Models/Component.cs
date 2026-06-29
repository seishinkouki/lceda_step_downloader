using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ModelDownloader.Models;

// 立创商城搜索结果
public class LCSCResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("result")]
    public List<ResultItem>? Result { get; set; }
}

// 通用用户信息
public class UserInfo
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

// 标签信息
public class TagInfo
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("name_cn")]
    public string? NameCn { get; set; }
}

// 主标签和子标签容器
public class TagsContainer
{
    [JsonPropertyName("parent_tag")]
    public TagInfo? ParentTag { get; set; }

    [JsonPropertyName("child_tag")]
    public TagInfo? ChildTag { get; set; }
}

// 符号信息
public class SymbolInfo
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; set; }
}

// 封装信息
public class FootprintInfo
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; set; }
}


// 结果项（用于第一个JSON的result数组）
public class ResultItem
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("owner")]
    public UserInfo? Owner { get; set; }

    [JsonPropertyName("creator")]
    public UserInfo? Creator { get; set; }

    [JsonPropertyName("modifier")]
    public UserInfo? Modifier { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("tags")]
    public TagsContainer? Tags { get; set; }

    [JsonPropertyName("images")]
    public List<string>? Images { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("project_uuid")]
    public string? ProjectUuid { get; set; }

    [JsonPropertyName("footprint_type")]
    public int FootprintType { get; set; }

    [JsonPropertyName("symbol_type")]
    public int SymbolType { get; set; }

    [JsonPropertyName("product_code")]
    public string? ProductCode { get; set; }

    [JsonPropertyName("updateTime")]
    public long UpdateTime { get; set; }

    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }

    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("ticket")]
    public int Ticket { get; set; }

    [JsonPropertyName("symbol")]
    public SymbolInfo? Symbol { get; set; }

    [JsonPropertyName("footprint")]
    public FootprintInfo? Footprint { get; set; }
}

public class Model3DComponent
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("result")]
    public Model3DDetail? Result { get; set; }
}

// 3D模型详情
public class Model3DDetail
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("modifier")]
    public UserInfo? Modifier { get; set; }

    [JsonPropertyName("creator")]
    public UserInfo? Creator { get; set; }

    [JsonPropertyName("owner")]
    public UserInfo? Owner { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("docType")]
    public int DocType { get; set; }

    [JsonPropertyName("dataStr")]
    public string? DataStr { get; set; }

    [JsonPropertyName("tags")]
    public Model3DTags? Tags { get; set; }

    [JsonPropertyName("public")]
    public bool Public { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("createTime")]
    public long CreateTime { get; set; }

    [JsonPropertyName("updateTime")]
    public long UpdateTime { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("ticket")]
    public int Ticket { get; set; }

    [JsonPropertyName("std_uuid")]
    public string? StdUuid { get; set; }

    [JsonPropertyName("3d_model_uuid")]
    public string? Model3DUuid { get; set; }

    [JsonPropertyName("has_device")]
    public bool HasDevice { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

// 3D模型的标签（parent_tag和child_tag都是数组）
public class Model3DTags
{
    [JsonPropertyName("parent_tag")]
    public List<object>? ParentTag { get; set; } // 空数组

    [JsonPropertyName("child_tag")]
    public List<object>? ChildTag { get; set; } // 空数组
}

// DataStr的解析类（可选，如果需要解析dataStr字段）
public class DataStrContent
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("src")]
    public string? Src { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }
}
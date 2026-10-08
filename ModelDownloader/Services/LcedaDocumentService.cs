using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ModelDownloader.Models;

namespace ModelDownloader.Services;

/// <summary>
/// 立创EDA库文档（符号 / 封装）下载服务,输出 .elibz2 压缩包(zip)。
/// 数据链路（实测）:
///   1. GET https://pro.lceda.cn/api/v2/components/{uuid}
///      → result.dataStrId（存储地址）+ result.iv（12 字节 hex）+ result.key（32 字节 hex）
///   2. GET dataStrId → AES-256-GCM 密文（尾部 16 字节为 auth tag）
///   3. AES-GCM 解密 → gzip 解压 → 标准格式文档(每行一条 ["TYPE", ...] 指令)
///   4. ElibuConverter 转换为专业版新版 elibu 格式(与官方导出一致)
/// 包结构（由官方导出样本逆向）:
///   符号包: <元件名>.elibu + device2.json(devices/symbols/footprints 元数据)
///   封装包: <封装名>.elibu + footprint2.json(仅 footprints 元数据)
/// </summary>
public static class LcedaDocumentService
{
    private const int TagSize = 16;

    private static readonly HttpClient Client = new();

    /// <summary>下载符号并按所选格式输出(elibz2 / .kicad_sym / .asc)。</summary>
    /// <param name="symbolUuid">符号 uuid(搜索结果 attributes["Symbol"])</param>
    /// <param name="device">搜索结果条目,提供 device 元数据(attributes/images 等)</param>
    /// <param name="targetFile">输出文件完整路径(扩展名已按格式给定)</param>
    /// <param name="format">elibz2 | kicad | pads</param>
    public static async Task DownloadSymbolAsync(
        string symbolUuid, ResultItem device, string targetFile, string format)
    {
        var (docText, meta) = await FetchDocumentAsync(symbolUuid);

        var name = Path.GetFileNameWithoutExtension(targetFile);
        var designator = device.Attributes?.TryGetValue("Designator", out var d) == true ? d : null;
        var value = device.Attributes?.TryGetValue("Value", out var v) == true ? v : meta.DisplayTitle ?? name;

        if (format == "kicad")
        {
            await WriteTextAsync(targetFile, KicadConverter.ConvertSymbol(docText, name, value, designator));
            return;
        }
        if (format == "pads")
        {
            await WriteTextAsync(targetFile, PadsConverter.ConvertSymbol(docText, name, designator));
            return;
        }

        // elibz2(默认):新版 elibu + device2.json
        var elibu = ElibuConverter.ConvertSymbol(
            docText, symbolUuid, meta.DisplayTitle ?? meta.Title ?? name, meta.Description);

        var libMeta = new Elibz2Meta();
        if (!string.IsNullOrEmpty(device.Uuid))
        {
            libMeta.Devices[device.Uuid] = DeviceMetaEntry.From(device);
        }
        libMeta.Symbols[symbolUuid] = LibMetaEntry.From(meta);

        // device2.json 的 footprints 节点需包含 device 关联的封装元数据
        var footprintUuid = device.Attributes?.TryGetValue("Footprint", out var f) == true ? f : device.Footprint?.Uuid;
        if (!string.IsNullOrEmpty(footprintUuid))
        {
            try
            {
                var (_, footprintMeta) = await FetchDocumentAsync(footprintUuid);
                libMeta.Footprints[footprintUuid] = LibMetaEntry.From(footprintMeta);
            }
            catch (Exception ex)
            {
                // 关联封装元数据获取失败不阻塞符号导出
                Debug.WriteLine(ex);
            }
        }

        await WriteElibz2Async(targetFile, "device2.json", elibu, libMeta);
    }

    /// <summary>下载封装并按所选格式输出(elibz2 / .kicad_mod / .asc)。</summary>
    /// <param name="footprintUuid">封装 uuid(搜索结果 attributes["Footprint"])</param>
    /// <param name="targetFile">输出文件完整路径(扩展名已按格式给定)</param>
    /// <param name="format">elibz2 | kicad | pads</param>
    public static async Task DownloadFootprintAsync(
        string footprintUuid, string targetFile, string format)
    {
        var (docText, meta) = await FetchDocumentAsync(footprintUuid);

        var name = Path.GetFileNameWithoutExtension(targetFile);

        if (format == "kicad")
        {
            await WriteTextAsync(targetFile,
                KicadConverter.ConvertFootprint(docText, name, meta.Description, null));
            return;
        }
        if (format == "pads")
        {
            await WriteTextAsync(targetFile, PadsConverter.ConvertFootprint(docText, name, null));
            return;
        }

        // elibz2(默认):新版 elibu + footprint2.json
        var elibu = ElibuConverter.ConvertFootprint(
            docText, footprintUuid, meta.DisplayTitle ?? meta.Title ?? name, meta.Description);

        var libMeta = new Elibz2Meta
        {
            Footprints = { [footprintUuid] = LibMetaEntry.From(meta) }
        };

        await WriteElibz2Async(targetFile, "footprint2.json", elibu, libMeta);
    }

    /// <summary>下载并解密指定 uuid 的库文档,返回标准格式文本与其详情元数据。</summary>
    private static async Task<(string Text, Model3DDetail Meta)> FetchDocumentAsync(string uuid)
    {
        var response = await Client.GetFromJsonAsync<Model3DComponent>(
            "https://pro.lceda.cn/api/v2/components/" + uuid,
            CustomJsonSerializerContext.Default.Model3DComponent)
            ?? throw new InvalidOperationException("组件详情接口无响应");

        var detail = response.Result
            ?? throw new InvalidOperationException("组件详情接口返回空结果: " + uuid);

        if (string.IsNullOrEmpty(detail.DataStrId)
            || string.IsNullOrEmpty(detail.Iv)
            || string.IsNullOrEmpty(detail.Key))
        {
            throw new InvalidOperationException("该文档缺少数据内容（dataStrId/iv/key 为空）");
        }

        var cipher = await Client.GetByteArrayAsync(detail.DataStrId);
        if (cipher.Length <= TagSize)
        {
            throw new InvalidDataException("文档数据异常（长度不足）");
        }

        // AES-256-GCM 解密:密文体 + 尾部 16 字节 auth tag
        var plain = new byte[cipher.Length - TagSize];
        using (var aes = new AesGcm(Convert.FromHexString(detail.Key), TagSize))
        {
            aes.Decrypt(
                Convert.FromHexString(detail.Iv),
                cipher.AsSpan(0, plain.Length),
                cipher.AsSpan(plain.Length),
                plain);
        }

        // 明文为 gzip 压缩的文本文档
        using var gz = new GZipStream(new MemoryStream(plain), CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var text = await reader.ReadToEndAsync();

        return (text, detail);
    }

    /// <summary>写入文本文件(UTF-8 无 BOM)。</summary>
    private static async Task WriteTextAsync(string targetFile, string text)
    {
        var directory = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(targetFile, text, new UTF8Encoding(false));
    }

    /// <summary>写入 .elibz2 压缩包:<名>.elibu(文档) + 固定名 json(元数据),与官方导出一致。</summary>
    private static async Task WriteElibz2Async(string targetFile, string jsonFileName, string elibuText, Elibz2Meta meta)
    {
        var directory = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var zip = ZipFile.Open(targetFile, ZipArchiveMode.Create);

        var elibuEntry = zip.CreateEntry(Path.GetFileNameWithoutExtension(targetFile) + ".elibu");
        await using (var stream = elibuEntry.Open())
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(elibuText);
        }

        var jsonEntry = zip.CreateEntry(jsonFileName);
        await using (var stream = jsonEntry.Open())
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(JsonSerializer.Serialize(meta, Elibz2JsonContext.Default.Elibz2Meta));
        }
    }
}

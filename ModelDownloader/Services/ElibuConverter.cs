using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ModelDownloader.Services;

/// <summary>
/// 将接口下发的标准格式文档(每行一条 ["TYPE", ...] JSON 指令,version 0.13.x)
/// 转换为专业版编辑器导出的新版 elibu 格式:
///   {"type":"TYPE","ticket":N,"id":"...","client":"..."}||{content json}|
/// 字段映射由"同一元件的接口数据 vs 官方导出 elibz2"逐行对拍得出;坐标单位均为 mil,直接透传。
/// 输出段结构与官方一致:DOCHEAD / META / DOCHEAD / LAYER / ACTIVE_LAYER / CANVAS / 实体 / NET+PRIMITIVE(封装)。
/// </summary>
public static class ElibuConverter
{
    // 行头 client 占位(16 位 hex,与样本形态一致)
    private const string Client = "6c7363646c64722e";

    private const string EditVersion = "4.1.60";

    /// <summary>转换封装文档(docType 4)为新版 elibu 文本。</summary>
    public static string ConvertFootprint(string oldDoc, string uuid, string title, string? description)
        => Convert(oldDoc, uuid, title, description, "FOOTPRINT", 4, isSymbol: false);

    /// <summary>转换符号文档(docType 2)为新版 elibu 文本。</summary>
    public static string ConvertSymbol(string oldDoc, string uuid, string title, string? description)
        => Convert(oldDoc, uuid, title, description, "SYMBOL", 2, isSymbol: true);

    private static string Convert(
        string oldDoc, string uuid, string title, string? description,
        string docTypeName, int docTypeCode, bool isSymbol)
    {
        // ── 阶段 1:解析旧格式指令 ──
        var instructions = new List<JsonElement>();
        foreach (var raw in oldDoc.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || !line.StartsWith('[')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    continue;
                instructions.Add(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                // 跳过无法解析的行
            }
        }

        // ── 阶段 2:按官方段结构输出 ──
        var nowMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var sb = new StringBuilder(64 * 1024);
        long ticket = 1;
        int zIndex = 0;
        string? partTitle = null;
        var placeholderSeen = new HashSet<string>();

        // 段头:DOCHEAD / META / DOCHEAD
        var docHeadContent =
            "{\"docType\":\"" + docTypeName + "\",\"client\":\"" + Client +
            "\",\"uuid\":\"" + uuid + "\",\"updateTime\":" + nowMs +
            ",\"version\":" + nowMs + ",\"editVersion\":\"" + EditVersion + "\",\"user\":{}}";
        sb.Append("{\"type\":\"DOCHEAD\"}||").Append(docHeadContent).Append("|\n");

        sb.Append("{\"type\":\"META\",\"ticket\":1,\"id\":\"META\",\"client\":\"").Append(Client)
          .Append("\"}||{\"title\":").Append(Esc(title))
          .Append(",\"description\":").Append(Esc(description))
          .Append(",\"tags\":[],\"source\":\"\"}|\n");

        sb.Append("{\"type\":\"DOCHEAD\"}||").Append(docHeadContent).Append("|\n");

        // LAYER 表(保持原顺序)
        foreach (var a in instructions)
        {
            if (S(a[0]) != "LAYER") continue;
            ticket++;
            var id = "[\"LAYER\"," + Raw(a[1]) + "]";
            AppendLine(sb, "LAYER", ticket, id,
                "{\"layerType\":" + Esc(S(a[2])) +
                ",\"layerName\":" + Esc(S(a[3])) +
                ",\"use\":true,\"show\":true,\"locked\":false" +
                ",\"activeColor\":" + Esc(S(a[5])) +
                ",\"activateTransparency\":" + Raw(a[6]) +
                ",\"inactiveColor\":" + Esc(S(a[7])) +
                ",\"inactiveTransparency\":" + Raw(a[8]) + "}");
        }

        // ACTIVE_LAYER(取首条)+ CANVAS(取首条),输出于实体之前(官方位置)
        foreach (var a in instructions)
        {
            if (S(a[0]) == "ACTIVE_LAYER")
            {
                ticket++;
                AppendLine(sb, "ACTIVE_LAYER", ticket, "ACTIVE_LAYER",
                    "{\"layerId\":" + Raw(a[1]) + "}");
                break;
            }
        }

        foreach (var a in instructions)
        {
            if (S(a[0]) != "CANVAS") continue;
            ticket++;
            // 符号首段 CANVAS 只有 origin 两个字段(与官方样本一致)
            var content = isSymbol
                ? "{\"originX\":" + Raw(a[1]) + ",\"originY\":" + Raw(a[2]) + "}"
                : "{\"originX\":" + Raw(a[1]) + ",\"originY\":" + Raw(a[2]) +
                  ",\"unit\":" + Esc(S(a[3])) +
                  ",\"gridXSize\":" + Raw(a[4]) + ",\"gridYSize\":" + Raw(a[5]) +
                  ",\"snapXSize\":" + Snap(Raw(a[6])) + ",\"snapYSize\":" + Snap(Raw(a[7])) +
                  ",\"gridType\":\"NONE\",\"multiGridType\":\"NONE\"" +
                  ",\"highlightValue\":0.5,\"layerBrightness\":\"NORMAL\"}";
            AppendLine(sb, "CANVAS", ticket, "CANVAS", content);
            break;
        }

        // 实体(保持旧顺序)
        foreach (var a in instructions)
        {
            switch (S(a[0]))
            {
                case "FILL":
                {
                    zIndex++; ticket++;
                    EmitPlaceholder(sb, isSymbol, ref ticket, placeholderSeen, "FILL", zIndex);
                    AppendLine(sb, "FILL", ticket, S(a[1]),
                        "{\"groupId\":" + Raw(a[2]) +
                        ",\"netName\":" + Esc(S(a[3])) +
                        ",\"layerId\":" + Raw(a[4]) +
                        ",\"width\":" + Raw(a[5]) +
                        ",\"fillStyle\":\"SOLID\"" +
                        ",\"path\":" + Raw(a[7]) +
                        ",\"locked\":false,\"zIndex\":" + zIndex +
                        ",\"isBridgingCopper\":false,\"networkList\":[],\"refs\":[]}");
                    break;
                }

                case "POLY":
                {
                    zIndex++; ticket++;
                    EmitPlaceholder(sb, isSymbol, ref ticket, placeholderSeen, "POLY", zIndex);
                    AppendLine(sb, "POLY", ticket, S(a[1]),
                        "{\"groupId\":" + Raw(a[2]) +
                        ",\"netName\":" + Esc(S(a[3])) +
                        ",\"layerId\":" + Raw(a[4]) +
                        ",\"width\":" + Raw(a[5]) +
                        ",\"path\":" + Raw(a[6]) +
                        ",\"locked\":false,\"zIndex\":" + zIndex +
                        ",\"polyType\":\"NORMAL\"}");
                    break;
                }

                case "PAD" when !isSymbol:
                {
                    zIndex++; ticket++;
                    EmitPlaceholder(sb, isSymbol, ref ticket, placeholderSeen, "PAD", zIndex);
                    AppendLine(sb, "PAD", ticket, S(a[1]),
                        "{\"groupId\":" + Raw(a[2]) +
                        ",\"netName\":" + Esc(S(a[3])) +
                        ",\"layerId\":" + Raw(a[4]) +
                        ",\"num\":" + Raw(a[5]) +
                        ",\"centerX\":" + Raw(a[6]) +
                        ",\"centerY\":" + Raw(a[7]) +
                        ",\"padAngle\":" + Raw(a[8]) +
                        ",\"hole\":" + HoleJson(a[9]) +
                        ",\"defaultPad\":" + PadShapeJson(a[10]) +
                        ",\"specialPad\":" + Raw(a[11]) +
                        ",\"padOffsetX\":" + Raw(a[12]) +
                        ",\"padOffsetY\":" + Raw(a[13]) +
                        ",\"relativeAngle\":" + Raw(a[14]) +
                        ",\"plated\":true,\"padType\":\"NORMAL\"" +
                        ",\"topSolderExpansion\":null,\"bottomSolderExpansion\":null" +
                        ",\"topPasteExpansion\":null,\"bottomPasteExpansion\":null" +
                        ",\"locked\":false,\"zIndex\":" + zIndex +
                        ",\"connectMode\":null,\"spokeSpace\":null,\"spokeWidth\":null,\"spokeAngle\":null,\"padLen\":0}");
                    break;
                }

                case "ATTR" when !isSymbol:
                {
                    zIndex++; ticket++;
                    EmitPlaceholder(sb, isSymbol, ref ticket, placeholderSeen, "ATTR", zIndex);
                    AppendLine(sb, "ATTR", ticket, S(a[1]),
                        "{\"groupId\":" + Raw(a[2]) +
                        ",\"parentId\":" + Esc(S(a[3])) +
                        ",\"layerId\":" + Raw(a[4]) +
                        ",\"x\":" + Raw(a[5]) +
                        ",\"y\":" + Raw(a[6]) +
                        ",\"key\":" + Esc(S(a[7])) +
                        ",\"value\":" + Esc(S(a[8])) +
                        ",\"keyVisible\":" + Raw(a[9]) +
                        ",\"valueVisible\":" + Raw(a[10]) +
                        ",\"fontFamily\":" + Esc(S(a[11])) +
                        ",\"fontSize\":" + Raw(a[12]) +
                        ",\"strokeWidth\":" + Raw(a[13]) +
                        ",\"bold\":" + Raw(a[14]) +
                        ",\"italic\":" + Raw(a[15]) +
                        ",\"origin\":\"LEFT_BOTTOM\"" +
                        ",\"angle\":" + Raw(a[17]) +
                        ",\"reverse\":" + Raw(a[18]) +
                        ",\"expansion\":" + Raw(a[19]) +
                        ",\"mirror\":" + Raw(a[20]) +
                        ",\"locked\":false,\"zIndex\":" + zIndex + "}");
                    break;
                }

                case "ATTR" when isSymbol:
                {
                    zIndex++; ticket++;
                    var parentId = S(a[2]);
                    AppendLine(sb, "ATTR", ticket, S(a[1]),
                        "{\"partId\":" + Esc(parentId.Length > 0 ? parentId : partTitle ?? "") +
                        ",\"groupId\":\"\",\"locked\":false,\"zIndex\":" + zIndex +
                        ",\"parentId\":" + Esc(parentId) +
                        ",\"key\":" + Esc(S(a[3])) +
                        ",\"value\":" + Esc(S(a[4])) +
                        ",\"keyVisible\":" + Raw(a[5]) +
                        ",\"valueVisible\":" + Raw(a[6]) +
                        ",\"x\":" + Raw(a[7]) +
                        ",\"y\":" + Raw(a[8]) +
                        ",\"rotation\":" + Raw(a[9]) +
                        ",\"color\":null,\"fillColor\":null" +
                        ",\"fontFamily\":null,\"fontSize\":null" +
                        ",\"strikeout\":false,\"underline\":false,\"italic\":false,\"fontWeight\":false" +
                        ",\"align\":\"LEFT_BOTTOM\",\"version\":\"2.0\"}");
                    break;
                }

                case "RECT" when isSymbol:
                {
                    zIndex++; ticket++;
                    EmitPlaceholder(sb, isSymbol, ref ticket, placeholderSeen, "RECT", zIndex);
                    AppendLine(sb, "RECT", ticket, S(a[1]),
                        "{\"partId\":" + Esc(partTitle ?? "") +
                        ",\"groupId\":\"\",\"locked\":false,\"zIndex\":" + zIndex +
                        ",\"dotX1\":" + Raw(a[2]) +
                        ",\"dotY1\":" + Raw(a[3]) +
                        ",\"dotX2\":" + Raw(a[4]) +
                        ",\"dotY2\":" + Raw(a[5]) +
                        ",\"radiusX\":0,\"radiusY\":0,\"rotation\":0" +
                        ",\"strokeColor\":null,\"strokeStyle\":null,\"fillColor\":null" +
                        ",\"strokeWidth\":null,\"fillStyle\":null}");
                    break;
                }

                case "PIN" when isSymbol:
                {
                    zIndex++; ticket++;
                    EmitPlaceholder(sb, isSymbol, ref ticket, placeholderSeen, "PIN", zIndex);
                    AppendLine(sb, "PIN", ticket, S(a[1]),
                        "{\"partId\":" + Esc(partTitle ?? "") +
                        ",\"groupId\":\"\",\"locked\":false,\"zIndex\":" + zIndex +
                        ",\"display\":" + (S(a[2]) == "0" ? "false" : "true") +
                        ",\"x\":" + Raw(a[4]) +
                        ",\"y\":" + Raw(a[5]) +
                        ",\"length\":" + Raw(a[6]) +
                        ",\"rotation\":" + Raw(a[7]) +
                        ",\"color\":null,\"pinShape\":\"NONE\"}");
                    break;
                }

                case "PART" when isSymbol:
                {
                    ticket++;
                    partTitle = S(a[1]);
                    var bbox = a.GetArrayLength() > 2 ? Raw(a[2]) : "{}";
                    AppendLine(sb, "PART", ticket, S(a[1]),
                        "{\"BBOX\":" + ExtractObj(bbox, "BBOX") + ",\"title\":" + Esc(S(a[1])) + "}");
                    break;
                }
            }
        }

        // ── 尾部样板:NET + PRIMITIVE(与官方导出样本逐字段一致) ──
        if (!isSymbol)
        {
            ticket++;
            sb.Append("{\"type\":\"NET\",\"ticket\":").Append(ticket)
              .Append(",\"id\":\"[\\\"NET\\\",\\\"\\\"]\",\"client\":\"").Append(Client)
              .Append("\"}||{\"netType\":null,\"specialColor\":null,\"retLine\":true,\"differentialName\":null,\"isPositiveNet\":false,\"equalLengthGroupName\":null}|\n");

            (string Id, string Content)[] primitives =
            [
                ("ALL", "{\"layerId\":0,\"display\":true,\"color\":null}"),
                ("DRC", "{\"layerId\":0,\"color\":\"#FFCC00\"}"),
                ("PROHIBITEDREGION", "{\"layerId\":0,\"color\":\"#9966FF\"}"),
                ("BOARDSHAPE", "{\"layerId\":0,\"color\":\"#6D6A69\"}"),
                ("PARTITION", "{\"layerId\":0,\"color\":\"#C0C0C0\"}"),
                ("CURSOR", "{\"display\":true,\"color\":\"#00ffff\"}"),
                ("BACKGROUND", "{\"display\":true,\"color\":\"#000000\"}"),
                ("GRID", "{\"display\":true,\"color\":\"#ffffff\"}"),
                ("BOLDGRID", "{\"display\":true,\"color\":\"#ffffff\"}"),
                ("COORDINATEAXIS", "{\"display\":true,\"color\":\"#ffffff\"}"),
                ("SELECTBOX", "{\"display\":true,\"color\":\"#00ffff\"}")
            ];
            foreach (var (id, content) in primitives)
            {
                ticket++;
                AppendLine(sb, "PRIMITIVE", ticket, "[\"PRIMITIVE\",\"" + id + "\"]", content);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 输出 ELE_PLACEHOLDER(每类实体的首个之前一条,max = 该类型首个实体的 zIndex)。
    /// 仅封装文档有(官方样本符号段未见)。
    /// </summary>
    private static void EmitPlaceholder(
        StringBuilder sb, bool isSymbol, ref long ticket, HashSet<string> seen, string dataType, int z)
    {
        if (isSymbol || !seen.Add(dataType)) return;
        ticket++;
        AppendLine(sb, "ELE_PLACEHOLDER", ticket, "placeholder" + seen.Count,
            "{\"dataType\":\"" + dataType + "\",\"max\":" + z + "}");
    }

    /// <summary>输出一行:{"type":..,"ticket":..,"id":..,"client":..}||{content}|</summary>
    private static void AppendLine(StringBuilder sb, string type, long ticket, string id, string content)
    {
        sb.Append("{\"type\":\"").Append(type).Append("\",\"ticket\":").Append(ticket)
          .Append(",\"id\":").Append(Esc(id))
          .Append(",\"client\":\"").Append(Client).Append("\"}||")
          .Append(content).Append("|\n");
    }

    /// <summary>PAD 孔洞:旧 null → null;["ROUND",w,h] → {"holeType":..,"width":..,"height":..}</summary>
    private static string HoleJson(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Null) return "null";
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 3)
        {
            return "{\"holeType\":" + Esc(S(e[0])) +
                   ",\"width\":" + Raw(e[1]) +
                   ",\"height\":" + Raw(e[2]) + "}";
        }
        return "null";
    }

    /// <summary>PAD 焊盘形状:["RECT",w,h,r] → {"padType":"RECT","width":w,"height":h}</summary>
    private static string PadShapeJson(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 3) return "null";
        var type = S(e[0]);
        if (type == "CIRCLE" || type == "ELLIPSE")
        {
            // 圆形:直径
            var d = e.GetArrayLength() > 2 ? Raw(e[1]) : "1";
            return "{\"padType\":\"ELLIPSE\",\"width\":" + d + ",\"height\":" + d + "}";
        }
        return "{\"padType\":" + Esc(type) +
               ",\"width\":" + Raw(e[1]) +
               ",\"height\":" + Raw(e[2]) + "}";
    }

    /// <summary>从旧 PART 行第三参(BBOX 对象文本)中提取 BBOX 数组原文。</summary>
    private static string ExtractObj(string objText, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(objText);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(key, out var v))
            {
                return v.GetRawText();
            }
        }
        catch (JsonException)
        {
        }
        return "[]";
    }

    /// <summary>字符串 → 带引号 JSON(手工转义,trim/AOT 安全)。</summary>
    private static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>取 JSON 元素的字符串值(非字符串时返回原文)。</summary>
    private static string S(JsonElement e)
        => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText();

    /// <summary>取 JSON 元素原文(数字/数组/对象/null 等,保精度)。</summary>
    private static string Raw(JsonElement e) => e.GetRawText();

    /// <summary>snap 值:0/缺失时回退默认 0.5,避免 0 导致网格异常。</summary>
    private static string Snap(string raw)
        => raw is "0" or "0.0" ? "0.5" : raw;
}

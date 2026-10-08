using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ModelDownloader.Services;

/// <summary>
/// 将标准格式文档(每行一条 ["TYPE", ...] 指令)转换为 KiCad 原生格式:
///   封装 → .kicad_mod(坐标 mil → mm ×0.0254,Y 轴与 KiCad 一致)
///   符号 → .kicad_sym(坐标 10mil → mm ×0.254,Y 轴取反)
/// </summary>
public static class KicadConverter
{
    private const double MilToMm = 0.0254;
    private const double SymUnitToMm = 0.254;

    // ─────────────────────────── 封装 ───────────────────────────

    /// <summary>封装文档 → .kicad_mod 文本。</summary>
    public static string ConvertFootprint(string oldDoc, string name, string? description, string? designator)
    {
        var body = new StringBuilder(16 * 1024);
        string? refText = designator;
        string? valueText = null;
        bool hasThru = false;

        foreach (var a in Parse(oldDoc))
        {
            switch (S(a[0]))
            {
                case "PAD":
                {
                    var layerFlag = S(a[4]);
                    var number = S(a[5]);
                    var x = N(a[6]) * MilToMm;
                    var y = N(a[7]) * MilToMm;
                    var rot = N(a[14]);
                    var hole = a[9];
                    var shape = a[10];

                    var tht = hole.ValueKind == JsonValueKind.Array && hole.GetArrayLength() > 0;
                    if (layerFlag is "12" or "3") tht = true;
                    hasThru |= tht;

                    var (kShape, w, h) = PadShape(shape);
                    if (w <= 0) w = h = 1.6;

                    string kType, layers;
                    if (tht)
                    {
                        kType = "thru_hole";
                        layers = "\"*.Cu\" \"*.Mask\"";
                        if (kShape == "rect") kShape = "oval";
                    }
                    else if (layerFlag == "2")
                    {
                        kType = "smd";
                        layers = "\"B.Cu\" \"B.Paste\" \"B.Mask\"";
                    }
                    else
                    {
                        kType = "smd";
                        layers = "\"F.Cu\" \"F.Paste\" \"F.Mask\"";
                    }

                    var rotPart = Math.Abs(rot) > 0.001 ? " " + Fmt(rot) : "";
                    var drill = tht ? HoleDrill(hole) : "";
                    body.Append("  (pad ").Append(Esc(number)).Append(' ').Append(kType).Append(' ')
                        .Append(kShape)
                        .Append(" (at ").Append(Fmt(x)).Append(' ').Append(Fmt(y)).Append(rotPart).Append(')')
                        .Append(" (size ").Append(Fmt(Math.Abs(w))).Append(' ').Append(Fmt(Math.Abs(h))).Append(')')
                        .Append(drill)
                        .Append(" (layers ").Append(layers).Append("))\n");
                    break;
                }

                case "POLY":
                {
                    var layer = LayerMap((int)N(a[4]));
                    var width = Math.Max(N(a[5]) * MilToMm, 0.05);
                    AppendSegments(body, a[6], layer, width);
                    break;
                }

                case "FILL":
                {
                    var layer = (int)N(a[4]);
                    if (layer == 49) break; // 3D 标记层
                    var kLayer = layer switch
                    {
                        5 => "F.Mask", 6 => "B.Mask",
                        7 => "F.Paste", 8 => "B.Paste",
                        _ => "F.Fab"
                    };
                    AppendFills(body, a[7], kLayer);
                    break;
                }

                case "ATTR":
                {
                    var key = S(a[7]);
                    var value = S(a[8]);
                    if (key == "Designator") refText = value;
                    else if (key == "Footprint") valueText = value;
                    break;
                }
            }
        }

        var head = new StringBuilder(2 * 1024);
        head.Append("(footprint ").Append(Esc(name)).Append(" (layer \"F.Cu\")\n");
        if (!string.IsNullOrEmpty(description))
            head.Append("  (descr ").Append(Esc(OneLine(description))).Append(")\n");
        head.Append("  (attr ").Append(hasThru ? "through_hole" : "smd").Append(")\n");
        head.Append("  (fp_text reference ").Append(Esc(refText ?? "REF**"))
            .Append(" (at 0 -2.4 unlocked) (layer \"F.SilkS\")")
            .Append(" (effects (font (size 1 1) (thickness 0.15))))\n");
        head.Append("  (fp_text value ").Append(Esc(valueText ?? name))
            .Append(" (at 0 2.4 unlocked) (layer \"F.Fab\")")
            .Append(" (effects (font (size 1 1) (thickness 0.15))))\n");

        return head.Append(body).Append(")\n").ToString();
    }

    // ─────────────────────────── 符号 ───────────────────────────

    /// <summary>符号文档 → .kicad_sym 文本。</summary>
    public static string ConvertSymbol(string oldDoc, string name, string? value, string? designator)
    {
        var body = new StringBuilder(16 * 1024);
        var pins = new StringBuilder(4 * 1024);
        string? partTitle = null;

        // 当前 PIN 的附属信息(id → name/number/type)
        var pinNames = new Dictionary<string, string>();
        var pinNumbers = new Dictionary<string, string>();
        var pinTypes = new Dictionary<string, string>();

        foreach (var a in Parse(oldDoc))
        {
            switch (S(a[0]))
            {
                case "PART":
                    partTitle ??= S(a[1]);
                    break;

                case "RECT":
                {
                    var x1 = N(a[2]) * SymUnitToMm;
                    var y1 = -N(a[3]) * SymUnitToMm;
                    var x2 = N(a[4]) * SymUnitToMm;
                    var y2 = -N(a[5]) * SymUnitToMm;
                    body.Append("    (rectangle (start ").Append(Fmt(x1)).Append(' ').Append(Fmt(y1))
                        .Append(") (end ").Append(Fmt(x2)).Append(' ').Append(Fmt(y2))
                        .Append(") (stroke (width 0.254) (type default)) (fill (type none)))\n");
                    break;
                }

                case "CIRCLE":
                {
                    var cx = N(a[2]) * SymUnitToMm;
                    var cy = -N(a[3]) * SymUnitToMm;
                    var r = N(a[4]) * SymUnitToMm;
                    body.Append("    (circle (center ").Append(Fmt(cx)).Append(' ').Append(Fmt(cy))
                        .Append(") (end ").Append(Fmt(cx + r)).Append(' ').Append(Fmt(cy))
                        .Append(") (stroke (width 0.254) (type default)) (fill (type none)))\n");
                    break;
                }

                case "PIN":
                {
                    var id = S(a[1]);
                    var x = N(a[4]) * SymUnitToMm;
                    var y = -N(a[5]) * SymUnitToMm;
                    var len = N(a[6]) * SymUnitToMm;
                    var rot = N(a[7]);
                    var etype = PinType(pinTypes.GetValueOrDefault(id));
                    pinNames.TryGetValue(id, out var pn);
                    pinNumbers.TryGetValue(id, out var pnum);
                    pins.Append("    (pin ").Append(etype).Append(" line (at ")
                        .Append(Fmt(x)).Append(' ').Append(Fmt(y)).Append(' ').Append(Fmt(rot))
                        .Append(") (length ").Append(Fmt(len)).Append(')')
                        .Append(" (name ").Append(Esc(string.IsNullOrEmpty(pn) ? "~" : pn)).Append(')')
                        .Append(" (number ").Append(Esc(string.IsNullOrEmpty(pnum) ? "~" : pnum)).Append("))\n");
                    break;
                }

                case "ATTR":
                {
                    var parentId = S(a[2]);
                    var key = S(a[3]);
                    var val = S(a[4]);
                    if (parentId.Length == 0)
                    {
                        if (key == "Designator") designator = val;
                        else if (key == "Symbol") value = val;
                    }
                    else if (key == "NAME") pinNames[parentId] = val;
                    else if (key == "NUMBER") pinNumbers[parentId] = val;
                    else if (key == "Pin Type") pinTypes[parentId] = val;
                    break;
                }
            }
        }

        var head = new StringBuilder(2 * 1024);
        head.Append("(kicad_symbol_lib\n");
        head.Append("\t(version 20241209)\n");
        head.Append("\t(generator \"lcsc_model_downloader\")\n");
        head.Append("\t(symbol ").Append(Esc(name)).Append('\n');
        head.Append("\t\t(in_bom yes)\n");
        head.Append("\t\t(on_board yes)\n");
        head.Append("\t\t(property \"Reference\" ").Append(Esc(designator ?? "U"))
            .Append(" (at 0 3.81 0) (effects (font (size 1.27 1.27))))\n");
        head.Append("\t\t(property \"Value\" ").Append(Esc(value ?? name))
            .Append(" (at 0 -3.81 0) (effects (font (size 1.27 1.27))))\n");
        head.Append("\t\t(property \"Footprint\" \"\" (at 0 0 0) (effects (font (size 1.27 1.27)) hide yes))\n");
        head.Append("\t\t(property \"Datasheet\" \"\" (at 0 0 0) (effects (font (size 1.27 1.27)) hide yes))\n");
        head.Append("\t\t(symbol ").Append(Esc(name + "_0_1")).Append('\n');

        return head.Append(body).Append(pins).Append("\t\t)\n\t)\n)\n").ToString();
    }

    // ─────────────────────────── 辅助 ───────────────────────────

    private static IEnumerable<JsonElement> Parse(string oldDoc)
    {
        foreach (var raw in oldDoc.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || !line.StartsWith('[')) continue;
            JsonElement? element = null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    element = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
            }
            if (element != null) yield return element.Value;
        }
    }

    /// <summary>旧 PAD 形状 → (KiCad 形状, 宽mm, 高mm)。</summary>
    private static (string Shape, double W, double H) PadShape(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 2)
            return ("rect", 0, 0);
        var type = S(e[0]);
        double a = e.GetArrayLength() > 1 ? N(e[1]) * MilToMm : 0;
        double b = e.GetArrayLength() > 2 ? N(e[2]) * MilToMm : a;
        return type switch
        {
            "RECT" => ("rect", a, b),
            "OVAL" or "ELLIPSE" => Math.Abs(a - b) < 0.0001 ? ("circle", a, a) : ("oval", a, b),
            "CIRCLE" => ("circle", a, a),
            _ => ("rect", a, b)
        };
    }

    /// <summary>THT 孔 → KiCad drill 子句。</summary>
    private static string HoleDrill(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 2)
            return " (drill 1)";
        var w = Math.Abs(N(e[1])) * MilToMm;
        var h = e.GetArrayLength() > 2 ? Math.Abs(N(e[2])) * MilToMm : w;
        return Math.Abs(w - h) < 0.0001
            ? " (drill " + Fmt(w) + ")"
            : " (drill (offset 0 0) (oval " + Fmt(w) + " " + Fmt(h) + "))";
    }

    /// <summary>POLY/FILL 的扁平点集 → 逐段 fp_line。</summary>
    private static void AppendSegments(StringBuilder sb, JsonElement pts, string layer, double width)
    {
        if (pts.ValueKind != JsonValueKind.Array || pts.GetArrayLength() < 4) return;

        // 扁平数组 [x1,y1,"L",x2,y2,...]:收集全部坐标点,相邻成段
        var px = new List<double>();
        var py = new List<double>();
        for (var i = 0; i + 1 < pts.GetArrayLength(); i++)
        {
            if (pts[i].ValueKind == JsonValueKind.Number)
            {
                px.Add(N(pts[i]));
                py.Add(N(pts[i + 1]));
                i++;
            }
        }

        for (var i = 0; i + 1 < px.Count; i++)
        {
            sb.Append("  (fp_line (start ").Append(Fmt(px[i] * MilToMm)).Append(' ').Append(Fmt(py[i] * MilToMm))
                .Append(") (end ").Append(Fmt(px[i + 1] * MilToMm)).Append(' ').Append(Fmt(py[i + 1] * MilToMm))
                .Append(") (stroke (width ").Append(Fmt(width)).Append(") (type solid)) (layer \"")
                .Append(layer).Append("\"))\n");
        }
    }

    /// <summary>FILL path → fp_poly / fp_circle(实心填充)。</summary>
    private static void AppendFills(StringBuilder sb, JsonElement path, string layer)
    {
        if (path.ValueKind != JsonValueKind.Array) return;
        foreach (var shape in path.EnumerateArray())
        {
            if (shape.ValueKind != JsonValueKind.Array || shape.GetArrayLength() == 0) continue;

            if (S(shape[0]) == "CIRCLE" && shape.GetArrayLength() >= 3)
            {
                var cx = N(shape[1]) * MilToMm;
                var cy = N(shape[2]) * MilToMm;
                var r = shape.GetArrayLength() > 3 ? N(shape[3]) * MilToMm : 0.5;
                sb.Append("  (fp_circle (center ").Append(Fmt(cx)).Append(' ').Append(Fmt(cy))
                    .Append(") (end ").Append(Fmt(cx + r)).Append(' ').Append(Fmt(cy))
                    .Append(") (stroke (width 0.05) (type solid)) (fill solid) (layer \"")
                    .Append(layer).Append("\"))\n");
                continue;
            }

            if (shape[0].ValueKind == JsonValueKind.Number && shape.GetArrayLength() >= 6)
            {
                sb.Append("  (fp_poly (pts");
                for (var i = 0; i + 1 < shape.GetArrayLength(); i++)
                {
                    if (shape[i].ValueKind != JsonValueKind.Number) continue;
                    sb.Append(" (xy ").Append(Fmt(N(shape[i]) * MilToMm)).Append(' ')
                        .Append(Fmt(N(shape[i + 1]) * MilToMm)).Append(')');
                    i++;
                }
                sb.Append(") (stroke (width 0.05) (type solid)) (fill solid) (layer \"")
                    .Append(layer).Append("\"))\n");
            }
        }
    }

    /// <summary>封装层号 → KiCad 层名。</summary>
    private static string LayerMap(int layer) => layer switch
    {
        1 => "F.Cu",
        2 => "B.Cu",
        4 => "B.SilkS",
        3 => "F.SilkS",
        6 => "B.Mask",
        5 => "F.Mask",
        8 => "B.Paste",
        7 => "F.Paste",
        11 => "Edge.Cuts",
        48 or 49 or 50 => "F.Fab",
        _ => "F.SilkS"
    };

    /// <summary>引脚电气类型文本 → KiCad 电气类型。</summary>
    private static string PinType(string? text)
    {
        var t = (text ?? "Undefined").Trim().ToUpperInvariant();
        if (t.Contains("INPUT") || t == "IN") return "input";
        if (t.Contains("OUTPUT") || t == "OUT") return "output";
        if (t.Contains("BIDIR") || t == "IO") return "bidirectional";
        if (t.Contains("POWER")) return "power_in";
        if (t.Contains("OPEN COLLECTOR") || t == "OC") return "open_collector";
        if (t.Contains("OPEN EMITTER") || t == "OE") return "open_emitter";
        if (t.Contains("3") && t.Contains("STATE") || t.Contains("TRI") || t == "TS") return "tri_state";
        if (t.Contains("CLOCK") || t == "CLK") return "clock";
        return "passive";
    }

    /// <summary>数字 → invariant 最多 6 位小数。</summary>
    private static string Fmt(double v)
        => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>字符串 → 带引号 s-expression(含转义)。</summary>
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
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string OneLine(string s) => s.ReplaceLineEndings(" ");

    private static string S(JsonElement e)
        => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText();

    private static double N(JsonElement e)
        => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;
}

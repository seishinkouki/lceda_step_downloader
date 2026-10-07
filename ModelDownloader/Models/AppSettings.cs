using System;
using System.IO;
using System.Text.Json;

namespace ModelDownloader.Models;

/// <summary>
/// 应用设置（持久化到用户配置目录的 settings.json）。
/// 使用 source-gen JSON 上下文序列化，兼容 NativeAOT / trimming。
/// </summary>
public class AppSettings
{
    /// <summary>下载文件的保存目录</summary>
    public string? SaveLocation { get; set; }

    private static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModelDownloader");

    private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                using var stream = File.OpenRead(SettingsFile);
                return JsonSerializer.Deserialize(stream, CustomJsonSerializerContext.Default.AppSettings)
                       ?? new AppSettings();
            }
        }
        catch
        {
            // 设置文件损坏时忽略，回退到默认值
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            using var stream = File.Create(SettingsFile);
            JsonSerializer.Serialize(stream, this, CustomJsonSerializerContext.Default.AppSettings);
        }
        catch
        {
            // 保存失败不影响主流程
        }
    }
}

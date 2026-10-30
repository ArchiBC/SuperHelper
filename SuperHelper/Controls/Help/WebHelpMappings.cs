using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SuperHelper;

internal enum WebHelpSource
{
    RhinoOfficial,
    RhinoChina,
    Gh100,
}

internal static class WebHelpMappings
{
    private static string OfficialRoot => HelperHost.Current.OfficialHelpRoot;
    private const string RhinoChinaRoot = "https://docs.rhino3d.com.cn/";
    private const string Gh100Root = "https://100-gh.com/";
    private const string EmbeddedRhinoChinaMap = "SuperHelper.Resources.urls.rhino-cn.json";
    private const string Embedded100GhMap = "SuperHelper.Resources.urls.100-gh.json";

    private static readonly Dictionary<WebHelpSource, Dictionary<string, string>> Maps = new()
    {
        [WebHelpSource.RhinoOfficial] = new(StringComparer.OrdinalIgnoreCase),
        [WebHelpSource.RhinoChina] = new(StringComparer.OrdinalIgnoreCase),
        [WebHelpSource.Gh100] = new(StringComparer.OrdinalIgnoreCase),
    };

    private static readonly IReadOnlyList<string> Labels =
    [
        "Rhino Official",
        "Rhino 中文文档",
        "100-GH 中文",
    ];

    private static bool _initialized;

    public static IReadOnlyList<string> SourceLabels => Labels;

    public static WebHelpSource CurrentSource
    {
        get
        {
            var value = HelperHost.Current.Settings.GetValue(nameof(CurrentSource), (int)WebHelpSource.RhinoOfficial);
            return Enum.IsDefined(typeof(WebHelpSource), value)
                ? (WebHelpSource)value
                : WebHelpSource.RhinoOfficial;
        }
        set => HelperHost.Current.Settings.SetValue(nameof(CurrentSource), (int)value);
    }

    private static string MappingDirectory => Path.Combine(HelperHost.Current.SettingsDirectory, "SuperHelper");

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        LoadEmbeddedMap(EmbeddedRhinoChinaMap, Maps[WebHelpSource.RhinoChina]);
        LoadEmbeddedMap(Embedded100GhMap, Maps[WebHelpSource.Gh100]);
        LoadMap(Path.Combine(HelperHost.Current.SettingsDirectory, "urls.json"), Maps[WebHelpSource.Gh100]);
        foreach (var source in Enum.GetValues<WebHelpSource>())
        {
            LoadMap(MapPath(source), Maps[source]);
        }
    }

    public static void Merge100GhJson(string json)
    {
        Initialize();
        MergeJson(json, Maps[WebHelpSource.Gh100]);
    }

    public static string Resolve(HelpObject obj, string html)
    {
        Initialize();
        var key = obj.ComponentGuid.ToString();
        var directUrl = ExtractGotoUrl(html);
        CacheRecognizedUrl(key, directUrl);

        if (Maps[CurrentSource].TryGetValue(key, out var mappedUrl) && IsAbsoluteHttpUrl(mappedUrl))
        {
            return mappedUrl;
        }

        return CurrentSource switch
        {
            WebHelpSource.RhinoOfficial => BuildOfficialFallback(obj),
            WebHelpSource.RhinoChina => BuildRhinoChinaFallback(obj),
            WebHelpSource.Gh100 => $"{Gh100Root}?s={Uri.EscapeDataString(obj.Name ?? obj.NickName ?? string.Empty)}",
            _ => OfficialRoot,
        };
    }

    public static void SaveCurrent(HelpObject obj, string url)
    {
        if (obj == null || !IsAbsoluteHttpUrl(url))
        {
            return;
        }

        Initialize();
        Maps[CurrentSource][obj.ComponentGuid.ToString()] = url;
        SaveMap(CurrentSource);
    }

    private static void CacheRecognizedUrl(string key, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        WebHelpSource? source = uri.Host.ToLowerInvariant() switch
        {
            "www.rhino3d.com" when (uri.AbsolutePath.StartsWith("/docs/grasshopper/", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.StartsWith("/docs/grasshopper2/", StringComparison.OrdinalIgnoreCase))
                => WebHelpSource.RhinoOfficial,
            "rhino3d.com" when (uri.AbsolutePath.StartsWith("/docs/grasshopper/", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.StartsWith("/docs/grasshopper2/", StringComparison.OrdinalIgnoreCase))
                => WebHelpSource.RhinoOfficial,
            "docs.rhino3d.com.cn" => WebHelpSource.RhinoChina,
            "100-gh.com" or "www.100-gh.com" => WebHelpSource.Gh100,
            _ => null,
        };
        if (source == null || Maps[source.Value].TryGetValue(key, out var existing) && existing == url)
        {
            return;
        }

        Maps[source.Value][key] = url;
        SaveMap(source.Value);
    }

    private static string BuildOfficialFallback(HelpObject obj)
    {
        if (!string.IsNullOrWhiteSpace(obj.OfficialUrl)) return obj.OfficialUrl;
        var category = CleanPathSegment(obj.Category);
        var page = Slug(obj.Name ?? obj.NickName);
        return string.IsNullOrEmpty(category) || string.IsNullOrEmpty(page)
            ? OfficialRoot
            : $"{OfficialRoot}{category}/{page}/";
    }

    private static string BuildRhinoChinaFallback(HelpObject obj)
    {
        var category = CleanPathSegment(obj.Category);
        return string.IsNullOrEmpty(category)
            ? RhinoChinaRoot
            : $"{RhinoChinaRoot}gh/{category}/";
    }

    private static string CleanPathSegment(string value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : Uri.EscapeDataString(value.Trim());

    private static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var slug = Regex.Replace(value.Trim(), @"[^\p{L}\p{Nd}]+", "-").Trim('-');
        return Uri.EscapeDataString(slug);
    }

    private static string ExtractGotoUrl(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || !html.StartsWith("GOTO:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var value = html[5..].Trim();
        return IsAbsoluteHttpUrl(value) ? value : null;
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";

    private static string MapPath(WebHelpSource source) => Path.Combine(
        MappingDirectory,
        source switch
        {
            WebHelpSource.RhinoOfficial => "urls.official.json",
            WebHelpSource.RhinoChina => "urls.rhino-cn.json",
            WebHelpSource.Gh100 => "urls.100-gh.json",
            _ => "urls.json",
        });

    private static void LoadEmbeddedMap(string resourceName, Dictionary<string, string> target)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return;
        }
        using var reader = new StreamReader(stream);
        MergeJson(reader.ReadToEnd(), target);
    }

    private static void LoadMap(string path, Dictionary<string, string> target)
    {
        try
        {
            if (File.Exists(path))
            {
                MergeJson(File.ReadAllText(path), target);
            }
        }
        catch (Exception exception)
        {
            Rhino.RhinoApp.WriteLine($"SuperHelper mapping load failed ({Path.GetFileName(path)}): {exception.Message}");
        }
    }

    private static void MergeJson(string json, Dictionary<string, string> target)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (values == null)
        {
            return;
        }
        foreach (var pair in values.Where(pair => IsAbsoluteHttpUrl(pair.Value)))
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void SaveMap(WebHelpSource source)
    {
        try
        {
            Directory.CreateDirectory(MappingDirectory);
            File.WriteAllText(MapPath(source), JsonSerializer.Serialize(Maps[source]));
        }
        catch (Exception exception)
        {
            Rhino.RhinoApp.WriteLine($"SuperHelper mapping save failed: {exception.Message}");
        }
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace SaltSteamEditor.Core;

public sealed record TemplateField(string Key, string Name, string Example)
{
    public string Token => "{" + Key + "}";
}

public sealed record TemplatePreset(string Name, string Format);

public sealed record TemplateSegment(string Text, bool IsField, string Label);

/// <summary>Template syntax and substitution order match SaltSteamPlugin's Config.java.</summary>
public static partial class SongTemplate
{
    public const string Default = "{artist} - {title}";

    public static IReadOnlyList<TemplateField> Fields { get; } =
    [
        new("title", "歌曲名", "夜航"),
        new("artist", "歌手", "海岸电台"),
        new("album", "专辑", "写给夜晚"),
        new("albumArtist", "专辑歌手", "海岸电台"),
        new("mainLyrics", "主歌词", "让晚风带着我们去远方"),
        new("subLyrics", "翻译歌词", "Let the night breeze take us away"),
        new("position", "当前进度", "01:24"),
        new("duration", "总时长", "04:08")
    ];

    public static IReadOnlyList<TemplatePreset> Presets { get; } =
    [
        new("歌手与歌曲", Default),
        new("正在聆听", "正在听 {title} · {artist}"),
        new("专辑信息", "{albumArtist} · {album} · {title}"),
        new("播放进度", "{title} · {position} / {duration}"),
        new("歌词时刻", "{title} ｜ {mainLyrics}"),
        new("双语歌词", "{mainLyrics} ｜ {subLyrics}")
    ];

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex TokenPattern();

    public static IReadOnlyList<TemplateSegment> Parse(string format)
    {
        var result = new List<TemplateSegment>();
        var position = 0;
        foreach (Match match in TokenPattern().Matches(format))
        {
            if (match.Index > position)
                result.Add(new(format[position..match.Index], false, "固定文字"));
            var field = Fields.FirstOrDefault(field => field.Token == match.Value);
            result.Add(new(match.Value, field != null, field?.Name ?? "固定文字"));
            position = match.Index + match.Length;
        }
        if (position < format.Length)
            result.Add(new(format[position..], false, "固定文字"));
        return result;
    }

    public static string Render(string format, IReadOnlyDictionary<string, string> values)
    {
        // The original plugin replaces sequentially, including tokens inside prior values.
        foreach (var field in Fields)
            format = format.Replace(field.Token, values.GetValueOrDefault(field.Key, ""), StringComparison.Ordinal);
        return format;
    }

    public static bool CanSave(string format) => !format.Any(char.IsControl);

    public static string Describe(string format, string preview)
    {
        var messages = new List<string>();
        if (!CanSave(format))
            messages.Add("请移除换行、制表符或其他控制字符后保存。");
        if (format.Length == 0)
            messages.Add("空模板会使插件清除 song 状态字段。");
        var unknown = TokenPattern().Matches(format).Select(match => match.Value)
            .Where(token => !Fields.Any(field => field.Token == token)).Distinct().ToArray();
        if (unknown.Length > 0)
            messages.Add("无法识别的字段会原样显示：" + string.Join("、", unknown));
        if (Encoding.UTF8.GetByteCount(preview) >= 256)
            messages.Add("示例内容已达到 Steam 的 256 字节上限，请缩短模板或歌词。");
        if (preview.Any(char.IsControl))
            messages.Add("示例数据含控制字符，请检查对应字段。");
        return messages.Count == 0
            ? "模板可用。实际歌曲名与歌词较长时，状态内容也会变长。"
            : string.Join("\n", messages);
    }
}

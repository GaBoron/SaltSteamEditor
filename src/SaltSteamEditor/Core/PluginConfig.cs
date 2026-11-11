using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaltSteamEditor.Core;

/// <summary>Edits only songFormat, retaining current host-owned fields and backing up replacements.</summary>
public sealed class PluginConfig
{
    private const int MaxFileBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string Path { get; }
    public string SongFormat { get; private set; }

    private PluginConfig(string path, string format)
    {
        Path = path;
        SongFormat = format;
    }

    public static PluginConfig Load(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var (_, root) = Read(fullPath);
        return new(fullPath, GetFormat(root));
    }

    public string? Save(string format)
    {
        ValidateTemplate(format);
        var (original, root) = Read(Path);
        if (GetFormat(root) != SongFormat)
            throw new IOException("配置中的模板已被其他程序修改。请先重新打开文件，或另存为新文件。");

        // Re-read before every write so Steamworks cache updates are retained.
        root["songFormat"] = format;
        var backup = Write(Path, Serialize(root), original);
        SongFormat = format;
        return backup;
    }

    public static (PluginConfig Config, string? Backup) Export(string path, string format)
    {
        ValidateTemplate(format);
        var fullPath = System.IO.Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            // Overwriting an existing export still requires a recognized plugin configuration.
            var existing = Load(fullPath);
            return (existing, existing.Save(format));
        }
        var root = new JsonObject { ["songFormat"] = format };
        Write(fullPath, Serialize(root), null);
        return (new(fullPath, format), null);
    }

    private static void ValidateTemplate(string format)
    {
        if (!SongTemplate.CanSave(format))
            throw new InvalidDataException("模板不能包含换行、制表符或其他控制字符。");
    }

    private static (byte[] Bytes, JsonObject Root) Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxFileBytes)
            throw new InvalidDataException("配置超过 2 MB，请确认选择的是原插件的 config.json。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        ReadOnlySpan<byte> json = bytes;
        if (json.StartsWith(Encoding.UTF8.Preamble))
            json = json[Encoding.UTF8.Preamble.Length..];
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            CheckDuplicateKeys(document.RootElement);
            var root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException("配置必须是 JSON 对象。");
            GetFormat(root);
            return (bytes, root);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("JSON 格式有误，请先修复配置文件。" + exception.Message, exception);
        }
    }

    private static string GetFormat(JsonObject root)
    {
        if (root["songFormat"] is JsonValue value && value.TryGetValue<string>(out var format))
            return format;
        throw new InvalidDataException("未找到字符串类型的 songFormat。请选择 SaltSteamPlugin 的 config.json，勿选 preference_config.json。");
    }

    private static void CheckDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("配置含重复的 JSON 字段：" + property.Name);
                CheckDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                CheckDuplicateKeys(child);
        }
    }

    private static byte[] Serialize(JsonObject root) => Encoding.UTF8.GetBytes(root.ToJsonString(JsonOptions) + "\n");

    private static string? Write(string path, byte[] bytes, byte[]? original)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (original == null)
            {
                File.Move(temporary, path, overwrite: false);
                return null;
            }
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(original))
                throw new IOException("配置在保存过程中发生变化，请重试。");
            var backup = path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")
                + "-" + Guid.NewGuid().ToString("N")[..8] + ".bak";
            File.Replace(temporary, path, backup);
            return backup;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Failed temporary files are retained for recovery; never remove user files.
            throw new IOException(exception.Message + "\n若临时文件已生成，会保留在：" + temporary, exception);
        }
    }
}

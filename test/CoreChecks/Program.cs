using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using SaltSteamEditor.Core;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        _checks++;
        Console.WriteLine("PASS " + name);
    }

    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Check(true, name);
            return;
        }
        throw new Exception("FAILED to reject: " + name);
    }

    public static void Main(string[] args)
    {
        var root = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("EDITOR_CHECK_ROOT") ?? Path.GetTempPath(), "fixtures-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var samples = new Dictionary<string, string> { ["title"] = "夜航", ["artist"] = "海岸电台", ["album"] = "专辑", ["albumArtist"] = "专辑歌手", ["mainLyrics"] = "歌词", ["subLyrics"] = "翻译", ["position"] = "01:24", ["duration"] = "04:08" };
        Check(SongTemplate.Render(SongTemplate.Default, samples) == "海岸电台 - 夜航", "default replacement");
        Check(SongTemplate.Render("{title}/{artist}/{album}/{albumArtist}/{mainLyrics}/{subLyrics}/{position}/{duration}", samples) == "夜航/海岸电台/专辑/专辑歌手/歌词/翻译/01:24/04:08", "all eight fields");
        Check(SongTemplate.Render("{title}-{unknown}", new Dictionary<string, string>()) == "-{unknown}", "missing values and unknown fields");
        var recursive = new Dictionary<string, string> { ["title"] = "{artist}", ["artist"] = "A" };
        Check(SongTemplate.Render("{title}", recursive) == "A", "matches original sequential substitution");
        foreach (var value in new[] { "", "A😀中", "{title}{title}", "x{{title}}y", "{unknown} x {albumArtist}", "a\nb" })
            Check(string.Concat(SongTemplate.Parse(value).Select(s => s.Text)) == value, "lossless segments: " + value.Replace("\n", "\\n"));
        Check(SongTemplate.Describe("{bad}", "").Contains("无法识别"), "unknown field warning");
        Check(SongTemplate.Describe("", "").Contains("空模板"), "empty format warning");
        Check(SongTemplate.Describe("{title}", new string('中', 90)).Contains("256"), "UTF-8 byte limit");
        Check(!SongTemplate.CanSave("a\nb") && !SongTemplate.CanSave("a\0b") && !SongTemplate.CanSave("a\tb"), "control characters blocked");
        var path = Path.Combine(root, "config.json");
        var original = "{\"songFormat\":\"{title}\",\"cachedSteamworksClassName\":\"old\",\"cachedSpwInfo\":\"1.19\",\"extra\":{\"enabled\":true,\"array\":[null,123,\"😀\"]}}";
        File.WriteAllText(path, original, new UTF8Encoding(true));
        var config = PluginConfig.Load(path);
        Check(config.SongFormat == "{title}", "read UTF-8 BOM");
        var hostUpdate = original.Replace("\"old\"", "\"latest\"");
        File.WriteAllText(path, hostUpdate, new UTF8Encoding(true));
        var beforeSave = File.ReadAllBytes(path);
        var backup = config.Save("{artist} · {title}");
        var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Check(saved["songFormat"]!.GetValue<string>() == "{artist} · {title}", "saved requested template");
        Check(saved["cachedSteamworksClassName"]!.GetValue<string>() == "latest", "merge host cache update");
        Check(JsonNode.DeepEquals(saved["extra"], JsonNode.Parse(original)!["extra"]), "unknown nested values retained");
        Check(backup != null && File.ReadAllBytes(backup).SequenceEqual(beforeSave), "exact original bytes in backup");
        Check(config.SongFormat == "{artist} · {title}", "baseline updated after save");
        config.Save("{album}");
        Check(Directory.GetFiles(root, "*.bak").Length == 2, "unique backups on repeated saves");
        File.WriteAllText(path, "{\"songFormat\":\"external\"}");
        Reject(() => config.Save("overwrite"), "reject concurrent template changes");
        Check(File.ReadAllText(path).Contains("external"), "conflict preserves external file");
        foreach (var bad in new[] { "{}", "[]", "{\"songFormat\":1}", "{\"songFormat\":null}", "{bad", "{\"songFormat\":\"a\nb\"}", "{\"songFormat\":\"x\",\"songFormat\":\"y\"}", "{\"songFormat\":\"x\",\"nested\":{\"x\":1,\"x\":2}}" })
        {
            File.WriteAllText(path, bad);
            Reject(() => PluginConfig.Load(path), "reject bad config: " + bad);
        }
        File.WriteAllText(path, "{\"songFormat\":\"x\",\"keep\":42}");
        Reject(() => PluginConfig.Load(path).Save("a\nb"), "refuse invalid template on save");
        Reject(() => PluginConfig.Export(Path.Combine(root, "invalid.json"), "a\nb"), "refuse invalid export");
        var exported = Path.Combine(root, "new.json");
        var export = PluginConfig.Export(exported, "😀 {title}");
        Check(export.Backup == null && PluginConfig.Load(exported).SongFormat == "😀 {title}", "export new Unicode config");
        var overwritten = PluginConfig.Export(path, "exported");
        Check(overwritten.Backup != null && JsonNode.Parse(File.ReadAllText(path))!["keep"]!.GetValue<int>() == 42, "existing export retained and backed up");
        File.WriteAllText(path, "{}");
        Reject(() => PluginConfig.Export(path, "x"), "do not export over unrelated JSON");
        Check(File.ReadAllText(path) == "{}", "rejected export preserves existing file");
        File.WriteAllBytes(path, new byte[2 * 1024 * 1024 + 1]);
        Reject(() => PluginConfig.Load(path), "file size bound");

        Console.WriteLine($"{_checks} checks passed. Fixtures retained: {root}");
    }
}

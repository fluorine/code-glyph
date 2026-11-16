using System.IO.Compression;
using System.Text.Json;

// Usage: PackIcons <lucide-static package folder> <output icons.json.gz>
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PackIcons <lucide-static package folder> <output icons.json.gz>");
    return 1;
}

var package = args[0];
var output = args[1];

var nodes = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
    File.ReadAllText(Path.Combine(package, "icon-nodes.json")))!;
var tags = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
    File.ReadAllText(Path.Combine(package, "tags.json")))!;
var version = JsonDocument.Parse(File.ReadAllText(Path.Combine(package, "package.json")))
    .RootElement.GetProperty("version").GetString()!;

var icons = new List<object>();
foreach (var name in nodes.Keys.OrderBy(k => k, StringComparer.Ordinal))
{
    var elements = new List<object>();
    foreach (var node in nodes[name].EnumerateArray())
    {
        var attrs = new Dictionary<string, string>();
        foreach (var p in node[1].EnumerateObject())
        {
            if (p.Name != "key") attrs[p.Name] = p.Value.ToString();
        }
        elements.Add(new { k = node[0].GetString(), a = attrs });
    }
    icons.Add(new { n = name, t = tags.TryGetValue(name, out var t) ? t : Array.Empty<string>(), e = elements });
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
using (var file = File.Create(output))
using (var gz = new GZipStream(file, CompressionLevel.SmallestSize))
{
    JsonSerializer.Serialize(gz, new { version, icons });
}

Console.WriteLine($"Packed {icons.Count} icons from lucide-static {version} into {output}");
return 0;

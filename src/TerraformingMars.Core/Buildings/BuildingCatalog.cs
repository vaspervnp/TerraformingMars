using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerraformingMars.Core.Buildings;

/// <summary>
/// Κατάλογος όλων των <see cref="BuildingDefinition"/>. Φορτώνεται από JSON:
/// <see cref="LoadDefault"/> (embedded) ή <see cref="LoadFromFile"/> (modding/hot-reload).
/// </summary>
public sealed class BuildingCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<string, BuildingDefinition> _byId;

    private BuildingCatalog(IEnumerable<BuildingDefinition> defs)
    {
        _byId = defs.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<BuildingDefinition> All => _byId.Values;
    public IEnumerable<BuildingDefinition> Buildables => _byId.Values.Where(d => d.Buildable);

    public BuildingDefinition Get(string id) => _byId[id];
    public bool TryGet(string id, out BuildingDefinition? def) => _byId.TryGetValue(id, out def);

    public static BuildingCatalog FromJson(string json)
    {
        var defs = JsonSerializer.Deserialize<List<BuildingDefinition>>(json, Options)
                   ?? throw new InvalidDataException("buildings.json: άκυρο ή κενό περιεχόμενο.");
        return new BuildingCatalog(defs);
    }

    public static BuildingCatalog LoadFromFile(string path) => FromJson(File.ReadAllText(path));

    public static BuildingCatalog LoadDefault()
    {
        var asm = typeof(BuildingCatalog).Assembly;
        string name = asm.GetManifestResourceNames()
                         .First(n => n.EndsWith("buildings.json", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }
}

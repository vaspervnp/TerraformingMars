using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerraformingMars.Core.Research;

/// <summary>
/// Κατάλογος όλων των <see cref="TechDefinition"/>. Φορτώνεται από JSON:
/// <see cref="LoadDefault"/> (embedded) ή <see cref="LoadFromFile"/> (modding).
/// </summary>
public sealed class TechCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<string, TechDefinition> _byId;

    private TechCatalog(IEnumerable<TechDefinition> techs)
    {
        _byId = techs.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<TechDefinition> All => _byId.Values;

    public TechDefinition Get(string id) => _byId[id];
    public bool TryGet(string id, out TechDefinition? tech) => _byId.TryGetValue(id, out tech);

    public static TechCatalog FromJson(string json)
    {
        var techs = JsonSerializer.Deserialize<List<TechDefinition>>(json, Options)
                    ?? throw new InvalidDataException("technologies.json: άκυρο ή κενό περιεχόμενο.");
        return new TechCatalog(techs);
    }

    public static TechCatalog LoadFromFile(string path) => FromJson(File.ReadAllText(path));

    public static TechCatalog LoadDefault()
    {
        var asm = typeof(TechCatalog).Assembly;
        string name = asm.GetManifestResourceNames()
                         .First(n => n.EndsWith("technologies.json", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }
}

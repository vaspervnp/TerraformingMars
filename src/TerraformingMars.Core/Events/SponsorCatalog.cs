using System.Text.Json;

namespace TerraformingMars.Core.Events;

/// <summary>Κατάλογος χορηγών/δυσκολιών από JSON (embedded default ή file override).</summary>
public sealed class SponsorCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, SponsorProfile> _byId;
    private readonly List<SponsorProfile> _ordered;

    private SponsorCatalog(List<SponsorProfile> sponsors)
    {
        _ordered = sponsors;
        _byId = sponsors.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<SponsorProfile> All => _ordered;

    public SponsorProfile Get(string id) => _byId[id];
    public bool TryGet(string id, out SponsorProfile? sponsor) => _byId.TryGetValue(id, out sponsor);

    public static SponsorCatalog FromJson(string json)
    {
        var sponsors = JsonSerializer.Deserialize<List<SponsorProfile>>(json, Options)
                       ?? throw new InvalidDataException("sponsors.json: άκυρο ή κενό περιεχόμενο.");
        return new SponsorCatalog(sponsors);
    }

    public static SponsorCatalog LoadFromFile(string path) => FromJson(File.ReadAllText(path));

    public static SponsorCatalog LoadDefault()
    {
        var asm = typeof(SponsorCatalog).Assembly;
        string name = asm.GetManifestResourceNames()
                         .First(n => n.EndsWith("sponsors.json", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }
}

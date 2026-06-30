using Microsoft.Xna.Framework;
using TerraformingMars.Core.Map;

namespace TerraformingMars.Game.Rendering;

/// <summary>Αντιστοίχιση enums εδάφους/πόρων σε χρώματα (Mars-ish παλέτα, low→high υψόμετρο).</summary>
public static class TerrainPalette
{
    public static Color Terrain(TerrainType t) => t switch
    {
        TerrainType.Canyon   => new Color(0x4a, 0x24, 0x1a),
        TerrainType.Lowland  => new Color(0x6f, 0x3a, 0x24),
        TerrainType.Flatland => new Color(0x9c, 0x4f, 0x2e),
        TerrainType.Crater   => new Color(0x82, 0x3f, 0x22),
        TerrainType.Highland => new Color(0xc2, 0x72, 0x4a),
        TerrainType.Mountain => new Color(0xe6, 0xa8, 0x78),
        TerrainType.PolarIce => new Color(0xe8, 0xf0, 0xf5),
        TerrainType.Water    => new Color(0x2b, 0x6f, 0xb0),
        _ => Color.Magenta
    };

    public static Color Resource(ResourceType r) => r switch
    {
        ResourceType.Ice      => new Color(0x9f, 0xe8, 0xff),
        ResourceType.Iron     => new Color(0xc4, 0xc4, 0xcc),
        ResourceType.Silicon  => new Color(0xd9, 0xd2, 0x4a),
        ResourceType.Regolith => new Color(0xd8, 0xb8, 0x90),
        _ => Color.Transparent
    };
}

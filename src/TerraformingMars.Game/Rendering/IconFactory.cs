using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TerraformingMars.Core.Buildings;

namespace TerraformingMars.Game.Rendering;

/// <summary>
/// Παράγει διανυσματικά-στιλ εικονίδια κτιρίων ως <see cref="Texture2D"/> (CPU rasterization
/// με anti-aliasing — χωρίς αρχεία assets). Κάθε εικονίδιο = πλακέτα κατηγορίας + σύμβολο που
/// μοιάζει με το κτίριο. Χρησιμοποιούνται στα κουμπιά UI και πάνω στα εξάγωνα.
/// </summary>
public static class IconFactory
{
    public const int Size = 64;

    public static Dictionary<string, Texture2D> CreateAll(GraphicsDevice gd, BuildingCatalog catalog)
    {
        var icons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in catalog.All)
        {
            var tex = new Texture2D(gd, Size, Size);
            tex.SetData(BuildBuffer(def.Id, def.Category));
            icons[def.Id] = tex;
        }
        return icons;
    }

    /// <summary>Παράγει το buffer εικονιδίου (χωρίς GraphicsDevice — χρήσιμο και για preview/εξαγωγή).</summary>
    public static Color[] BuildBuffer(string id, string category)
    {
        var buffer = new Color[Size * Size];
        DrawPlate(buffer, CategoryColor(category));
        DrawSymbol(buffer, id, category);
        return buffer;
    }

    /// <summary>Εικονίδιο ανακύκλωσης (κουμπί reclaim): τρία πράσινα βέλη σε τριγωνικό κύκλο.</summary>
    public static Texture2D CreateReclaim(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, Size, Size);
        tex.SetData(BuildReclaimBuffer());
        return tex;
    }

    /// <summary>Εικονίδιο μενού κτιρίων (toggle της παλέτας): μικρό «skyline» τριών κτιρίων.</summary>
    public static Texture2D CreateBuildings(GraphicsDevice gd)
    {
        var tex = new Texture2D(gd, Size, Size);
        tex.SetData(BuildBuildingsBuffer());
        return tex;
    }

    public static Color[] BuildBuildingsBuffer()
    {
        var buffer = new Color[Size * Size];
        DrawPlate(buffer, new Color(120, 170, 230));

        var wall = new Color(205, 211, 221);
        var win = new Color(120, 180, 235);
        Rect(buffer, 13, 32, 12, 18, wall);      // αριστερό κτίριο
        Rect(buffer, 26, 20, 13, 30, wall);      // κεντρικός πύργος
        Rect(buffer, 40, 28, 12, 22, wall);      // δεξί κτίριο
        Rect(buffer, 16, 36, 6, 5, win); Rect(buffer, 16, 44, 6, 4, win);
        Rect(buffer, 29, 24, 7, 6, win); Rect(buffer, 29, 33, 7, 6, win); Rect(buffer, 29, 42, 7, 6, win);
        Rect(buffer, 43, 32, 6, 5, win); Rect(buffer, 43, 40, 6, 5, win);
        return buffer;
    }

    public static Color[] BuildReclaimBuffer()
    {
        var buffer = new Color[Size * Size];
        DrawPlate(buffer, new Color(90, 200, 90));

        var green = new Color(95, 215, 105);
        // Κορυφές τριγώνου (δεξιόστροφα) και τρία βέλη που «κυνηγιούνται».
        (float x, float y) top = (32, 15), br = (49, 44), bl = (15, 44);
        Arrow(buffer, top.x, top.y, br.x, br.y, green);
        Arrow(buffer, br.x, br.y, bl.x, bl.y, green);
        Arrow(buffer, bl.x, bl.y, top.x, top.y, green);
        return buffer;
    }

    /// <summary>Παχιά γραμμή με τριγωνική μύτη στο τέλος (x1,y1) — για το εικονίδιο ανακύκλωσης.</summary>
    private static void Arrow(Color[] b, float x0, float y0, float x1, float y1, Color c, float th = 4f, float head = 8f)
    {
        Line(b, x0, y0, x1, y1, th, c);
        float dx = x1 - x0, dy = y1 - y0;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f) return;
        float ux = dx / len, uy = dy / len;   // κατεύθυνση
        float px = -uy, py = ux;               // κάθετη
        float baseX = x1 - ux * head, baseY = y1 - uy * head;
        Tri(b, x1, y1,
            baseX + px * head * 0.6f, baseY + py * head * 0.6f,
            baseX - px * head * 0.6f, baseY - py * head * 0.6f, c);
    }

    private static Color CategoryColor(string category) => category switch
    {
        "Power" => new Color(255, 210, 70),
        "LifeSupport" => new Color(90, 200, 255),
        "Food" => new Color(110, 220, 110),
        "Industry" => new Color(255, 150, 70),
        "Habitat" => new Color(235, 235, 240),
        "Research" => new Color(180, 140, 255),
        "Planetary" => new Color(120, 220, 200),
        "Biosphere" => new Color(90, 200, 90),
        _ => new Color(200, 200, 205)
    };

    private static void DrawPlate(Color[] b, Color border)
    {
        RoundRect(b, 5, 5, 54, 54, 13, border, 0.95f);
        RoundRect(b, 7, 7, 50, 50, 11, new Color(18, 20, 28), 0.96f);
    }

    private static void DrawSymbol(Color[] b, string id, string category)
    {
        var steel = new Color(170, 178, 190);
        var dark = new Color(70, 76, 90);
        switch (id)
        {
            case "landing_capsule":
                RoundRect(b, 22, 18, 20, 26, 9, new Color(220, 224, 232), 1f);
                Disc(b, 32, 27, 5, new Color(90, 150, 220));
                Line(b, 24, 44, 20, 54, 3, steel); Line(b, 40, 44, 44, 54, 3, steel);
                break;
            case "solar_panel":
                Rect(b, 14, 26, 36, 22, new Color(50, 110, 200));
                for (int gx = 0; gx < 4; gx++) Rect(b, 14 + gx * 9, 26, 1, 22, dark);
                Rect(b, 14, 37, 36, 1, dark);
                Disc(b, 47, 18, 7, new Color(255, 215, 90));
                break;
            case "battery":
                Rect(b, 30, 14, 8, 4, steel);
                RoundRect(b, 20, 18, 28, 30, 5, new Color(70, 180, 90), 1f);
                Tri(b, 36, 24, 28, 36, 36, 36); Tri(b, 36, 24, 36, 36, 42, 28, new Color(255, 230, 90));
                break;
            case "o2_recycler":
                RoundRect(b, 20, 16, 24, 34, 10, new Color(70, 190, 215), 1f);
                Disc(b, 28, 26, 3, new Color(235, 250, 255));
                Disc(b, 36, 33, 4, new Color(235, 250, 255));
                Disc(b, 29, 40, 2, new Color(235, 250, 255));
                break;
            case "ice_drill":
                Rect(b, 28, 14, 8, 24, steel);
                Tri(b, 26, 38, 38, 38, 32, 52, new Color(150, 210, 245));
                Disc(b, 32, 16, 4, new Color(210, 240, 255));
                break;
            case "iron_mine":
                Rect(b, 20, 36, 24, 10, steel);
                Tri(b, 20, 36, 26, 28, 44, 36, new Color(210, 216, 228));
                break;
            case "silicon_mine":
                Rect(b, 20, 40, 24, 8, steel);                              // στοά ορυχείου
                Tri(b, 24, 40, 30, 20, 36, 40, new Color(150, 185, 225));   // κρύσταλλος πυριτίου
                Tri(b, 30, 20, 36, 40, 42, 30, new Color(120, 155, 205));
                break;
            case "export_terminal":
                Line(b, 14, 50, 44, 20, 5, steel);                          // ράγα mass driver
                Line(b, 30, 35, 50, 15, 4, new Color(255, 200, 90));        // ίχνος εκτόξευσης
                Tri(b, 50, 12, 44, 24, 54, 22, new Color(255, 220, 120));   // εκτοξευόμενο φορτίο
                Disc(b, 49, 18, 3, new Color(255, 235, 150));
                break;
            case "regolith_printer":
                RoundRect(b, 16, 18, 32, 18, 4, new Color(220, 130, 60), 1f);
                Rect(b, 29, 36, 6, 7, dark);
                Rect(b, 22, 46, 20, 3, new Color(220, 130, 60));
                break;
            case "hydroponics":
            case "gm_forest":
                Rect(b, 28, 30, 4, 18, new Color(120, 80, 50));            // κορμός
                Disc(b, 30, 24, 10, new Color(70, 180, 80));               // φύλλωμα
                Disc(b, 22, 30, 7, new Color(80, 195, 90));
                Disc(b, 40, 30, 7, new Color(80, 195, 90));
                break;
            case "research_lab":
                Rect(b, 29, 14, 6, 10, steel);                              // λαιμός φιάλης
                Tri(b, 18, 50, 46, 50, 32, 22, new Color(150, 110, 230));   // σώμα φιάλης
                Rect(b, 22, 42, 20, 8, new Color(190, 160, 250));           // υγρό
                break;
            case "fission_reactor":
                Ring(b, 32, 32, 16, 2, new Color(255, 220, 90));
                Ring(b, 32, 32, 10, 2, new Color(255, 200, 70));
                Disc(b, 32, 32, 5, new Color(255, 235, 120));
                break;
            case "ghg_factory":
                Rect(b, 24, 26, 8, 22, steel); Rect(b, 34, 30, 8, 18, steel);
                Disc(b, 26, 20, 5, new Color(150, 160, 170));
                Disc(b, 34, 16, 6, new Color(180, 188, 196));
                break;
            case "orbital_mirror":
                Disc(b, 32, 30, 13, new Color(200, 235, 235));
                Disc(b, 32, 30, 9, new Color(150, 210, 215));
                Line(b, 46, 18, 54, 12, 2, new Color(255, 255, 200));
                break;
            case "magnetosphere_station":
                Disc(b, 32, 34, 9, new Color(120, 170, 230));
                Ring(b, 32, 32, 16, 2, new Color(130, 230, 210));
                Ring(b, 32, 30, 20, 2, new Color(110, 200, 200));
                break;
            case "comet_redirector":
                Disc(b, 26, 26, 7, new Color(220, 240, 255));
                Line(b, 30, 30, 50, 50, 4, new Color(150, 200, 255));
                break;
            case "cyanobacteria_farm":
                Ring(b, 32, 32, 15, 2, new Color(120, 200, 120));
                Disc(b, 26, 28, 3, new Color(80, 200, 90)); Disc(b, 36, 30, 3, new Color(80, 200, 90));
                Disc(b, 30, 38, 3, new Color(80, 200, 90)); Disc(b, 38, 38, 2, new Color(80, 200, 90));
                break;
            case "fauna_reserve":
                Disc(b, 32, 38, 8, new Color(210, 180, 140));               // πέλμα
                Disc(b, 24, 26, 4, new Color(210, 180, 140)); Disc(b, 32, 22, 4, new Color(210, 180, 140));
                Disc(b, 40, 26, 4, new Color(210, 180, 140));
                break;
            case "domeless_city":
                Rect(b, 18, 32, 8, 16, steel); Rect(b, 28, 24, 8, 24, new Color(200, 206, 216));
                Rect(b, 38, 30, 8, 18, steel);
                Disc(b, 42, 22, 6, new Color(150, 210, 245));
                break;
            case "habitat_module":
                Disc(b, 32, 33, 15, new Color(150, 205, 240));              // θόλος
                Rect(b, 17, 33, 30, 15, new Color(200, 206, 216));          // σώμα κατοικίας
                Rect(b, 28, 38, 8, 10, dark);                               // πόρτα
                Rect(b, 21, 37, 5, 5, new Color(150, 205, 240));            // παράθυρα
                Rect(b, 38, 37, 5, 5, new Color(150, 205, 240));
                break;
            default:
                Disc(b, 32, 32, 12, CategoryColor(category));
                break;
        }
    }

    // ------------------------------------------------------------- rasterizer

    private static void Blend(Color[] b, int x, int y, Color c, float a)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size || a <= 0f) return;
        if (a > 1f) a = 1f;
        int i = y * Size + x;
        Color d = b[i];
        float ca = a * (c.A / 255f);
        b[i] = new Color(
            (int)(d.R + (c.R - d.R) * ca),
            (int)(d.G + (c.G - d.G) * ca),
            (int)(d.B + (c.B - d.B) * ca),
            (int)Math.Min(255f, d.A + (255 - d.A) * ca));
    }

    private static void Rect(Color[] b, int x, int y, int w, int h, Color c, float alpha = 1f)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                Blend(b, xx, yy, c, alpha);
    }

    private static void Disc(Color[] b, float cx, float cy, float r, Color c, float alpha = 1f)
    {
        for (int y = (int)(cy - r - 1); y <= cy + r + 1; y++)
            for (int x = (int)(cx - r - 1); x <= cx + r + 1; x++)
            {
                float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                Blend(b, x, y, c, Math.Clamp(r - d + 0.5f, 0f, 1f) * alpha);
            }
    }

    private static void Ring(Color[] b, float cx, float cy, float r, float th, Color c)
    {
        for (int y = (int)(cy - r - th); y <= cy + r + th; y++)
            for (int x = (int)(cx - r - th); x <= cx + r + th; x++)
            {
                float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                Blend(b, x, y, c, Math.Clamp(th * 0.5f + 0.5f - MathF.Abs(d - r), 0f, 1f));
            }
    }

    private static void RoundRect(Color[] b, int x, int y, int w, int h, float radius, Color c, float alpha)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
            {
                float dx = MathF.Max(MathF.Max(x + radius - xx, xx - (x + w - 1 - radius)), 0f);
                float dy = MathF.Max(MathF.Max(y + radius - yy, yy - (y + h - 1 - radius)), 0f);
                float d = MathF.Sqrt(dx * dx + dy * dy);
                Blend(b, xx, yy, c, Math.Clamp(radius - d + 0.5f, 0f, 1f) * alpha);
            }
    }

    private static void Line(Color[] b, float x0, float y0, float x1, float y1, float th, Color c)
    {
        int minX = (int)MathF.Min(x0, x1) - 2, maxX = (int)MathF.Max(x0, x1) + 2;
        int minY = (int)MathF.Min(y0, y1) - 2, maxY = (int)MathF.Max(y0, y1) + 2;
        float dx = x1 - x0, dy = y1 - y0;
        float len2 = dx * dx + dy * dy;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float t = len2 <= 0 ? 0 : Math.Clamp(((x - x0) * dx + (y - y0) * dy) / len2, 0f, 1f);
                float px = x0 + t * dx, py = y0 + t * dy;
                float dist = MathF.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                Blend(b, x, y, c, Math.Clamp(th * 0.5f + 0.5f - dist, 0f, 1f));
            }
    }

    private static void Tri(Color[] b, float ax, float ay, float bx, float by, float cx, float cy, Color? color = null)
    {
        Color c = color ?? new Color(170, 178, 190);
        int minX = (int)MathF.Min(ax, MathF.Min(bx, cx)), maxX = (int)MathF.Max(ax, MathF.Max(bx, cx));
        int minY = (int)MathF.Min(ay, MathF.Min(by, cy)), maxY = (int)MathF.Max(ay, MathF.Max(by, cy));
        float area = Edge(ax, ay, bx, by, cx, cy);
        if (MathF.Abs(area) < 1e-3f) return;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float w0 = Edge(bx, by, cx, cy, x + 0.5f, y + 0.5f);
                float w1 = Edge(cx, cy, ax, ay, x + 0.5f, y + 0.5f);
                float w2 = Edge(ax, ay, bx, by, x + 0.5f, y + 0.5f);
                bool inside = (w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0);
                if (inside) Blend(b, x, y, c, 1f);
            }
    }

    private static float Edge(float ax, float ay, float bx, float by, float px, float py) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax);
}

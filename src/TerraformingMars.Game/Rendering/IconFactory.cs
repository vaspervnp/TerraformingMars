using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Research;

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

    /// <summary>
    /// Εικονίδια για την παλέτα έρευνας: κάθε τεχνολογία δανείζεται το εικονίδιο του κτιρίου που
    /// ξεκλειδώνει (πιο κατατοπιστικό)· όσες δεν ξεκλειδώνουν κτίριο παίρνουν ειδικό εικονίδιο.
    /// </summary>
    public static Dictionary<string, Texture2D> CreateTechIcons(
        GraphicsDevice gd, TechCatalog techs, IReadOnlyDictionary<string, Texture2D> buildingIcons, Texture2D reclaimIcon)
    {
        var icons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in techs.All)
        {
            Texture2D? tex = null;
            foreach (var buildingId in t.Unlocks)
                if (buildingIcons.TryGetValue(buildingId, out var bt)) { tex = bt; break; }

            tex ??= t.Id.Equals("reclaim", StringComparison.OrdinalIgnoreCase)
                ? reclaimIcon
                : BuildTechOnlyIcon(gd, t.Id);
            icons[t.Id] = tex;
        }
        return icons;
    }

    /// <summary>Εικονίδιο για τεχνολογία που δεν ξεκλειδώνει κτίριο (rover networks / γενική έρευνα).</summary>
    private static Texture2D BuildTechOnlyIcon(GraphicsDevice gd, string id)
    {
        var b = new Color[Size * Size];
        DrawPlate(b, new Color(180, 140, 255)); // πλακέτα «Research»
        switch (id)
        {
            case "rover_networks":
                var body = new Color(210, 216, 228);
                RoundRect(b, 15, 30, 34, 14, 4, body, 1f);              // σασί
                Rect(b, 22, 22, 19, 9, new Color(150, 185, 235));       // καμπίνα
                Disc(b, 22, 46, 6, new Color(60, 66, 78));              // τροχοί
                Disc(b, 32, 46, 6, new Color(60, 66, 78));
                Disc(b, 42, 46, 6, new Color(60, 66, 78));
                Line(b, 47, 30, 51, 16, 2, body);                       // κεραία
                Disc(b, 51, 15, 2, new Color(255, 230, 120));
                break;
            default: // άτομο (γενική έρευνα)
                Ring(b, 32, 32, 16, 3, new Color(190, 150, 255));
                Ring(b, 32, 32, 9, 2, new Color(150, 120, 220));
                Disc(b, 32, 32, 4, new Color(220, 200, 255));
                break;
        }
        var tex = new Texture2D(gd, Size, Size);
        tex.SetData(b);
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

    /// <summary>Μικρά εικονίδια πόρων για τη μπάρα στην κορυφή (χωρίς πλακέτα — σκέτο σύμβολο σε διαφανές φόντο).</summary>
    public static Dictionary<string, Texture2D> CreateResourceIcons(GraphicsDevice gd)
    {
        var icons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in new[] { "energy", "water", "oxygen", "food", "materials", "silicon", "credits", "crew" })
        {
            var buffer = new Color[Size * Size]; // default = διαφανές
            DrawResourceSymbol(buffer, id);
            var tex = new Texture2D(gd, Size, Size);
            tex.SetData(buffer);
            icons[id] = tex;
        }
        return icons;
    }

    private static void DrawResourceSymbol(Color[] b, string id)
    {
        switch (id)
        {
            case "energy": // κεραυνός
                var yellow = new Color(255, 210, 60);
                Tri(b, 38, 6, 20, 36, 34, 34, yellow);
                Tri(b, 30, 58, 46, 28, 32, 32, yellow);
                break;
            case "water": // σταγόνα
                var blue = new Color(70, 150, 235);
                Disc(b, 32, 40, 15, blue);
                Tri(b, 32, 8, 19, 40, 45, 40, blue);
                break;
            case "oxygen": // O₂ — δύο δαχτυλίδια
                var cyan = new Color(120, 215, 235);
                Ring(b, 26, 29, 13, 4, cyan);
                Ring(b, 43, 41, 9, 3, cyan);
                break;
            case "food": // μήλο + φύλλο
                Disc(b, 32, 37, 15, new Color(90, 195, 90));
                Rect(b, 30, 16, 4, 9, new Color(120, 80, 50));
                Tri(b, 34, 18, 47, 13, 41, 25, new Color(120, 210, 110));
                break;
            case "materials": // κιβώτιο
                var orange = new Color(230, 140, 60);
                RoundRect(b, 15, 19, 34, 34, 4, orange, 1f);
                Line(b, 16, 20, 48, 52, 3, new Color(150, 90, 40));
                Line(b, 48, 20, 16, 52, 3, new Color(150, 90, 40));
                break;
            case "silicon": // κρύσταλλος
                var s1 = new Color(155, 190, 230);
                var s2 = new Color(120, 150, 205);
                Tri(b, 23, 44, 32, 11, 41, 44, s1);
                Tri(b, 32, 11, 41, 44, 47, 29, s2);
                Tri(b, 23, 44, 17, 29, 32, 11, s2);
                break;
            case "credits": // νόμισμα
                var gold = new Color(240, 195, 70);
                Disc(b, 32, 32, 16, gold);
                Ring(b, 32, 32, 16, 2, new Color(180, 140, 40));
                Ring(b, 32, 32, 8, 2, new Color(180, 140, 40));
                break;
            case "crew": // άνθρωπος
                var wt = new Color(220, 225, 235);
                Disc(b, 32, 21, 9, wt);
                RoundRect(b, 19, 33, 26, 23, 9, wt, 1f);
                break;
        }
    }

    /// <summary>Εικονίδια UI της μπάρας εργαλείων (research/clock/save/menu/mute) & του popup ταχύτητας.</summary>
    public static Dictionary<string, Texture2D> CreateUiIcons(GraphicsDevice gd)
    {
        var icons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in new[] { "research", "speed", "save", "menu", "mute_on", "mute_off", "center",
                                   "crew_needed", "depleted",
                                   "pause", "speed1", "speed2", "speed4",
                                   "sol", "sponsor", "temperature", "pressure", "planet", "biomass" })
        {
            var buffer = new Color[Size * Size];
            DrawUiSymbol(buffer, id);
            var tex = new Texture2D(gd, Size, Size);
            tex.SetData(buffer);
            icons[id] = tex;
        }
        return icons;
    }

    private static void DrawUiSymbol(Color[] b, string id)
    {
        var steel = new Color(210, 215, 225);
        var play = new Color(130, 230, 150);
        switch (id)
        {
            case "research": // άτομο
                Ring(b, 32, 32, 17, 3, new Color(190, 150, 255));
                Ring(b, 32, 32, 10, 2, new Color(150, 120, 220));
                Disc(b, 32, 32, 5, new Color(220, 200, 255));
                break;
            case "speed": // ρολόι
                Ring(b, 32, 32, 17, 3, steel);
                Line(b, 32, 32, 32, 19, 3, steel);
                Line(b, 32, 32, 42, 37, 3, steel);
                break;
            case "save": // δισκέτα
                RoundRect(b, 15, 15, 34, 34, 3, new Color(110, 160, 225), 1f);
                Rect(b, 23, 15, 18, 11, new Color(55, 85, 135));   // μεταλλικό κλείστρο
                Rect(b, 34, 17, 4, 7, new Color(205, 215, 230));   // ετικέτα κλείστρου
                Rect(b, 22, 31, 20, 16, new Color(210, 220, 235)); // ετικέτα
                break;
            case "menu": // hamburger
                Rect(b, 15, 19, 34, 5, steel);
                Rect(b, 15, 30, 34, 5, steel);
                Rect(b, 15, 41, 34, 5, steel);
                break;
            case "mute_on": // ηχείο + κύματα
                Rect(b, 12, 27, 8, 10, steel);
                Tri(b, 20, 32, 33, 17, 33, 47, steel);
                Ring(b, 8, 32, 32, 3, new Color(150, 200, 245));
                Ring(b, 8, 32, 40, 3, new Color(150, 200, 245));
                break;
            case "mute_off": // ηχείο + X (σίγαση)
                Rect(b, 12, 27, 8, 10, steel);
                Tri(b, 20, 32, 33, 17, 33, 47, steel);
                Line(b, 40, 23, 54, 41, 3, new Color(240, 90, 80));
                Line(b, 54, 23, 40, 41, 3, new Color(240, 90, 80));
                break;
            case "center": // στόχαστρο (κεντράρισμα στο landing module)
                var cross = new Color(150, 200, 245);
                Ring(b, 32, 32, 14, 3, cross);
                Disc(b, 32, 32, 3, cross);
                Line(b, 32, 6, 32, 15, 3, cross);   // πάνω
                Line(b, 32, 49, 32, 58, 3, cross);  // κάτω
                Line(b, 6, 32, 15, 32, 3, cross);   // αριστερά
                Line(b, 49, 32, 58, 32, 3, cross);  // δεξιά
                break;
            case "crew_needed": // άποικος + πορτοκαλί «+» (κτήριο που ζητά προσωπικό)
                var person = new Color(150, 200, 245);
                Disc(b, 25, 24, 8, person);                     // κεφάλι
                RoundRect(b, 13, 34, 24, 20, 8, person, 1f);    // σώμα
                var plus = new Color(255, 180, 70);
                Rect(b, 44, 33, 5, 17, plus);                   // κάθετη ράβδος του +
                Rect(b, 39, 39, 15, 5, plus);                   // οριζόντια ράβδος του +
                break;
            case "depleted": // άδειος ρόμβος κοιτάσματος + κόκκινο κάτω βέλος (εξάντληση)
                var edge = new Color(150, 155, 165);
                Line(b, 32, 12, 50, 32, 3, edge);
                Line(b, 50, 32, 32, 52, 3, edge);
                Line(b, 32, 52, 14, 32, 3, edge);
                Line(b, 14, 32, 32, 12, 3, edge);
                var down = new Color(240, 90, 80);
                Line(b, 32, 22, 32, 38, 4, down);
                Tri(b, 32, 45, 24, 34, 40, 34, down);           // μύτη βέλους προς τα κάτω
                break;
            case "pause":
                Rect(b, 22, 17, 7, 30, new Color(235, 240, 250));
                Rect(b, 35, 17, 7, 30, new Color(235, 240, 250));
                break;
            case "speed1": // ένα «play»
                Tri(b, 25, 17, 25, 47, 45, 32, play);
                break;
            case "speed2": // διπλό
                Tri(b, 17, 19, 17, 45, 32, 32, play);
                Tri(b, 32, 19, 32, 45, 47, 32, play);
                break;
            case "speed4": // τριπλό
                Tri(b, 13, 21, 13, 43, 26, 32, play);
                Tri(b, 26, 21, 26, 43, 39, 32, play);
                Tri(b, 39, 21, 39, 43, 52, 32, play);
                break;
            case "sol": // ήλιος (Martian sol)
                var sun = new Color(255, 200, 80);
                Disc(b, 32, 32, 10, sun);
                Line(b, 32, 8, 32, 17, 3, sun); Line(b, 32, 47, 32, 56, 3, sun);
                Line(b, 8, 32, 17, 32, 3, sun); Line(b, 47, 32, 56, 32, 3, sun);
                Line(b, 15, 15, 21, 21, 3, sun); Line(b, 43, 43, 49, 49, 3, sun);
                Line(b, 49, 15, 43, 21, 3, sun); Line(b, 21, 43, 15, 49, 3, sun);
                break;
            case "sponsor": // σημαία σε ιστό
                Rect(b, 19, 11, 3, 42, new Color(200, 205, 215));
                Tri(b, 22, 13, 22, 31, 47, 22, new Color(120, 180, 240));
                break;
            case "temperature": // θερμόμετρο
                var tg = new Color(210, 215, 225);
                var tr = new Color(230, 90, 80);
                RoundRect(b, 28, 9, 8, 30, 4, tg, 1f);
                Disc(b, 32, 47, 9, tg);
                Disc(b, 32, 47, 6, tr);
                Rect(b, 30, 23, 4, 24, tr);
                break;
            case "pressure": // μανόμετρο
                var pg = new Color(210, 215, 225);
                Ring(b, 32, 34, 16, 3, pg);
                Line(b, 32, 34, 43, 24, 3, new Color(230, 90, 80));
                Disc(b, 32, 34, 3, pg);
                break;
            case "planet": // ο Άρης (συνολικό terraforming)
                var mars = new Color(200, 110, 70);
                Disc(b, 32, 32, 16, mars);
                Disc(b, 26, 27, 4, new Color(170, 85, 55));
                Disc(b, 38, 37, 5, new Color(180, 95, 60));
                Disc(b, 34, 22, 3, new Color(220, 145, 100));
                break;
            case "biomass": // φύλλο (βλάστηση)
                var lg = new Color(95, 205, 95);
                var ld = new Color(65, 150, 65);
                Tri(b, 16, 48, 48, 16, 44, 44, lg);
                Tri(b, 16, 48, 20, 20, 48, 16, lg);
                Line(b, 16, 48, 46, 18, 2, ld);
                break;
        }
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
        "Civic" => new Color(150, 190, 230),
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
            // ---------- Phase 2A/2B buildings ----------
            case "cryo_carbon_capturer": // χιονονιφάδα (ψύξη/δέσμευση CO2)
                var ice = new Color(190, 235, 255);
                Line(b, 32, 13, 32, 51, 3, ice);
                Line(b, 16, 22, 48, 42, 3, ice);
                Line(b, 48, 22, 16, 42, 3, ice);
                Line(b, 32, 13, 27, 19, 2, ice); Line(b, 32, 13, 37, 19, 2, ice);
                Line(b, 32, 51, 27, 45, 2, ice); Line(b, 32, 51, 37, 45, 2, ice);
                Disc(b, 32, 32, 4, new Color(235, 250, 255));
                break;
            case "high_density_arcology": // ουρανοξύστης με παράθυρα
                var arcWall = new Color(205, 211, 221);
                var arcWin = new Color(120, 180, 235);
                Line(b, 32, 12, 32, 6, 2, arcWall);
                RoundRect(b, 24, 12, 16, 40, 3, arcWall, 1f);
                for (int wy = 16; wy <= 44; wy += 8)
                { Rect(b, 27, wy, 4, 4, arcWin); Rect(b, 33, wy, 4, 4, arcWin); }
                break;
            case "district_town_hall": // πρόσοψη με αέτωμα & κίονες
                var stone = new Color(222, 226, 234);
                Tri(b, 32, 14, 15, 27, 49, 27, stone);
                Rect(b, 16, 27, 32, 4, stone);
                Rect(b, 20, 31, 3, 17, stone); Rect(b, 27, 31, 3, 17, stone);
                Rect(b, 34, 31, 3, 17, stone); Rect(b, 41, 31, 3, 17, stone);
                Rect(b, 15, 48, 34, 4, stone);
                break;
            case "atmospheric_scrubber": // πύργος φίλτρου + καθαρός αέρας
                var scrBody = new Color(200, 210, 220);
                RoundRect(b, 26, 20, 12, 30, 4, scrBody, 1f);
                Rect(b, 28, 27, 8, 2, dark); Rect(b, 28, 32, 8, 2, dark); Rect(b, 28, 37, 8, 2, dark);
                Disc(b, 32, 15, 5, new Color(120, 210, 140));
                break;
            case "quantum_processor_plant": // μικροτσίπ
                var chip = new Color(120, 150, 205);
                RoundRect(b, 22, 22, 20, 20, 3, chip, 1f);
                Rect(b, 28, 28, 8, 8, new Color(190, 215, 255));
                for (int py = 26; py <= 38; py += 6)
                { Rect(b, 18, py, 4, 2, steel); Rect(b, 42, py, 4, 2, steel); }
                for (int px = 26; px <= 38; px += 6)
                { Rect(b, px, 18, 2, 4, steel); Rect(b, px, 42, 2, 4, steel); }
                break;
            case "deep_core_drill": // γεωτρύπανο προς τα κάτω
                var drillMetal = new Color(210, 216, 228);
                Rect(b, 29, 12, 6, 22, drillMetal);
                Rect(b, 29, 20, 6, 1, dark); Rect(b, 29, 26, 6, 1, dark);
                Tri(b, 26, 34, 38, 34, 32, 51, new Color(255, 180, 90));
                Rect(b, 17, 47, 30, 3, new Color(150, 110, 80));
                break;
            case "ai_drone_hive": // drone (quadcopter)
                var droneBody = new Color(200, 210, 225);
                var rotor = new Color(150, 200, 245);
                Line(b, 24, 30, 18, 22, 2, droneBody); Line(b, 40, 30, 46, 22, 2, droneBody);
                Line(b, 24, 34, 18, 42, 2, droneBody); Line(b, 40, 34, 46, 42, 2, droneBody);
                Disc(b, 18, 22, 5, rotor); Disc(b, 46, 22, 5, rotor);
                Disc(b, 18, 42, 5, rotor); Disc(b, 46, 42, 5, rotor);
                RoundRect(b, 27, 28, 10, 8, 3, droneBody, 1f);
                break;
            case "sea_wall": // τείχος + κύματα
                var swWall = new Color(202, 202, 208);
                Rect(b, 16, 22, 32, 22, swWall);
                Rect(b, 16, 30, 32, 1, dark); Rect(b, 16, 38, 32, 1, dark);
                Rect(b, 32, 22, 1, 8, dark); Rect(b, 24, 30, 1, 8, dark); Rect(b, 40, 30, 1, 8, dark);
                var wave = new Color(70, 150, 235);
                Line(b, 14, 50, 22, 46, 2, wave); Line(b, 22, 46, 30, 50, 2, wave);
                Line(b, 30, 50, 38, 46, 2, wave); Line(b, 38, 46, 46, 50, 2, wave);
                break;
            case "genetic_vault": // διπλή έλικα DNA
                var dna = new Color(120, 220, 140);
                var rung = new Color(180, 240, 190);
                for (int i = 0; i < 6; i++)
                {
                    int yy = 15 + i * 6;
                    int off = (i % 2 == 0) ? 7 : -7;
                    Line(b, 32 - off, yy, 32 + off, yy, 1, rung);
                    Disc(b, 32 - off, yy, 2, dna); Disc(b, 32 + off, yy, 2, dna);
                }
                break;
            case "interplanetary_stock_exchange": // ανοδικό γράφημα + χρυσό βέλος
                var barCol = new Color(120, 210, 140);
                Rect(b, 18, 40, 6, 10, barCol); Rect(b, 27, 32, 6, 18, barCol); Rect(b, 36, 24, 6, 26, barCol);
                var gold = new Color(240, 195, 70);
                Line(b, 16, 32, 45, 17, 3, gold);
                Tri(b, 48, 15, 39, 17, 45, 24, gold);
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

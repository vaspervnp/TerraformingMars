using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;

namespace TerraformingMars.Game.Rendering;

/// <summary>
/// Χτίζει στατικά vertex buffers για έναν <see cref="HexMap"/> (fill / outline / resource markers)
/// και τα σχεδιάζει με <see cref="BasicEffect"/> κάτω από το camera matrix.
/// Pointy-top εξάγωνα, world coords από <see cref="HexLayout"/>.
/// </summary>
public sealed class HexMapRenderer : IDisposable
{
    private static readonly Color OutlineColor = new(0, 0, 0, 60);

    private readonly GraphicsDevice _gd;
    private readonly BasicEffect _effect;
    private readonly float _size;

    private VertexBuffer? _fill;
    private int _fillTriangles;
    private VertexBuffer? _outline;
    private int _outlineLines;
    private VertexBuffer? _markers;
    private int _markerTriangles;

    private readonly VertexPositionColor[] _highlight = new VertexPositionColor[7];
    private readonly List<VertexPositionColor> _buildFill = new();
    private readonly List<VertexPositionColor> _buildLine = new();

    public HexLayout Layout { get; }

    public HexMapRenderer(GraphicsDevice gd, float size)
    {
        _gd = gd;
        _size = size;
        Layout = new HexLayout(size, 0, 0);
        _effect = new BasicEffect(gd)
        {
            VertexColorEnabled = true,
            TextureEnabled = false,
            LightingEnabled = false,
            World = Matrix.Identity
        };
    }

    private Vector2 Center(Hex h)
    {
        var (x, y) = Layout.HexToPixel(h);
        return new Vector2((float)x, (float)y);
    }

    private Vector2 Corner(Vector2 center, int i)
    {
        float ang = MathHelper.ToRadians(60f * i - 30f); // pointy-top
        return new Vector2(center.X + _size * MathF.Cos(ang), center.Y + _size * MathF.Sin(ang));
    }

    /// <summary>Διαμορφώνει το χρώμα terrain ανά υψόμετρο για ψευδο-ανάγλυφο (κοιλάδες σκούρες, κορυφές φωτεινές).</summary>
    private static Color ShadeByElevation(Color baseColor, TerrainType terrain, float elevation)
    {
        if (terrain is TerrainType.PolarIce or TerrainType.Water) return baseColor; // μένουν επίπεδα
        float shade = MathHelper.Clamp(0.80f + elevation * 0.40f, 0.55f, 1.25f);
        return Shade(baseColor, shade);
    }

    private static Color Shade(Color c, float f) => new(
        (int)MathHelper.Clamp(c.R * f, 0, 255),
        (int)MathHelper.Clamp(c.G * f, 0, 255),
        (int)MathHelper.Clamp(c.B * f, 0, 255),
        c.A);

    public void Build(HexMap map)
    {
        var fill = new List<VertexPositionColor>(map.Count * 18);
        var outline = new List<VertexPositionColor>(map.Count * 12);
        var markers = new List<VertexPositionColor>();

        var corners = new Vector2[6];

        foreach (var tile in map.Tiles)
        {
            Vector2 c = Center(tile.Coord);
            Color terrain = ShadeByElevation(TerrainPalette.Terrain(tile.Terrain), tile.Terrain, tile.Elevation);
            for (int i = 0; i < 6; i++) corners[i] = Corner(c, i);

            for (int i = 0; i < 6; i++)
            {
                int j = (i + 1) % 6;
                fill.Add(new VertexPositionColor(new Vector3(c, 0f), terrain));
                fill.Add(new VertexPositionColor(new Vector3(corners[i], 0f), terrain));
                fill.Add(new VertexPositionColor(new Vector3(corners[j], 0f), terrain));

                outline.Add(new VertexPositionColor(new Vector3(corners[i], 0f), OutlineColor));
                outline.Add(new VertexPositionColor(new Vector3(corners[j], 0f), OutlineColor));
            }

            if (!tile.Deposit.IsEmpty)
            {
                Color rc = TerrainPalette.Resource(tile.Deposit.Type);
                float r = _size * (tile.Deposit.Hidden ? 0.26f : 0.36f);
                if (tile.Deposit.Hidden) rc = new Color(rc, 0.45f);

                var up = new Vector3(c.X, c.Y - r, 0f);
                var right = new Vector3(c.X + r, c.Y, 0f);
                var down = new Vector3(c.X, c.Y + r, 0f);
                var left = new Vector3(c.X - r, c.Y, 0f);

                markers.Add(new VertexPositionColor(up, rc));
                markers.Add(new VertexPositionColor(right, rc));
                markers.Add(new VertexPositionColor(down, rc));
                markers.Add(new VertexPositionColor(up, rc));
                markers.Add(new VertexPositionColor(down, rc));
                markers.Add(new VertexPositionColor(left, rc));
            }
        }

        SetBuffer(ref _fill, fill);
        _fillTriangles = fill.Count / 3;
        SetBuffer(ref _outline, outline);
        _outlineLines = outline.Count / 2;
        SetBuffer(ref _markers, markers);
        _markerTriangles = markers.Count / 3;
    }

    private void SetBuffer(ref VertexBuffer? buffer, List<VertexPositionColor> verts)
    {
        buffer?.Dispose();
        if (verts.Count == 0) { buffer = null; return; }
        buffer = new VertexBuffer(_gd, typeof(VertexPositionColor), verts.Count, BufferUsage.WriteOnly);
        buffer.SetData(verts.ToArray());
    }

    public void Draw(Matrix view, Matrix projection)
    {
        _gd.BlendState = BlendState.NonPremultiplied;
        _gd.DepthStencilState = DepthStencilState.None;
        _gd.RasterizerState = RasterizerState.CullNone;

        _effect.View = view;
        _effect.Projection = projection;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            if (_fill is not null && _fillTriangles > 0)
            {
                _gd.SetVertexBuffer(_fill);
                _gd.DrawPrimitives(PrimitiveType.TriangleList, 0, _fillTriangles);
            }
            if (_outline is not null && _outlineLines > 0)
            {
                _gd.SetVertexBuffer(_outline);
                _gd.DrawPrimitives(PrimitiveType.LineList, 0, _outlineLines);
            }
            if (_markers is not null && _markerTriangles > 0)
            {
                _gd.SetVertexBuffer(_markers);
                _gd.DrawPrimitives(PrimitiveType.TriangleList, 0, _markerTriangles);
            }
        }
    }

    public void DrawHighlight(Hex hex, Color color, Matrix view, Matrix projection)
    {
        Vector2 c = Center(hex);
        for (int i = 0; i <= 6; i++)
            _highlight[i] = new VertexPositionColor(new Vector3(Corner(c, i % 6), 0f), color);

        _effect.View = view;
        _effect.Projection = projection;
        _gd.BlendState = BlendState.NonPremultiplied;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.LineStrip, _highlight, 0, 6);
        }
    }

    /// <summary>Σχεδιάζει τα κτίρια ως τετράγωνα ανά κατηγορία· operational = γεμάτα, υπό κατασκευή = ημιδιάφανα με περίγραμμα.</summary>
    public void DrawBuildings(IEnumerable<Building> buildings, Matrix view, Matrix projection)
    {
        _buildFill.Clear();
        _buildLine.Clear();
        float s2 = _size * 0.42f;

        foreach (var b in buildings)
        {
            Vector2 c = Center(b.Location);
            Color col = CategoryColor(b.Definition.Category);
            bool operational = b.State == BuildingState.Operational;
            Color fill = operational ? col : new Color(col, 0.35f);

            var p0 = new Vector3(c.X - s2, c.Y - s2, 0f);
            var p1 = new Vector3(c.X + s2, c.Y - s2, 0f);
            var p2 = new Vector3(c.X + s2, c.Y + s2, 0f);
            var p3 = new Vector3(c.X - s2, c.Y + s2, 0f);

            _buildFill.Add(new(p0, fill)); _buildFill.Add(new(p1, fill)); _buildFill.Add(new(p2, fill));
            _buildFill.Add(new(p0, fill)); _buildFill.Add(new(p2, fill)); _buildFill.Add(new(p3, fill));

            if (!operational)
            {
                Color edge = b.State == BuildingState.Disabled ? new Color(255, 80, 80) : Color.White;
                _buildLine.Add(new(p0, edge)); _buildLine.Add(new(p1, edge));
                _buildLine.Add(new(p1, edge)); _buildLine.Add(new(p2, edge));
                _buildLine.Add(new(p2, edge)); _buildLine.Add(new(p3, edge));
                _buildLine.Add(new(p3, edge)); _buildLine.Add(new(p0, edge));
            }
        }

        if (_buildFill.Count == 0) return;

        _gd.BlendState = BlendState.NonPremultiplied;
        _gd.DepthStencilState = DepthStencilState.None;
        _gd.RasterizerState = RasterizerState.CullNone;
        _effect.View = view;
        _effect.Projection = projection;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _buildFill.ToArray(), 0, _buildFill.Count / 3);
            if (_buildLine.Count > 0)
                _gd.DrawUserPrimitives(PrimitiveType.LineList, _buildLine.ToArray(), 0, _buildLine.Count / 2);
        }
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
        _ => new Color(200, 200, 205)
    };

    public void Dispose()
    {
        _fill?.Dispose();
        _outline?.Dispose();
        _markers?.Dispose();
        _effect.Dispose();
    }
}

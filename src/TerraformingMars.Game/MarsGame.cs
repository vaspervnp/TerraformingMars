using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Persistence;
using TerraformingMars.Core.Planet;
using TerraformingMars.Core.Simulation;
using TerraformingMars.Game.Audio;
using TerraformingMars.Game.Rendering;

namespace TerraformingMars.Game;

/// <summary>
/// Φάσεις 1–2 — οπτικός viewer με ζωντανή σιμουλασιόν.
/// Pan (drag), zoom (wheel), hover/select hex (mouse → <see cref="HexLayout.PixelToHex"/>),
/// regenerate (N), έλεγχος ταχύτητας (Space, 1/2/3). HUD on-screen με SpriteFont.
/// </summary>
public class MarsGame : Microsoft.Xna.Framework.Game
{
    private const float HexSize = 22f;
    private static readonly string SavePath = Path.Combine(AppContext.BaseDirectory, "terraforming_save.json");

    private readonly GraphicsDeviceManager _graphics;
    private readonly Camera2D _camera = new();

    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private Texture2D _pixel = null!;

    private HexMapRenderer _renderer = null!;
    private MapGenerationSettings _settings = null!;
    private HexMap _map = null!;
    private World _world = null!;
    private int _seed = 424242;
    private int _lastMapRevision;

    private SponsorCatalog _sponsorCatalog = null!;
    private SponsorProfile _sponsor = null!;
    private int _sponsorIndex;

    private MouseState _prevMouse;
    private KeyboardState _prevKeys;

    private Hex _hoverHex;
    private bool _hasHover;
    private HexTile? _hoveredTile;
    private HexTile? _selected;
    private Building? _selectedBuilding;

    // Build mode (Φάση 3)
    private BuildingCatalog _catalog = null!;
    private List<BuildingDefinition> _buildables = null!;
    private int _buildIndex;
    private bool _buildMode;
    private string _status = "";
    private double _statusTimer;
    private Point _mouseDownPos;

    // Polish: μενού, audio, ανίχνευση μεταβάσεων (win/lose/research/events)
    private enum GameState { Menu, Playing }
    private GameState _state = GameState.Menu;
    private AudioManager _audio = null!;
    private int _prevResearchedCount;
    private string _lastNotification = "";
    private bool _prevWon;
    private bool _prevLost;

    public MarsGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "Terraforming Mars";
    }

    protected override void Initialize()
    {
        _catalog = BuildingCatalog.LoadDefault();
        _buildables = _catalog.Buildables.ToList();

        _sponsorCatalog = SponsorCatalog.LoadDefault();
        _sponsorIndex = Math.Max(0, _sponsorCatalog.All.ToList().FindIndex(s => s.Id == "normal"));
        _sponsor = _sponsorCatalog.All[_sponsorIndex];

        _settings = new MapGenerationSettings { Width = 64, Height = 44, Seed = _seed };
        _map = new MapGenerator(_settings).Generate();
        _world = ColonyFactory.CreateStartingWorld(_map, _catalog, _sponsor, enableEvents: true);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Hud");
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _renderer = new HexMapRenderer(GraphicsDevice, HexSize);
        _renderer.Build(_map);
        _lastMapRevision = _world.MapRevision;
        _audio = new AudioManager();
        FitCamera();
    }

    private void StartGame()
    {
        _settings = new MapGenerationSettings { Width = 64, Height = 44, Seed = _seed };
        _map = new MapGenerator(_settings).Generate();
        _world = ColonyFactory.CreateStartingWorld(_map, _catalog, _sponsor, enableEvents: true);
        _renderer.Build(_map);
        _lastMapRevision = _world.MapRevision;
        _selected = null;
        _selectedBuilding = null;
        _buildMode = false;
        ResetTransitionTrackers();
        FitCamera();
        _state = GameState.Playing;
    }

    private void ResetTransitionTrackers()
    {
        _prevResearchedCount = _world.Colony.Tech.Researched.Count;
        _lastNotification = _world.EventNotifications.Count > 0 ? _world.EventNotifications[^1] : "";
        _prevWon = false;
        _prevLost = false;
    }

    private void CheckAudioTransitions()
    {
        int researched = _world.Colony.Tech.Researched.Count;
        if (researched > _prevResearchedCount) _audio.Chime();
        _prevResearchedCount = researched;

        string last = _world.EventNotifications.Count > 0 ? _world.EventNotifications[^1] : "";
        if (last.Length > 0 && last != _lastNotification) _audio.Alert();
        _lastNotification = last;

        bool won = _world.IsTerraformed;
        if (won && !_prevWon) _audio.Win();
        _prevWon = won;

        bool lost = _world.IsLost;
        if (lost && !_prevLost) _audio.Lose();
        _prevLost = lost;
    }

    private void UpdateMenu(KeyboardState keys)
    {
        if (KeyPressed(keys, Keys.Escape)) Exit();
        if (KeyPressed(keys, Keys.Left) || KeyPressed(keys, Keys.A))
            _sponsorIndex = (_sponsorIndex - 1 + _sponsorCatalog.All.Count) % _sponsorCatalog.All.Count;
        if (KeyPressed(keys, Keys.Right) || KeyPressed(keys, Keys.D))
            _sponsorIndex = (_sponsorIndex + 1) % _sponsorCatalog.All.Count;
        _sponsor = _sponsorCatalog.All[_sponsorIndex];

        if (KeyPressed(keys, Keys.R)) _seed = new Random().Next();
        if (KeyPressed(keys, Keys.Enter)) StartGame();
    }

    private void DrawMenu()
    {
        GraphicsDevice.Clear(new Color(0x0b, 0x0b, 0x12));
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        float h = GraphicsDevice.Viewport.Height;
        DrawCentered("TERRAFORMING MARS", h * 0.20f, 2.4f, new Color(230, 120, 80));

        float y = h * 0.42f;
        DrawCentered($"<   Sponsor:  {_sponsor.Name}   >", y, 1.3f, HudWhite); y += 40;
        DrawCentered(_sponsor.Description, y, 1.0f, HudDim); y += 48;
        DrawCentered($"Seed:  {_seed}", y, 1.2f, HudWhite); y += 64;
        DrawCentered("Left/Right = sponsor     R = random seed", y, 1.0f, HudDim); y += 30;
        DrawCentered("Enter = start     Esc = quit", y, 1.0f, HudDim);

        _spriteBatch.End();
    }

    private void DrawCentered(string text, float y, float scale, Color color)
    {
        var size = _font.MeasureString(text);
        var pos = new Vector2((GraphicsDevice.Viewport.Width - size.X * scale) / 2f, y);
        _spriteBatch.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void Regenerate(int seed)
    {
        _seed = seed;
        _settings = new MapGenerationSettings { Width = _settings.Width, Height = _settings.Height, Seed = _seed };
        _map = new MapGenerator(_settings).Generate();
        _world = ColonyFactory.CreateStartingWorld(_map, _catalog, _sponsor, enableEvents: true);
        _renderer.Build(_map);
        _lastMapRevision = _world.MapRevision;
        _selected = null;
        _selectedBuilding = null;
        ResetTransitionTrackers();
    }

    private void FitCamera()
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var t in _map.Tiles)
        {
            var (x, y) = _renderer.Layout.HexToPixel(t.Coord);
            minX = MathF.Min(minX, (float)x); minY = MathF.Min(minY, (float)y);
            maxX = MathF.Max(maxX, (float)x); maxY = MathF.Max(maxY, (float)y);
        }

        float w = (maxX - minX) + HexSize * 2f;
        float h = (maxY - minY) + HexSize * 2f;
        _camera.Position = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        float vw = GraphicsDevice.Viewport.Width;
        float vh = GraphicsDevice.Viewport.Height;
        _camera.Zoom = MathHelper.Clamp(MathF.Min(vw / w, vh / h), _camera.MinZoom, _camera.MaxZoom);
    }

    protected override void Update(GameTime gameTime)
    {
        var mouse = Mouse.GetState();
        var keys = Keyboard.GetState();

        if (_state == GameState.Menu)
        {
            UpdateMenu(keys);
            _prevMouse = mouse;
            _prevKeys = keys;
            base.Update(gameTime);
            return;
        }

        // Esc → πίσω στο μενού (από εκεί κλείνει το παιχνίδι)
        if (KeyPressed(keys, Keys.Escape))
        {
            _state = GameState.Menu;
            _prevMouse = mouse;
            _prevKeys = keys;
            base.Update(gameTime);
            return;
        }
        if (KeyPressed(keys, Keys.U)) _audio.Enabled = !_audio.Enabled;

        _camera.SetViewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

        // Pan με drag (αριστερό ή μεσαίο κουμπί)
        bool dragging = mouse.LeftButton == ButtonState.Pressed || mouse.MiddleButton == ButtonState.Pressed;
        bool wasDragging = _prevMouse.LeftButton == ButtonState.Pressed || _prevMouse.MiddleButton == ButtonState.Pressed;
        if (dragging && wasDragging)
            _camera.Pan(new Vector2(mouse.X - _prevMouse.X, mouse.Y - _prevMouse.Y));

        // Zoom με wheel, γύρω από τον κέρσορα
        int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (wheel != 0)
            _camera.ZoomAt(new Vector2(mouse.X, mouse.Y), wheel > 0 ? 1.1f : 1f / 1.1f);

        // Pan με πληκτρολόγιο
        float panSpeed = 700f * (float)gameTime.ElapsedGameTime.TotalSeconds / _camera.Zoom;
        Vector2 move = Vector2.Zero;
        if (keys.IsKeyDown(Keys.W) || keys.IsKeyDown(Keys.Up)) move.Y -= 1;
        if (keys.IsKeyDown(Keys.S) || keys.IsKeyDown(Keys.Down)) move.Y += 1;
        if (keys.IsKeyDown(Keys.A) || keys.IsKeyDown(Keys.Left)) move.X -= 1;
        if (keys.IsKeyDown(Keys.D) || keys.IsKeyDown(Keys.Right)) move.X += 1;
        if (move != Vector2.Zero) _camera.Position += Vector2.Normalize(move) * panSpeed;

        // Νέος χάρτης (N) / fit (F) / εναλλαγή sponsor (G, ξεκινά νέο παιχνίδι)
        if (KeyPressed(keys, Keys.N)) Regenerate(new Random().Next());
        if (KeyPressed(keys, Keys.F)) FitCamera();
        if (KeyPressed(keys, Keys.G))
        {
            _sponsorIndex = (_sponsorIndex + 1) % _sponsorCatalog.All.Count;
            _sponsor = _sponsorCatalog.All[_sponsorIndex];
            Regenerate(_seed);
        }

        // Save (F5) / Load (F9)
        if (KeyPressed(keys, Keys.F5))
        {
            File.WriteAllText(SavePath, SaveSystem.ToJson(_world, _sponsor));
            _status = "Game saved";
            _statusTimer = 3.0;
        }
        if (KeyPressed(keys, Keys.F9) && File.Exists(SavePath))
        {
            _world = SaveSystem.Load(File.ReadAllText(SavePath), _catalog, _sponsorCatalog, out _sponsor);
            _map = _world.Map;
            _sponsorIndex = Math.Max(0, _sponsorCatalog.All.ToList().FindIndex(s => s.Id == _sponsor.Id));
            _renderer.Build(_map);
            _lastMapRevision = _world.MapRevision;
            _selected = null;
            _selectedBuilding = null;
            ResetTransitionTrackers();
            _status = "Game loaded";
            _statusTimer = 3.0;
        }

        // Έλεγχος ταχύτητας σιμουλασιόν
        if (KeyPressed(keys, Keys.Space))
            _world.Clock.Speed = _world.Clock.Speed == GameSpeed.Paused ? GameSpeed.Normal : GameSpeed.Paused;
        if (KeyPressed(keys, Keys.D1)) _world.Clock.Speed = GameSpeed.Normal;
        if (KeyPressed(keys, Keys.D2)) _world.Clock.Speed = GameSpeed.Fast;
        if (KeyPressed(keys, Keys.D3)) _world.Clock.Speed = GameSpeed.Ultra;

        // Διαθέσιμα κτίρια = όσα έχουν ξεκλειδωθεί από έρευνα
        _buildables = _catalog.Buildables.Where(d => _world.Colony.Tech.IsResearched(d.RequiredTech)).ToList();
        if (_buildIndex >= _buildables.Count) _buildIndex = 0;

        // Επιλογή έρευνας: T κυκλώνει τα διαθέσιμα techs ως target
        if (KeyPressed(keys, Keys.T))
        {
            var available = _world.Colony.Tech.Available.ToList();
            if (available.Count > 0)
            {
                int idx = _world.Colony.Tech.CurrentTarget is null
                    ? -1
                    : available.FindIndex(t => t.Id == _world.Colony.Tech.CurrentTarget);
                _world.Colony.Tech.StartResearch(available[(idx + 1) % available.Count].Id);
                _audio.Blip();
            }
        }

        // Build mode: B μπαίνει/κυκλώνει τύπους κτιρίων
        if (KeyPressed(keys, Keys.B))
        {
            if (!_buildMode) { _buildMode = true; _buildIndex = 0; }
            else _buildIndex = (_buildIndex + 1) % _buildables.Count;
        }

        // Ανάθεση/αφαίρεση αποίκων στο επιλεγμένο κτίριο (+/-)
        if (_selectedBuilding is { } selected && selected.Definition.MaxWorkers > 0)
        {
            if (KeyPressed(keys, Keys.OemPlus) || KeyPressed(keys, Keys.Add)) AssignCrew(selected);
            if (KeyPressed(keys, Keys.OemMinus) || KeyPressed(keys, Keys.Subtract)) RemoveCrew(selected);
        }

        if (_statusTimer > 0) _statusTimer -= gameTime.ElapsedGameTime.TotalSeconds;

        // Προώθηση σιμουλασιόν (fixed-timestep μέσα στο World)
        _world.Update(gameTime.ElapsedGameTime.TotalSeconds);

        // Αν άλλαξε το terrain (πάγος→νερό), ξαναχτίζουμε τον χάρτη
        if (_world.MapRevision != _lastMapRevision)
        {
            _renderer.Build(_map);
            _lastMapRevision = _world.MapRevision;
        }

        CheckAudioTransitions();

        // Hover hex μέσω screen→world→PixelToHex
        Vector2 world = _camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
        _hoverHex = _renderer.Layout.PixelToHex(world.X, world.Y);
        _hasHover = _map.TryGetTile(_hoverHex, out _hoveredTile) && _hoveredTile is not null;

        // Αριστερό κλικ (χωρίς drag) σε build mode → τοποθέτηση κτιρίου
        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            _mouseDownPos = new Point(mouse.X, mouse.Y);
        if (mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed)
        {
            int dx = mouse.X - _mouseDownPos.X, dy = mouse.Y - _mouseDownPos.Y;
            bool isClick = dx * dx + dy * dy < 36; // < 6px ⇒ κλικ (όχι drag/pan)
            if (isClick && _buildMode && _hasHover) TryPlaceSelected();
        }

        // Δεξί κλικ: ακυρώνει το build mode, αλλιώς επιλέγει tile/κτίριο
        if (mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released)
        {
            if (_buildMode) _buildMode = false;
            else if (_hasHover)
            {
                _selected = _hoveredTile;
                _selectedBuilding = _world.Colony.Buildings.FirstOrDefault(b => b.Location == _hoverHex);
            }
        }

        _prevMouse = mouse;
        _prevKeys = keys;
        base.Update(gameTime);
    }

    private bool KeyPressed(KeyboardState keys, Keys key) => keys.IsKeyDown(key) && _prevKeys.IsKeyUp(key);

    private void TryPlaceSelected()
    {
        var def = _buildables[_buildIndex];
        var result = _world.Colony.TryPlaceBuilding(def, _hoverHex, _map);
        _status = result.Success ? $"Built {def.Name}" : $"Cannot place {def.Name}: {result.Error}";
        _statusTimer = 3.0;
        if (result.Success) _audio.Blip();
    }

    private static string CostString(BuildingDefinition def) =>
        def.Cost.Count == 0 ? "free" : string.Join(", ", def.Cost.Select(kv => $"{kv.Value:0} {kv.Key}"));

    private void AssignCrew(Building building)
    {
        if (building.Workers.Count >= building.Definition.MaxWorkers) return;
        var candidate = _world.Colony.IdleColonists
            .OrderByDescending(c => c.Specialty == building.Definition.OptimalSpecialty ? 1 : 0)
            .FirstOrDefault();
        if (candidate is not null) _world.Colony.Assign(candidate, building);
    }

    private void RemoveCrew(Building building)
    {
        if (building.Workers.Count > 0)
            _world.Colony.Unassign(building.Workers[^1]);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_state == GameState.Menu)
        {
            DrawMenu();
            base.Draw(gameTime);
            return;
        }

        GraphicsDevice.Clear(new Color(0x0b, 0x0b, 0x12));

        Matrix view = _camera.GetViewMatrix();
        Matrix projection = Matrix.CreateOrthographicOffCenter(
            0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, 0, 0f, 1f);

        _renderer.Draw(view, projection);
        _renderer.DrawBuildings(_world.Colony.Buildings, view, projection);

        if (_selected is not null)
            _renderer.DrawHighlight(_selected.Coord, new Color(0xff, 0xd0, 0x40), view, projection);

        if (_buildMode && _hasHover)
        {
            bool canPlace = _world.Colony.CanPlace(_buildables[_buildIndex], _hoverHex, _map).Success;
            _renderer.DrawHighlight(_hoverHex, canPlace ? new Color(80, 230, 120) : new Color(230, 80, 80), view, projection);
        }
        else if (_hasHover)
        {
            _renderer.DrawHighlight(_hoverHex, Color.White, view, projection);
        }

        DrawHud();

        base.Draw(gameTime);
    }

    // ----------------------------------------------------------------- HUD

    private static readonly Color HudWhite = new(235, 235, 240);
    private static readonly Color HudDim = new(170, 170, 185);
    private static readonly Color HudWarn = new(255, 95, 80);

    private void DrawHud()
    {
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        var clock = _world.Clock;
        var colony = _world.Colony;

        var top = new List<(string text, Color color)>
        {
            ($"Sol {clock.Sol}   {clock.HourOfSol:00}:{clock.MinuteOfHour:00}   [{clock.Speed}]", HudWhite),
            ("", HudWhite),
            (ResLine("Energy", ResourceKind.Energy), RateColor(ResourceKind.Energy)),
            (ResLine("Water", ResourceKind.Water), RateColor(ResourceKind.Water)),
            (ResLine("Oxygen", ResourceKind.Oxygen), RateColor(ResourceKind.Oxygen)),
            (ResLine("Food", ResourceKind.Food), RateColor(ResourceKind.Food)),
            (ResLine("Materials", ResourceKind.Materials), RateColor(ResourceKind.Materials)),
            (ResLine("Credits", ResourceKind.Credits), HudDim),
            ($"Crew       {colony.Crew}   (idle {colony.IdleColonists.Count()})", HudDim),
        };
        if (colony.LifeSupportFailing) top.Add(("!! LIFE SUPPORT FAILURE !!", HudWarn));

        // Έρευνα
        var tech = colony.Tech;
        double researchOut = colony.Buildings
            .Where(b => b.State == BuildingState.Operational)
            .Sum(b => b.Definition.Production.GetValueOrDefault(ResourceKind.Research) * b.WorkerEfficiency());
        if (tech.CurrentTech is { } current)
            top.Add(($"Research   {current.Name}  {tech.CurrentProgress / current.Cost * 100:0}%  (+{researchOut:0.0}/t)",
                new Color(190, 150, 255)));
        else
            top.Add(($"Research   none  (T to choose, {tech.Available.Count()} available)", HudDim));

        // Πλανητικές μετρικές (terraforming)
        var planet = _world.Planet;
        top.Add(("", HudWhite));
        top.Add(($"PLANET   terraforming {planet.OverallProgress * 100:0}%", HudWhite));
        top.Add((MetricLine("Temp", planet.Temperature, "C", planet.Progress(PlanetMetric.Temperature)),
            MetricColor(planet.Progress(PlanetMetric.Temperature))));
        top.Add((MetricLine("Pressure", planet.Pressure, "kPa", planet.Progress(PlanetMetric.Pressure)),
            MetricColor(planet.Progress(PlanetMetric.Pressure))));
        top.Add((MetricLine("Oxygen", planet.Oxygen, "%", planet.Progress(PlanetMetric.Oxygen)),
            MetricColor(planet.Progress(PlanetMetric.Oxygen))));
        top.Add((MetricLine("Water", planet.WaterCoverage * 100, "%", planet.Progress(PlanetMetric.Water)),
            MetricColor(planet.Progress(PlanetMetric.Water))));
        top.Add(($"Biomass  {planet.Biomass * 100,6:0.0} %", new Color(90, 200, 90)));
        int housing = _world.Colony.Buildings.Where(b => b.State == BuildingState.Operational).Sum(b => b.Definition.HousingCapacity);
        top.Add(($"Population {_world.Colony.Colonists.Count}/{housing}", HudDim));

        // Sponsor & alerts (Φάση 6)
        top.Add(("", HudWhite));
        top.Add(($"SPONSOR  {_sponsor.Name}", HudDim));
        foreach (var ev in _world.ActiveEvents)
            top.Add(($"!! {EventLabel(ev.Type)}  {ev.TicksRemaining / 4}s", HudWarn));
        if (_world.SolarEfficiency < 1.0)
            top.Add(($"solar output {_world.SolarEfficiency * 100:0}%", HudWarn));
        if (_world.PowerOutage)
            top.Add(("BROWNOUT - low power", HudWarn));
        if (_world.HasCaveShelter)
            top.Add(("cave shelter active", new Color(120, 230, 120)));
        double minHealth = _world.Colony.Colonists.Count > 0 ? _world.Colony.Colonists.Min(c => c.Health) : 1.0;
        if (minHealth < 0.95)
            top.Add(($"crew health {minHealth * 100:0}%", HudWarn));

        DrawTextPanel(new Vector2(10, 10), top);

        var bottom = new List<(string text, Color color)>();
        if (_hoveredTile is not null)
        {
            var t = _hoveredTile;
            bottom.Add(($"{OffsetCoord.FromHex(t.Coord)}  {t.Terrain}  elev {t.Elevation:0.00}", HudWhite));
            bottom.Add((t.Deposit.IsEmpty
                ? "deposit: none"
                : $"deposit: {t.Deposit.Type} {(int)t.RemainingDeposit}/{t.Deposit.Amount}{(t.Deposit.Hidden ? "  (hidden)" : "")}",
                t.Deposit.IsEmpty ? HudDim : HudWhite));
        }
        else
        {
            bottom.Add(("hover a hex...", HudDim));
        }
        if (_buildMode)
        {
            var def = _buildables[_buildIndex];
            bottom.Add(($"BUILD: {def.Name}   cost: {CostString(def)}", new Color(120, 230, 120)));
            bottom.Add(("click=place   B=next   RMB=cancel", HudDim));
        }
        else
        {
            bottom.Add(($"B=build menu   buildings: {_world.Colony.Buildings.Count}", HudDim));
        }
        if (_statusTimer > 0)
            bottom.Add((_status, _status.StartsWith("Built") ? new Color(120, 230, 120) : HudWarn));

        foreach (var note in _world.EventNotifications.AsEnumerable().Reverse().Take(3))
            bottom.Add(("* " + note, HudDim));

        bottom.Add(("Space=pause 1/2/3=speed B=build T=research G=sponsor F5=save F9=load N=new Esc=quit", HudDim));

        float panelH = bottom.Count * _font.LineSpacing + 16f;
        DrawTextPanel(new Vector2(10, GraphicsDevice.Viewport.Height - panelH - 10f), bottom);

        if (_selectedBuilding is not null)
            DrawBuildingPanel();

        if (_world.IsTerraformed)
            DrawCenterBanner("***  PLANET TERRAFORMED  ***", new Color(120, 230, 120));
        else if (_world.IsLost)
            DrawCenterBanner("***  COLONY LOST - press Esc  ***", new Color(255, 90, 80));

        _spriteBatch.End();
    }

    private void DrawCenterBanner(string msg, Color color)
    {
        const float scale = 1.6f;
        var size = _font.MeasureString(msg);
        var pos = new Vector2((GraphicsDevice.Viewport.Width - size.X * scale) / 2f, GraphicsDevice.Viewport.Height * 0.14f);

        _spriteBatch.Draw(_pixel,
            new Rectangle((int)pos.X - 24, (int)pos.Y - 12, (int)(size.X * scale) + 48, (int)(size.Y * scale) + 24),
            new Color(0, 0, 0, 200));
        _spriteBatch.DrawString(_font, msg, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    private void DrawBuildingPanel()
    {
        var b = _selectedBuilding!;
        var d = b.Definition;
        var lines = new List<(string text, Color color)>
        {
            (d.Name, HudWhite),
            (d.Category, HudDim),
        };

        if (b.State == BuildingState.UnderConstruction)
        {
            lines.Add(($"building... {b.BuildFraction * 100:0}%", HudWhite));
            if (b.Stalled) lines.Add(("STALLED - no materials", HudWarn));
        }
        else if (b.State == BuildingState.Disabled)
        {
            lines.Add(($"DISABLED  ({b.RepairTicksRemaining / 4}s to repair)", HudWarn));
            if (b.Definition.MaxWorkers > 0) lines.Add(("assign Engineer to speed repair", HudDim));
        }
        else
        {
            lines.Add((b.State.ToString(), HudDim));
        }

        if (d.Production.Count > 0)
        {
            lines.Add(("output / tick:", HudDim));
            foreach (var (kind, value) in d.Production)
                lines.Add(($"  {value.ToString("+0.00;-0.00")} {kind}",
                    value >= 0 ? new Color(120, 230, 120) : new Color(240, 130, 110)));
        }

        if (d.ExtractionPerTick > 0)
        {
            var tile = _map.GetTile(b.Location);
            if (tile is not null)
                lines.Add(($"deposit: {(int)tile.RemainingDeposit} left", b.DepositDepleted ? HudWarn : HudDim));
            if (b.DepositDepleted) lines.Add(("DEPLETED - idle", HudWarn));
        }

        if (d.MaxWorkers > 0)
        {
            lines.Add(($"workers {b.Workers.Count}/{d.MaxWorkers}   eff {b.WorkerEfficiency():0.00}", HudWhite));
            foreach (var w in b.Workers)
                lines.Add(($"  {w.Name} [{w.Specialty}]", HudDim));
            lines.Add(($"optimal: {d.OptimalSpecialty}", HudDim));
            lines.Add(("[+] assign   [-] remove", new Color(120, 230, 120)));
        }
        else
        {
            lines.Add(("automatic (no crew)", HudDim));
        }

        float width = PanelWidth(lines);
        DrawTextPanel(new Vector2(GraphicsDevice.Viewport.Width - width - 10f, 10f), lines);
    }

    private float PanelWidth(IReadOnlyList<(string text, Color color)> lines)
    {
        float maxW = 0f;
        foreach (var (text, _) in lines)
            if (text.Length > 0)
                maxW = MathF.Max(maxW, _font.MeasureString(text).X);
        return maxW + 16f;
    }

    private void DrawTextPanel(Vector2 pos, IReadOnlyList<(string text, Color color)> lines)
    {
        const float pad = 8f;
        float lineH = _font.LineSpacing;

        float maxW = 0f;
        foreach (var (text, _) in lines)
            if (text.Length > 0)
                maxW = MathF.Max(maxW, _font.MeasureString(text).X);

        var panel = new Rectangle(
            (int)pos.X, (int)pos.Y,
            (int)(maxW + pad * 2f), (int)(lines.Count * lineH + pad * 2f));
        _spriteBatch.Draw(_pixel, panel, new Color(0, 0, 0, 170));

        float y = pos.Y + pad;
        foreach (var (text, color) in lines)
        {
            if (text.Length > 0)
                _spriteBatch.DrawString(_font, text, new Vector2(pos.X + pad, y), color);
            y += lineH;
        }
    }

    private string ResLine(string label, ResourceKind k)
    {
        var l = _world.Colony.Ledger;
        double amt = l.Get(k);
        string cap = l.HasCapacityLimit(k) ? $" / {l.Capacity(k):0}" : "";
        string rate = k == ResourceKind.Credits
            ? ""
            : "   " + l.RatePerTick(k).ToString("+0.00;-0.00; 0.00") + "/t";
        return $"{label,-10}{amt,7:0}{cap}{rate}";
    }

    private Color RateColor(ResourceKind k)
    {
        double r = _world.Colony.Ledger.RatePerTick(k);
        if (r > 0.001) return new Color(120, 230, 120);
        if (r < -0.001) return new Color(240, 130, 110);
        return HudDim;
    }

    private static string MetricLine(string label, double value, string unit, double progress) =>
        $"{label,-9}{value,7:0.0} {unit,-3} {progress * 100,4:0}%";

    private static Color MetricColor(double progress) =>
        progress >= 1.0 ? new Color(120, 230, 120) : new Color(210, 210, 220);

    private static string EventLabel(EventType type) => type switch
    {
        EventType.DustStorm => "DUST STORM",
        EventType.SolarFlare => "SOLAR FLARE",
        EventType.LifeSupportFailure => "LIFE SUPPORT FAILURE",
        EventType.CaveDiscovery => "CAVE",
        _ => type.ToString()
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderer?.Dispose();
            _pixel?.Dispose();
            _spriteBatch?.Dispose();
        }
        base.Dispose(disposing);
    }
}

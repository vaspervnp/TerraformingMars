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
    private Building? _hoveredBuilding;
    private HexTile? _selected;
    private Building? _selectedBuilding;

    // Build mode (Φάση 3)
    private BuildingCatalog _catalog = null!;
    private List<BuildingDefinition> _buildables = null!;
    private int _buildIndex;
    private bool _buildMode;
    private bool _buildMenuOpen;   // popup παλέτα κτιρίων (2 σειρές πάνω από τη μπάρα)
    private string _status = "";
    private double _statusTimer;
    private Point _mouseDownPos;

    // Polish: μενού, audio, ανίχνευση μεταβάσεων (win/lose/research/events)
    private enum GameState { Menu, Playing }
    private GameState _state = GameState.Menu;
    private AudioManager _audio = null!;
    private MusicPlayer _music = null!;
    private AudioSettings _audioSettings = null!;
    private List<string> _tracks = null!;
    private int _trackIndex;
    private int _menuRow;

    // Background μενού: οι 4 φάσεις γεωπλασίας του Άρη (από mars_terraforming.jpg) που κάνουν dissolve.
    private Texture2D? _phaseTex;
    private readonly Rectangle[] _phaseSrc = new Rectangle[4];
    private double _bgTime;
    private int _prevResearchedCount;
    private string _lastNotification = "";
    private bool _prevWon;
    private bool _prevLost;
    private bool _uiClick;
    private bool _hasActiveGame;
    private Dictionary<string, Texture2D> _icons = null!;

    // Modal dialog με κουμπιά (mouse + Enter/Esc). Όσο είναι ανοιχτό, παγώνει η σιμουλασιόν.
    private sealed class DialogButton
    {
        public string Label = "";
        public Color Color = new(235, 235, 240);
        public Action OnClick = () => { };
        public Rectangle Rect;
    }
    private readonly List<string> _dialogLines = new();
    private readonly List<DialogButton> _dialogButtons = new();
    private Rectangle _dialogPanel;
    private bool DialogOpen => _dialogButtons.Count > 0;

    // Reclaim (ανακύκλωση κτιρίων για credits) — ξεκλειδώνει με την τεχνολογία "reclaim"
    private const string ReclaimTechId = "reclaim";
    private Texture2D _reclaimIcon = null!;
    private Texture2D _buildingsIcon = null!;
    private bool _reclaimMode;
    private Building? _reclaimTarget;
    private bool ReclaimUnlocked => _world.Colony.Tech.IsResearched(ReclaimTechId);

    // Σταθερή μπάρα: [Κτίρια] [Reclaim;] [Βοήθεια]. Τα κτίρια ανοίγουν popup παλέτα (2 σειρές).
    private const int BuildingsSlotIndex = 0;
    private int ReclaimSlotIndex => ReclaimUnlocked ? 1 : -1;
    private int HelpSlotIndex => ReclaimUnlocked ? 2 : 1;      // το help είναι πάντα τελευταίο
    private int TotalToolbarSlots => HelpSlotIndex + 1;

    // Οθόνη βοήθειας (modal· ανοίγει από τη μπάρα ή το μενού)
    private bool _showHelp;

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
        _music = new MusicPlayer();
        InitAudio();
        _icons = IconFactory.CreateAll(GraphicsDevice, _catalog);
        _reclaimIcon = IconFactory.CreateReclaim(GraphicsDevice);
        _buildingsIcon = IconFactory.CreateBuildings(GraphicsDevice);
        _phaseTex = LoadPhaseTexture();
        InitCamera();
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
        _buildMenuOpen = false;
        _reclaimMode = false;
        _reclaimTarget = null;
        CloseDialog();
        ResetTransitionTrackers();
        InitCamera();
        _hasActiveGame = true;
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

    private const int MenuRowCount = 7;

    /// <summary>Φορτώνει ρυθμίσεις ήχου, απαριθμεί κομμάτια και ξεκινά τη μουσική υποβάθρου.</summary>
    private void InitAudio()
    {
        _audioSettings = AudioSettings.Load();
        _tracks = AudioSettings.AvailableTracks();

        _audio.Enabled = _audioSettings.SfxEnabled;
        _audio.Volume = _audioSettings.SfxVolume;

        // Αρχικό κομμάτι: το αποθηκευμένο αν υπάρχει· αλλιώς το πρώτο διαθέσιμο αρχείο.
        _trackIndex = _tracks.FindIndex(t => string.Equals(t, _audioSettings.MusicTrack, StringComparison.OrdinalIgnoreCase));
        if (_trackIndex < 0) _trackIndex = _tracks.Count > 1 ? 1 : 0; // 0 = "None"
        _audioSettings.MusicTrack = _tracks[_trackIndex];

        _music.Volume = _audioSettings.MusicVolume;
        _music.Muted = _audioSettings.MusicMuted;
        _music.Play(AudioSettings.PathFor(_tracks[_trackIndex]));
    }

    private void SaveAudio() => _audioSettings.Save();
    private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);

    private void UpdateMenu(KeyboardState keys, MouseState mouse)
    {
        var actions = MenuActions();

        // Πληκτρολόγιο: Esc = Continue (αν υπάρχει παιχνίδι) αλλιώς Quit· Enter = πρωτεύον κουμπί.
        if (KeyPressed(keys, Keys.Escape)) { if (_hasActiveGame) _state = GameState.Playing; else Exit(); return; }
        if (KeyPressed(keys, Keys.Enter)) { actions[0].action(); return; }

        if (KeyPressed(keys, Keys.Up) || KeyPressed(keys, Keys.W))
            _menuRow = (_menuRow - 1 + MenuRowCount) % MenuRowCount;
        if (KeyPressed(keys, Keys.Down) || KeyPressed(keys, Keys.S))
            _menuRow = (_menuRow + 1) % MenuRowCount;

        int dir = 0;
        if (KeyPressed(keys, Keys.Left) || KeyPressed(keys, Keys.A)) dir = -1;
        if (KeyPressed(keys, Keys.Right) || KeyPressed(keys, Keys.D)) dir = 1;
        if (dir != 0) ChangeMenuRow(_menuRow, dir);

        if (KeyPressed(keys, Keys.R)) _seed = new Random().Next();

        // Ποντίκι: hover εστιάζει γραμμή, κλικ σε βέλη/σώμα/κουμπιά ενεργεί.
        for (int i = 0; i < MenuRowCount; i++)
            if (MenuRowRect(i).Contains(mouse.X, mouse.Y)) _menuRow = i;

        bool click = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
        if (!click) return;

        var p = new Point(mouse.X, mouse.Y);
        for (int i = 0; i < MenuRowCount; i++)
        {
            if (RowHasArrows(i) && MenuArrowRect(i, -1).Contains(p)) { _menuRow = i; ChangeMenuRow(i, -1); return; }
            if (RowHasArrows(i) && MenuArrowRect(i, 1).Contains(p)) { _menuRow = i; ChangeMenuRow(i, 1); return; }
            if (MenuRowRect(i).Contains(p)) { _menuRow = i; MenuRowBodyClick(i); return; }
        }
        for (int i = 0; i < actions.Count; i++)
            if (MenuActionRect(i, actions.Count).Contains(p)) { actions[i].action(); return; }
    }

    /// <summary>Τα κουμπιά ενεργειών: Continue (αν υπάρχει ενεργό παιχνίδι), New/Start Game, Quit.</summary>
    private List<(string label, Action action, Color color)> MenuActions()
    {
        var list = new List<(string, Action, Color)>();
        if (_hasActiveGame) list.Add(("Continue", () => _state = GameState.Playing, new Color(120, 230, 120)));
        list.Add((_hasActiveGame ? "New Game" : "Start Game", StartGame, new Color(255, 220, 120)));
        list.Add(("Help", () => _showHelp = true, new Color(150, 200, 255)));
        list.Add(("Quit", Exit, new Color(230, 120, 110)));
        return list;
    }

    // Οι γραμμές τιμών (mute, sfx on/off) εναλλάσσονται με κλικ στο σώμα — δεν έχουν βέλη.
    private static bool RowHasArrows(int i) => i != 4 && i != 5;

    /// <summary>Κλικ στο σώμα μιας γραμμής: cycle/randomize/toggle (οι εντάσεις αλλάζουν μόνο με βέλη).</summary>
    private void MenuRowBodyClick(int i)
    {
        switch (i)
        {
            case 0: case 1: case 2: ChangeMenuRow(i, 1); break; // sponsor/seed/music
            case 4: case 5: ChangeMenuRow(i, 1); break;         // toggles
        }
    }

    private const int MenuRowW = 640, MenuRowH = 34, MenuRowGap = 6, MenuArrowW = 46;
    private int MenuRowsTop() => (int)(GraphicsDevice.Viewport.Height * 0.24f);

    private Rectangle MenuRowRect(int i)
    {
        int x = (GraphicsDevice.Viewport.Width - MenuRowW) / 2;
        return new Rectangle(x, MenuRowsTop() + i * (MenuRowH + MenuRowGap), MenuRowW, MenuRowH);
    }

    private Rectangle MenuArrowRect(int i, int dir)
    {
        var r = MenuRowRect(i);
        return dir < 0 ? new Rectangle(r.X, r.Y, MenuArrowW, r.Height)
                       : new Rectangle(r.Right - MenuArrowW, r.Y, MenuArrowW, r.Height);
    }

    private Rectangle MenuActionRect(int idx, int count)
    {
        const int bw = 210, bh = 50, gap = 20;
        int total = count * bw + (count - 1) * gap;
        int x = (GraphicsDevice.Viewport.Width - total) / 2;
        int y = MenuRowRect(MenuRowCount - 1).Bottom + 96;
        return new Rectangle(x + idx * (bw + gap), y, bw, bh);
    }

    /// <summary>Μεταβάλλει την τιμή της εστιασμένης γραμμής του μενού (dir = -1/+1).</summary>
    private void ChangeMenuRow(int row, int dir)
    {
        switch (row)
        {
            case 0: // Sponsor
                _sponsorIndex = (_sponsorIndex + dir + _sponsorCatalog.All.Count) % _sponsorCatalog.All.Count;
                _sponsor = _sponsorCatalog.All[_sponsorIndex];
                break;
            case 1: // Seed → νέο τυχαίο
                _seed = new Random().Next();
                break;
            case 2: // Music track
                if (_tracks.Count > 0)
                {
                    _trackIndex = (_trackIndex + dir + _tracks.Count) % _tracks.Count;
                    _audioSettings.MusicTrack = _tracks[_trackIndex];
                    _music.Play(AudioSettings.PathFor(_tracks[_trackIndex]));
                    SaveAudio();
                }
                break;
            case 3: // Music volume
                _music.Volume = _audioSettings.MusicVolume = Clamp01(_audioSettings.MusicVolume + dir * 0.05f);
                SaveAudio();
                break;
            case 4: // Music mute (Left/Right εναλλάσσει)
                _music.Muted = _audioSettings.MusicMuted = !_audioSettings.MusicMuted;
                SaveAudio();
                break;
            case 5: // Sound effects on/off
                _audio.Enabled = _audioSettings.SfxEnabled = !_audioSettings.SfxEnabled;
                if (_audio.Enabled) _audio.Blip();
                SaveAudio();
                break;
            case 6: // SFX volume (+ preview)
                _audio.Volume = _audioSettings.SfxVolume = Clamp01(_audioSettings.SfxVolume + dir * 0.05f);
                _audio.Blip();
                SaveAudio();
                break;
        }
    }

    private List<string> MenuRows()
    {
        string musicName = _tracks.Count > 0
            ? Path.GetFileNameWithoutExtension(_tracks[_trackIndex])
            : AudioSettings.NoTrack;
        return new List<string>
        {
            $"Sponsor:  {_sponsor.Name}",
            $"Seed:  {_seed}",
            $"Music:  {musicName}",
            $"Music volume:  {(_audioSettings.MusicMuted ? "muted" : $"{_audioSettings.MusicVolume * 100:0}%")}",
            $"Music mute:  {(_audioSettings.MusicMuted ? "On" : "Off")}",
            $"Sound effects:  {(_audioSettings.SfxEnabled ? "On" : "Off")}",
            $"SFX volume:  {_audioSettings.SfxVolume * 100:0}%",
        };
    }

    /// <summary>Φορτώνει το mars_terraforming.jpg και υπολογίζει τα 4 τετράγωνα crop (ένας πλανήτης ανά φάση).</summary>
    private Texture2D? LoadPhaseTexture()
    {
        try
        {
            string path = Path.Combine(AudioSettings.AssetsDir, "MarsTransitionV.jpg");
            if (!File.Exists(path)) return null;

            using var fs = File.OpenRead(path);
            var tex = Texture2D.FromStream(GraphicsDevice, fs);

            // Πλέγμα 2×2, σειρά dissolve TL → TR → BL → BR (ξηρός Άρης → πλήρης γεωπλασία).
            if (tex.Width == 2606 && tex.Height == 2626)
            {
                // Μετρημένα tight bounding boxes ανά πλανήτη — χωρίς bleed από τη γειτονική φάση.
                _phaseSrc[0] = new Rectangle(16, 3, 1276, 1287);
                _phaseSrc[1] = new Rectangle(1341, 9, 1227, 1277);
                _phaseSrc[2] = new Rectangle(73, 1298, 1209, 1257);
                _phaseSrc[3] = new Rectangle(1288, 1302, 1272, 1278);
            }
            else // fallback: απλό πλέγμα 2×2 αν αλλάξει η εικόνα
            {
                int hw = tex.Width / 2, hh = tex.Height / 2;
                _phaseSrc[0] = new Rectangle(0, 0, hw, hh);
                _phaseSrc[1] = new Rectangle(hw, 0, hw, hh);
                _phaseSrc[2] = new Rectangle(0, hh, hw, hh);
                _phaseSrc[3] = new Rectangle(hw, hh, hw, hh);
            }
            return tex;
        }
        catch { return null; } // λείπει/χαλασμένο αρχείο → χωρίς background
    }

    /// <summary>Σχεδιάζει τον κεντρικό πλανήτη που κάνει dissolve από τη μία φάση στην επόμενη.</summary>
    private void DrawMenuBackground()
    {
        if (_phaseTex is null) return;
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;

        // Ο πλανήτης γεμίζει όλο το ύψος (διατηρώντας την αναλογία του crop), κεντραρισμένος.
        Rectangle DestFor(Rectangle src)
        {
            int destW = (int)(vh * (float)src.Width / src.Height);
            return new Rectangle((vw - destW) / 2, 0, destW, vh);
        }

        const double hold = 3.0, fade = 2.2;             // δευτ. σταθερό + δευτ. dissolve ανά φάση
        double per = hold + fade;
        double t = _bgTime % (per * 4);
        int i = (int)(t / per);
        double local = t - i * per;
        int next = (i + 1) % 4;

        _spriteBatch.Draw(_phaseTex, DestFor(_phaseSrc[i]), _phaseSrc[i], Color.White);
        if (local > hold)
        {
            float a = (float)Math.Clamp((local - hold) / fade, 0, 1);
            _spriteBatch.Draw(_phaseTex, DestFor(_phaseSrc[next]), _phaseSrc[next], Color.White * a); // dissolve-in
        }

        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 120)); // dim για ευανάγνωστο κείμενο
    }

    private void DrawMenu()
    {
        GraphicsDevice.Clear(new Color(0x0b, 0x0b, 0x12));

        // Φόντο (φωτογραφία) με ομαλό (linear) sampling — ξεχωριστό batch από το κείμενο.
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
        DrawMenuBackground();
        _spriteBatch.End();

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        var ms = Mouse.GetState();
        DrawCentered("TERRAFORMING MARS", GraphicsDevice.Viewport.Height * 0.09f, 2.4f, new Color(230, 120, 80));

        var rows = MenuRows();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = MenuRowRect(i);
            bool focused = i == _menuRow;
            // Premultiplied AlphaBlend: πολλαπλασιάζουμε το χρώμα επί το opacity (αλλιώς βγαίνει σχεδόν λευκό).
            if (focused)
                _spriteBatch.Draw(_pixel, r, new Color(45, 75, 140) * 0.85f); // μπλε selection bar
            else if (r.Contains(ms.X, ms.Y))
                _spriteBatch.Draw(_pixel, r, new Color(70, 82, 105) * 0.5f);  // απαλό hover

            var size = _font.MeasureString(rows[i]) * 1.1f;
            _spriteBatch.DrawString(_font, rows[i],
                new Vector2(r.X + (r.Width - size.X) / 2f, r.Y + (r.Height - size.Y) / 2f),
                focused ? new Color(255, 232, 150) : HudWhite, 0f, Vector2.Zero, 1.1f, SpriteEffects.None, 0f);

            if (RowHasArrows(i))
            {
                DrawMenuArrow(MenuArrowRect(i, -1), "<", ms);
                DrawMenuArrow(MenuArrowRect(i, 1), ">", ms);
            }
        }

        DrawCentered(_sponsor.Description, MenuRowRect(MenuRowCount - 1).Bottom + 16, 1.0f, HudDim);

        var actions = MenuActions();
        for (int i = 0; i < actions.Count; i++)
        {
            var r = MenuActionRect(i, actions.Count);
            bool hover = r.Contains(ms.X, ms.Y);
            _spriteBatch.Draw(_pixel, r, hover ? new Color(60, 70, 90) : new Color(30, 34, 46));
            DrawRectOutline(r, actions[i].color);
            var size = _font.MeasureString(actions[i].label) * 1.2f;
            _spriteBatch.DrawString(_font, actions[i].label,
                new Vector2(r.X + (r.Width - size.X) / 2f, r.Y + (r.Height - size.Y) / 2f),
                actions[i].color, 0f, Vector2.Zero, 1.2f, SpriteEffects.None, 0f);
        }

        DrawCentered($"Click, or use arrow keys.    Enter = {actions[0].label}    Esc = {(_hasActiveGame ? "Continue" : "Quit")}",
            MenuActionRect(0, actions.Count).Bottom + 20, 0.9f, HudDim);

        _spriteBatch.End();
    }

    private void DrawMenuArrow(Rectangle r, string glyph, MouseState ms)
    {
        bool hover = r.Contains(ms.X, ms.Y);
        _spriteBatch.Draw(_pixel, r, hover ? new Color(70, 80, 100) : new Color(28, 32, 42));
        DrawRectOutline(r, new Color(90, 96, 115));
        var size = _font.MeasureString(glyph) * 1.2f;
        _spriteBatch.DrawString(_font, glyph, new Vector2(r.X + (r.Width - size.X) / 2f, r.Y + (r.Height - size.Y) / 2f),
            HudWhite, 0f, Vector2.Zero, 1.2f, SpriteEffects.None, 0f);
    }

    // ----------------------------------------------------------------- Help screen

    // Οι κεφαλίδες (χωρίς πεζά) χρωματίζονται διαφορετικά από το σώμα κειμένου.
    private static readonly string[] HelpText =
    {
        "GOAL",
        "Terraform Mars: raise Temperature, Pressure, Oxygen and Water to their targets.",
        "Reach 100% on all four planet metrics to win - and keep your crew alive.",
        "",
        "BUILD",
        "Click an icon in the bottom toolbar, then click a hex to place a building.",
        "Buildings cost Credits up front and use Materials while under construction.",
        "Mines need a matching deposit (H2O / Fe / Si / Rg), shown as coloured diamonds.",
        "",
        "CREW & RESOURCES",
        "Select a building and press +/- to assign or remove crew (staffed = more output).",
        "Watch Energy, Water, Oxygen, Food and Materials - shortages stall the colony.",
        "",
        "RESEARCH & RECLAIM",
        "Press T to choose a technology; finishing it unlocks new buildings.",
        "Research 'Reclaim' to recycle a building for some Credits back (refund shrinks",
        "over time). Click the recycle icon, then the building.",
        "",
        "CONTROLS",
        "Move: WASD / arrows / drag     Zoom: mouse wheel",
        "Speed: Space = pause, 1 / 2 / 3 = normal / fast / ultra",
        "Save / Load: F5 / F9     New map: N     Mute SFX: U     Menu: Esc",
    };

    private Rectangle HelpPanelRect()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        int w = Math.Min(vw - 80, 880), h = Math.Min(vh - 60, 680);
        return new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
    }

    private Rectangle HelpCloseRect()
    {
        var p = HelpPanelRect();
        const int bw = 170, bh = 44;
        return new Rectangle(p.Center.X - bw / 2, p.Bottom - bh - 16, bw, bh);
    }

    private void DrawHelpOverlay()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        var ms = Mouse.GetState();
        var panel = HelpPanelRect();

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 180));
        _spriteBatch.Draw(_pixel, panel, new Color(16, 18, 26, 250));
        DrawRectOutline(panel, new Color(120, 130, 150));

        const string title = "HOW TO PLAY";
        var tsz = _font.MeasureString(title) * 1.5f;
        _spriteBatch.DrawString(_font, title, new Vector2(panel.Center.X - tsz.X / 2f, panel.Y + 16),
            new Color(230, 150, 90), 0f, Vector2.Zero, 1.5f, SpriteEffects.None, 0f);

        float y = panel.Y + 62;
        foreach (var line in HelpText)
        {
            if (line.Length == 0) { y += 9f; continue; }
            bool header = !line.Any(char.IsLower);
            _spriteBatch.DrawString(_font, line, new Vector2(panel.X + 26, y),
                header ? new Color(255, 205, 120) : HudWhite, 0f, Vector2.Zero,
                header ? 1.05f : 1.0f, SpriteEffects.None, 0f);
            y += header ? 27f : 22f;
        }

        var close = HelpCloseRect();
        bool hover = close.Contains(ms.X, ms.Y);
        _spriteBatch.Draw(_pixel, close, hover ? new Color(60, 70, 90) : new Color(34, 38, 50));
        DrawRectOutline(close, new Color(150, 200, 255));
        const string cl = "Close (Esc)";
        var csz = _font.MeasureString(cl);
        _spriteBatch.DrawString(_font, cl, new Vector2(close.Center.X - csz.X / 2f, close.Center.Y - csz.Y / 2f), HudWhite);

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
        _reclaimMode = false;
        _reclaimTarget = null;
        CloseDialog();
        ResetTransitionTrackers();
        InitCamera();
    }

    /// <summary>Μέγιστο zoom-out = ~20 εξάγωνα οριζόντια (προσαρμόζεται στο πλάτος παραθύρου).</summary>
    private void UpdateMinZoom()
    {
        float minZoom = GraphicsDevice.Viewport.Width / (20f * MathF.Sqrt(3f) * HexSize);
        _camera.MinZoom = minZoom;
        if (_camera.Zoom < minZoom) _camera.Zoom = minZoom;
    }

    private void InitCamera()
    {
        UpdateMinZoom();
        _camera.Zoom = _camera.MinZoom; // ξεκινάμε από το μέγιστο zoom-out (20 εξάγωνα)

        Hex center = _world.Colony.Buildings.Count > 0
            ? _world.Colony.Buildings[0].Location
            : new OffsetCoord(_map.Width / 2, _map.Height / 2).ToHex();
        var (x, y) = _renderer.Layout.HexToPixel(center);
        _camera.Position = new Vector2((float)x, (float)y);
    }

    protected override void Update(GameTime gameTime)
    {
        var mouse = Mouse.GetState();
        var keys = Keyboard.GetState();

        // Οθόνη βοήθειας: modal σε όλες τις καταστάσεις (Close κουμπί ή Esc κλείνει).
        if (_showHelp)
        {
            _uiClick = false; // αποφυγή stale click μετά το κλείσιμο
            bool click = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
            if (KeyPressed(keys, Keys.Escape) || (click && HelpCloseRect().Contains(mouse.X, mouse.Y)))
            {
                _showHelp = false;
                _audio.Blip();
            }
            _prevMouse = mouse;
            _prevKeys = keys;
            base.Update(gameTime);
            return;
        }

        if (_state == GameState.Menu)
        {
            _bgTime += gameTime.ElapsedGameTime.TotalSeconds;
            UpdateMenu(keys, mouse);
            _prevMouse = mouse;
            _prevKeys = keys;
            base.Update(gameTime);
            return;
        }

        // Modal dialog με κουμπιά (mouse click ή Enter=πρωτεύον / Esc=άκυρο). Παγώνει τη σιμουλασιόν.
        if (DialogOpen)
        {
            LayoutDialog();
            if (mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed)
            {
                var hit = _dialogButtons.FirstOrDefault(b => b.Rect.Contains(mouse.X, mouse.Y));
                if (hit is not null) InvokeDialogButton(hit);
            }
            else if (KeyPressed(keys, Keys.Enter)) InvokeDialogButton(_dialogButtons[0]);
            else if (KeyPressed(keys, Keys.Escape)) InvokeDialogButton(_dialogButtons[^1]);
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
        if (KeyPressed(keys, Keys.U))
        {
            _audio.Enabled = _audioSettings.SfxEnabled = !_audio.Enabled;
            SaveAudio();
        }

        _camera.SetViewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        UpdateMinZoom();

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

        // Νέος χάρτης (N, με επιβεβαίωση) / εναλλαγή sponsor (G, ξεκινά νέο παιχνίδι)
        if (KeyPressed(keys, Keys.N))
            OpenDialog(new[] { "Create a new random map?", "The current colony will be lost." },
                Btn("New map", new Color(120, 230, 120), () => Regenerate(new Random().Next())),
                Btn("Cancel", HudDim, () => { }));
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
            _reclaimMode = false;
            _reclaimTarget = null;
            CloseDialog();
            ResetTransitionTrackers();
            InitCamera();
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

        // Build menu: B ανοίγει/κλείνει την παλέτα κτιρίων
        if (KeyPressed(keys, Keys.B))
        {
            _buildMenuOpen = !_buildMenuOpen;
            if (_buildMenuOpen) { _buildMode = false; _reclaimMode = false; }
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
        _hoveredBuilding = _hasHover ? _world.Colony.Buildings.FirstOrDefault(b => b.Location == _hoverHex) : null;

        // Αριστερό κλικ: μπάρα κτιρίων (UI) ή τοποθέτηση στον χάρτη
        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
        {
            _mouseDownPos = new Point(mouse.X, mouse.Y);

            // Προτεραιότητα: επιλογή κτιρίου από την ανοιχτή παλέτα → μπαίνει σε build mode & κλείνει.
            int menuBtn = _buildMenuOpen ? BuildMenuHitIndex(mouse.X, mouse.Y) : -1;
            if (menuBtn >= 0)
            {
                _buildMode = true;
                _buildIndex = menuBtn;
                _buildMenuOpen = false;
                _reclaimMode = false;
                _audio.Blip();
                _uiClick = true;
            }
            else if (BuildingsButtonHit(mouse.X, mouse.Y))
            {
                _buildMenuOpen = !_buildMenuOpen; // toggle η παλέτα
                if (_buildMenuOpen) { _buildMode = false; _reclaimMode = false; }
                _audio.Blip();
                _uiClick = true;
            }
            else if (ReclaimButtonHit(mouse.X, mouse.Y))
            {
                _reclaimMode = !_reclaimMode;
                _buildMode = false;
                _buildMenuOpen = false;
                _audio.Blip();
                _uiClick = true;
            }
            else if (HelpButtonHit(mouse.X, mouse.Y))
            {
                _showHelp = true;
                _buildMenuOpen = false;
                _audio.Blip();
                _uiClick = true;
            }
        }
        if (mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed)
        {
            int dx = mouse.X - _mouseDownPos.X, dy = mouse.Y - _mouseDownPos.Y;
            bool isClick = dx * dx + dy * dy < 36; // < 6px ⇒ κλικ (όχι drag/pan)
            if (!_uiClick && isClick && _buildMode && _hasHover) TryPlaceSelected();
            else if (!_uiClick && isClick && _reclaimMode && _hoveredBuilding is { } target) BeginReclaim(target);
            _uiClick = false;
        }

        // Δεξί κλικ: ακυρώνει το build mode, αλλιώς επιλέγει tile/κτίριο
        if (mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released)
        {
            if (_buildMenuOpen) _buildMenuOpen = false;
            else if (_buildMode) _buildMode = false;
            else if (_reclaimMode) _reclaimMode = false;
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
        var result = _world.Colony.TryPlaceBuilding(def, _hoverHex, _map, _world.Clock.TotalTicks);
        _status = result.Success ? $"Built {def.Name}" : $"Cannot place {def.Name}: {result.Error}";
        _statusTimer = 3.0;
        if (result.Success) _audio.Blip();
    }

    /// <summary>Επιλογή κτιρίου για ανακύκλωση: ανοίγει το dialog επιβεβαίωσης (ή δείχνει γιατί δεν γίνεται).</summary>
    private void BeginReclaim(Building building)
    {
        if (!Colony.CanReclaim(building))
        {
            _status = $"Cannot reclaim {building.Definition.Name}";
            _statusTimer = 3.0;
            return;
        }
        _reclaimTarget = building;
        double refund = _world.Colony.ReclaimValue(building, _world.Clock.TotalTicks);
        double pct = Colony.ReclaimFraction(building, _world.Clock.TotalTicks) * 100;
        OpenDialog(
            new[] { $"Reclaim {building.Definition.Name}?", $"Refund {refund:0} credits ({pct:0}% of cost)" },
            Btn("Reclaim", new Color(240, 170, 80), ConfirmReclaim),
            Btn("Cancel", HudDim, () => _reclaimTarget = null));
    }

    /// <summary>Εκτελεί την ανακύκλωση του επιλεγμένου κτιρίου και κλείνει το reclaim mode.</summary>
    private void ConfirmReclaim()
    {
        if (_reclaimTarget is { } target && _world.Colony.Buildings.Contains(target))
        {
            double refund = _world.Colony.Reclaim(target, _world.Clock.TotalTicks);
            _status = $"Reclaimed {target.Definition.Name}  (+{refund:0} credits)";
            _statusTimer = 3.0;
            _audio.Blip();
            if (_selectedBuilding == target) { _selectedBuilding = null; _selected = null; }
        }
        _reclaimMode = false;
        _reclaimTarget = null;
    }

    // ----------------------------------------------------------------- Modal dialog

    private DialogButton Btn(string label, Color color, Action onClick) =>
        new() { Label = label, Color = color, OnClick = onClick };

    private void OpenDialog(IEnumerable<string> lines, params DialogButton[] buttons)
    {
        _dialogLines.Clear();
        _dialogLines.AddRange(lines);
        _dialogButtons.Clear();
        _dialogButtons.AddRange(buttons);
        _audio.Blip();
    }

    private void CloseDialog()
    {
        _dialogLines.Clear();
        _dialogButtons.Clear();
    }

    private void InvokeDialogButton(DialogButton button)
    {
        var action = button.OnClick;
        CloseDialog();
        action();
    }

    /// <summary>Υπολογίζει το panel και τα ορθογώνια των κουμπιών (κοινό για update hit-test & draw).</summary>
    private void LayoutDialog()
    {
        const float scale = 1.1f;
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        float lineH = _font.LineSpacing * scale;

        float maxLineW = 0f;
        foreach (var l in _dialogLines) maxLineW = MathF.Max(maxLineW, _font.MeasureString(l).X * scale);

        const int btnH = 46, btnPadX = 20, btnGap = 16;
        int totalBtnW = btnGap * Math.Max(0, _dialogButtons.Count - 1);
        foreach (var b in _dialogButtons) totalBtnW += (int)(_font.MeasureString(b.Label).X * scale) + btnPadX * 2;

        int panelW = (int)MathF.Max(maxLineW, totalBtnW) + 64;
        int panelH = (int)(_dialogLines.Count * lineH) + btnH + 76;
        _dialogPanel = new Rectangle((vw - panelW) / 2, (vh - panelH) / 2, panelW, panelH);

        int bx = _dialogPanel.X + (panelW - totalBtnW) / 2;
        int by = _dialogPanel.Bottom - btnH - 20;
        foreach (var b in _dialogButtons)
        {
            int bw = (int)(_font.MeasureString(b.Label).X * scale) + btnPadX * 2;
            b.Rect = new Rectangle(bx, by, bw, btnH);
            bx += bw + btnGap;
        }
    }

    private void DrawDialog()
    {
        LayoutDialog();
        const float scale = 1.1f;
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        var ms = Mouse.GetState();

        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 150)); // dim
        _spriteBatch.Draw(_pixel, _dialogPanel, new Color(20, 22, 30, 245));
        DrawRectOutline(_dialogPanel, new Color(120, 130, 150));

        float y = _dialogPanel.Y + 22;
        foreach (var l in _dialogLines)
        {
            var size = _font.MeasureString(l) * scale;
            _spriteBatch.DrawString(_font, l, new Vector2(_dialogPanel.X + (_dialogPanel.Width - size.X) / 2f, y),
                HudWhite, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            y += _font.LineSpacing * scale;
        }

        foreach (var b in _dialogButtons)
        {
            bool hover = b.Rect.Contains(ms.X, ms.Y);
            _spriteBatch.Draw(_pixel, b.Rect, hover ? new Color(60, 70, 90) : new Color(34, 38, 50));
            DrawRectOutline(b.Rect, b.Color);
            var size = _font.MeasureString(b.Label) * scale;
            _spriteBatch.DrawString(_font, b.Label,
                new Vector2(b.Rect.X + (b.Rect.Width - size.X) / 2f, b.Rect.Y + (b.Rect.Height - size.Y) / 2f),
                b.Color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
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
            if (_showHelp) DrawHelpOverlay();
            base.Draw(gameTime);
            return;
        }

        GraphicsDevice.Clear(new Color(0x0b, 0x0b, 0x12));

        Matrix view = _camera.GetViewMatrix();
        Matrix projection = Matrix.CreateOrthographicOffCenter(
            0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, 0, 0f, 1f);

        _renderer.Draw(view, projection);
        DrawDepositSymbols(view);
        DrawBuildingIcons(view);

        if (_selected is not null)
            _renderer.DrawHighlight(_selected.Coord, new Color(0xff, 0xd0, 0x40), view, projection);

        if (_buildMode && _hasHover)
        {
            bool canPlace = _world.Colony.CanPlace(_buildables[_buildIndex], _hoverHex, _map).Success;
            _renderer.DrawHighlight(_hoverHex, canPlace ? new Color(80, 230, 120) : new Color(230, 80, 80), view, projection);
        }
        else if (_reclaimMode && _hasHover)
        {
            bool canReclaim = _hoveredBuilding is { } hb && Colony.CanReclaim(hb);
            _renderer.DrawHighlight(_hoverHex, canReclaim ? new Color(240, 150, 60) : new Color(120, 120, 130), view, projection);
        }
        else if (_hasHover)
        {
            _renderer.DrawHighlight(_hoverHex, Color.White, view, projection);
        }

        DrawHud();
        if (_showHelp) DrawHelpOverlay();

        base.Draw(gameTime);
    }

    /// <summary>Χημικό σύμβολο κοιτάσματος (Regolith = "Rg", μείγμα χωρίς μοναδικό στοιχείο).</summary>
    private static string ResourceSymbol(ResourceType type) => type switch
    {
        ResourceType.Ice => "H2O",
        ResourceType.Iron => "Fe",
        ResourceType.Silicon => "Si",
        ResourceType.Regolith => "Rg",
        _ => ""
    };

    /// <summary>Σχεδιάζει το χημικό σύμβολο πάνω σε κάθε ορατό (μη-εξαντλημένο) κοίτασμα, σε world-space.</summary>
    private void DrawDepositSymbols(Matrix view)
    {
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, view);

        foreach (var tile in _map.Tiles)
        {
            if (tile.Deposit.IsEmpty || tile.RemainingDeposit <= 0) continue;
            string sym = ResourceSymbol(tile.Deposit.Type);
            if (sym.Length == 0) continue;

            var (wx, wy) = _renderer.Layout.HexToPixel(tile.Coord);
            var size = _font.MeasureString(sym);
            // Εγγραφή στον ρόμβο (ίδιες ακτίνες με τον renderer): halfW + halfH ≤ r.
            float r = HexSize * (tile.Deposit.Hidden ? 0.36f : 0.5f);
            float scale = 2f * r * 0.82f / (size.X + size.Y);
            Color col = tile.Deposit.Hidden ? new Color(30, 33, 44) * 0.5f : new Color(25, 28, 38);
            _spriteBatch.DrawString(_font, sym, new Vector2((float)wx, (float)wy), col,
                0f, size / 2f, scale, SpriteEffects.None, 0f);
        }

        _spriteBatch.End();
    }

    /// <summary>Σχεδιάζει τα εικονίδια κτιρίων σε world-space (γεμίζουν με χρώμα όσο χτίζονται).</summary>
    private void DrawBuildingIcons(Matrix view)
    {
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, view);

        const float iconWorld = HexSize * 1.7f;
        foreach (var b in _world.Colony.Buildings)
        {
            if (!_icons.TryGetValue(b.Definition.Id, out var tex)) continue;
            var (wx, wy) = _renderer.Layout.HexToPixel(b.Location);
            var rect = new Rectangle((int)(wx - iconWorld / 2), (int)(wy - iconWorld / 2), (int)iconWorld, (int)iconWorld);

            double progress = b.State == BuildingState.Operational ? 1.0 : b.BuildFraction;
            Color dim = b.State == BuildingState.Disabled ? new Color(120, 50, 50) : new Color(55, 58, 70);

            _spriteBatch.Draw(tex, rect, null, dim); // αχτιστο = αμυδρό περίγραμμα
            if (progress > 0)                        // χτισμένο μέρος = γεμίζει με χρώμα από κάτω
            {
                int fillTex = (int)(progress * IconFactory.Size);
                int fillDst = (int)(progress * rect.Height);
                var src = new Rectangle(0, IconFactory.Size - fillTex, IconFactory.Size, fillTex);
                var dst = new Rectangle(rect.X, rect.Bottom - fillDst, rect.Width, fillDst);
                _spriteBatch.Draw(tex, dst, src, Color.White);
            }
        }

        _spriteBatch.End();
    }

    private Rectangle ToolbarButtonRect(int index, int count)
    {
        const int bs = 46, gap = 6, bottom = 8;
        int total = count * bs + (count - 1) * gap;
        int startX = (GraphicsDevice.Viewport.Width - total) / 2;
        int y = GraphicsDevice.Viewport.Height - bs - bottom;
        return new Rectangle(startX + index * (bs + gap), y, bs, bs);
    }

    private bool BuildingsButtonHit(int mx, int my) =>
        ToolbarButtonRect(BuildingsSlotIndex, TotalToolbarSlots).Contains(mx, my);

    private bool ReclaimButtonHit(int mx, int my) =>
        ReclaimUnlocked && ToolbarButtonRect(ReclaimSlotIndex, TotalToolbarSlots).Contains(mx, my);

    private bool HelpButtonHit(int mx, int my) =>
        ToolbarButtonRect(HelpSlotIndex, TotalToolbarSlots).Contains(mx, my);

    /// <summary>Rect ενός κουμπιού της popup παλέτας: 2 σειρές, στοιχισμένες στο κέντρο, πάνω από τη μπάρα.</summary>
    private Rectangle BuildMenuButtonRect(int index)
    {
        const int bs = 46, gap = 6, bottom = 8;
        int n = _buildables.Count;
        int cols = Math.Max(1, (int)Math.Ceiling(n / 2.0));
        bool bottomRow = index >= cols;
        int col = bottomRow ? index - cols : index;
        int rowCount = bottomRow ? n - cols : Math.Min(n, cols);
        int rowWidth = rowCount * bs + (rowCount - 1) * gap;
        int startX = (GraphicsDevice.Viewport.Width - rowWidth) / 2;
        int toolbarY = GraphicsDevice.Viewport.Height - bs - bottom;
        int y = toolbarY - (bottomRow ? 1 : 2) * (bs + gap); // κάτω σειρά ακριβώς πάνω από τη μπάρα, πάνω σειρά πιο ψηλά
        return new Rectangle(startX + col * (bs + gap), y, bs, bs);
    }

    private int BuildMenuHitIndex(int mx, int my)
    {
        for (int i = 0; i < _buildables.Count; i++)
            if (BuildMenuButtonRect(i).Contains(mx, my)) return i;
        return -1;
    }

    /// <summary>True αν ο δείκτης είναι πάνω σε κουμπί της μπάρας ή της ανοιχτής παλέτας (για απόκρυψη hover hint).</summary>
    private bool IsPointerOverToolbar(int mx, int my)
    {
        for (int i = 0; i < TotalToolbarSlots; i++)
            if (ToolbarButtonRect(i, TotalToolbarSlots).Contains(mx, my)) return true;
        return _buildMenuOpen && BuildMenuHitIndex(mx, my) >= 0;
    }

    private void DrawToolbar()
    {
        int slots = TotalToolbarSlots;
        var ms = Mouse.GetState();

        // Αν είναι ανοιχτή, ζωγράφισε πρώτα την popup παλέτα (2 σειρές πάνω από τη μπάρα).
        if (_buildMenuOpen) DrawBuildMenu(ms);

        // Κουμπί «Κτίρια» (toggle παλέτας) — πρώτο στη μπάρα.
        {
            var rect = ToolbarButtonRect(BuildingsSlotIndex, slots);
            bool active = _buildMenuOpen || _buildMode;
            _spriteBatch.Draw(_pixel, rect, active ? new Color(50, 70, 90, 235) : new Color(18, 20, 28, 215));
            DrawRectOutline(rect, active ? new Color(120, 190, 240) : new Color(70, 74, 90));
            _spriteBatch.Draw(_buildingsIcon, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6), Color.White);
        }

        // Κουμπί reclaim (μόλις ερευνηθεί η τεχνολογία "reclaim")
        if (ReclaimUnlocked)
        {
            var rect = ToolbarButtonRect(ReclaimSlotIndex, slots);
            _spriteBatch.Draw(_pixel, rect, _reclaimMode ? new Color(80, 60, 30, 235) : new Color(18, 20, 28, 215));
            DrawRectOutline(rect, _reclaimMode ? new Color(240, 170, 80) : new Color(70, 74, 90));
            _spriteBatch.Draw(_reclaimIcon, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6), Color.White);
        }

        // Κουμπί βοήθειας "?" (πάντα τελευταίο)
        {
            var rect = ToolbarButtonRect(HelpSlotIndex, slots);
            _spriteBatch.Draw(_pixel, rect, _showHelp ? new Color(30, 50, 80, 235) : new Color(18, 20, 28, 215));
            DrawRectOutline(rect, _showHelp ? new Color(150, 200, 255) : new Color(70, 74, 90));
            var qs = _font.MeasureString("?") * 1.6f;
            _spriteBatch.DrawString(_font, "?", new Vector2(rect.Center.X - qs.X / 2f, rect.Center.Y - qs.Y / 2f),
                new Color(150, 200, 255), 0f, Vector2.Zero, 1.6f, SpriteEffects.None, 0f);
        }

        // Tooltips μπάρας (η παλέτα έχει δικά της tooltips μέσα στο DrawBuildMenu)
        if (BuildingsButtonHit(ms.X, ms.Y))
            DrawToolbarTooltip("Buildings   (B — browse & place)", ToolbarButtonRect(BuildingsSlotIndex, slots).Center.X);
        else if (ReclaimButtonHit(ms.X, ms.Y))
            DrawToolbarTooltip("Reclaim   (recycle a building for credits)", ToolbarButtonRect(ReclaimSlotIndex, slots).Center.X);
        else if (HelpButtonHit(ms.X, ms.Y))
            DrawToolbarTooltip("Help   (how to play)", ToolbarButtonRect(HelpSlotIndex, slots).Center.X);
    }

    /// <summary>Ζωγραφίζει την popup παλέτα κτιρίων: πλαίσιο φόντου + 2 σειρές εικονιδίων + tooltip.</summary>
    private void DrawBuildMenu(MouseState ms)
    {
        if (_buildables.Count == 0) return;

        // Πλαίσιο φόντου γύρω από όλα τα κουμπιά.
        Rectangle panel = BuildMenuButtonRect(0);
        for (int i = 1; i < _buildables.Count; i++)
            panel = Rectangle.Union(panel, BuildMenuButtonRect(i));
        panel.Inflate(8, 8);
        _spriteBatch.Draw(_pixel, panel, new Color(12, 14, 20, 240));
        DrawRectOutline(panel, new Color(90, 130, 170));

        for (int i = 0; i < _buildables.Count; i++)
        {
            var def = _buildables[i];
            var rect = BuildMenuButtonRect(i);
            bool selected = _buildMode && _buildIndex == i;
            _spriteBatch.Draw(_pixel, rect, selected ? new Color(50, 80, 50, 235) : new Color(18, 20, 28, 235));
            DrawRectOutline(rect, selected ? new Color(120, 230, 120) : new Color(70, 74, 90));
            if (_icons.TryGetValue(def.Id, out var tex))
                _spriteBatch.Draw(tex, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6), Color.White);
        }

        int hover = BuildMenuHitIndex(ms.X, ms.Y);
        if (hover >= 0)
        {
            var def = _buildables[hover];
            var rect = BuildMenuButtonRect(hover);
            var label = $"{def.Name}   ({CostString(def)})";
            var size = _font.MeasureString(label);
            var pos = new Vector2(rect.Center.X - size.X / 2f, rect.Y - size.Y - 6);
            _spriteBatch.Draw(_pixel, new Rectangle((int)pos.X - 5, (int)pos.Y - 2, (int)size.X + 10, (int)size.Y + 4), new Color(0, 0, 0, 230));
            _spriteBatch.DrawString(_font, label, pos, HudWhite);
        }
    }

    private void DrawToolbarTooltip(string label, int centerX)
    {
        var size = _font.MeasureString(label);
        var pos = new Vector2(centerX - size.X / 2f, GraphicsDevice.Viewport.Height - 46 - 8 - 24);
        _spriteBatch.Draw(_pixel, new Rectangle((int)pos.X - 5, (int)pos.Y - 2, (int)size.X + 10, (int)size.Y + 4), new Color(0, 0, 0, 220));
        _spriteBatch.DrawString(_font, label, pos, HudWhite);
    }

    private void DrawRectOutline(Rectangle r, Color c)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, 1), c);
        _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
        _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Y, 1, r.Height), c);
        _spriteBatch.Draw(_pixel, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
    }

    /// <summary>Tooltip κοντά στον κέρσορα όταν είμαστε πάνω σε κτίριο: τίτλος, προσωπικό, % χτισίματος.</summary>
    private void DrawHoverHint()
    {
        if (_hoveredBuilding is null) return;
        var b = _hoveredBuilding;
        var d = b.Definition;

        var lines = new List<(string text, Color color)> { (d.Name, HudWhite) };
        lines.Add(d.MaxWorkers > 0
            ? ($"workers: {b.Workers.Count}/{d.MaxWorkers}", HudDim)
            : ("automatic (no crew)", HudDim));

        if (b.State == BuildingState.UnderConstruction)
            lines.Add(($"building {b.BuildFraction * 100:0}%{(b.Stalled ? " (stalled)" : "")}",
                b.Stalled ? HudWarn : new Color(120, 230, 120)));
        else if (b.State == BuildingState.Disabled)
            lines.Add(($"DISABLED ({b.RepairTicksRemaining / 4}s to repair)", HudWarn));

        var ms = Mouse.GetState();
        float w = PanelWidth(lines);
        float h = lines.Count * _font.LineSpacing + 16f;
        float x = MathF.Min(ms.X + 16, GraphicsDevice.Viewport.Width - w - 6);
        float y = MathF.Min(ms.Y + 16, GraphicsDevice.Viewport.Height - h - 6);
        DrawTextPanel(new Vector2(x, y), lines);
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
            (ResLine("Silicon", ResourceKind.Silicon), RateColor(ResourceKind.Silicon)),
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
        top.Add(($"Population {_world.Colony.Colonists.Count}/{_world.Colony.Housing}", HudDim));

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
        else if (_reclaimMode)
        {
            bottom.Add(("RECLAIM: click a building to recycle it for credits", new Color(240, 170, 80)));
            bottom.Add(("RMB=cancel", HudDim));
        }
        else
        {
            bottom.Add(($"B=build menu   buildings: {_world.Colony.Buildings.Count}", HudDim));
        }
        if (_statusTimer > 0)
            bottom.Add((_status, _status.StartsWith("Built") ? new Color(120, 230, 120) : HudWarn));

        foreach (var note in _world.EventNotifications.AsEnumerable().Reverse().Take(3))
            bottom.Add(("* " + note, HudDim));

        bottom.Add(("Space=pause  1/2/3=speed  click icon=build  T=research  +/-=crew  G=sponsor  F5/F9=save/load  N=new  U=mute  Esc=menu", HudDim));

        float panelH = bottom.Count * _font.LineSpacing + 16f;
        DrawTextPanel(new Vector2(10, GraphicsDevice.Viewport.Height - panelH - 64f), bottom);

        if (_selectedBuilding is not null)
            DrawBuildingPanel();

        DrawToolbar();

        if (!IsPointerOverToolbar(Mouse.GetState().X, Mouse.GetState().Y))
            DrawHoverHint();

        if (_world.IsTerraformed)
            DrawCenterBanner("***  PLANET TERRAFORMED  ***", new Color(120, 230, 120));
        else if (_world.IsLost)
            DrawCenterBanner("***  COLONY LOST - press Esc  ***", new Color(255, 90, 80));

        if (DialogOpen) DrawDialog();

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
            _music?.Dispose();
            _phaseTex?.Dispose();
            _renderer?.Dispose();
            _pixel?.Dispose();
            _spriteBatch?.Dispose();
        }
        base.Dispose(disposing);
    }
}

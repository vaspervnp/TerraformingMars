using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Persistence;
using TerraformingMars.Core.Planet;
using TerraformingMars.Core.Research;
using TerraformingMars.Core.Simulation;
using TerraformingMars.Game.Audio;
using TerraformingMars.Game.Persistence;
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
    private bool _speedMenuOpen;   // popup ταχύτητας (pause / x1 / x2 / x4)
    private bool _researchMenuOpen; // popup διαθέσιμων ερευνών
    private enum CatalogHelp { None, Buildings, Research }
    private CatalogHelp _catalogHelp = CatalogHelp.None; // ανοιχτό modal βοήθειας (όλα τα κτίρια/έρευνες σε ένα παράθυρο)
    private int _catalogScroll;    // κύλιση (px) του παραθύρου βοήθειας καταλόγου
    private int _catalogScrollMax; // μέγιστη κύλιση — υπολογίζεται στο Draw
    private RasterizerState _scissorRaster = null!; // clip της κυλιόμενης λίστας του παραθύρου καταλόγου
    private Rectangle _crewPlusRect, _crewMinusRect; // κουμπιά επάνδρωσης (+/-) στο panel κτηρίου
    private string _status = "";
    private double _statusTimer;
    private Point _mouseDownPos;

    // Μετρητές HUD (κτήρια χωρίς εργαζόμενους / με τελειωμένα resources): ανανεώνονται περιοδικά, όχι κάθε frame.
    private int _crewNeededCount;
    private int _depletedCount;
    private double _buildingCheckTimer;
    private const double BuildingCheckInterval = 3.0;

    // Μετακίνηση & απομνημόνευση θέσης του panel κτηρίου (drag + JSON persistence).
    private UiSettings _uiSettings = null!;
    private Rectangle _buildingPanelRect;   // ορατό κουτί του panel (για hit-test του drag)
    private Vector2 _buildingPanelPos;       // τρέχουσα θέση (πάνω-αριστερά)
    private bool _buildingPanelPosSet;       // αν έχει «κλειδώσει» θέση· αλλιώς προεπιλογή πάνω-δεξιά
    private bool _draggingPanel;             // σέρνεται αυτή τη στιγμή το panel;
    private Point _panelDragOffset;          // απόσταση κέρσορα από τη γωνία του panel τη στιγμή που ξεκίνησε το drag
    private bool _panelMoved;                // μετακινήθηκε πραγματικά μέσα σε αυτό το drag (για αποθήκευση);

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
    private bool _phase2Celebrating;   // ανοιχτό modal εορτασμού μετάβασης στη Φάση 2
    private bool _uiClick;
    private bool _hasActiveGame;
    private Dictionary<string, Texture2D> _icons = null!;
    private Dictionary<string, Texture2D> _techIcons = null!;
    private Dictionary<string, Texture2D> _resIcons = null!;
    private Dictionary<string, Texture2D> _toolIcons = null!;

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

    // Ενημερωτικό popup γεγονότος (μη-modal: ΔΕΝ παγώνει τον χρόνο· κλείνει με κουμπί/Enter/Esc).
    private sealed class EventPopup
    {
        public string Title = "";
        public Color Accent;
        public List<(string text, Color color)> Lines = new();
    }
    private readonly Queue<EventPopup> _eventPopups = new();
    private Rectangle _eventPopupPanel, _eventPopupCloseRect;

    // Popup ολοκλήρωσης τεχνολογίας (modal, με «X» πάνω δεξιά — όπως τα παράθυρα help).
    private readonly HashSet<string> _knownResearched = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<TechDefinition> _techDone = new();

    // Tutorial: wizard βήμα-βήμα που προχωρά όταν ο παίκτης εκτελεί κάθε ενέργεια (Esc για έξοδο).
    private enum TutStep { Move, Center, BuildMenu, Place, Select, Research, StartResearch, Speed, Done }
    private bool _tutorialActive;
    private TutStep _tutStep;
    private int _tutBaseBuildings;
    private Vector2 _tutBaseCamPos;
    private float _tutBaseZoom;

    // Saves: πολλαπλά αρχεία στον φάκελο SavedGames, με screenshot της στιγμής αποθήκευσης.
    private RenderTarget2D? _captureRT;                 // στιγμιότυπο οθόνης για το save
    private (string slug, string name)? _captureRequest; // εκκρεμές save (γράφεται αφού τραβηχτεί το screenshot)
    private double _autoSaveTimer;                       // χρονόμετρο αυτόματης αποθήκευσης (κάθε 5 λεπτά)
    private int _autoSaveIndex;                          // 0..2 → Auto 1..3 (κυκλικά)
    private const double AutoSaveInterval = 300.0;

    // Οθόνη Load: λίστα με thumbnails + ημ/ώρα, scroll, μεγάλο preview στο κλικ.
    private sealed class SaveEntry { public string Slug = ""; public string Name = ""; public DateTime When; public Texture2D? Thumb; }
    private bool _loadScreenOpen;
    private int _loadScroll, _loadScrollMax;
    private int _previewIndex = -1;                      // -1 = λίστα· >=0 = μεγάλο preview της εγγραφής
    private int _deleteConfirmIndex = -1;                // >=0 = ανοιχτός διάλογος επιβεβαίωσης διαγραφής
    private List<SaveEntry> _saveEntries = new();

    // Reclaim (ανακύκλωση κτιρίων για credits) — ξεκλειδώνει με την τεχνολογία "reclaim"
    private const string ReclaimTechId = "reclaim";
    private const string LandingModuleId = "landing_capsule"; // το αρχικό κτήριο (landing module)
    private Texture2D _reclaimIcon = null!;
    private Texture2D _buildingsIcon = null!;
    private bool _reclaimMode;
    private Building? _reclaimTarget;
    private bool ReclaimUnlocked => _world.Colony.Tech.IsResearched(ReclaimTechId);

    // Μπάρα εργαλείων: δυναμική λίστα κουμπιών (το Reclaim εμφανίζεται μόνο όταν ξεκλειδωθεί).
    private enum Tool { Buildings, Research, Speed, Save, Mute, Center, CrewNeeded, Depleted, Reclaim, Menu, Help }
    private List<Tool> ToolbarTools()
    {
        var list = new List<Tool>
        {
            Tool.Buildings, Tool.Research, Tool.Speed, Tool.Save, Tool.Mute,
            Tool.Center, Tool.CrewNeeded, Tool.Depleted,
        };
        if (ReclaimUnlocked) list.Add(Tool.Reclaim);
        list.Add(Tool.Menu);
        list.Add(Tool.Help);
        return list;
    }

    // Κυκλική πλοήγηση: το τελευταίο κτήριο που δείξαμε με «.»/«,» ώστε το επόμενο πάτημα να πάει παρακάτω.
    private Building? _crewFocus;
    private Building? _depletedFocus;

    // Οθόνη βοήθειας (modal· ανοίγει από τη μπάρα ή το μενού)
    private bool _showHelp;

    // Έκδοση παιχνιδιού (από το <Version> του .csproj) — εμφανίζεται στο κεντρικό μενού.
    private static readonly string VersionText =
        typeof(MarsGame).Assembly.GetName().Version is { } v ? $"v{v.Major}.{v.Minor}" : "v1.0";

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
        _scissorRaster = new RasterizerState { ScissorTestEnable = true };

        _renderer = new HexMapRenderer(GraphicsDevice, HexSize);
        _renderer.Build(_map);
        _lastMapRevision = _world.MapRevision;
        _audio = new AudioManager();
        _music = new MusicPlayer();
        InitAudio();
        _uiSettings = UiSettings.Load();
        _icons = IconFactory.CreateAll(GraphicsDevice, _catalog);
        _reclaimIcon = IconFactory.CreateReclaim(GraphicsDevice);
        _buildingsIcon = IconFactory.CreateBuildings(GraphicsDevice);
        _techIcons = IconFactory.CreateTechIcons(GraphicsDevice, _world.Colony.Tech.Catalog, _icons, _reclaimIcon);
        _resIcons = IconFactory.CreateResourceIcons(GraphicsDevice);
        _toolIcons = IconFactory.CreateUiIcons(GraphicsDevice);
        _phaseTex = LoadPhaseTexture();
        InitCamera();
    }

    private void StartGame()
    {
        _settings = new MapGenerationSettings { Width = 64, Height = 44, Seed = _seed };
        _map = new MapGenerator(_settings).Generate();
        _world = ColonyFactory.CreateStartingWorld(_map, _catalog, _sponsor, enableEvents: true);
        ResetSessionState();
    }

    /// <summary>Κοινή επαναφορά κατάστασης μετά από νέο παιχνίδι ή φόρτωση (UI, κάμερα, trackers).</summary>
    private void ResetSessionState()
    {
        _renderer.Build(_map);
        _lastMapRevision = _world.MapRevision;
        _selected = null;
        _selectedBuilding = null;
        _buildMode = false;
        _buildMenuOpen = false;
        _speedMenuOpen = false;
        _researchMenuOpen = false;
        _reclaimMode = false;
        _reclaimTarget = null;
        _eventPopups.Clear();
        CloseDialog();
        ResetTransitionTrackers();
        InitCamera();
        _tutorialActive = false;
        _autoSaveTimer = 0;
        _hasActiveGame = true;
        _state = GameState.Playing;
    }

    /// <summary>Ξεκινά νέο παιχνίδι σε λειτουργία tutorial: βήμα-βήμα οδηγίες, παγωμένος χρόνος στην αρχή.</summary>
    private void StartTutorial()
    {
        StartGame();
        _tutorialActive = true;
        _tutStep = TutStep.Move;
        _world.Clock.Speed = GameSpeed.Paused;          // ήρεμη ανάγνωση· το βήμα «Speed» διδάσκει το ξεκίνημα
        _tutBaseCamPos = _camera.Position;
        _tutBaseZoom = _camera.Zoom;
        _status = "Tutorial started - press Esc to exit anytime";
        _statusTimer = 4.0;
    }

    /// <summary>Τερματίζει το tutorial (Esc ή ολοκλήρωση) και ξαναρχίζει τον χρόνο αν ήταν παγωμένος.</summary>
    private void EndTutorial()
    {
        _tutorialActive = false;
        if (_world.Clock.Speed == GameSpeed.Paused) _world.Clock.Speed = GameSpeed.Normal;
        _status = "Tutorial ended";
        _statusTimer = 3.0;
        _audio.Blip();
    }

    /// <summary>Φορτώνει ένα συγκεκριμένο save (slug) από τον φάκελο SavedGames και μπαίνει σε Playing.</summary>
    private void LoadSlot(string slug)
    {
        try
        {
            _world = SaveSystem.Load(File.ReadAllText(SaveManager.JsonPath(slug)), _catalog, _sponsorCatalog, out _sponsor);
            _map = _world.Map;
            _sponsorIndex = Math.Max(0, _sponsorCatalog.All.ToList().FindIndex(s => s.Id == _sponsor.Id));
            ResetSessionState();
            CloseLoadScreen();
            _status = "Game loaded";
            _statusTimer = 3.0;
        }
        catch
        {
            _status = "Load failed (corrupt save?)";
            _statusTimer = 3.0;
        }
    }

    // -------- Οθόνη Load (λίστα save με thumbnails, ημ/ώρα, scroll & μεγάλο preview) --------

    private void OpenLoadScreen()
    {
        LoadSaveEntries();
        _loadScreenOpen = true;
        _loadScroll = 0;
        _previewIndex = -1;
        _audio.Blip();
    }

    private void CloseLoadScreen()
    {
        foreach (var e in _saveEntries) e.Thumb?.Dispose();
        _saveEntries.Clear();
        _loadScreenOpen = false;
        _previewIndex = -1;
        _deleteConfirmIndex = -1;
    }

    /// <summary>Σκανάρει τον φάκελο SavedGames: metadata + thumbnail κάθε save, ταξινομημένα (νεότερα πρώτα).</summary>
    private void LoadSaveEntries()
    {
        foreach (var e in _saveEntries) e.Thumb?.Dispose();
        var list = new List<SaveEntry>();
        foreach (var slug in SaveManager.Slugs())
        {
            var entry = new SaveEntry { Slug = slug };
            string json = SaveManager.JsonPath(slug);
            try
            {
                var (name, when) = SaveSystem.ReadInfo(File.ReadAllText(json));
                entry.Name = string.IsNullOrEmpty(name) ? slug : name;
                entry.When = when == DateTime.MinValue ? File.GetLastWriteTime(json) : when;
            }
            catch { entry.Name = slug; entry.When = File.GetLastWriteTime(json); }

            string png = SaveManager.PngPath(slug);
            if (File.Exists(png))
                try { using var fs = File.OpenRead(png); entry.Thumb = Texture2D.FromStream(GraphicsDevice, fs); }
                catch { /* χωρίς thumbnail */ }

            list.Add(entry);
        }
        _saveEntries = list.OrderByDescending(e => e.When).ToList();
    }

    private void DeleteSaveEntry(int i)
    {
        if (i < 0 || i >= _saveEntries.Count) return;
        SaveManager.Delete(_saveEntries[i].Slug);
        LoadSaveEntries();
        _loadScroll = Math.Clamp(_loadScroll, 0, LoadScrollMax());
        _audio.Blip();
    }

    // ---- Γεωμετρία οθόνης Load ----
    private const int LoadRowH = 116;

    private Rectangle LoadPanelRect()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        int w = Math.Min(vw - 40, 760);
        int h = Math.Min(vh - 40, 820);
        return new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
    }

    private Rectangle LoadCloseRect() => CloseButtonRect(LoadPanelRect());

    private Rectangle LoadContentRect()
    {
        var p = LoadPanelRect();
        const int pad = 20;
        int top = p.Y + 64;
        return new Rectangle(p.X + pad, top, p.Width - pad * 2, p.Bottom - pad - top);
    }

    private Rectangle LoadRowRect(int i)
    {
        var c = LoadContentRect();
        return new Rectangle(c.X, c.Y - _loadScroll + i * LoadRowH, c.Width, LoadRowH - 8);
    }

    private Rectangle LoadThumbRect(int i) { var r = LoadRowRect(i); return new Rectangle(r.X + 6, r.Y + 6, 160, r.Height - 12); }
    private Rectangle LoadRowLoadBtn(int i) { var r = LoadRowRect(i); return new Rectangle(r.Right - 110, r.Y + 12, 96, 34); }
    private Rectangle LoadRowDelBtn(int i) { var r = LoadRowRect(i); return new Rectangle(r.Right - 110, r.Bottom - 12 - 34, 96, 34); }

    private int LoadScrollMax() => Math.Max(0, _saveEntries.Count * LoadRowH - LoadContentRect().Height);

    private Rectangle PreviewPanelRect()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        int w = Math.Min(vw - 60, 940);
        int h = Math.Min(vh - 60, 680);
        return new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
    }
    private Rectangle PreviewCloseRect() => CloseButtonRect(PreviewPanelRect());
    private Rectangle PreviewLoadBtn()
    {
        var p = PreviewPanelRect();
        const int bw = 180, bh = 46;
        return new Rectangle(p.Center.X - bw / 2, p.Bottom - bh - 16, bw, bh);
    }

    private Rectangle DeleteConfirmPanel()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        const int w = 440, h = 170;
        return new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
    }
    private Rectangle DeleteConfirmBtn() { var p = DeleteConfirmPanel(); return new Rectangle(p.Center.X - 190, p.Bottom - 20 - 40, 180, 40); }
    private Rectangle DeleteCancelBtn() { var p = DeleteConfirmPanel(); return new Rectangle(p.Center.X + 10, p.Bottom - 20 - 40, 180, 40); }

    // ---- Είσοδος οθόνης Load ----
    private void UpdateLoadScreen(KeyboardState keys, MouseState mouse)
    {
        _uiClick = false;
        bool click = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;

        // Επιβεβαίωση διαγραφής (πάνω από όλα): Delete / Cancel-Esc / κλικ έξω.
        if (_deleteConfirmIndex >= 0)
        {
            if (KeyPressed(keys, Keys.Escape) || (click && DeleteCancelBtn().Contains(mouse.X, mouse.Y)))
            { _deleteConfirmIndex = -1; _audio.Blip(); return; }
            if (click && DeleteConfirmBtn().Contains(mouse.X, mouse.Y))
            { int idx = _deleteConfirmIndex; _deleteConfirmIndex = -1; DeleteSaveEntry(idx); return; }
            if (click && !DeleteConfirmPanel().Contains(mouse.X, mouse.Y))
            { _deleteConfirmIndex = -1; _audio.Blip(); }
            return;
        }

        // Μεγάλο preview: Load / Close(X)/Esc / κλικ έξω.
        if (_previewIndex >= 0)
        {
            if (KeyPressed(keys, Keys.Escape) || (click && PreviewCloseRect().Contains(mouse.X, mouse.Y)))
            { _previewIndex = -1; _audio.Blip(); return; }
            if (click && PreviewLoadBtn().Contains(mouse.X, mouse.Y))
            { LoadSlot(_saveEntries[_previewIndex].Slug); return; }
            if (click && !PreviewPanelRect().Contains(mouse.X, mouse.Y))
            { _previewIndex = -1; _audio.Blip(); }
            return;
        }

        int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (wheel != 0) _loadScroll = Math.Clamp(_loadScroll - Math.Sign(wheel) * 48, 0, LoadScrollMax());

        if (KeyPressed(keys, Keys.Escape) || (click && LoadCloseRect().Contains(mouse.X, mouse.Y)))
        { CloseLoadScreen(); _audio.Blip(); return; }

        if (click && LoadContentRect().Contains(mouse.X, mouse.Y))
            for (int i = 0; i < _saveEntries.Count; i++)
            {
                if (LoadThumbRect(i).Contains(mouse.X, mouse.Y)) { _previewIndex = i; _audio.Blip(); return; }
                if (LoadRowLoadBtn(i).Contains(mouse.X, mouse.Y)) { LoadSlot(_saveEntries[i].Slug); return; }
                if (LoadRowDelBtn(i).Contains(mouse.X, mouse.Y)) { _deleteConfirmIndex = i; _audio.Blip(); return; }
            }
    }

    // ---- Σχεδίαση οθόνης Load ----
    private void DrawLoadScreen()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        var ms = Mouse.GetState();
        var panel = LoadPanelRect();
        var content = LoadContentRect();
        _loadScrollMax = LoadScrollMax();
        _loadScroll = Math.Clamp(_loadScroll, 0, _loadScrollMax);

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 190));
        _spriteBatch.Draw(_pixel, panel, new Color(16, 18, 26, 250));
        DrawRectOutline(panel, new Color(120, 130, 150));
        const string title = "LOAD GAME";
        var tsz = _font.MeasureString(title) * 1.4f;
        _spriteBatch.DrawString(_font, title, new Vector2(panel.Center.X - tsz.X / 2f, panel.Y + 16),
            new Color(150, 210, 255), 0f, Vector2.Zero, 1.4f, SpriteEffects.None, 0f);
        if (_saveEntries.Count == 0)
        {
            var msg = "No saved games yet.";
            var msz = _font.MeasureString(msg);
            _spriteBatch.DrawString(_font, msg, new Vector2(content.Center.X - msz.X / 2f, content.Center.Y - msz.Y / 2f), HudDim);
        }
        _spriteBatch.End();

        if (_saveEntries.Count > 0)
        {
            var prevScissor = GraphicsDevice.ScissorRectangle;
            GraphicsDevice.ScissorRectangle = content;
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, _scissorRaster);
            for (int i = 0; i < _saveEntries.Count; i++)
            {
                var row = LoadRowRect(i);
                if (row.Bottom < content.Y || row.Y > content.Bottom) continue; // εκτός ορατού
                var e = _saveEntries[i];
                bool hoverRow = row.Contains(ms.X, ms.Y) && content.Contains(ms.X, ms.Y);
                _spriteBatch.Draw(_pixel, row, hoverRow ? new Color(30, 40, 56, 235) : new Color(22, 26, 36, 235));
                DrawRectOutline(row, new Color(70, 80, 100));

                var thumb = LoadThumbRect(i);
                if (e.Thumb is not null) _spriteBatch.Draw(e.Thumb, thumb, Color.White);
                else _spriteBatch.Draw(_pixel, thumb, new Color(40, 44, 54));
                DrawRectOutline(thumb, new Color(60, 66, 80));

                int tx = thumb.Right + 16;
                _spriteBatch.DrawString(_font, e.Name, new Vector2(tx, row.Y + 14), new Color(255, 205, 120),
                    0f, Vector2.Zero, 1.1f, SpriteEffects.None, 0f);
                _spriteBatch.DrawString(_font, e.When.ToString("ddd dd MMM yyyy  HH:mm:ss"),
                    new Vector2(tx, row.Y + 14 + (int)(_font.LineSpacing * 1.1f) + 4), HudDim);

                DrawSmallButton(LoadRowLoadBtn(i), "Load", new Color(120, 230, 140), ms);
                DrawSmallButton(LoadRowDelBtn(i), "Delete", new Color(230, 120, 110), ms);
            }
            _spriteBatch.End();
            GraphicsDevice.ScissorRectangle = prevScissor;
        }

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        if (_loadScrollMax > 0)
        {
            int trackX = content.Right - 5, trackH = content.Height, total = _saveEntries.Count * LoadRowH;
            _spriteBatch.Draw(_pixel, new Rectangle(trackX, content.Y, 5, trackH), new Color(40, 44, 54));
            int thumbH = Math.Max(24, (int)((long)trackH * content.Height / total));
            int thumbY = content.Y + (int)((long)(trackH - thumbH) * _loadScroll / _loadScrollMax);
            _spriteBatch.Draw(_pixel, new Rectangle(trackX, thumbY, 5, thumbH), new Color(130, 150, 180));
        }
        DrawCloseButton(LoadCloseRect(), ms);
        _spriteBatch.End();

        if (_previewIndex >= 0 && _previewIndex < _saveEntries.Count) DrawPreview(ms);
        if (_deleteConfirmIndex >= 0 && _deleteConfirmIndex < _saveEntries.Count) DrawDeleteConfirm(ms);
        else _deleteConfirmIndex = -1;
    }

    /// <summary>Διάλογος επιβεβαίωσης διαγραφής ενός save (Delete / Cancel).</summary>
    private void DrawDeleteConfirm(MouseState ms)
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        var e = _saveEntries[_deleteConfirmIndex];
        var panel = DeleteConfirmPanel();

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 200));
        _spriteBatch.Draw(_pixel, panel, new Color(20, 18, 22, 252));
        DrawRectOutline(panel, new Color(230, 120, 110));

        const string q = "Delete this save?";
        var qsz = _font.MeasureString(q) * 1.1f;
        _spriteBatch.DrawString(_font, q, new Vector2(panel.Center.X - qsz.X / 2f, panel.Y + 20),
            HudWhite, 0f, Vector2.Zero, 1.1f, SpriteEffects.None, 0f);

        string sub = $"{e.Name}   ·   {e.When:dd MMM yyyy  HH:mm}";
        var ssz = _font.MeasureString(sub);
        _spriteBatch.DrawString(_font, sub, new Vector2(panel.Center.X - ssz.X / 2f, panel.Y + 20 + qsz.Y + 8), HudDim);

        DrawSmallButton(DeleteConfirmBtn(), "Delete", new Color(230, 120, 110), ms);
        DrawSmallButton(DeleteCancelBtn(), "Cancel", new Color(180, 190, 205), ms);
        _spriteBatch.End();
    }

    /// <summary>Μικρό κουμπί κειμένου (Load/Delete) με highlight στο hover.</summary>
    private void DrawSmallButton(Rectangle r, string label, Color accent, MouseState ms)
    {
        bool hover = r.Contains(ms.X, ms.Y);
        _spriteBatch.Draw(_pixel, r, hover ? new Color(50, 58, 74) : new Color(30, 34, 46));
        DrawRectOutline(r, accent);
        var sz = _font.MeasureString(label);
        _spriteBatch.DrawString(_font, label, new Vector2(r.Center.X - sz.X / 2f, r.Center.Y - sz.Y / 2f), accent);
    }

    /// <summary>Μεγάλο preview του screenshot ενός save, με Load και «X» κλείσιμο.</summary>
    private void DrawPreview(MouseState ms)
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        var e = _saveEntries[_previewIndex];
        var panel = PreviewPanelRect();
        const int pad = 20;

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 210));
        _spriteBatch.Draw(_pixel, panel, new Color(16, 18, 26, 252));
        DrawRectOutline(panel, new Color(120, 130, 150));

        string header = $"{e.Name}   ·   {e.When:ddd dd MMM yyyy  HH:mm:ss}";
        _spriteBatch.DrawString(_font, header, new Vector2(panel.X + pad, panel.Y + 16), HudWhite,
            0f, Vector2.Zero, 1.05f, SpriteEffects.None, 0f);

        var area = new Rectangle(panel.X + pad, panel.Y + 52, panel.Width - pad * 2, panel.Height - 52 - 78);
        float ar = e.Thumb is not null && e.Thumb.Height > 0 ? (float)e.Thumb.Width / e.Thumb.Height : 1.6f;
        int iw = area.Width, ih = (int)(iw / ar);
        if (ih > area.Height) { ih = area.Height; iw = (int)(ih * ar); }
        var img = new Rectangle(area.Center.X - iw / 2, area.Y + (area.Height - ih) / 2, iw, ih);
        if (e.Thumb is not null) _spriteBatch.Draw(e.Thumb, img, Color.White);
        else _spriteBatch.Draw(_pixel, img, new Color(40, 44, 54));
        DrawRectOutline(img, new Color(70, 80, 100));

        DrawSmallButton(PreviewLoadBtn(), "Load", new Color(120, 230, 140), ms);
        DrawCloseButton(PreviewCloseRect(), ms);
        _spriteBatch.End();
    }

    private void ResetTransitionTrackers()
    {
        _prevResearchedCount = _world.Colony.Tech.Researched.Count;
        _lastNotification = _world.EventNotifications.Count > 0 ? _world.EventNotifications[^1] : "";
        _prevWon = _world.Phase2Active;   // load Φάσης-2 save: μην ξαναπαίξει το chime ούτε το popup
        _phase2Celebrating = false;
        _prevLost = false;

        // Οι ήδη ερευνημένες (π.χ. από load) δεν βγάζουν popup — μόνο όσες ολοκληρωθούν από εδώ κι εμπρός.
        _knownResearched.Clear();
        foreach (var id in _world.Colony.Tech.Researched) _knownResearched.Add(id);
        _techDone.Clear();

        RefreshBuildingCounts(); // άμεσος υπολογισμός ώστε το HUD να είναι σωστό από το πρώτο frame
        _buildingCheckTimer = BuildingCheckInterval;
    }

    /// <summary>Ανανεώνει τους μετρητές HUD: κτήρια που χρειάζονται προσωπικό / με εξαντλημένο κοίτασμα.</summary>
    private void RefreshBuildingCounts()
    {
        _crewNeededCount = _world.Colony.Buildings.Count(NeedsCrew);
        _depletedCount = _world.Colony.Buildings.Count(IsDepleted);
    }

    private void CheckAudioTransitions()
    {
        int researched = _world.Colony.Tech.Researched.Count;
        if (researched > _prevResearchedCount) _audio.Chime();
        _prevResearchedCount = researched;

        string last = _world.EventNotifications.Count > 0 ? _world.EventNotifications[^1] : "";
        if (last.Length > 0 && last != _lastNotification) _audio.Alert();
        _lastNotification = last;

        bool won = _world.Phase2Active;   // latched: το chime παίζει μία φορά στη μετάβαση στη Φάση 2
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
        if (SaveManager.HasAny()) list.Add(("Load Game", OpenLoadScreen, new Color(150, 210, 255)));
        list.Add((_hasActiveGame ? "New Game" : "Start Game", StartGame, new Color(255, 220, 120)));
        list.Add(("Tutorial", StartTutorial, new Color(150, 230, 150)));
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
        const int bh = 50, gap = 20, maxBw = 210, margin = 30;
        int avail = GraphicsDevice.Viewport.Width - margin * 2 - (count - 1) * gap;
        int bw = Math.Clamp(avail / Math.Max(1, count), 120, maxBw); // σμίκρυνση ώστε να χωρούν όλα τα κουμπιά
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

        // Έκδοση, κάτω-δεξιά γωνία.
        var vsz = _font.MeasureString(VersionText) * 0.9f;
        _spriteBatch.DrawString(_font, VersionText,
            new Vector2(GraphicsDevice.Viewport.Width - vsz.X - 12f, GraphicsDevice.Viewport.Height - vsz.Y - 10f),
            HudDim, 0f, Vector2.Zero, 0.9f, SpriteEffects.None, 0f);

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
        "Raise Temperature, Pressure, Oxygen and Water to their target levels.",
        "Win when all four goals (top, under the resource bar) hit 100% - keep your crew alive.",
        "",
        "BUILD",
        "Click the buildings icon (bottom bar), pick a building, then a hex.",
        "Costs Credits up front, plus Materials while it is being built.",
        "Mines need a matching deposit (H2O / Fe / Si / Rg) - coloured diamonds.",
        "",
        "CREW",
        "Right-click a building to select it. Then use [-] / [+] (or +/- keys)",
        "to assign or remove colonists - staffed buildings produce more.",
        "",
        "RESEARCH & RECLAIM",
        "Click the research icon (or T) and pick a technology to research.",
        "Research 'Reclaim' to recycle a building for Credits and Materials back (R).",
        "",
        "TIME, SAVE & LOAD",
        "Clock icon: pause / x1 / x2 / x4  (or Space, 1 / 2 / 3).",
        "Save: F5 or the disk icon.   Load: F9, or 'Load Game' from the menu.",
        "",
        "CONTROLS",
        "Move: WASD / arrows / drag   Zoom: wheel   Select: right-click   Mute: U",
        "Center on landing module: H, or the crosshair icon in the bottom bar.",
        "Jump to a building needing crew: '.'   to a depleted deposit: ','",
        "Resource numbers (top) turn red when falling.   Menu / back: Esc.",
    };

    private Rectangle HelpPanelRect()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        // Πλάτος προσαρμοσμένο στη μεγαλύτερη γραμμή (26px περιθώριο δεξιά+αριστερά), με cap στο παράθυρο.
        float maxLine = _font.MeasureString("HOW TO PLAY").X * 1.5f;
        foreach (var line in HelpText)
            maxLine = MathF.Max(maxLine, _font.MeasureString(line).X);
        int w = Math.Min(vw - 40, (int)maxLine + 52);
        int h = Math.Min(vh - 60, 680);
        return new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
    }

    private Rectangle HelpCloseRect() => CloseButtonRect(HelpPanelRect());

    /// <summary>Τετράγωνο κουμπί «X» κλεισίματος στην πάνω-δεξιά γωνία ενός παραθύρου.</summary>
    private static Rectangle CloseButtonRect(Rectangle panel)
    {
        const int s = 34;
        return new Rectangle(panel.Right - s - 10, panel.Y + 10, s, s);
    }

    /// <summary>Ζωγραφίζει το κουμπί «X» κλεισίματος (τονίζεται κόκκινο στο hover).</summary>
    private void DrawCloseButton(Rectangle rect, MouseState ms)
    {
        bool hover = rect.Contains(ms.X, ms.Y);
        _spriteBatch.Draw(_pixel, rect, hover ? new Color(120, 46, 44) : new Color(34, 38, 50));
        DrawRectOutline(rect, hover ? new Color(255, 130, 120) : new Color(150, 200, 255));
        const float sc = 1.2f;
        var xs = _font.MeasureString("X") * sc;
        _spriteBatch.DrawString(_font, "X", new Vector2(rect.Center.X - xs.X / 2f, rect.Center.Y - xs.Y / 2f),
            hover ? new Color(255, 210, 205) : HudWhite, 0f, Vector2.Zero, sc, SpriteEffects.None, 0f);
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

        DrawCloseButton(HelpCloseRect(), ms);

        _spriteBatch.End();
    }

    // -------- Παράθυρο βοήθειας καταλόγου (όλα τα κτίρια / όλες οι τεχνολογίες σε ένα modal) --------

    private Rectangle CatalogHelpPanelRect()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        int w = Math.Min(vw - 40, 640);
        int h = Math.Min(vh - 60, 720);
        return new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
    }

    private Rectangle CatalogHelpCloseRect() => CloseButtonRect(CatalogHelpPanelRect());

    /// <summary>
    /// Modal (στο στιλ του κεντρικού help): όλα τα κτίρια ή όλες οι τεχνολογίες μαζί — καθένα με
    /// μεγάλο εικονίδιο + περιγραφή/χαρακτηριστικά — με κύλιση (ρόδα) και κουμπί Close.
    /// </summary>
    private void DrawCatalogHelpOverlay()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        var ms = Mouse.GetState();
        var panel = CatalogHelpPanelRect();
        bool buildings = _catalogHelp == CatalogHelp.Buildings;

        const int pad = 20, iconSz = 56, gap = 16, entryGap = 18;
        int lineH = _font.LineSpacing;
        const float nameScale = 1.06f;
        int nameH = (int)(lineH * nameScale) + 2;

        int contentTop = panel.Y + 64;
        var content = new Rectangle(panel.X + pad, contentTop, panel.Width - pad * 2, panel.Bottom - pad - contentTop);
        int textX = content.X + iconSz + gap;
        int textW = content.Right - textX - 12; // αφήνει χώρο για τη μπάρα κύλισης

        // Δόμηση εγγραφών: εικονίδιο, όνομα, κόστος, αναδιπλωμένο σώμα, ύψος.
        var entries = new List<(Texture2D? icon, string name, string subtitle, List<string> body, int height)>();
        void Add(Texture2D? icon, string name, string subtitle, List<string> paragraphs)
        {
            var body = new List<string>();
            foreach (var p in paragraphs)
            {
                if (p.Length == 0) body.Add("");
                else WrapText(body, p, textW);
            }
            int h = Math.Max(iconSz, nameH + lineH + body.Count * lineH) + entryGap;
            entries.Add((icon, name, subtitle, body, h));
        }
        if (buildings)
            foreach (var def in _catalog.All.OrderBy(d => d.Category).ThenBy(d => d.Name))
                Add(_icons.GetValueOrDefault(def.Id), def.Name, $"Cost: {CostString(def)}", BuildingHelpBody(def));
        else
            foreach (var t in _world.Colony.Tech.Catalog.All.OrderBy(x => x.Phase).ThenBy(x => x.Cost))
                Add(_techIcons.GetValueOrDefault(t.Id), t.Name, $"{t.Cost:0} RP  ·  Phase {t.Phase}", TechHelpBody(t));

        int totalH = entries.Sum(e => e.height);
        _catalogScrollMax = Math.Max(0, totalH - content.Height);
        _catalogScroll = Math.Clamp(_catalogScroll, 0, _catalogScrollMax);

        // Batch 1: σκίαση φόντου + πλαίσιο + τίτλος.
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 180));
        _spriteBatch.Draw(_pixel, panel, new Color(16, 18, 26, 250));
        DrawRectOutline(panel, new Color(120, 130, 150));
        string title = buildings ? "BUILDINGS" : "TECHNOLOGIES";
        var tsz = _font.MeasureString(title) * 1.4f;
        _spriteBatch.DrawString(_font, title, new Vector2(panel.Center.X - tsz.X / 2f, panel.Y + 16),
            new Color(230, 150, 90), 0f, Vector2.Zero, 1.4f, SpriteEffects.None, 0f);
        _spriteBatch.End();

        // Batch 2: κυλιόμενες εγγραφές, περιορισμένες (scissor) μέσα στο content.
        var prevScissor = GraphicsDevice.ScissorRectangle;
        GraphicsDevice.ScissorRectangle = content;
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, _scissorRaster);
        float y = content.Y - _catalogScroll;
        foreach (var e in entries)
        {
            if (y + e.height >= content.Y && y <= content.Bottom) // μόνο οι ορατές
            {
                if (e.icon is not null)
                    _spriteBatch.Draw(e.icon, new Rectangle(content.X, (int)y, iconSz, iconSz), Color.White);
                _spriteBatch.DrawString(_font, e.name, new Vector2(textX, y), new Color(255, 205, 120),
                    0f, Vector2.Zero, nameScale, SpriteEffects.None, 0f);
                _spriteBatch.DrawString(_font, e.subtitle, new Vector2(textX, y + nameH), HudDim);
                float by = y + nameH + lineH;
                foreach (var ln in e.body)
                {
                    if (ln.Length > 0) _spriteBatch.DrawString(_font, ln, new Vector2(textX, by), HudWhite);
                    by += lineH;
                }
                _spriteBatch.Draw(_pixel, new Rectangle(content.X, (int)(y + e.height - entryGap / 2), content.Width - 10, 1),
                    new Color(60, 64, 76));
            }
            y += e.height;
        }
        _spriteBatch.End();
        GraphicsDevice.ScissorRectangle = prevScissor;

        // Batch 3: μπάρα κύλισης + κουμπί «X» κλεισίματος (πάνω δεξιά).
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        if (_catalogScrollMax > 0)
        {
            int trackX = content.Right - 5, trackH = content.Height;
            _spriteBatch.Draw(_pixel, new Rectangle(trackX, content.Y, 5, trackH), new Color(40, 44, 54));
            int thumbH = Math.Max(20, (int)((long)trackH * content.Height / totalH));
            int thumbY = content.Y + (int)((long)(trackH - thumbH) * _catalogScroll / _catalogScrollMax);
            _spriteBatch.Draw(_pixel, new Rectangle(trackX, thumbY, 5, thumbH), new Color(130, 150, 180));
        }
        DrawCloseButton(CatalogHelpCloseRect(), ms);
        _spriteBatch.End();
    }

    // -------- Popup ολοκλήρωσης τεχνολογίας (modal, «X» πάνω δεξιά) --------

    private const int TechDonePadX = 20, TechDoneIcon = 64;

    /// <summary>Σώμα του popup: περιγραφή + τι έγινε διαθέσιμο (αναδιπλωμένο στο δοσμένο πλάτος).</summary>
    private List<string> TechDoneBody(TechDefinition t, int textW)
    {
        var paras = new List<string> { t.Description, "" };
        paras.Add(t.Unlocks.Count > 0
            ? "Now available: " + string.Join(", ", t.Unlocks.Select(BuildingName))
            : "A permanent upgrade for your colony.");
        var lines = new List<string>();
        foreach (var p in paras)
        {
            if (p.Length == 0) lines.Add("");
            else WrapText(lines, p, textW);
        }
        return lines;
    }

    private Rectangle TechDonePanelRect()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        int lineH = _font.LineSpacing;
        const int padTop = 14, headGap = 12, sepGap = 16, padBottom = 18;
        int titleH = (int)(lineH * 1.3f);
        int w = Math.Min(vw - 40, 520);
        int bodyCount = _techDone.Count > 0 ? TechDoneBody(_techDone.Peek(), w - TechDonePadX * 2).Count : 1;
        int h = padTop + titleH + headGap + TechDoneIcon + sepGap + bodyCount * lineH + padBottom;
        h = Math.Min(h, vh - 40);
        return new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
    }

    private Rectangle TechDoneCloseRect() =>
        _techDone.Count > 0 ? CloseButtonRect(TechDonePanelRect()) : Rectangle.Empty;

    /// <summary>Modal παράθυρο για την τεχνολογία που μόλις ολοκληρώθηκε: μεγάλο εικονίδιο + όνομα + περιγραφή/ξεκλειδώματα.</summary>
    private void DrawTechDoneOverlay()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        var ms = Mouse.GetState();
        var t = _techDone.Peek();
        var panel = TechDonePanelRect();
        int lineH = _font.LineSpacing;
        const int padX = TechDonePadX, padTop = 14, headGap = 12, iconSz = TechDoneIcon, iconGap = 14, sepGap = 16;
        int titleH = (int)(lineH * 1.3f);
        int nameLineH = (int)(lineH * 1.1f) + 2;

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 180));
        _spriteBatch.Draw(_pixel, panel, new Color(16, 18, 26, 250));
        DrawRectOutline(panel, new Color(120, 130, 150));

        const string title = "RESEARCH COMPLETE";
        var tsz = _font.MeasureString(title) * 1.3f;
        _spriteBatch.DrawString(_font, title, new Vector2(panel.Center.X - tsz.X / 2f, panel.Y + padTop),
            new Color(120, 230, 120), 0f, Vector2.Zero, 1.3f, SpriteEffects.None, 0f);

        int headerY = panel.Y + padTop + titleH + headGap;
        if (_techIcons.TryGetValue(t.Id, out var tex))
            _spriteBatch.Draw(tex, new Rectangle(panel.X + padX, headerY, iconSz, iconSz), Color.White);
        int nameX = panel.X + padX + iconSz + iconGap;
        _spriteBatch.DrawString(_font, t.Name, new Vector2(nameX, headerY), new Color(255, 205, 120),
            0f, Vector2.Zero, 1.1f, SpriteEffects.None, 0f);
        _spriteBatch.DrawString(_font, $"{t.Cost:0} RP  ·  Phase {t.Phase}", new Vector2(nameX, headerY + nameLineH), HudDim);

        int sepY = headerY + iconSz + sepGap / 2;
        _spriteBatch.Draw(_pixel, new Rectangle(panel.X + padX, sepY, panel.Width - padX * 2, 1), new Color(80, 86, 100));

        int bodyY = headerY + iconSz + sepGap;
        var body = TechDoneBody(t, panel.Width - padX * 2);
        for (int i = 0; i < body.Count; i++)
            if (body[i].Length > 0)
                _spriteBatch.DrawString(_font, body[i], new Vector2(panel.X + padX, bodyY + i * lineH), HudWhite);

        DrawCloseButton(TechDoneCloseRect(), ms);
        _spriteBatch.End();
    }

    // -------- Popup εορτασμού μετάβασης στη Φάση 2 (modal, «X» πάνω δεξιά) --------

    private List<string> Phase2Body(int textW)
    {
        var paras = new List<string>
        {
            "Congratulations - Mars is now habitable. All four terraforming goals are met, and the first great migration from Earth has begun.",
            "",
            "But a living world is volatile. If your greenhouse factories and orbital mirrors keep running, temperature and pressure overshoot the sweet spot - a RUNAWAY GREENHOUSE that withers plants, boils off water and sickens your people.",
            "",
            "New tech unlocked: Atmosphere Sink Arrays. Build Cryo-Carbon Capturers to pull the climate back into balance - and watch your growing population grow.",
        };
        var lines = new List<string>();
        foreach (var p in paras)
        {
            if (p.Length == 0) lines.Add("");
            else WrapText(lines, p, textW);
        }
        return lines;
    }

    private Rectangle Phase2PanelRect()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        int lineH = _font.LineSpacing;
        const int padTop = 16, titleGap = 14, padBottom = 20, padX = 24;
        int titleH = (int)(lineH * 1.3f);
        int w = Math.Min(vw - 40, 580);
        int bodyCount = Phase2Body(w - padX * 2).Count;
        int h = padTop + titleH + titleGap + bodyCount * lineH + padBottom;
        h = Math.Min(h, vh - 40);
        return new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
    }

    private Rectangle Phase2CloseRect() => _phase2Celebrating ? CloseButtonRect(Phase2PanelRect()) : Rectangle.Empty;

    /// <summary>Modal εορτασμού: ανακοινώνει τη Φάση 2, το runaway greenhouse και το νέο tech.</summary>
    private void DrawPhase2Overlay()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        var ms = Mouse.GetState();
        var panel = Phase2PanelRect();
        int lineH = _font.LineSpacing;
        const int padX = 24, padTop = 16, titleGap = 14;
        int titleH = (int)(lineH * 1.3f);

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 190));
        _spriteBatch.Draw(_pixel, panel, new Color(16, 20, 28, 252));
        DrawRectOutline(panel, new Color(120, 210, 150));

        const string title = "PHASE 2 - THE LIVING PLANET";
        var tsz = _font.MeasureString(title) * 1.3f;
        _spriteBatch.DrawString(_font, title, new Vector2(panel.Center.X - tsz.X / 2f, panel.Y + padTop),
            new Color(150, 230, 150), 0f, Vector2.Zero, 1.3f, SpriteEffects.None, 0f);

        int bodyY = panel.Y + padTop + titleH + titleGap;
        var body = Phase2Body(panel.Width - padX * 2);
        for (int i = 0; i < body.Count; i++)
            if (body[i].Length > 0)
                _spriteBatch.DrawString(_font, body[i], new Vector2(panel.X + padX, bodyY + i * lineH), HudWhite);

        DrawCloseButton(Phase2CloseRect(), ms);
        _spriteBatch.End();
    }

    /// <summary>Συμπαγής μορφή μεγάλου αριθμού για το HUD (π.χ. 12.3k, 4.5M).</summary>
    private static string FormatCompact(double v) =>
        v >= 1_000_000 ? $"{v / 1_000_000:0.#}M" : v >= 1_000 ? $"{v / 1_000:0.#}k" : $"{v:0}";

    private void DrawCentered(string text, float y, float scale, Color color)
    {
        var size = _font.MeasureString(text);
        var pos = new Vector2((GraphicsDevice.Viewport.Width - size.X * scale) / 2f, y);
        _spriteBatch.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
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

    /// <summary>Κεντράρει την κάμερα στο landing module (αρχική κάψουλα)· διατηρεί το τρέχον zoom.</summary>
    private void CenterOnLandingModule()
    {
        var landing = _world.Colony.Buildings.FirstOrDefault(b => b.Definition.Id == LandingModuleId)
                      ?? _world.Colony.Buildings.FirstOrDefault();
        if (landing is not null) CenterOnBuilding(landing);
    }

    /// <summary>Κεντράρει την κάμερα πάνω σε ένα κτήριο (διατηρεί το τρέχον zoom).</summary>
    private void CenterOnBuilding(Building building)
    {
        var (x, y) = _renderer.Layout.HexToPixel(building.Location);
        _camera.Position = new Vector2((float)x, (float)y);
    }

    // Κτήρια που «χρειάζονται προσωπικό»: λειτουργικά, με θέσεις εργασίας αλλά υποστελεχωμένα.
    private static bool NeedsCrew(Building b) =>
        b.State == BuildingState.Operational
        && b.Definition.MaxWorkers > 0
        && b.Workers.Count < b.Definition.MaxWorkers;

    // Κτήρια με εξαντλημένο κοίτασμα (ορυχεία/γεωτρήσεις που σταμάτησαν να παράγουν).
    private static bool IsDepleted(Building b) => b.DepositDepleted;

    /// <summary>
    /// Κεντράρει (κυκλικά) στο επόμενο κτήριο που ταιριάζει στο <paramref name="match"/>, το επιλέγει
    /// ώστε να φανεί το panel του, και θυμάται ποιο έδειξε ώστε το επόμενο πάτημα να πάει στο μεθεπόμενο.
    /// </summary>
    private void CenterOnNextBuilding(Func<Building, bool> match, ref Building? focus, string emptyStatus)
    {
        var list = _world.Colony.Buildings.Where(match).ToList();
        if (list.Count == 0)
        {
            focus = null;
            _status = emptyStatus;
            _statusTimer = 3.0;
            return;
        }
        int prev = focus is null ? -1 : list.IndexOf(focus);
        var next = list[(prev + 1) % list.Count];
        focus = next;
        CenterOnBuilding(next);
        _selected = _map.GetTile(next.Location);
        _selectedBuilding = next;
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

        // Οθόνη Load: modal (σε μενού & παιχνίδι) — scroll λίστας, μεγάλο preview, επιλογή για φόρτωμα.
        if (_loadScreenOpen)
        {
            UpdateLoadScreen(keys, mouse);
            _prevMouse = mouse;
            _prevKeys = keys;
            base.Update(gameTime);
            return;
        }

        // Παράθυρο βοήθειας καταλόγου (όλα τα κτίρια/έρευνες): modal — κύλιση με ρόδα, Close/Esc κλείνει.
        if (_catalogHelp != CatalogHelp.None)
        {
            _uiClick = false;
            int cw = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
            if (cw != 0) _catalogScroll = Math.Clamp(_catalogScroll - Math.Sign(cw) * 48, 0, _catalogScrollMax);
            bool click = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
            if (KeyPressed(keys, Keys.Escape) || (click && CatalogHelpCloseRect().Contains(mouse.X, mouse.Y)))
            {
                _catalogHelp = CatalogHelp.None;
                _audio.Blip();
            }
            _prevMouse = mouse;
            _prevKeys = keys;
            base.Update(gameTime);
            return;
        }

        // Popup εορτασμού μετάβασης στη Φάση 2: modal — «X»/Esc/Enter κλείνει.
        if (_state == GameState.Playing && _phase2Celebrating)
        {
            _uiClick = false;
            bool click = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
            if (KeyPressed(keys, Keys.Escape) || KeyPressed(keys, Keys.Enter)
                || (click && Phase2CloseRect().Contains(mouse.X, mouse.Y)))
            {
                _phase2Celebrating = false;
                _audio.Blip();
            }
            _prevMouse = mouse;
            _prevKeys = keys;
            base.Update(gameTime);
            return;
        }

        // Popup ολοκλήρωσης τεχνολογίας: modal — «X»/Esc/Enter κλείνει (και δείχνει το επόμενο στην ουρά).
        if (_state == GameState.Playing && _techDone.Count > 0)
        {
            _uiClick = false;
            bool click = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;
            if (KeyPressed(keys, Keys.Escape) || KeyPressed(keys, Keys.Enter)
                || (click && TechDoneCloseRect().Contains(mouse.X, mouse.Y)))
            {
                _techDone.Dequeue();
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

        // Esc → κλείνει πρώτα το tutorial (αν τρέχει)· μετά ανοιχτό event popup· αλλιώς πίσω στο μενού.
        if (KeyPressed(keys, Keys.Escape))
        {
            if (_tutorialActive) { EndTutorial(); }
            else if (_eventPopups.Count > 0) { _eventPopups.Dequeue(); _audio.Blip(); }
            else
            {
                _state = GameState.Menu;
                _prevMouse = mouse;
                _prevKeys = keys;
                base.Update(gameTime);
                return;
            }
        }
        if (KeyPressed(keys, Keys.U)) ToggleMute();

        _camera.SetViewport(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        UpdateMinZoom();

        // Μετακίνηση του panel κτηρίου με drag (κρατάει & θυμάται τη θέση του).
        UpdateBuildingPanelDrag(mouse);

        // Pan με drag (αριστερό ή μεσαίο κουμπί) — όχι όσο σέρνουμε το panel κτηρίου.
        bool dragging = mouse.LeftButton == ButtonState.Pressed || mouse.MiddleButton == ButtonState.Pressed;
        bool wasDragging = _prevMouse.LeftButton == ButtonState.Pressed || _prevMouse.MiddleButton == ButtonState.Pressed;
        if (dragging && wasDragging && !_draggingPanel)
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

        // H: κεντράρισμα κάμερας στο landing module (αρχική κάψουλα)
        if (KeyPressed(keys, Keys.H)) { CenterOnLandingModule(); _audio.Blip(); }

        // . / , : κεντράρισμα στο επόμενο κτήριο που χρειάζεται προσωπικό / έχει εξαντλημένο κοίτασμα
        if (KeyPressed(keys, Keys.OemPeriod))
        { CenterOnNextBuilding(NeedsCrew, ref _crewFocus, "No building needs crew"); _audio.Blip(); }
        if (KeyPressed(keys, Keys.OemComma))
        { CenterOnNextBuilding(IsDepleted, ref _depletedFocus, "No depleted deposits"); _audio.Blip(); }

        // R: εναλλαγή reclaim mode (μόνο όταν έχει ξεκλειδωθεί από έρευνα)
        if (KeyPressed(keys, Keys.R))
        {
            if (ReclaimUnlocked) HandleToolClick(Tool.Reclaim);
            else { _status = "Reclaim locked - research 'Reclaim' first"; _statusTimer = 3.0; }
        }

        // Save (F5) / Load (F9). Νέος χάρτης & αλλαγή χορηγού γίνονται πλέον μόνο από το μενού.
        if (KeyPressed(keys, Keys.F5)) SaveGameToFile();
        if (KeyPressed(keys, Keys.F9)) OpenLoadScreen();

        // Έλεγχος ταχύτητας σιμουλασιόν
        if (KeyPressed(keys, Keys.Space))
            _world.Clock.Speed = _world.Clock.Speed == GameSpeed.Paused ? GameSpeed.Normal : GameSpeed.Paused;
        if (KeyPressed(keys, Keys.D1)) _world.Clock.Speed = GameSpeed.Normal;
        if (KeyPressed(keys, Keys.D2)) _world.Clock.Speed = GameSpeed.Fast;
        if (KeyPressed(keys, Keys.D3)) _world.Clock.Speed = GameSpeed.Ultra;

        // Διαθέσιμα κτίρια = όσα έχουν ξεκλειδωθεί από έρευνα ΚΑΙ από το πληθυσμιακό κατώφλι
        _buildables = _catalog.Buildables
            .Where(d => _world.Colony.Tech.IsResearched(d.RequiredTech)
                        && _world.Colony.PeakPopulation >= d.RequiresPopulation)
            .ToList();
        if (_buildIndex >= _buildables.Count) _buildIndex = 0;

        // Επιλογή έρευνας: T κυκλώνει τα διαθέσιμα techs ως target
        if (KeyPressed(keys, Keys.T))
        {
            _researchMenuOpen = !_researchMenuOpen;
            if (_researchMenuOpen) { _buildMenuOpen = false; _speedMenuOpen = false; }
        }

        // Build menu: B ανοίγει/κλείνει την παλέτα κτιρίων
        if (KeyPressed(keys, Keys.B))
        {
            _buildMenuOpen = !_buildMenuOpen;
            if (_buildMenuOpen) { _buildMode = false; _reclaimMode = false; _speedMenuOpen = false; _researchMenuOpen = false; }
        }

        // Ανάθεση/αφαίρεση αποίκων στο επιλεγμένο κτίριο (+/-)
        if (_selectedBuilding is { } selected && selected.Definition.MaxWorkers > 0)
        {
            if (KeyPressed(keys, Keys.OemPlus) || KeyPressed(keys, Keys.Add)) AssignCrew(selected);
            if (KeyPressed(keys, Keys.OemMinus) || KeyPressed(keys, Keys.Subtract)) RemoveCrew(selected);
        }

        if (_statusTimer > 0) _statusTimer -= gameTime.ElapsedGameTime.TotalSeconds;

        // Περιοδικός έλεγχος (κάθε 3s) για κτήρια χωρίς εργαζόμενους / με τελειωμένα resources → μετρητές HUD.
        _buildingCheckTimer -= gameTime.ElapsedGameTime.TotalSeconds;
        if (_buildingCheckTimer <= 0)
        {
            RefreshBuildingCounts();
            _buildingCheckTimer = BuildingCheckInterval;
        }

        // Αυτόματη αποθήκευση κάθε 5 λεπτά (κυκλικά Auto 1/2/3), εκτός tutorial.
        if (!_tutorialActive)
        {
            _autoSaveTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (_autoSaveTimer >= AutoSaveInterval) { _autoSaveTimer = 0; RequestAutoSave(); }
        }

        // Προώθηση σιμουλασιόν (fixed-timestep μέσα στο World)
        _world.Update(gameTime.ElapsedGameTime.TotalSeconds);

        // Αν άλλαξε το terrain (πάγος→νερό), ξαναχτίζουμε τον χάρτη
        if (_world.MapRevision != _lastMapRevision)
        {
            _renderer.Build(_map);
            _lastMapRevision = _world.MapRevision;
        }

        CheckAudioTransitions();

        // Μετάβαση στη Φάση 2 → one-time modal εορτασμού (celebrate-then-continue).
        if (_world.ConsumePhase2Celebration()) _phase2Celebrating = true;

        // Κατώφλι Urbanization (10k) → ξεκλειδώνει το High-Density Arcology.
        if (_world.ConsumeUrbanization())
        {
            _status = "URBANIZATION ERA - population 10,000 - High-Density Arcology unlocked";
            _statusTimer = 5.0;
            _audio.Chime();
        }

        // Κατώφλι Industrial Shift (50k) → ξεκλειδώνει το Interplanetary Stock Exchange.
        if (_world.ConsumeIndustrialShift())
        {
            _status = "THE INDUSTRIAL SHIFT - population 50,000 - Interplanetary Stock Exchange unlocked";
            _statusTimer = 5.0;
            _audio.Chime();
        }

        // Τεχνολογίες που μόλις ολοκληρώθηκαν → modal popup (μία-μία, με «X» πάνω δεξιά).
        foreach (var id in _world.Colony.Tech.Researched)
            if (_knownResearched.Add(id) && _world.Colony.Tech.Catalog.TryGet(id, out var td) && td is not null)
                _techDone.Enqueue(td);

        // Γεγονότα που μόλις ξεκίνησαν → ενημερωτικά popup (χωρίς πάγωμα του χρόνου).
        foreach (var start in _world.StartedEvents)
            _eventPopups.Enqueue(BuildEventPopup(start.Type, start.DurationTicks));
        _world.StartedEvents.Clear();
        LayoutEventPopup();
        if (_eventPopups.Count > 0 && KeyPressed(keys, Keys.Enter)) { _eventPopups.Dequeue(); _audio.Blip(); }

        // Hover hex μέσω screen→world→PixelToHex
        Vector2 world = _camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));
        _hoverHex = _renderer.Layout.PixelToHex(world.X, world.Y);
        _hasHover = _map.TryGetTile(_hoverHex, out _hoveredTile) && _hoveredTile is not null;
        _hoveredBuilding = _hasHover ? _world.Colony.Buildings.FirstOrDefault(b => b.Location == _hoverHex) : null;

        // Αριστερό κλικ: μπάρα κτιρίων (UI) ή τοποθέτηση στον χάρτη
        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
        {
            _mouseDownPos = new Point(mouse.X, mouse.Y);

            // Προτεραιότητα: επιλογή από ανοιχτό popup (κτίρια / ταχύτητα / έρευνα), μετά κουμπιά μπάρας.
            int menuBtn = _buildMenuOpen ? BuildMenuHitIndex(mouse.X, mouse.Y) : -1;
            int speedBtn = _speedMenuOpen ? SpeedMenuHitIndex(mouse.X, mouse.Y) : -1;
            int researchBtn = _researchMenuOpen ? ResearchMenuHitIndex(mouse.X, mouse.Y) : -1;
            if (_eventPopups.Count > 0 && _eventPopupCloseRect.Contains(mouse.X, mouse.Y))
            {
                _eventPopups.Dequeue();       // κουμπί «Close/Next» του event popup
                _audio.Blip();
                _uiClick = true;
            }
            else if (_eventPopups.Count > 0 && _eventPopupPanel.Contains(mouse.X, mouse.Y))
            {
                _uiClick = true;              // κλικ πάνω στην κάρτα: μην τοποθετηθεί κτίριο από κάτω
            }
            else if (_buildMenuOpen && BuildHelpButtonRect().Contains(mouse.X, mouse.Y))
            {
                _catalogHelp = CatalogHelp.Buildings; // κουμπί help της παλέτας κτιρίων → παράθυρο με όλα τα κτίρια
                _catalogScroll = 0;
                _audio.Blip();
                _uiClick = true;
            }
            else if (_researchMenuOpen && ResearchHelpButtonRect(ResearchOptions().Count).Contains(mouse.X, mouse.Y))
            {
                _catalogHelp = CatalogHelp.Research;  // κουμπί help της παλέτας έρευνας → παράθυρο με όλες τις τεχνολογίες
                _catalogScroll = 0;
                _audio.Blip();
                _uiClick = true;
            }
            else if (CrewButtonClick(mouse.X, mouse.Y))
            {
                // κουμπιά επάνδρωσης (+/-) στο panel του επιλεγμένου κτηρίου
            }
            else if (menuBtn >= 0)
            {
                _buildMode = true;
                _buildIndex = menuBtn;
                _buildMenuOpen = false;
                _reclaimMode = false;
                _audio.Blip();
                _uiClick = true;
            }
            else if (speedBtn >= 0)
            {
                _world.Clock.Speed = SpeedOptions[speedBtn].speed;
                _speedMenuOpen = false;
                _audio.Blip();
                _uiClick = true;
            }
            else if (researchBtn >= 0)
            {
                var techs = ResearchOptions();
                if (researchBtn < techs.Count) _world.Colony.Tech.StartResearch(techs[researchBtn].Id);
                _researchMenuOpen = false;
                _audio.Blip();
                _uiClick = true;
            }
            else if (TryToolbarClick(mouse.X, mouse.Y, out var tool))
            {
                HandleToolClick(tool);
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
            else if (_speedMenuOpen) _speedMenuOpen = false;
            else if (_researchMenuOpen) _researchMenuOpen = false;
            else if (_buildMode) _buildMode = false;
            else if (_reclaimMode) _reclaimMode = false;
            else if (_hasHover)
            {
                _selected = _hoveredTile;
                _selectedBuilding = _world.Colony.Buildings.FirstOrDefault(b => b.Location == _hoverHex);
            }
        }

        UpdateTutorial(keys); // προχωρά το βήμα του tutorial όταν ο παίκτης εκτέλεσε την ενέργεια

        _prevMouse = mouse;
        _prevKeys = keys;
        base.Update(gameTime);
    }

    // ----------------------------------------------------------------- Tutorial (wizard)

    /// <summary>Οδηγία για το τρέχον βήμα του tutorial.</summary>
    private static string TutorialInstruction(TutStep step) => step switch
    {
        TutStep.Move => "Move around the map with WASD, the arrow keys, or by dragging with the mouse. Zoom with the mouse wheel.",
        TutStep.Center => "Press H (or click the crosshair icon in the bottom bar) to recenter on your landing capsule.",
        TutStep.BuildMenu => "Open the build menu: press B, or click the buildings icon in the bottom bar.",
        TutStep.Place => "Pick a building from the palette, then left-click a highlighted hex to build it. You have plenty of Credits.",
        TutStep.Select => "Right-click any of your buildings to select it and open its info panel.",
        TutStep.Research => "Open the research menu: press T, or click the atom icon in the bottom bar.",
        TutStep.StartResearch => "Click a technology in the palette to start researching it.",
        TutStep.Speed => "Time is paused. Start it with the 1 / 2 / 3 keys (or the clock icon): 1 = normal, 2 = fast, 3 = ultra.",
        _ => "That's the basics! Keep raising Temperature, Pressure, Oxygen and Water toward 100%. Press Esc to leave the tutorial."
    };

    /// <summary>True όταν η κάμερα είναι (περίπου) κεντραρισμένη στην κάψουλα προσγείωσης.</summary>
    private bool CameraNearLanding()
    {
        var landing = _world.Colony.Buildings.FirstOrDefault(b => b.Definition.Id == LandingModuleId)
                      ?? _world.Colony.Buildings.FirstOrDefault();
        if (landing is null) return false;
        var (x, y) = _renderer.Layout.HexToPixel(landing.Location);
        return Vector2.Distance(_camera.Position, new Vector2((float)x, (float)y)) < 30f;
    }

    /// <summary>Ελέγχει αν ολοκληρώθηκε το τρέχον βήμα (με βάση την ενέργεια του παίκτη) και προχωρά.</summary>
    private void UpdateTutorial(KeyboardState keys)
    {
        if (!_tutorialActive || _tutStep == TutStep.Done) return;

        bool done = _tutStep switch
        {
            TutStep.Move => Vector2.Distance(_camera.Position, _tutBaseCamPos) > 50f
                            || MathF.Abs(_camera.Zoom - _tutBaseZoom) > 0.02f,
            TutStep.Center => KeyPressed(keys, Keys.H) || CameraNearLanding(),
            TutStep.BuildMenu => _buildMenuOpen,
            TutStep.Place => _world.Colony.Buildings.Count > _tutBaseBuildings,
            TutStep.Select => _selectedBuilding is not null,
            TutStep.Research => _researchMenuOpen,
            TutStep.StartResearch => _world.Colony.Tech.CurrentTarget is not null,
            TutStep.Speed => _world.Clock.Speed != GameSpeed.Paused,
            _ => false
        };

        if (!done) return;
        _tutStep++;
        _audio.Chime();
        if (_tutStep == TutStep.Place) _tutBaseBuildings = _world.Colony.Buildings.Count; // baseline πριν την τοποθέτηση
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
        long now = _world.Clock.TotalTicks;
        double credits = _world.Colony.ReclaimValue(building, now);
        double materials = _world.Colony.ReclaimMaterialsValue(building, now);
        double pct = Colony.ReclaimFraction(building, now) * 100;
        string refundLine = materials >= 1
            ? $"Refund {credits:0} credits + {materials:0} materials ({pct:0}% of cost)"
            : $"Refund {credits:0} credits ({pct:0}% of cost)";
        OpenDialog(
            new[] { $"Reclaim {building.Definition.Name}?", refundLine },
            Btn("Reclaim", new Color(240, 170, 80), ConfirmReclaim),
            Btn("Cancel", HudDim, () => _reclaimTarget = null));
    }

    /// <summary>Εκτελεί την ανακύκλωση του επιλεγμένου κτιρίου και κλείνει το reclaim mode.</summary>
    private void ConfirmReclaim()
    {
        if (_reclaimTarget is { } target && _world.Colony.Buildings.Contains(target))
        {
            var (credits, materials) = _world.Colony.Reclaim(target, _world.Clock.TotalTicks);
            _status = materials >= 1
                ? $"Reclaimed {target.Definition.Name}  (+{credits:0} credits, +{materials:0} materials)"
                : $"Reclaimed {target.Definition.Name}  (+{credits:0} credits)";
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

    // ----------------------------------------------------------------- Event popup (μη-modal)

    /// <summary>True αν η αποικία είναι θωρακισμένη από ακτινοβολία (σπήλαιο ή magnetosphere station).</summary>
    private bool IsShielded() =>
        _world.HasCaveShelter ||
        _world.Colony.Buildings.Any(b => b.State == BuildingState.Operational && b.Definition.ShieldsAtmosphere);

    /// <summary>Χτίζει την ενημερωτική κάρτα ενός γεγονότος: περιγραφή, εκτιμώμενη διάρκεια, σύσταση.</summary>
    private EventPopup BuildEventPopup(EventType type, int durationTicks)
    {
        // Ίδια μονάδα με τον μετρητή στο HUD (ticks/4 ≈ δευτ. στο ×1).
        string dur = $"~{Math.Max(1, durationTicks / 4)}s";
        var suggest = new Color(150, 220, 150);
        var p = new EventPopup { Title = EventLabel(type) };

        switch (type)
        {
            case EventType.DustStorm:
                p.Accent = new Color(210, 160, 90);
                p.Lines.Add(("A planet-wide dust storm is blocking sunlight.", HudWhite));
                p.Lines.Add(("Solar power output drops sharply until it clears.", HudWhite));
                p.Lines.Add(($"Estimated duration: {dur}", HudDim));
                p.Lines.Add(("Suggested: lean on stored energy or fission power", suggest));
                p.Lines.Add(("and hold off on power-hungry construction.", suggest));
                break;

            case EventType.SolarFlare:
                p.Accent = new Color(255, 180, 70);
                p.Lines.Add(("Intense solar radiation is sweeping the colony.", HudWhite));
                if (IsShielded())
                    p.Lines.Add(("Your colony is shielded - the crew stays safe.", new Color(120, 220, 120)));
                else
                    p.Lines.Add(("Unshielded crew lose health and electronics may fail.", HudWarn));
                p.Lines.Add(($"Estimated duration: {dur}", HudDim));
                p.Lines.Add(("Suggested: build a Magnetosphere Station or shelter,", suggest));
                p.Lines.Add(("and keep an Engineer ready to repair damage.", suggest));
                break;

            case EventType.LifeSupportFailure:
                p.Accent = HudWarn;
                p.Lines.Add(("A life-support building has broken down.", HudWhite));
                p.Lines.Add(("Its oxygen / water output has stopped.", HudWhite));
                p.Lines.Add(($"Estimated repair: {dur} (faster with an Engineer)", HudDim));
                p.Lines.Add(("Suggested: assign an Engineer to the disabled", suggest));
                p.Lines.Add(("building to bring it back online sooner.", suggest));
                break;

            case EventType.CaveDiscovery:
                p.Accent = new Color(120, 220, 120);
                p.Lines.Add(("Explorers have found a natural cavern.", HudWhite));
                p.Lines.Add(("It grants permanent shelter from solar radiation.", HudWhite));
                p.Lines.Add(("Effect: ongoing protection from solar flares.", HudDim));
                p.Lines.Add(("No action needed - a lucky break.", suggest));
                break;
        }
        return p;
    }

    /// <summary>Υπολογίζει panel & κουμπί «Close» της τρέχουσας κάρτας (κοινό για hit-test & draw).</summary>
    private void LayoutEventPopup()
    {
        if (_eventPopups.Count == 0) { _eventPopupPanel = _eventPopupCloseRect = Rectangle.Empty; return; }

        var popup = _eventPopups.Peek();
        const float titleScale = 1.3f;
        float lineH = _font.LineSpacing;
        float maxW = _font.MeasureString(popup.Title).X * titleScale;
        foreach (var (text, _) in popup.Lines) maxW = MathF.Max(maxW, _font.MeasureString(text).X);

        const int pad = 18, btnH = 40, btnW = 170;
        int titleH = (int)(lineH * titleScale);
        int panelW = (int)MathF.Max(maxW, btnW) + pad * 2;
        int panelH = pad + titleH + 8 + popup.Lines.Count * (int)lineH + 14 + btnH + pad;
        int px = (GraphicsDevice.Viewport.Width - panelW) / 2;
        int py = (int)(GraphicsDevice.Viewport.Height * 0.17f);

        _eventPopupPanel = new Rectangle(px, py, panelW, panelH);
        _eventPopupCloseRect = new Rectangle(_eventPopupPanel.Center.X - btnW / 2, _eventPopupPanel.Bottom - btnH - pad, btnW, btnH);
    }

    private void DrawEventPopup()
    {
        LayoutEventPopup();
        if (_eventPopups.Count == 0) return;

        var popup = _eventPopups.Peek();
        var ms = Mouse.GetState();
        const float titleScale = 1.3f;
        const int pad = 18;
        float lineH = _font.LineSpacing;

        _spriteBatch.Draw(_pixel, _eventPopupPanel, new Color(18, 20, 30, 248));
        DrawRectOutline(_eventPopupPanel, popup.Accent);
        _spriteBatch.Draw(_pixel, new Rectangle(_eventPopupPanel.X, _eventPopupPanel.Y, 4, _eventPopupPanel.Height), popup.Accent); // έγχρωμη λωρίδα έμφασης

        float y = _eventPopupPanel.Y + pad;
        _spriteBatch.DrawString(_font, popup.Title, new Vector2(_eventPopupPanel.X + pad, y),
            popup.Accent, 0f, Vector2.Zero, titleScale, SpriteEffects.None, 0f);
        y += lineH * titleScale + 8;
        foreach (var (text, color) in popup.Lines)
        {
            _spriteBatch.DrawString(_font, text, new Vector2(_eventPopupPanel.X + pad, y), color);
            y += lineH;
        }

        bool hover = _eventPopupCloseRect.Contains(ms.X, ms.Y);
        _spriteBatch.Draw(_pixel, _eventPopupCloseRect, hover ? new Color(60, 70, 90) : new Color(34, 38, 50));
        DrawRectOutline(_eventPopupCloseRect, new Color(150, 200, 255));
        string label = _eventPopups.Count > 1 ? $"Next ({_eventPopups.Count})" : "Close  (Enter)";
        var lsz = _font.MeasureString(label);
        _spriteBatch.DrawString(_font, label,
            new Vector2(_eventPopupCloseRect.Center.X - lsz.X / 2f, _eventPopupCloseRect.Center.Y - lsz.Y / 2f), HudWhite);
    }

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

    /// <summary>Χειρίζεται κλικ στα κουμπιά επάνδρωσης (+/-) του επιλεγμένου κτηρίου. True αν καταναλώθηκε.</summary>
    private bool CrewButtonClick(int mx, int my)
    {
        if (_selectedBuilding is not { } b || b.Definition.MaxWorkers <= 0) return false;
        if (_crewPlusRect.Contains(mx, my)) { AssignCrew(b); _audio.Blip(); _uiClick = true; return true; }
        if (_crewMinusRect.Contains(mx, my)) { RemoveCrew(b); _audio.Blip(); _uiClick = true; return true; }
        return false;
    }

    /// <summary>
    /// Σέρνει το panel του επιλεγμένου κτηρίου με το αριστερό κουμπί και αποθηκεύει τη νέα του θέση
    /// όταν αφεθεί. Το drag ξεκινά μόνο πάνω στο ορατό κουτί του panel (όχι στα κουμπιά +/-).
    /// </summary>
    private void UpdateBuildingPanelDrag(MouseState mouse)
    {
        bool leftDown = mouse.LeftButton == ButtonState.Pressed;
        bool leftPressedNow = leftDown && _prevMouse.LeftButton == ButtonState.Released;

        if (!_draggingPanel && leftPressedNow && _selectedBuilding is not null
            && _buildingPanelRect.Contains(mouse.X, mouse.Y)
            && !_crewPlusRect.Contains(mouse.X, mouse.Y)
            && !_crewMinusRect.Contains(mouse.X, mouse.Y))
        {
            _draggingPanel = true;
            _panelMoved = false;
            _panelDragOffset = new Point(mouse.X - _buildingPanelRect.X, mouse.Y - _buildingPanelRect.Y);
            _uiClick = true; // απόφυγε να εκληφθεί ως κλικ στον χάρτη
        }

        if (!_draggingPanel) return;

        if (leftDown && _selectedBuilding is not null)
        {
            int vpW = GraphicsDevice.Viewport.Width, vpH = GraphicsDevice.Viewport.Height;
            int nx = Math.Clamp(mouse.X - _panelDragOffset.X, 0, Math.Max(0, vpW - _buildingPanelRect.Width));
            int ny = Math.Clamp(mouse.Y - _panelDragOffset.Y, 0, Math.Max(0, vpH - _buildingPanelRect.Height));
            if (nx != (int)_buildingPanelPos.X || ny != (int)_buildingPanelPos.Y) _panelMoved = true;
            _buildingPanelPos = new Vector2(nx, ny);
            _buildingPanelPosSet = true;
        }
        else
        {
            _draggingPanel = false;
            if (_panelMoved)
            {
                _uiSettings.BuildingPanelX = (int)_buildingPanelPos.X;
                _uiSettings.BuildingPanelY = (int)_buildingPanelPos.Y;
                _uiSettings.Save();
            }
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_state == GameState.Menu)
        {
            DrawMenu();
            if (_showHelp) DrawHelpOverlay();
            if (_loadScreenOpen) DrawLoadScreen();
            base.Draw(gameTime);
            return;
        }

        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        bool capturing = _captureRequest is not null;
        if (capturing)
        {
            EnsureCaptureTarget(vw, vh);
            GraphicsDevice.SetRenderTarget(_captureRT); // ζωγραφίζουμε το frame σε render target για screenshot
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
        if (_catalogHelp != CatalogHelp.None) DrawCatalogHelpOverlay();
        if (_techDone.Count > 0) DrawTechDoneOverlay();
        if (_phase2Celebrating) DrawPhase2Overlay();

        if (capturing)
        {
            GraphicsDevice.SetRenderTarget(null);
            PerformCaptureSave();                       // json + PNG από το render target
            GraphicsDevice.Clear(Color.Black);          // εμφάνιση του ίδιου frame στην οθόνη
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
            _spriteBatch.Draw(_captureRT, new Rectangle(0, 0, vw, vh), Color.White);
            _spriteBatch.End();
        }

        if (_loadScreenOpen) DrawLoadScreen();          // πάνω από όλα, εκτός screenshot

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

    /// <summary>Ποιο κουμπί της μπάρας βρίσκεται κάτω από τον δείκτη (αν κάποιο).</summary>
    private bool TryToolbarClick(int mx, int my, out Tool tool)
    {
        var tools = ToolbarTools();
        for (int i = 0; i < tools.Count; i++)
            if (ToolbarButtonRect(i, tools.Count).Contains(mx, my)) { tool = tools[i]; return true; }
        tool = Tool.Help;
        return false;
    }

    private static readonly (string icon, GameSpeed speed, string tip)[] SpeedOptions =
    {
        ("pause", GameSpeed.Paused, "Pause"),
        ("speed1", GameSpeed.Normal, "x1   Normal"),
        ("speed2", GameSpeed.Fast, "x2   Fast"),
        ("speed4", GameSpeed.Ultra, "x4   Ultra"),
    };

    /// <summary>Rect κουμπιού του popup ταχύτητας: οριζόντια σειρά 4, κεντραρισμένη πάνω από το κουμπί «Speed».</summary>
    private Rectangle SpeedMenuButtonRect(int index)
    {
        const int bs = 46, gap = 6;
        var tools = ToolbarTools();
        Rectangle speedRect = ToolbarButtonRect(tools.IndexOf(Tool.Speed), tools.Count);
        int count = SpeedOptions.Length;
        int rowWidth = count * bs + (count - 1) * gap;
        int startX = Math.Clamp(speedRect.Center.X - rowWidth / 2, 6, GraphicsDevice.Viewport.Width - rowWidth - 6);
        int y = speedRect.Y - (bs + gap);
        return new Rectangle(startX + index * (bs + gap), y, bs, bs);
    }

    private int SpeedMenuHitIndex(int mx, int my)
    {
        for (int i = 0; i < SpeedOptions.Length; i++)
            if (SpeedMenuButtonRect(i).Contains(mx, my)) return i;
        return -1;
    }

    /// <summary>Rect ενός κουμπιού μιας popup παλέτας, κεντραρισμένο πάνω από τη μπάρα (1 ή 2 σειρές).</summary>
    private Rectangle PaletteButtonRect(int index, int count, int rows = 2)
    {
        const int bs = 46, gap = 6, bottom = 8;
        rows = Math.Clamp(rows, 1, 2);
        int cols = Math.Max(1, (int)Math.Ceiling(count / (double)rows));
        bool bottomRow = rows == 2 && index >= cols;
        int col = bottomRow ? index - cols : index;
        int rowCount = bottomRow ? count - cols : Math.Min(count, cols);
        int rowWidth = rowCount * bs + (rowCount - 1) * gap;
        int startX = (GraphicsDevice.Viewport.Width - rowWidth) / 2;
        int toolbarY = GraphicsDevice.Viewport.Height - bs - bottom;
        int rowFromBottom = (rows == 1 || bottomRow) ? 1 : 2; // (μοναδική/κάτω) σειρά ακριβώς πάνω από τη μπάρα, πάνω σειρά πιο ψηλά
        int y = toolbarY - rowFromBottom * (bs + gap);
        return new Rectangle(startX + col * (bs + gap), y, bs, bs);
    }

    /// <summary>Πλαίσιο φόντου μιας παλέτας: η ένωση όλων των κουμπιών της, με λίγο περιθώριο.</summary>
    private Rectangle PalettePanel(int count, int rows = 2)
    {
        if (count <= 0) return Rectangle.Empty;
        Rectangle panel = PaletteButtonRect(0, count, rows);
        for (int i = 1; i < count; i++) panel = Rectangle.Union(panel, PaletteButtonRect(i, count, rows));
        panel.Inflate(8, 8);
        return panel;
    }

    // Οι παλέτες έχουν ένα επιπλέον κουμπί (help) ως τελευταίο slot, στο ίδιο μέγεθος με τα υπόλοιπα.
    private Rectangle BuildMenuButtonRect(int index) => PaletteButtonRect(index, _buildables.Count + 1);
    private Rectangle BuildHelpButtonRect() => PaletteButtonRect(_buildables.Count, _buildables.Count + 1);

    private int BuildMenuHitIndex(int mx, int my)
    {
        for (int i = 0; i < _buildables.Count; i++)
            if (BuildMenuButtonRect(i).Contains(mx, my)) return i;
        return -1;
    }

    /// <summary>True αν ο δείκτης είναι πάνω σε κουμπί της μπάρας ή της ανοιχτής παλέτας (για απόκρυψη hover hint).</summary>
    private bool IsPointerOverToolbar(int mx, int my)
    {
        var tools = ToolbarTools();
        for (int i = 0; i < tools.Count; i++)
            if (ToolbarButtonRect(i, tools.Count).Contains(mx, my)) return true;
        if (_buildMenuOpen && (BuildMenuHitIndex(mx, my) >= 0 || BuildHelpButtonRect().Contains(mx, my))) return true;
        if (_speedMenuOpen && SpeedMenuHitIndex(mx, my) >= 0) return true;
        if (_researchMenuOpen)
        {
            int techCount = ResearchOptions().Count;
            if (ResearchMenuHitIndex(mx, my) >= 0 || ResearchHelpButtonRect(techCount).Contains(mx, my)) return true;
        }
        return false;
    }

    private void DrawToolbar()
    {
        var ms = Mouse.GetState();
        var tools = ToolbarTools();

        // Ανοιχτά popups ζωγραφίζονται πρώτα (πάνω από τη μπάρα).
        if (_buildMenuOpen) DrawBuildMenu(ms);
        if (_speedMenuOpen) DrawSpeedMenu(ms);
        if (_researchMenuOpen) DrawResearchMenu(ms);

        for (int i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            var rect = ToolbarButtonRect(i, tools.Count);
            bool active = tool switch
            {
                Tool.Buildings => _buildMenuOpen || _buildMode,
                Tool.Speed => _speedMenuOpen,
                Tool.Research => _researchMenuOpen,
                Tool.Reclaim => _reclaimMode,
                Tool.Help => _showHelp,
                _ => false
            };
            _spriteBatch.Draw(_pixel, rect, active ? new Color(45, 62, 84, 235) : new Color(18, 20, 28, 215));
            DrawRectOutline(rect, active ? new Color(140, 195, 245) : new Color(70, 74, 90));

            if (tool == Tool.Help)
            {
                var qs = _font.MeasureString("?") * 1.6f;
                _spriteBatch.DrawString(_font, "?", new Vector2(rect.Center.X - qs.X / 2f, rect.Center.Y - qs.Y / 2f),
                    new Color(150, 200, 255), 0f, Vector2.Zero, 1.6f, SpriteEffects.None, 0f);
            }
            else
            {
                _spriteBatch.Draw(ToolIcon(tool), new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6), Color.White);
            }
        }

        // Tooltip για το κουμπί κάτω από τον δείκτη (τα popups έχουν δικά τους tooltips).
        if (TryToolbarClick(ms.X, ms.Y, out var hovered))
        {
            int slot = tools.IndexOf(hovered);
            DrawToolbarTooltip(ToolTooltip(hovered), ToolbarButtonRect(slot, tools.Count).Center.X);
        }
    }

    private Texture2D ToolIcon(Tool tool) => tool switch
    {
        Tool.Buildings => _buildingsIcon,
        Tool.Reclaim => _reclaimIcon,
        Tool.Research => _toolIcons["research"],
        Tool.Speed => _toolIcons["speed"],
        Tool.Save => _toolIcons["save"],
        Tool.Mute => _toolIcons[_audio.Enabled ? "mute_on" : "mute_off"],
        Tool.Center => _toolIcons["center"],
        Tool.CrewNeeded => _toolIcons["crew_needed"],
        Tool.Depleted => _toolIcons["depleted"],
        Tool.Menu => _toolIcons["menu"],
        _ => _buildingsIcon
    };

    private string ToolTooltip(Tool tool) => tool switch
    {
        Tool.Buildings => "Buildings   (B - browse & place)",
        Tool.Research => _world.Colony.Tech.CurrentTech is { } ct
            ? $"Research: {ct.Name}  {_world.Colony.Tech.CurrentProgress / ct.Cost * 100:0}%   (T - choose)"
            : $"Research   (T - choose, {_world.Colony.Tech.Available.Count()} available)",
        Tool.Speed => "Speed   (pause / x1 / x2 / x4)",
        Tool.Save => "Save game   (F5)",
        Tool.Mute => _audio.Enabled ? "Mute sound   (U)" : "Unmute sound   (U)",
        Tool.Reclaim => "Reclaim   (R - recycle a building for credits)",
        Tool.Center => "Center on landing module   (H)",
        Tool.CrewNeeded => "Next understaffed building   (.)",
        Tool.Depleted => "Next depleted deposit   (,)",
        Tool.Menu => "Menu   (Esc - back to main menu)",
        Tool.Help => "Help   (how to play)",
        _ => ""
    };

    /// <summary>Εκτελεί την ενέργεια ενός κουμπιού της μπάρας.</summary>
    private void HandleToolClick(Tool tool)
    {
        if (tool != Tool.Mute) _audio.Blip();

        bool wasBuild = _buildMenuOpen, wasSpeed = _speedMenuOpen, wasResearch = _researchMenuOpen;
        _buildMenuOpen = false;
        _speedMenuOpen = false;
        _researchMenuOpen = false;

        switch (tool)
        {
            case Tool.Buildings:
                _buildMenuOpen = !wasBuild;
                if (_buildMenuOpen) { _buildMode = false; _reclaimMode = false; }
                break;
            case Tool.Speed:
                _speedMenuOpen = !wasSpeed;
                break;
            case Tool.Research:
                _researchMenuOpen = !wasResearch;
                break;
            case Tool.Save:
                SaveGameToFile();
                break;
            case Tool.Mute:
                ToggleMute();
                break;
            case Tool.Reclaim:
                _reclaimMode = !_reclaimMode;
                _buildMode = false;
                break;
            case Tool.Center:
                CenterOnLandingModule();
                break;
            case Tool.CrewNeeded:
                CenterOnNextBuilding(NeedsCrew, ref _crewFocus, "No building needs crew");
                break;
            case Tool.Depleted:
                CenterOnNextBuilding(IsDepleted, ref _depletedFocus, "No depleted deposits");
                break;
            case Tool.Menu:
                _state = GameState.Menu;
                break;
            case Tool.Help:
                _showHelp = true;
                break;
        }
    }

    // -------- popup επιλογής έρευνας: πλέγμα εικονιδίων (όπως τα κτίρια), πάνω από τη μπάρα --------

    private List<TechDefinition> ResearchOptions() => _world.Colony.Tech.Available.ToList();

    // Οι τεχνολογίες + το κουμπί help σε μία σειρά (rows: 1), το help τελευταίο και στο ίδιο μέγεθος.
    private Rectangle ResearchMenuButtonRect(int index, int count) => PaletteButtonRect(index, count + 1, rows: 1);
    private Rectangle ResearchHelpButtonRect(int count) => PaletteButtonRect(count, count + 1, rows: 1);

    private int ResearchMenuHitIndex(int mx, int my)
    {
        var techs = ResearchOptions();
        for (int i = 0; i < techs.Count; i++)
            if (ResearchMenuButtonRect(i, techs.Count).Contains(mx, my)) return i;
        return -1;
    }

    /// <summary>popup διαθέσιμων ερευνών: πλέγμα εικονιδίων + κουμπί help (τελευταίο)· κλικ ξεκινά την έρευνα, το help ανοίγει το παράθυρο καταλόγου.</summary>
    private void DrawResearchMenu(MouseState ms)
    {
        var techs = ResearchOptions();
        if (techs.Count == 0)
        {
            // Δεν υπάρχει διαθέσιμη έρευνα: δείχνουμε μόνο το κουμπί help (ώστε να φαίνονται όλες οι τεχνολογίες).
            Rectangle emptyPanel = PalettePanel(1, rows: 1);
            _spriteBatch.Draw(_pixel, emptyPanel, new Color(12, 14, 20, 240));
            DrawRectOutline(emptyPanel, new Color(90, 130, 170));
            DrawPaletteHelpButton(ResearchHelpButtonRect(0), ms, "Help: none available now - see all techs");
            return;
        }

        Rectangle panel = PalettePanel(techs.Count + 1, rows: 1);
        _spriteBatch.Draw(_pixel, panel, new Color(12, 14, 20, 240));
        DrawRectOutline(panel, new Color(90, 130, 170));

        string? current = _world.Colony.Tech.CurrentTarget;
        for (int i = 0; i < techs.Count; i++)
        {
            var t = techs[i];
            var rect = ResearchMenuButtonRect(i, techs.Count);
            bool isCurrent = t.Id == current;
            _spriteBatch.Draw(_pixel, rect, isCurrent ? new Color(50, 80, 50, 235) : new Color(18, 20, 28, 235));
            DrawRectOutline(rect, isCurrent ? new Color(120, 230, 120) : new Color(70, 74, 90));
            if (_techIcons.TryGetValue(t.Id, out var tex))
                _spriteBatch.Draw(tex, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6), Color.White);
        }

        DrawPaletteHelpButton(ResearchHelpButtonRect(techs.Count), ms, "Help: what each tech does");

        int hover = ResearchMenuHitIndex(ms.X, ms.Y);
        if (hover >= 0)
            DrawPaletteTooltip($"{techs[hover].Name}   {techs[hover].Cost} RP", ResearchMenuButtonRect(hover, techs.Count));
    }

    /// <summary>Ζητά χειροκίνητο save· το γράψιμο (json + screenshot) γίνεται στο επόμενο Draw.</summary>
    private void SaveGameToFile()
    {
        if (_captureRequest is not null) return; // ήδη εκκρεμεί ένα save
        _captureRequest = (SaveManager.ManualSlug(DateTime.Now), "Save");
        _status = "Saving...";
        _statusTimer = 1.5;
    }

    /// <summary>Ζητά αυτόματο save στο επόμενο κυκλικό slot (Auto 1/2/3).</summary>
    private void RequestAutoSave()
    {
        if (_captureRequest is not null) return;
        int slot = _autoSaveIndex + 1;
        _captureRequest = (SaveManager.AutoSlug(slot), "Auto " + slot);
        _autoSaveIndex = (_autoSaveIndex + 1) % 3;
    }

    /// <summary>Γράφει το εκκρεμές save: json + PNG thumbnail από το render target της οθόνης.</summary>
    private void PerformCaptureSave()
    {
        if (_captureRequest is not ({ } slug, { } name) || _captureRT is null) { _captureRequest = null; return; }
        SaveManager.EnsureFolder();
        int vw = _captureRT.Width, vh = _captureRT.Height;

        try { File.WriteAllText(SaveManager.JsonPath(slug), SaveSystem.ToJson(_world, _sponsor, name)); }
        catch { _status = "Save failed"; _statusTimer = 3.0; _captureRequest = null; return; }

        try
        {
            var full = new Color[vw * vh];
            _captureRT.GetData(full);
            int tw = 512, th = Math.Max(1, tw * vh / vw);
            var small = DownscalePixels(full, vw, vh, tw, th);
            using var tex = new Texture2D(GraphicsDevice, tw, th);
            tex.SetData(small);
            using var fs = File.Create(SaveManager.PngPath(slug));
            tex.SaveAsPng(fs, tw, th);
        }
        catch { /* το json σώθηκε· χωρίς thumbnail απλώς δεν θα φαίνεται εικόνα */ }

        _status = $"{name} saved";
        _statusTimer = 3.0;
        _captureRequest = null;
    }

    /// <summary>Nearest-neighbor σμίκρυνση pixel buffer (για το thumbnail του save).</summary>
    private static Color[] DownscalePixels(Color[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new Color[dw * dh];
        for (int y = 0; y < dh; y++)
        {
            int sy = y * sh / dh;
            for (int x = 0; x < dw; x++)
                dst[y * dw + x] = src[sy * sw + x * sw / dw];
        }
        return dst;
    }

    private void EnsureCaptureTarget(int w, int h)
    {
        if (_captureRT is not null && _captureRT.Width == w && _captureRT.Height == h) return;
        _captureRT?.Dispose();
        _captureRT = new RenderTarget2D(GraphicsDevice, w, h, false,
            GraphicsDevice.PresentationParameters.BackBufferFormat, DepthFormat.Depth24);
    }

    private void ToggleMute()
    {
        _audio.Enabled = _audioSettings.SfxEnabled = !_audio.Enabled;
        if (_audio.Enabled) _audio.Blip();
        SaveAudio();
    }

    /// <summary>popup ταχύτητας: 4 εικονίδια (pause/x1/x2/x4) με hints, πάνω από το κουμπί «Speed».</summary>
    private void DrawSpeedMenu(MouseState ms)
    {
        Rectangle panel = SpeedMenuButtonRect(0);
        for (int i = 1; i < SpeedOptions.Length; i++)
            panel = Rectangle.Union(panel, SpeedMenuButtonRect(i));
        panel.Inflate(8, 8);
        _spriteBatch.Draw(_pixel, panel, new Color(12, 14, 20, 240));
        DrawRectOutline(panel, new Color(90, 130, 170));

        for (int i = 0; i < SpeedOptions.Length; i++)
        {
            var (ic, speed, _) = SpeedOptions[i];
            var rect = SpeedMenuButtonRect(i);
            bool current = _world.Clock.Speed == speed;
            _spriteBatch.Draw(_pixel, rect, current ? new Color(50, 80, 50, 235) : new Color(18, 20, 28, 235));
            DrawRectOutline(rect, current ? new Color(120, 230, 120) : new Color(70, 74, 90));
            _spriteBatch.Draw(_toolIcons[ic], new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6), Color.White);
        }

        int hover = SpeedMenuHitIndex(ms.X, ms.Y);
        if (hover >= 0)
        {
            var rect = SpeedMenuButtonRect(hover);
            var label = SpeedOptions[hover].tip;
            var size = _font.MeasureString(label);
            var pos = new Vector2(rect.Center.X - size.X / 2f, rect.Y - size.Y - 6);
            _spriteBatch.Draw(_pixel, new Rectangle((int)pos.X - 5, (int)pos.Y - 2, (int)size.X + 10, (int)size.Y + 4), new Color(0, 0, 0, 230));
            _spriteBatch.DrawString(_font, label, pos, HudWhite);
        }
    }

    /// <summary>Ζωγραφίζει την popup παλέτα κτιρίων: πλαίσιο φόντου + εικονίδια + κουμπί help (τελευταίο) + tooltip.</summary>
    private void DrawBuildMenu(MouseState ms)
    {
        if (_buildables.Count == 0) return;

        Rectangle panel = PalettePanel(_buildables.Count + 1);
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

        DrawPaletteHelpButton(BuildHelpButtonRect(), ms, "Help: what each building does");

        int hover = BuildMenuHitIndex(ms.X, ms.Y);
        if (hover >= 0)
            DrawPaletteTooltip($"{_buildables[hover].Name}   ({CostString(_buildables[hover])})", BuildMenuButtonRect(hover));
    }

    /// <summary>Κουμπί help μιας παλέτας (τελευταίο slot, ίδιο μέγεθος): κλικ ανοίγει το παράθυρο καταλόγου. Hover = hint.</summary>
    private void DrawPaletteHelpButton(Rectangle rect, MouseState ms, string hint)
    {
        bool hover = rect.Contains(ms.X, ms.Y);
        _spriteBatch.Draw(_pixel, rect, hover ? new Color(45, 62, 84, 240) : new Color(18, 20, 28, 235));
        DrawRectOutline(rect, hover ? new Color(140, 195, 245) : new Color(90, 130, 170));
        var qs = _font.MeasureString("?") * 1.6f;
        _spriteBatch.DrawString(_font, "?", new Vector2(rect.Center.X - qs.X / 2f, rect.Center.Y - qs.Y / 2f),
            hover ? new Color(150, 200, 255) : HudWhite, 0f, Vector2.Zero, 1.6f, SpriteEffects.None, 0f);
        if (hover) DrawPaletteTooltip(hint, rect);
    }

    /// <summary>Tooltip πάνω από ένα κουμπί παλέτας, κεντραρισμένο & περιορισμένο μέσα στην οθόνη.</summary>
    private void DrawPaletteTooltip(string text, Rectangle itemRect)
    {
        var size = _font.MeasureString(text);
        float x = Math.Clamp(itemRect.Center.X - size.X / 2f, 6f, GraphicsDevice.Viewport.Width - size.X - 6f);
        float y = itemRect.Y - size.Y - 6f;
        _spriteBatch.Draw(_pixel, new Rectangle((int)x - 5, (int)y - 2, (int)size.X + 10, (int)size.Y + 4), new Color(0, 0, 0, 235));
        _spriteBatch.DrawString(_font, text, new Vector2(x, y), HudWhite);
    }

    /// <summary>Αναδιπλώνει κείμενο σε γραμμές που χωρούν σε <paramref name="maxWidth"/> px (στο δοσμένο scale).</summary>
    private void WrapText(List<string> outLines, string text, float maxWidth, float scale = 1f)
    {
        string cur = "";
        foreach (var word in text.Split(' '))
        {
            string trial = cur.Length == 0 ? word : cur + " " + word;
            if (cur.Length > 0 && _font.MeasureString(trial).X * scale > maxWidth)
            {
                outLines.Add(cur);
                cur = word;
            }
            else cur = trial;
        }
        if (cur.Length > 0) outLines.Add(cur);
    }

    /// <summary>Σώμα της κάρτας βοήθειας ενός κτιρίου: περιγραφή + βασικά χαρακτηριστικά.</summary>
    private List<string> BuildingHelpBody(BuildingDefinition def)
    {
        var body = new List<string> { def.Description, "" };
        if (def.RequiredTech.Length > 0) body.Add($"Requires tech: {TechName(def.RequiredTech)}");
        if (def.RequiresDeposit != ResourceType.None) body.Add($"Requires deposit: {def.RequiresDeposit}");
        body.Add(def.MaxWorkers > 0
            ? $"Crew: up to {def.MaxWorkers}" + (def.OptimalSpecialty != Specialty.None ? $" (best: {def.OptimalSpecialty})" : "")
            : "Crew: automatic (none needed)");
        if (def.Production.Count > 0)
            body.Add("Output/tick: " + string.Join(", ", def.Production.Select(kv => $"{Signed(kv.Value)} {kv.Key}")));
        if (def.PlanetEffects.Count > 0)
            body.Add("Planet/tick: " + string.Join(", ", def.PlanetEffects.Select(kv => $"{Signed(kv.Value)} {kv.Key}")));
        if (def.Storage.Count > 0)
            body.Add("Storage: " + string.Join(", ", def.Storage.Select(kv => $"{kv.Value:0} {kv.Key}")));
        if (def.HousingCapacity > 0) body.Add($"Housing: +{def.HousingCapacity}");
        if (def.VegetationSpreadPerTick > 0) body.Add("Spreads vegetation");
        return body;
    }

    /// <summary>Σώμα της κάρτας βοήθειας μιας τεχνολογίας: περιγραφή + φάση/προαπαιτούμενα/ξεκλειδώματα.</summary>
    private List<string> TechHelpBody(TechDefinition t)
    {
        var body = new List<string> { t.Description, "" };
        body.Add($"Phase: {t.Phase}");
        if (t.Prerequisites.Count > 0)
            body.Add("Needs: " + string.Join(", ", t.Prerequisites.Select(TechName)));
        if (t.Unlocks.Count > 0)
            body.Add("Unlocks: " + string.Join(", ", t.Unlocks.Select(BuildingName)));
        return body;
    }

    private static string Signed(double v) => (v >= 0 ? "+" : "") + v.ToString("0.###");

    private string TechName(string id) =>
        _world.Colony.Tech.Catalog.TryGet(id, out var t) && t is not null ? t.Name : id;

    private string BuildingName(string id) =>
        _catalog.TryGet(id, out var d) && d is not null ? d.Name : id;

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

    private static readonly (string icon, string label, ResourceKind kind)[] BarResources =
    {
        ("energy", "Energy", ResourceKind.Energy),
        ("water", "Water", ResourceKind.Water),
        ("oxygen", "Oxygen", ResourceKind.Oxygen),
        ("food", "Food", ResourceKind.Food),
        ("materials", "Materials", ResourceKind.Materials),
        ("silicon", "Silicon", ResourceKind.Silicon),
        ("credits", "Credits", ResourceKind.Credits),
    };

    /// <summary>
    /// Μπάρα πόρων στην κορυφή: μικρό εικονίδιο + τρέχουσα τιμή για κάθε πόρο (+ Crew), σε μία σειρά.
    /// Με hover πάνω σε ένα chip εμφανίζεται tooltip με το όριο (capacity) και τη μεταβολή/tick.
    /// </summary>
    private void DrawResourceBar()
    {
        var ledger = _world.Colony.Ledger;
        const int icon = 20, gap = 16;

        var falling = new Color(240, 110, 90); // κόκκινο όταν ο πόρος μειώνεται
        var chips = new List<(string icon, string value, string tip, Color color)>();
        foreach (var (ic, label, kind) in BarResources)
        {
            double amt = ledger.Get(kind);
            string cap = ledger.HasCapacityLimit(kind) ? $" / {ledger.Capacity(kind):0}" : "";
            double rate = ledger.RatePerTick(kind);
            Color col = rate < -0.001 ? falling : HudWhite;
            chips.Add((ic, $"{amt:0}", $"{label}   {amt:0}{cap}   {rate:+0.00;-0.00; 0.00}/t", col));
        }
        int crew = _world.Colony.Colonists.Count;
        int idle = _world.Colony.IdleColonists.Count();
        chips.Add(("crew", crew.ToString(), $"Crew   {crew} / {_world.Colony.Housing} housing   idle {idle}", HudWhite));

        // Στη Φάση 2: αφηρημένος πληθυσμός / χωρητικότητα (ξεχωριστός από το πλήρωμα)· κόκκινο σε stagnation.
        if (_world.Phase2Active)
            chips.Add(("crew", FormatCompact(_world.Colony.Population),
                $"Population   {_world.Colony.Population:N0} / {_world.Colony.AggregateHousing:N0} housing",
                _world.StagnationActive ? falling : new Color(150, 200, 245)));

        // Μέτρησε πλάτη ανά chip για οριζόντιο κεντράρισμα.
        var widths = new float[chips.Count];
        float total = 0;
        for (int i = 0; i < chips.Count; i++)
        {
            widths[i] = icon + 4 + _font.MeasureString(chips[i].value).X;
            total += widths[i];
        }
        total += gap * (chips.Count - 1);

        float startX = (GraphicsDevice.Viewport.Width - total) / 2f;
        const float y = 8f;
        const int barH = icon + 8;
        _spriteBatch.Draw(_pixel, new Rectangle((int)startX - 10, (int)y - 4, (int)total + 20, barH), new Color(12, 14, 20, 215));

        var ms = Mouse.GetState();
        string? hoverTip = null;
        float hoverCenter = 0;
        float x = startX;
        for (int i = 0; i < chips.Count; i++)
        {
            var (ic, value, tip, color) = chips[i];
            if (_resIcons.TryGetValue(ic, out var tex))
                _spriteBatch.Draw(tex, new Rectangle((int)x, (int)y, icon, icon), Color.White);
            _spriteBatch.DrawString(_font, value, new Vector2(x + icon + 4, y + (icon - _font.LineSpacing) / 2f), color);

            var chipRect = new Rectangle((int)x - 4, (int)y - 4, (int)widths[i] + 8, barH);
            if (chipRect.Contains(ms.X, ms.Y)) { hoverTip = tip; hoverCenter = x + widths[i] / 2f; }
            x += widths[i] + gap;
        }

        if (hoverTip is not null)
        {
            var size = _font.MeasureString(hoverTip);
            float tx = Math.Clamp(hoverCenter - size.X / 2f, 6f, GraphicsDevice.Viewport.Width - size.X - 6f);
            var pos = new Vector2(tx, y + barH + 2);
            _spriteBatch.Draw(_pixel, new Rectangle((int)pos.X - 5, (int)pos.Y - 2, (int)size.X + 10, (int)size.Y + 4), new Color(0, 0, 0, 235));
            _spriteBatch.DrawString(_font, hoverTip, pos, HudWhite);
        }
    }

    // Στόχοι terraforming (win conditions): εικονίδιο + πρόοδος %· τιμή/στόχος στο hint.
    private static readonly (PlanetMetric metric, string icon, string label, string unit, double target)[] Goals =
    {
        (PlanetMetric.Temperature, "temperature", "Temperature", "C", PlanetState.TargetTemperature),
        (PlanetMetric.Pressure, "pressure", "Pressure", "kPa", PlanetState.TargetPressure),
        (PlanetMetric.Oxygen, "oxygen", "Oxygen", "%", PlanetState.TargetOxygen),
        (PlanetMetric.Water, "water", "Water", "%", PlanetState.TargetWater * 100),
    };

    private Texture2D GoalIcon(string key) => _resIcons.TryGetValue(key, out var t) ? t : _toolIcons[key];

    /// <summary>Πάνω-κέντρο (κάτω από τη μπάρα πόρων): στόχοι + συνολικό terraforming + biomass (πρόοδος % στο εικονίδιο, λεπτομέρεια στο hint).</summary>
    private void DrawGoals()
    {
        var planet = _world.Planet;

        // Ετοίμασε τα chips: (εικονίδιο, κείμενο %, hint, χρώμα).
        var chips = new List<(string icon, string text, string tip, Color color)>();
        foreach (var (metric, ic, label, unit, target) in Goals)
        {
            double raw = planet.Get(metric);
            double value = metric == PlanetMetric.Water ? raw * 100 : raw;
            double progress = planet.Progress(metric);
            string valStr = unit == "%" ? $"{value:0.0}%" : $"{value:0.0} {unit}";
            string tgtStr = unit == "%" ? $"{target:0}%" : $"{target:0} {unit}";
            chips.Add((ic, $"{progress * 100:0}%", $"{label}   {valStr}   (target {tgtStr})", MetricColor(progress)));
        }
        double overall = planet.OverallProgress;
        chips.Add(("planet", $"{overall * 100:0}%", $"Terraforming   overall {overall * 100:0}%   (avg of 4 goals)", MetricColor(overall)));
        chips.Add(("biomass", $"{planet.Biomass * 100:0}%", $"Biomass   vegetation cover {planet.Biomass * 100:0.0}%", new Color(90, 200, 90)));

        const int icon = 20, gap = 12;

        // Πλάτη ανά chip για οριζόντιο κεντράρισμα, κάτω από τη μπάρα πόρων.
        var widths = new float[chips.Count];
        float total = 0;
        for (int i = 0; i < chips.Count; i++)
        {
            widths[i] = icon + 3 + _font.MeasureString(chips[i].text).X;
            total += widths[i];
        }
        total += gap * (chips.Count - 1);

        var ms = Mouse.GetState();
        float x = (GraphicsDevice.Viewport.Width - total) / 2f;
        const float y = 40f; // ακριβώς κάτω από τη μπάρα πόρων (που τελειώνει ~y=32)
        string? tip = null;
        float tipCx = 0;

        for (int i = 0; i < chips.Count; i++)
        {
            var (ic, text, chipTip, color) = chips[i];
            float w = widths[i];
            var rect = new Rectangle((int)x - 4, (int)y - 4, (int)w + 8, icon + 8);
            _spriteBatch.Draw(_pixel, rect, new Color(12, 14, 20, 205));
            _spriteBatch.Draw(GoalIcon(ic), new Rectangle((int)x, (int)y, icon, icon), Color.White);
            _spriteBatch.DrawString(_font, text, new Vector2(x + icon + 3, y + (icon - _font.LineSpacing) / 2f), color);

            if (rect.Contains(ms.X, ms.Y)) { tip = chipTip; tipCx = x + w / 2f; }
            x += w + gap;
        }
        if (tip is not null) DrawTip(tip, tipCx, y + icon + 4, above: false);
    }

    /// <summary>Κάτω δεξιά: δείκτης έρευνας — εικονίδιο + πρόοδος %. Γκρίζο όταν δεν τρέχει έρευνα. Hint με λεπτομέρειες.</summary>
    private void DrawResearchIndicator()
    {
        var tech = _world.Colony.Tech;
        const int icon = 20;
        var ms = Mouse.GetState();

        string text;
        string tip;
        Color iconTint, textColor;
        if (tech.CurrentTech is { } ct)
        {
            double pct = ct.Cost > 0 ? tech.CurrentProgress / ct.Cost * 100 : 0;
            double rate = _world.Colony.Buildings
                .Where(b => b.State == BuildingState.Operational)
                .Sum(b => b.Definition.Production.GetValueOrDefault(ResourceKind.Research) * b.WorkerEfficiency());
            text = $"{pct:0}%";
            tip = $"Researching: {ct.Name}   {pct:0}%   (+{rate:0.0}/t)";
            iconTint = Color.White;
            textColor = new Color(190, 150, 255);
        }
        else
        {
            text = "-";
            tip = $"No research active - press T or click Research to pick ({tech.Available.Count()} available)";
            iconTint = new Color(120, 120, 130, 180); // γκρίζο/αχνό
            textColor = HudDim;
        }

        float w = icon + 4 + _font.MeasureString(text).X;
        float x = GraphicsDevice.Viewport.Width - w - 12f;
        float y = GraphicsDevice.Viewport.Height - 30f;
        var rect = new Rectangle((int)x - 4, (int)y - 4, (int)w + 8, icon + 8);
        _spriteBatch.Draw(_pixel, rect, new Color(12, 14, 20, 205));
        _spriteBatch.Draw(_toolIcons["research"], new Rectangle((int)x, (int)y, icon, icon), iconTint);
        _spriteBatch.DrawString(_font, text, new Vector2(x + icon + 4, y + (icon - _font.LineSpacing) / 2f), textColor);

        if (rect.Contains(ms.X, ms.Y)) DrawTip(tip, rect.Center.X, y - 4, above: true);
    }

    /// <summary>Πάνω αριστερά: Sol + Sponsor ως μικρά εικονίδια με hint.</summary>
    private void DrawTopLeftStatus()
    {
        var clock = _world.Clock;
        const int icon = 20, gap = 10;
        var ms = Mouse.GetState();
        float x = 12f, y = 10f;
        string? tip = null;
        float tipCx = 0;

        string solTxt = $"Sol {clock.Sol}";
        float solW = icon + 4 + _font.MeasureString(solTxt).X;
        var solRect = new Rectangle((int)x - 4, (int)y - 4, (int)solW + 8, icon + 8);
        _spriteBatch.Draw(_pixel, solRect, new Color(12, 14, 20, 205));
        _spriteBatch.Draw(_toolIcons["sol"], new Rectangle((int)x, (int)y, icon, icon), Color.White);
        _spriteBatch.DrawString(_font, solTxt, new Vector2(x + icon + 4, y + (icon - _font.LineSpacing) / 2f), HudWhite);
        if (solRect.Contains(ms.X, ms.Y)) { tip = $"Sol {clock.Sol}   {clock.HourOfSol:00}:{clock.MinuteOfHour:00}   [{clock.Speed}]"; tipCx = x + solW / 2f; }
        x += solW + gap;

        var spRect = new Rectangle((int)x - 4, (int)y - 4, icon + 8, icon + 8);
        _spriteBatch.Draw(_pixel, spRect, new Color(12, 14, 20, 205));
        _spriteBatch.Draw(_toolIcons["sponsor"], new Rectangle((int)x, (int)y, icon, icon), Color.White);
        if (spRect.Contains(ms.X, ms.Y)) { tip = $"Sponsor: {_sponsor.Name}"; tipCx = x + icon / 2f; }

        if (tip is not null) DrawTip(tip, tipCx, y + icon + 4, above: false);
    }

    /// <summary>Μικρό tooltip κεντραρισμένο στο centerX, πάνω ή κάτω από το edgeY.</summary>
    private void DrawTip(string text, float centerX, float edgeY, bool above)
    {
        var size = _font.MeasureString(text);
        float x = Math.Clamp(centerX - size.X / 2f, 6f, GraphicsDevice.Viewport.Width - size.X - 6f);
        float y = above ? edgeY - size.Y - 6f : edgeY + 6f;
        _spriteBatch.Draw(_pixel, new Rectangle((int)x - 5, (int)y - 2, (int)size.X + 10, (int)size.Y + 4), new Color(0, 0, 0, 235));
        _spriteBatch.DrawString(_font, text, new Vector2(x, y), HudWhite);
    }

    private void DrawHud()
    {
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        // Πάνω αριστερά: Sol + Sponsor.  Πάνω κέντρο: μπάρα πόρων + από κάτω οι στόχοι.
        // (Οι στόχοι ζωγραφίζονται πριν τη μπάρα ώστε το tooltip της μπάρας να μένει από πάνω.)
        DrawTopLeftStatus();
        DrawGoals();
        DrawResourceBar();
        DrawResearchIndicator();

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
            bottom.Add(("click=place   RMB=cancel", HudDim));
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

        // Σύνοψη κτηρίων που χρειάζονται προσοχή (ανανεώνεται περιοδικά κάθε 3s στο Update).
        if (_crewNeededCount > 0)
            bottom.Add(($"{_crewNeededCount} building{(_crewNeededCount == 1 ? "" : "s")} needing crew   (. to find)", new Color(150, 200, 245)));
        if (_depletedCount > 0)
            bottom.Add(($"{_depletedCount} building{(_depletedCount == 1 ? "" : "s")} out of resources   (, to find)", new Color(240, 170, 80)));

        // Κρίσιμα alerts (μεταφέρθηκαν από το πρώην πάνω HUD ώστε να μη χάνονται).
        if (_world.RunawayActive)
            bottom.Add(("!! RUNAWAY GREENHOUSE - temp/pressure overshoot - build a Cryo-Carbon Capturer !!", HudWarn));
        if (_world.StagnationActive)
            bottom.Add(("!! SYSTEMIC STAGNATION - population outgrew its food/water/housing - expand infrastructure !!", HudWarn));
        if (_world.Phase2Active)
            bottom.Add(($"Factions: Industry {_world.Colony.IndustrialistApproval * 100:0}%   Ecology {_world.Colony.EcologistApproval * 100:0}%   Pollution {_world.PollutionLevel * 100:0}%   Automation {_world.AutomationLevel * 100:0}%",
                _world.IndustrialStrike || _world.EcologistStrike ? HudWarn : HudDim));
        if (_world.PollutionLevel > 0.5)
            bottom.Add(("!! HEAVY POLLUTION - industry is withering nearby vegetation - build Atmospheric Scrubbers !!", HudWarn));
        if (_world.SeismicLevel > 0.7)
            bottom.Add(("!! SEISMIC INSTABILITY - marsquake imminent - space out your Deep Core Drills !!", HudWarn));
        if (_world.StormLevel > 0.8)
            bottom.Add(("!! SUPER-STORM BUILDING - hurricane imminent - shield solar/low ground with Sea Walls !!", HudWarn));
        if (_world.InfestationLevel > 0.3)
            bottom.Add(($"!! INVASIVE SPECIES {_world.InfestationLevel * 100:0}% - eating crops & vegetation - build Wildlife Reserves / a Genetic Vault !!", HudWarn));
        if (_world.IndustrialStrike)
            bottom.Add(("!! INDUSTRIALIST STRIKE - mines & factories halted - build a District Town Hall !!", HudWarn));
        if (_world.EcologistStrike)
            bottom.Add(("!! ECOLOGIST STRIKE - biosphere halted - build a District Town Hall !!", HudWarn));
        if (_world.Colony.LifeSupportFailing) bottom.Add(("!! LIFE SUPPORT FAILURE !!", HudWarn));
        foreach (var ev in _world.ActiveEvents)
            bottom.Add(($"!! {EventLabel(ev.Type)}  {ev.TicksRemaining / 4}s", HudWarn));
        if (_world.SolarEfficiency < 1.0) bottom.Add(($"solar output {_world.SolarEfficiency * 100:0}%", HudWarn));
        if (_world.PowerOutage) bottom.Add(("BROWNOUT - low power", HudWarn));
        if (_world.HasCaveShelter) bottom.Add(("cave shelter active", new Color(120, 230, 120)));
        double minHealth = _world.Colony.Colonists.Count > 0 ? _world.Colony.Colonists.Min(c => c.Health) : 1.0;
        if (minHealth < 0.95) bottom.Add(($"crew health {minHealth * 100:0}%", HudWarn));

        float panelH = bottom.Count * _font.LineSpacing + 16f;
        DrawTextPanel(new Vector2(10, GraphicsDevice.Viewport.Height - panelH - 64f), bottom);

        if (_selectedBuilding is not null)
            DrawBuildingPanel();
        else
        {
            _crewPlusRect = _crewMinusRect = Rectangle.Empty;
            _buildingPanelRect = Rectangle.Empty;
        }

        DrawToolbar();

        if (!IsPointerOverToolbar(Mouse.GetState().X, Mouse.GetState().Y))
            DrawHoverHint();

        // Η ολοκλήρωση terraforming δεν είναι πλέον τέλος: εορτάζεται μία φορά (DrawPhase2Overlay)
        // και το παιχνίδι συνεχίζει στη Φάση 2. Κρατάμε μόνο το banner ήττας.
        if (_world.IsLost)
            DrawCenterBanner("***  COLONY LOST - press Esc  ***", new Color(255, 90, 80));

        DrawEventPopup();

        if (_tutorialActive) DrawTutorialOverlay();

        if (DialogOpen) DrawDialog();

        _spriteBatch.End();
    }

    /// <summary>Πλαίσιο οδηγιών του tutorial (αριστερά-κέντρο, μη-modal): βήμα, οδηγία και υπενθύμιση Esc.</summary>
    private void DrawTutorialOverlay()
    {
        int vh = GraphicsDevice.Viewport.Height;
        const int pad = 16, w = 360;
        int lineH = _font.LineSpacing;
        int headerH = (int)(lineH * 1.15f) + 8;

        var lines = new List<string>();
        WrapText(lines, TutorialInstruction(_tutStep), w - pad * 2);

        int bodyH = lines.Count * lineH;
        int footerH = lineH + 6;
        int h = pad + headerH + bodyH + 10 + footerH + pad;
        var panel = new Rectangle(16, (vh - h) / 2, w, h);

        var accent = new Color(120, 210, 130);
        _spriteBatch.Draw(_pixel, panel, new Color(14, 22, 28, 245));
        DrawRectOutline(panel, accent);
        _spriteBatch.Draw(_pixel, new Rectangle(panel.X, panel.Y, 4, panel.Height), accent); // λωρίδα έμφασης

        int total = (int)TutStep.Done;
        string header = _tutStep == TutStep.Done ? "TUTORIAL COMPLETE" : $"TUTORIAL  ·  step {(int)_tutStep + 1}/{total}";
        _spriteBatch.DrawString(_font, header, new Vector2(panel.X + pad, panel.Y + pad),
            new Color(150, 230, 150), 0f, Vector2.Zero, 1.15f, SpriteEffects.None, 0f);

        float y = panel.Y + pad + headerH;
        foreach (var ln in lines)
        {
            _spriteBatch.DrawString(_font, ln, new Vector2(panel.X + pad, y), HudWhite);
            y += lineH;
        }

        _spriteBatch.DrawString(_font, "Press Esc to exit the tutorial",
            new Vector2(panel.X + pad, panel.Bottom - pad - footerH + 3), HudDim);
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

        bool showCrew = d.MaxWorkers > 0;
        if (showCrew)
        {
            lines.Add(($"workers {b.Workers.Count}/{d.MaxWorkers}   eff {b.WorkerEfficiency():0.00}", HudWhite));
            foreach (var w in b.Workers)
                lines.Add(($"  {w.Name} [{w.Specialty}]", HudDim));
            lines.Add(($"optimal: {d.OptimalSpecialty}", HudDim));
            lines.Add(("crew:", new Color(120, 230, 120)));   // δίπλα ζωγραφίζονται τα κουμπιά [-] [+]
        }
        else
        {
            lines.Add(("automatic (no crew)", HudDim));
        }

        float width = PanelWidth(lines);
        if (showCrew) width = MathF.Max(width, _font.MeasureString("crew: ").X + 70f); // χώρος για τα κουμπιά
        float panelH = lines.Count * _font.LineSpacing + 16f;
        int vpW = GraphicsDevice.Viewport.Width, vpH = GraphicsDevice.Viewport.Height;

        // Θέση: αποθηκευμένη/σερνόμενη αν υπάρχει· αλλιώς προεπιλογή πάνω-δεξιά.
        Vector2 pos;
        if (_buildingPanelPosSet)
            pos = _buildingPanelPos;
        else if (_uiSettings.BuildingPanelX is int sx && _uiSettings.BuildingPanelY is int sy)
            pos = new Vector2(sx, sy);
        else
            pos = new Vector2(vpW - width - 10f, 10f);

        // Περιορισμός μέσα στην οθόνη (το viewport μπορεί να έχει αλλάξει από την τελευταία φορά).
        // Χρησιμοποιούμε το πλήρες πλάτος (μαζί με τον χώρο των κουμπιών) ώστε να μένουν ορατά.
        pos.X = Math.Clamp(pos.X, 0f, MathF.Max(0f, vpW - width));
        pos.Y = Math.Clamp(pos.Y, 0f, MathF.Max(0f, vpH - panelH));

        // Αν η θέση έχει «κλειδώσει» (αποθηκευμένη ή από drag), κράτα την συγχρονισμένη με το ορθογώνιο.
        if (_buildingPanelPosSet || _uiSettings.BuildingPanelX is not null)
        {
            _buildingPanelPos = pos;
            _buildingPanelPosSet = true;
        }

        DrawTextPanel(pos, lines);
        _buildingPanelRect = new Rectangle((int)pos.X, (int)pos.Y, (int)width, (int)panelH);

        _crewPlusRect = _crewMinusRect = Rectangle.Empty;
        if (showCrew)
        {
            float lineH = _font.LineSpacing;
            int bs = (int)lineH + 2;
            int by = (int)(pos.Y + 8f + (lines.Count - 1) * lineH) - 1; // γραμμή "crew:"
            int bx = (int)(pos.X + 8f + _font.MeasureString("crew: ").X);
            _crewMinusRect = new Rectangle(bx, by, bs, bs);
            _crewPlusRect = new Rectangle(bx + bs + 6, by, bs, bs);
            DrawCrewButton(_crewMinusRect, "-", b.Workers.Count > 0);
            DrawCrewButton(_crewPlusRect, "+", b.Workers.Count < d.MaxWorkers && _world.Colony.IdleColonists.Any());
        }
    }

    private void DrawCrewButton(Rectangle rect, string glyph, bool enabled)
    {
        var ms = Mouse.GetState();
        bool hover = enabled && rect.Contains(ms.X, ms.Y);
        _spriteBatch.Draw(_pixel, rect, hover ? new Color(50, 80, 50, 240) : new Color(24, 28, 36, 240));
        DrawRectOutline(rect, enabled ? new Color(120, 200, 140) : new Color(70, 74, 90));
        var sz = _font.MeasureString(glyph);
        _spriteBatch.DrawString(_font, glyph, new Vector2(rect.Center.X - sz.X / 2f, rect.Center.Y - sz.Y / 2f), enabled ? HudWhite : HudDim);
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

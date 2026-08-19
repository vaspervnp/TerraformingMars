using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;

namespace TerraformingMars.Game;

/// <summary>
/// Οθόνη αναθέσεων πληρώματος (modal, ανοίγει με «C» ή από τη μπάρα εργαλείων):
/// αριστερά οι ελεύθεροι άποικοι, δεξιά όλα τα κτήρια με θέσεις εργασίας.
/// Το drag &amp; drop μιας «καρτέλας» αποίκου σε θέση κτηρίου κάνει ανάθεση· αν η θέση είναι
/// πιασμένη, οι δύο άποικοι ανταλλάσσουν κτήριο. Drop στη στήλη «unassigned» αποδεσμεύει.
/// </summary>
public partial class MarsGame
{
    private bool _crewScreenOpen;
    private int _crewScroll, _crewScrollMax;         // κύλιση της λίστας κτηρίων
    private int _crewIdleScroll, _crewIdleScrollMax; // κύλιση της στήλης ελεύθερων αποίκων

    private Colonist? _crewDragCandidate;  // πατήθηκε πάνω του, δεν έχει ξεκινήσει ακόμη το drag
    private Colonist? _crewDrag;           // σέρνεται αυτή τη στιγμή
    private Point _crewDragStart;          // θέση του κέρσορα όταν πατήθηκε
    private Point _crewDragGrab;           // απόσταση κέρσορα από τη γωνία της καρτέλας
    private string _crewMessage = "";      // στιγμιαία ανατροφοδότηση («X -> Iron Mine»)
    private double _crewMessageTimer;

    private const int SlotW = 178, CrewPad = 18, IdleColW = 236;

    /// <summary>Ύψος μιας καρτέλας αποίκου (δύο σειρές κειμένου: όνομα + ειδικότητα).</summary>
    private int ChipH => Math.Max(26, (int)(_font.LineSpacing * 1.7f));

    // ------------------------------------------------------------------ layout

    private sealed class CrewSlot
    {
        public Rectangle Rect;
        public Building Building = null!;
        public Colonist? Colonist;   // null = ελεύθερη θέση
        public bool IsRepair;        // θέση συνεργείου επισκευής (υπάρχει μόνο σε χαλασμένα κτήρια)
    }

    private sealed class CrewCard
    {
        public Rectangle Rect;
        public Building Building = null!;
        public List<CrewSlot> Slots = new();
    }

    private sealed class CrewLayout
    {
        public Rectangle Panel, Close, IdleArea, ListArea;
        public List<(Rectangle rect, Colonist colonist)> IdleChips = new();
        public List<CrewCard> Cards = new();
        public int IdleContentH, ListContentH;
    }

    private Rectangle CrewPanelRect()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        int w = Math.Min(vw - 40, 1040);
        int h = Math.Min(vh - 60, 760);
        return new Rectangle((vw - w) / 2, (vh - h) / 2, w, h);
    }

    private Rectangle CrewCloseRect() => CloseButtonRect(CrewPanelRect());

    /// <summary>
    /// Τι μπαίνει στη λίστα: κτήρια με θέσεις εργασίας, συν όσα είναι χαλασμένα (ακόμη κι αν είναι
    /// αυτόματα) — εκεί μπορεί να σταλεί συνεργείο. Τα χαλασμένα πάνω-πάνω· κατά τα άλλα σταθερή
    /// σειρά, ώστε να μη «χοροπηδούν» οι κάρτες όσο σέρνεις.
    /// </summary>
    private List<Building> CrewBuildings() =>
        _world.Colony.Buildings
            .Where(b => b.Definition.MaxWorkers > 0 || b.State == BuildingState.Disabled)
            .OrderByDescending(b => b.State == BuildingState.Disabled)
            .ThenBy(b => b.Definition.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Location.Q).ThenBy(b => b.Location.R)
            .ToList();

    /// <summary>
    /// Υπολογίζει όλα τα ορθογώνια της οθόνης (καρτέλες, θέσεις, περιοχές κύλισης). Καλείται και από
    /// το Update (hit-test) και από το Draw, ώστε το drag &amp; drop να μην έχει καθυστέρηση ενός frame.
    /// </summary>
    private CrewLayout BuildCrewLayout()
    {
        var lay = new CrewLayout { Panel = CrewPanelRect() };
        lay.Close = CloseButtonRect(lay.Panel);

        int contentTop = lay.Panel.Y + 76;
        int contentBottom = lay.Panel.Bottom - CrewPad - 26; // χώρος για τη γραμμή υποσημείωσης
        lay.IdleArea = new Rectangle(lay.Panel.X + CrewPad, contentTop, IdleColW, contentBottom - contentTop);
        lay.ListArea = new Rectangle(lay.IdleArea.Right + 16, contentTop,
            lay.Panel.Right - CrewPad - (lay.IdleArea.Right + 16), contentBottom - contentTop);

        // Στήλη ελεύθερων αποίκων: κάθετη λίστα καρτελών.
        var idle = _world.Colony.Colonists.Where(c => c.Assignment is null).ToList();
        lay.IdleContentH = Math.Max(0, idle.Count * (ChipH + 6));
        _crewIdleScrollMax = Math.Max(0, lay.IdleContentH - lay.IdleArea.Height);
        _crewIdleScroll = Math.Clamp(_crewIdleScroll, 0, _crewIdleScrollMax);
        int iy = lay.IdleArea.Y - _crewIdleScroll;
        foreach (var c in idle)
        {
            lay.IdleChips.Add((new Rectangle(lay.IdleArea.X, iy, lay.IdleArea.Width - 8, ChipH), c));
            iy += ChipH + 6;
        }

        // Λίστα κτηρίων: μία καρτέλα ανά κτήριο (κεφαλίδα + σειρά θέσεων).
        int headerH = _font.LineSpacing + 6;
        int cardH = headerH + ChipH + 12;
        int y = lay.ListArea.Y - _crewScroll;
        foreach (var b in CrewBuildings())
        {
            var card = new CrewCard
            {
                Building = b,
                Rect = new Rectangle(lay.ListArea.X, y, lay.ListArea.Width - 8, cardH),
            };
            int sx = card.Rect.X + 8, sy = card.Rect.Y + headerH;

            // Χαλασμένο κτήριο: πρώτη-πρώτη η θέση του συνεργείου επισκευής.
            if (b.State == BuildingState.Disabled)
            {
                card.Slots.Add(new CrewSlot
                {
                    Rect = new Rectangle(sx, sy, SlotW, ChipH),
                    Building = b,
                    Colonist = b.RepairCrew,
                    IsRepair = true,
                });
                sx += SlotW + 8;
            }

            for (int i = 0; i < b.Definition.MaxWorkers; i++)
            {
                card.Slots.Add(new CrewSlot
                {
                    Rect = new Rectangle(sx, sy, SlotW, ChipH),
                    Building = b,
                    Colonist = i < b.Workers.Count ? b.Workers[i] : null,
                });
                sx += SlotW + 8;
            }
            lay.Cards.Add(card);
            y += cardH + 8;
        }
        lay.ListContentH = lay.Cards.Count * (cardH + 8);
        _crewScrollMax = Math.Max(0, lay.ListContentH - lay.ListArea.Height);
        _crewScroll = Math.Clamp(_crewScroll, 0, _crewScrollMax);

        return lay;
    }

    // ------------------------------------------------------------------ input

    /// <summary>Modal βρόχος της οθόνης: κύλιση, drag &amp; drop, κλείσιμο.</summary>
    private void UpdateCrewScreen(GameTime gameTime, KeyboardState keys, MouseState mouse)
    {
        _uiClick = false;
        if (_crewMessageTimer > 0) _crewMessageTimer -= gameTime.ElapsedGameTime.TotalSeconds;

        var lay = BuildCrewLayout();

        int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (wheel != 0)
        {
            if (lay.IdleArea.Contains(mouse.X, mouse.Y))
                _crewIdleScroll = Math.Clamp(_crewIdleScroll - Math.Sign(wheel) * 40, 0, _crewIdleScrollMax);
            else
                _crewScroll = Math.Clamp(_crewScroll - Math.Sign(wheel) * 48, 0, _crewScrollMax);
        }

        bool pressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
        bool released = mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed;

        if (pressed)
        {
            _crewDragStart = new Point(mouse.X, mouse.Y);
            _crewDragCandidate = null;

            foreach (var (rect, colonist) in lay.IdleChips)
                if (rect.Contains(mouse.X, mouse.Y) && lay.IdleArea.Contains(mouse.X, mouse.Y))
                {
                    _crewDragCandidate = colonist;
                    _crewDragGrab = new Point(mouse.X - rect.X, mouse.Y - rect.Y);
                }

            if (_crewDragCandidate is null && lay.ListArea.Contains(mouse.X, mouse.Y))
                foreach (var card in lay.Cards)
                    foreach (var slot in card.Slots)
                        if (slot.Colonist is not null && slot.Rect.Contains(mouse.X, mouse.Y))
                        {
                            _crewDragCandidate = slot.Colonist;
                            _crewDragGrab = new Point(mouse.X - slot.Rect.X, mouse.Y - slot.Rect.Y);
                        }
        }

        // Το drag ξεκινά μόλις ο κέρσορας απομακρυνθεί λίγο (ώστε ένα απλό κλικ να μην «σέρνει»).
        if (_crewDrag is null && _crewDragCandidate is not null && mouse.LeftButton == ButtonState.Pressed)
        {
            int dx = mouse.X - _crewDragStart.X, dy = mouse.Y - _crewDragStart.Y;
            if (dx * dx + dy * dy >= 16) _crewDrag = _crewDragCandidate;
        }

        if (released)
        {
            if (_crewDrag is { } dragged) DropCrew(dragged, lay, mouse.X, mouse.Y);
            else if (lay.Close.Contains(mouse.X, mouse.Y)) CloseCrewScreen();
            _crewDrag = null;
            _crewDragCandidate = null;
        }

        if (KeyPressed(keys, Keys.Escape) || KeyPressed(keys, Keys.C))
        {
            if (_crewDrag is not null) { _crewDrag = null; _crewDragCandidate = null; } // Esc ακυρώνει πρώτα το drag
            else CloseCrewScreen();
        }
    }

    /// <summary>Ανοίγει την οθόνη αναθέσεων (κλείνοντας τυχόν ανοιχτές παλέτες/λειτουργίες).</summary>
    private void OpenCrewScreen()
    {
        _crewScreenOpen = true;
        _buildMenuOpen = _speedMenuOpen = _researchMenuOpen = false;
        _buildMode = false;
        _reclaimMode = false;
        _crewScroll = _crewIdleScroll = 0;
        _crewMessageTimer = 0;
        _crewDrag = _crewDragCandidate = null;
        _audio.Blip();
    }

    private void CloseCrewScreen()
    {
        _crewScreenOpen = false;
        _crewDrag = null;
        _crewDragCandidate = null;
        _audio.Blip();
    }

    /// <summary>Αφήνει τον άποικο στο σημείο του κέρσορα: ανάθεση, ανταλλαγή ή αποδέσμευση.</summary>
    private void DropCrew(Colonist dragged, CrewLayout lay, int mx, int my)
    {
        // Στήλη «unassigned» → αποδέσμευση.
        if (lay.IdleArea.Contains(mx, my))
        {
            if (_world.Colony.Unassign(dragged))
            {
                CrewFeedback($"{dragged.Name} -> unassigned");
                _audio.Blip();
            }
            return;
        }

        if (!lay.ListArea.Contains(mx, my)) return;

        foreach (var card in lay.Cards)
        {
            var slot = card.Slots.FirstOrDefault(s => s.Rect.Contains(mx, my));
            if (slot is null && !card.Rect.Contains(mx, my)) continue;

            var target = card.Building;
            var occupant = slot?.Colonist;
            if (ReferenceEquals(occupant, dragged)) return;         // αφέθηκε στη θέση του

            // Συνεργείο επισκευής: ρητά στη θέση «repair», ή σε χαλασμένο κτήριο που δεν έχει
            // καθόλου θέσεις εργασίας (αυτόματο) — εκεί το μόνο που βγάζει νόημα είναι επισκευή.
            bool repair = slot?.IsRepair == true
                          || (slot is null && target.State == BuildingState.Disabled
                              && target.Definition.MaxWorkers <= 0);
            if (repair)
            {
                if (_world.Colony.AssignRepair(dragged, target))
                {
                    CrewFeedback($"{dragged.Name} -> repairing {target.Definition.Name}"
                                 + (dragged.Specialty == Specialty.Engineer ? "  (engineer: 3x faster)" : ""));
                    _audio.Blip();
                }
                return;
            }

            // Drop στο σώμα μιας γεμάτης καρτέλας (όχι σε συγκεκριμένη θέση): ανταλλαγή με τον τελευταίο.
            var partner = occupant ?? (target.Workers.Count >= target.Definition.MaxWorkers
                ? target.Workers.LastOrDefault()
                : null);

            if (_world.Colony.AssignOrSwap(dragged, target, occupant))
            {
                CrewFeedback(partner is not null && !ReferenceEquals(partner, dragged)
                    ? $"{dragged.Name} <-> {partner.Name}   ({target.Definition.Name})"
                    : $"{dragged.Name} -> {target.Definition.Name}");
                _audio.Blip();
            }
            return;
        }
    }

    private void CrewFeedback(string message)
    {
        _crewMessage = message;
        _crewMessageTimer = 3.0;
    }

    // ------------------------------------------------------------------ drawing

    private static Color SpecialtyColor(Specialty specialty) => specialty switch
    {
        Specialty.Geologist => new Color(225, 175, 95),
        Specialty.Engineer => new Color(140, 190, 250),
        Specialty.Botanist => new Color(130, 225, 140),
        Specialty.Climatologist => new Color(185, 160, 255),
        Specialty.Doctor => new Color(255, 140, 150),
        _ => new Color(180, 185, 195)
    };

    /// <summary>Κόβει κείμενο ώστε να χωρά σε δοσμένο πλάτος (με «..» στο τέλος).</summary>
    private string Truncate(string text, float maxWidth, float scale)
    {
        if (_font.MeasureString(text).X * scale <= maxWidth) return text;
        for (int len = text.Length - 1; len > 1; len--)
        {
            string candidate = text[..len] + "..";
            if (_font.MeasureString(candidate).X * scale <= maxWidth) return candidate;
        }
        return "";
    }

    private void DrawCrewScreen()
    {
        int vw = GraphicsDevice.Viewport.Width, vh = GraphicsDevice.Viewport.Height;
        var ms = Mouse.GetState();
        var lay = BuildCrewLayout();

        // Batch 1: σκίαση, πλαίσιο, τίτλος, κεφαλίδες στηλών.
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, vw, vh), new Color(0, 0, 0, 180));
        _spriteBatch.Draw(_pixel, lay.Panel, new Color(16, 18, 26, 250));
        DrawRectOutline(lay.Panel, new Color(120, 130, 150));

        const string title = "CREW ASSIGNMENTS";
        var tsz = _font.MeasureString(title) * 1.4f;
        _spriteBatch.DrawString(_font, title, new Vector2(lay.Panel.Center.X - tsz.X / 2f, lay.Panel.Y + 16),
            new Color(230, 150, 90), 0f, Vector2.Zero, 1.4f, SpriteEffects.None, 0f);

        int idleCount = lay.IdleChips.Count;
        _spriteBatch.DrawString(_font, $"UNASSIGNED ({idleCount})",
            new Vector2(lay.IdleArea.X, lay.IdleArea.Y - 26), new Color(255, 205, 120));
        _spriteBatch.DrawString(_font, "BUILDINGS", new Vector2(lay.ListArea.X, lay.ListArea.Y - 26),
            new Color(255, 205, 120));

        // Πλαίσιο της στήλης idle — τονίζεται όταν σέρνουμε πάνω της (drop = αποδέσμευση).
        bool overIdle = _crewDrag is not null && lay.IdleArea.Contains(ms.X, ms.Y);
        var idleBox = lay.IdleArea; idleBox.Inflate(6, 6);
        _spriteBatch.Draw(_pixel, idleBox, new Color(22, 26, 34, 200));
        DrawRectOutline(idleBox, overIdle ? new Color(255, 180, 90) : new Color(60, 66, 80));
        _spriteBatch.End();

        // Batch 2: στήλη ελεύθερων αποίκων (clipped).
        var prevScissor = GraphicsDevice.ScissorRectangle;
        GraphicsDevice.ScissorRectangle = lay.IdleArea;
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, _scissorRaster);
        foreach (var (rect, colonist) in lay.IdleChips)
        {
            if (rect.Bottom < lay.IdleArea.Y || rect.Y > lay.IdleArea.Bottom) continue;
            if (ReferenceEquals(colonist, _crewDrag)) continue; // σέρνεται — ζωγραφίζεται στον κέρσορα
            DrawCrewChip(rect, colonist, rect.Contains(ms.X, ms.Y), Specialty.None);
        }
        if (idleCount == 0)
            _spriteBatch.DrawString(_font, "everyone is at work", new Vector2(lay.IdleArea.X + 4, lay.IdleArea.Y + 4), HudDim);
        _spriteBatch.End();

        // Batch 3: καρτέλες κτηρίων (clipped).
        GraphicsDevice.ScissorRectangle = lay.ListArea;
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, _scissorRaster);
        foreach (var card in lay.Cards)
        {
            if (card.Rect.Bottom < lay.ListArea.Y || card.Rect.Y > lay.ListArea.Bottom) continue;
            DrawCrewCard(card, ms);
        }
        if (lay.Cards.Count == 0)
            _spriteBatch.DrawString(_font, "no buildings with jobs yet",
                new Vector2(lay.ListArea.X + 4, lay.ListArea.Y + 4), HudDim);
        _spriteBatch.End();
        GraphicsDevice.ScissorRectangle = prevScissor;

        // Batch 4: μπάρες κύλισης, υποσημείωση, καρτέλα που σέρνεται, κουμπί «X».
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        DrawCrewScrollbar(lay.IdleArea, lay.IdleContentH, _crewIdleScroll, _crewIdleScrollMax);
        DrawCrewScrollbar(lay.ListArea, lay.ListContentH, _crewScroll, _crewScrollMax);

        string footer = _crewMessageTimer > 0
            ? _crewMessage
            : "Drag onto a slot; a taken slot swaps. Red = repair (Engineer 3x). Drop on UNASSIGNED to free.";
        _spriteBatch.DrawString(_font, Truncate(footer, lay.Panel.Width - CrewPad * 2, 0.85f),
            new Vector2(lay.Panel.X + CrewPad, lay.Panel.Bottom - 24),
            _crewMessageTimer > 0 ? new Color(150, 230, 160) : HudDim, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 0f);

        if (_crewDrag is { } dragged)
        {
            var rect = new Rectangle(ms.X - _crewDragGrab.X, ms.Y - _crewDragGrab.Y, SlotW, ChipH);
            DrawCrewChip(rect, dragged, hover: true, Specialty.None, ghost: true);
        }

        DrawCloseButton(lay.Close, ms);
        _spriteBatch.End();
    }

    private void DrawCrewScrollbar(Rectangle area, int contentH, int scroll, int scrollMax)
    {
        if (scrollMax <= 0) return;
        int trackX = area.Right - 4;
        _spriteBatch.Draw(_pixel, new Rectangle(trackX, area.Y, 4, area.Height), new Color(40, 44, 54));
        int thumbH = Math.Max(20, (int)((long)area.Height * area.Height / Math.Max(1, contentH)));
        int thumbY = area.Y + (int)((long)(area.Height - thumbH) * scroll / scrollMax);
        _spriteBatch.Draw(_pixel, new Rectangle(trackX, thumbY, 4, thumbH), new Color(130, 150, 180));
    }

    /// <summary>Καρτέλα κτηρίου: εικονίδιο, όνομα, ιδανική ειδικότητα/απόδοση και οι θέσεις εργασίας.</summary>
    private void DrawCrewCard(CrewCard card, MouseState ms)
    {
        var b = card.Building;
        var d = b.Definition;
        bool dropHere = _crewDrag is not null && card.Rect.Contains(ms.X, ms.Y);

        bool broken = b.State == BuildingState.Disabled;

        _spriteBatch.Draw(_pixel, card.Rect, broken ? new Color(40, 24, 26, 240) : new Color(24, 28, 38, 235));
        DrawRectOutline(card.Rect, dropHere ? new Color(150, 220, 160)
            : broken ? new Color(150, 70, 66) : new Color(58, 64, 78));

        int iconSz = _font.LineSpacing + 2;
        if (_icons.TryGetValue(d.Id, out var icon))
            _spriteBatch.Draw(icon, new Rectangle(card.Rect.X + 6, card.Rect.Y + 4, iconSz, iconSz), Color.White);

        float nameX = card.Rect.X + 6 + iconSz + 6;
        _spriteBatch.DrawString(_font, Truncate(d.Name, 260f, 0.95f), new Vector2(nameX, card.Rect.Y + 4),
            HudWhite, 0f, Vector2.Zero, 0.95f, SpriteEffects.None, 0f);

        // Δεξιά της κεφαλίδας: κατάσταση, ιδανική ειδικότητα και τρέχουσα απόδοση.
        string state = b.State switch
        {
            BuildingState.UnderConstruction => "under construction",
            BuildingState.Disabled => $"BROKEN - {b.RepairTicksRemaining / 4}s of repair left",
            _ => b.Automated ? "automated" : $"eff {b.WorkerEfficiency():0.00}"
        };
        string info = !broken && d.OptimalSpecialty != Specialty.None
            ? $"best: {d.OptimalSpecialty}   {state}"
            : state;
        var isz = _font.MeasureString(info) * 0.85f;
        _spriteBatch.DrawString(_font, info, new Vector2(card.Rect.Right - 10 - isz.X, card.Rect.Y + 6),
            broken ? WarnPulse(HudWarn, 0.7f) : b.State == BuildingState.Operational ? HudDim : HudWarn,
            0f, Vector2.Zero, 0.85f, SpriteEffects.None, 0f);

        foreach (var slot in card.Slots)
        {
            bool hover = slot.Rect.Contains(ms.X, ms.Y);
            // Στη θέση επισκευής «ιδανικός» είναι ο Engineer, όποιο κι αν είναι το κτήριο.
            var optimal = slot.IsRepair ? Specialty.Engineer : d.OptimalSpecialty;

            if (slot.Colonist is { } c && !ReferenceEquals(c, _crewDrag))
            {
                DrawCrewChip(slot.Rect, c, hover, optimal, repair: slot.IsRepair);
            }
            else
            {
                // Άδεια (ή προσωρινά κενή, επειδή σέρνεται ο άποικός της) θέση.
                bool highlight = _crewDrag is not null && hover;
                _spriteBatch.Draw(_pixel, slot.Rect, slot.IsRepair ? new Color(34, 20, 22, 230) : new Color(18, 22, 30, 220));
                DrawRectOutline(slot.Rect, highlight ? new Color(150, 220, 160)
                    : slot.IsRepair ? new Color(190, 90, 80) : new Color(60, 66, 80));
                string label = Truncate(slot.IsRepair ? "send an Engineer" : "empty", slot.Rect.Width - 16, 0.85f);
                _spriteBatch.DrawString(_font, label,
                    new Vector2(slot.Rect.X + 8, slot.Rect.Y + 5),
                    slot.IsRepair ? new Color(235, 140, 120) : new Color(110, 116, 130),
                    0f, Vector2.Zero, 0.85f, SpriteEffects.None, 0f);
            }
        }
    }

    /// <summary>
    /// «Καρτέλα» αποίκου: χρωματιστή λωρίδα ειδικότητας, όνομα και ειδικότητα. Όταν η ειδικότητα
    /// ταιριάζει με το κτήριο (<paramref name="optimal"/>) μπαίνει «*» και πράσινο περίγραμμα.
    /// </summary>
    private void DrawCrewChip(Rectangle rect, Colonist colonist, bool hover, Specialty optimal,
        bool ghost = false, bool repair = false)
    {
        var accent = SpecialtyColor(colonist.Specialty);
        bool match = optimal != Specialty.None && colonist.Specialty == optimal;

        _spriteBatch.Draw(_pixel, rect, ghost ? new Color(40, 52, 68, 245)
            : repair ? new Color(48, 30, 30, hover ? 250 : 240) : new Color(30, 36, 48, hover ? 250 : 235));
        DrawRectOutline(rect, ghost ? new Color(255, 220, 150) : match ? new Color(130, 220, 140) : hover ? new Color(120, 150, 190) : new Color(62, 68, 84));
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X + 2, rect.Y + 2, 4, rect.Height - 4), accent);

        float textX = rect.X + 12;
        float avail = rect.Width - 18;
        string name = (match ? "* " : "") + colonist.Name;
        _spriteBatch.DrawString(_font, Truncate(name, avail, 0.85f), new Vector2(textX, rect.Y + 2),
            HudWhite, 0f, Vector2.Zero, 0.85f, SpriteEffects.None, 0f);

        string tag = colonist.Specialty == Specialty.None ? "worker" : colonist.Specialty.ToString().ToLowerInvariant();
        if (repair) tag = "repairing  " + tag;
        if (colonist.Health < 0.9) tag += $"  hp {colonist.Health * 100:0}%";
        _spriteBatch.DrawString(_font, Truncate(tag, avail, 0.7f), new Vector2(textX, rect.Y + 2 + _font.LineSpacing * 0.72f),
            accent, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);
    }
}

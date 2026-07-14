# 🔴 Terraforming Mars

**🇬🇧 English** · [🇬🇷 Ελληνικά](README.el.md)

**Build a colony on Mars. Then make Mars habitable. Then keep it that way.**

An open-source base-building / simulation game written in **C# / .NET 9 + MonoGame**, with a
procedurally generated hex map and a real-time simulation. You start with a landing capsule, four
colonists and a dead, frozen planet. You mine, you build, you research — and slowly you push up the
temperature, pressure, oxygen and water, until the ice melts into seas and vegetation creeps across
the plains.

And then the second half begins: a **living planet** that pushes back.

![The colony](docs/screenshots/colony.png)

---

## 📸 Screenshots

| | |
|---|---|
| ![Main menu](docs/screenshots/menu.png) <br> **Main menu** — sponsor, seed, music; the planet in the background cycles through every stage of terraforming | ![Buildings](docs/screenshots/buildings.png) <br> **Building palette** — 33 buildings, each with its own procedurally drawn icon |
| ![Research](docs/screenshots/research.png) <br> **Tech tree** — 23 technologies, 10 of which unlock **only after** terraforming is complete | ![Saves](docs/screenshots/saves.png) <br> **Load Game** — multiple saves with screenshots, timestamps and 3 cyclic autosaves |

---

## ✨ What's in the game

### Phase 1 — The Terraforming

* **Procedural hex map** (Perlin/fBm) with elevation, terrain and deposits (ice, iron, silicon, regolith).
* **Economy & population:** mining, production, brownouts when power runs short, and life support that will kill your crew if you neglect it. New colonists arrive when there is housing and surplus food.
* **Specialties:** Geologist, Engineer, Botanist, Climatologist, Doctor — the right person in the right building is worth +50% output.
* **Four planetary metrics** (temperature, pressure, O₂, water). Ice melts into seas, vegetation spreads, and the map visibly changes as you play.
* **Events:** dust storms (which cripple solar power), solar flares, life-support failures, cave discoveries.
* **Sponsors** (Easy / Normal / Hard) that change your starting capital, housing, event frequency and repair speed.
* **Trade:** sell surplus silicon to Earth with a Mass Driver to bankroll the megaprojects.

**You win** when all four metrics hit their targets. But winning isn't the end…

### Phase 2 — The Living Planet 🌍

The moment terraforming completes, **2 new tech tiers (10 technologies, 12 buildings)** unlock and the
planet stops being a passive target — it becomes a system you have to **maintain**. Your population
explodes from a dozen named colonists into **tens of thousands** of residents.

| System | What happens | Your answer |
|---|---|---|
| 🔥 **Runaway Greenhouse** | The factories that won you the game don't switch themselves off — temperature and pressure overshoot, the oceans start boiling away, and the crew sickens | **Cryo-Carbon Capturer** |
| 🏙️ **Urbanization** | Waves of migrants from Earth; if they outgrow your housing → *systemic stagnation* and production crawls | **High-Density Arcology** |
| ⚖️ **Faction Politics** | Industrialists vs Ecologists. Low approval means a **strike** — your mines (or your entire biosphere) stop working | **District Town Hall** |
| 🏭 **Pollution** | Heavy industry contaminates individual hexes; the vegetation around them withers | **Atmospheric Scrubber** |
| 💹 **Silicon Monopoly** | Stop selling cheap raw silicon — process it instead | **Quantum Processor Plant**, **Interplanetary Stock Exchange** |
| 🌋 **Deep Core Extraction** | Limitless metals from the mantle — at the price of **seismic instability** and marsquakes that crack buildings | Spread your **Deep Core Drills** out |
| 🤖 **Advanced Automation** | Drones run heavy industry with no humans at all | **AI Drone Hive** |
| 🌀 **Extreme Weather** | Thick air + oceans = **super-storms**. Hurricanes sweep away solar arrays and flood low-lying builds | **Sea Wall** |
| 🐛 **Invasive Species** | Earth pests eat your crops and wither your vegetation | **Genetic Vault**, Wildlife Reserves |
| 🚄 **Hyperloop Network** | Distant mines run at half output — and if a node is broken by a hurricane, they **black out** | A chain of **Hyperloop Terminals** |
| ☣️ **The Martian Plague** | Mutated pathogens bloom in the new oceans; your workforce sickens | **Isolation Hospital** staffed with **Doctors** |

### Quality of life

* **Step-by-step tutorial wizard** (it advances as you actually perform each action; Esc to leave).
* **Multiple saves** in a `SavedGames` folder, each with a **screenshot**, name and timestamp — a scrollable list, a large preview, and a confirmation before Delete.
* **3 cyclic autosaves** (Auto 1/2/3) every 5 minutes.
* **In-game help** for every building and every technology, in a scrollable window.
* **HUD counters** for buildings that need crew or have exhausted their deposit (with a "jump to the next one" button).
* **Sound & music** (OGG), one-click mute.

---

## 🛠 Stack

* **.NET 9** — a clean domain layer, completely engine-agnostic
* **MonoGame (DesktopGL)** for rendering/input — every icon is **generated procedurally** (a CPU rasterizer with anti-aliasing), with no asset files
* Hex grid (pointy-top, axial/cube), procedural generation with Perlin noise (fBm + quantile classification)
* Fixed-timestep simulation (pause / ×1 / ×2 / ×4) — **1 tick = 1 hour**, 24 hours = 1 Sol
* **Data-driven**: buildings, technologies and sponsors are defined in JSON — you can add a building without touching code
* **191 unit tests** (xUnit)

## 📁 Solution layout

```
src/TerraformingMars.Core   — domain & simulation (engine-agnostic)
    Grid/        Hex, OffsetCoord, HexLayout
    Map/         TerrainType, ResourceType, ResourceDeposit, HexTile, HexMap
    Generation/  INoiseSource, PerlinNoise, MapGenerator, MapGenerationSettings
    Simulation/  World, Colony, GameClock, ResourceLedger + 19 simulation systems:
                 Construction · Production · Market · Research · Planet · Biosphere ·
                 Population · LifeSupport · Event
                 ── Phase 2 ──
                 Phase2 (runaway) · Society (population) · Faction (politics) ·
                 Pollution · Seismic · Automation · Weather · Ecosystem ·
                 Hyperloop (logistics) · Plague
    Buildings/   BuildingDefinition, BuildingCatalog, Building
    Colonists/   Specialty, Colonist
    Research/    TechDefinition, TechCatalog, TechTree
    Planet/      PlanetMetric, PlanetState (terraforming metrics)
    Events/      EventType, SponsorProfile, SponsorCatalog
    Persistence/ SaveSystem, SaveGame (JSON save/load)
    Data/        buildings.json (33) · technologies.json (23) · sponsors.json (3)

src/TerraformingMars.Game   — MonoGame: Camera2D, HexMapRenderer, IconFactory,
                              AudioManager + MusicPlayer, SaveManager, MarsGame

tests/TerraformingMars.Core.Tests — xUnit (191 tests)
```

## ▶ Build & Run

```bash
dotnet test                                    # 191 unit tests
dotnet run --project src/TerraformingMars.Game # the game
```

> **Linux:** audio needs the system OpenAL — `sudo apt install libopenal1` (MonoGame's bundled
> libopenal wants a newer glibc). Music is OGG (cross-platform via `MediaPlayer`) and the HUD font is
> a bundled DejaVu Sans Mono.

## ⌨ Controls

The UI is mostly **mouse-driven**: a toolbar along the bottom plus status icons in the corners.

**Menu:** *Load Game* · *Start Game* · **Tutorial** · *Help* · *Quit* — click, or arrow keys + Enter.
Settings (sponsor, seed, music, volumes) via clicks/arrows; **R** = random seed.

**Toolbar (bottom):**

| Button | Action |
|---|---|
| Buildings | opens the palette (2 rows) → pick a building → click a hex to place it · **?** = help for every building |
| Research | lists the available technologies → click to pick one · **?** = help for every technology |
| Speed (clock) | pause / ×1 / ×2 / ×4 |
| Save (disk) | save the game (with a screenshot) |
| Mute (speaker) | mute/unmute |
| Center | snap the camera back to the landing capsule (**H**) |
| Crew · Deposit | counters for buildings that need workers / have run dry — click to jump to the next one |
| Reclaim | (once researched) dismantle a building for credits & materials |
| Menu · ? | back to the menu · help |

**Mouse & keys:**

| | Action |
|---|---|
| drag (left/middle) · wheel | pan · zoom |
| WASD / arrows | move the camera |
| right click | select a hex/building (or cancel a build/popup) |
| **[−] / [+]** on the building panel, or **+ / −** | assign/remove a colonist |
| Space · 1 / 2 / 3 | pause · speed ×1 / ×2 / ×4 |
| B · T · H | building palette · research · center camera |
| F5 · F9 | save · load |
| U · Esc | mute · back to the menu |

**HUD:** top-left **Sol & sponsor**; top-centre the **resource bar** (a value turns red when it's
falling; hover for its cap and its change per tick); below it the **4 targets + terraforming % +
biomass**; bottom-right **research progress**; bottom-left a panel with the selected tile/building,
**alerts** and events. In Phase 2 you also get **population**, **factions**, **pollution**,
**automation**, and warnings for runaway greenhouse, marsquakes, super-storms, invasive species,
plague and logistics blackouts.

---

## 📖 More

A full, non-technical guide to the gameplay: **[GAMEPLAY.md](GAMEPLAY.md)**

## License

[MIT](LICENSE)

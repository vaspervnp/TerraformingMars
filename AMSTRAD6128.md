# Terraforming Mars — Amstrad CPC 6128 Design & Port Plan

> **Scope.** This is **not** a port of the .NET/MonoGame build — that game is real-time, uses
> `double` everywhere, 2 400 hexes and 19 simulation systems. It is a **redesign of the same game
> for a 4 MHz Z80 with 128 KB**, keeping *all* the content (33 buildings, 23 technologies, every
> event and every Phase-2 crisis) and rebuilding the machinery around integer maths, byte-packed
> tables and a stepped tick.
>
> The headline finding: **content is not the problem.** All 33 buildings, 23 technologies and every
> event table encode into **≈820 bytes** (≈1.7 KB with on-screen names). The budget is spent on
> *rendering* and on *code for the ~22 unique behaviours*.

---

# Part I — What happens after terraforming

Winning the terraforming game (all four planetary metrics at 100 %) does not end the run; it
switches the game into **Phase 2: The Living Planet**. The planet stops being a passive target you
push and becomes a system you must **maintain**, while the population jumps from a dozen named
colonists to tens of thousands of abstract residents.

Phase 2 is split into two eras, gated by population.

## Phase 2A — Society & Climate (from the moment you win)

| Crisis | Trigger | Effect | Counter |
|---|---|---|---|
| **Runaway greenhouse** | temp or pressure > target + 4 | crew health decays, vegetation withers, oceans vaporise | Cryo-Carbon Capturer, or shut the gas factories down |
| **The Great Migration / stagnation** | population > housing | production ×0.85, health decays | High-Density Arcology |
| **Faction politics** | Industrialist or Ecologist approval < 0.25 | **strike** — all `Industry` (or all `Biosphere`) buildings halt | District Town Hall |
| **Pollution** | industry emits onto its own hex | vegetation on neighbouring hexes withers, oxygen falls | Atmospheric Scrubber |
| **The Martian Plague** | ocean coverage ≥ 0.25, after a 3-Sol grace | severity ramps, production drag up to ×0.5 | **staffed** Isolation Hospital (Doctors) |

## Phase 2B — The Infrastructure Boom (population ≥ 50 000)

| System | What it gives / costs | Counter / tool |
|---|---|---|
| **Silicon monopoly** | raw silicon is worth 35 cr; processed it is worth 350 | Quantum Processor Plant, Interplanetary Stock Exchange |
| **Deep core extraction** | limitless metals with no deposit — but **seismic stress** builds to a marsquake that cracks buildings in radius 1 | spread the drills out |
| **Advanced automation** | heavy industry runs with **no crew** | AI Drone Hive (4 buildings each) |
| **Extreme weather** | storm energy builds to a **hurricane**: sweeps solar arrays, floods low ground | Sea Wall (shield radius 2) |
| **Invasive species** | pest pressure scales with biomass; eats crops, withers vegetation | Genetic Vault, Wildlife Reserves |
| **Hyperloop network** | `Industry` further than 6 hexes from the core runs at ×0.5; a node broken by a hurricane blacks out everything it fed | chain of Hyperloop Terminals |

Two population thresholds latch permanently: **Urbanization at 10 000** (unlocks the Arcology) and
**Industrial Shift at 50 000** (unlocks the Stock Exchange).

---

# Part II — The 6128 budget

## Hardware facts we must live inside

| | |
|---|---|
| CPU | Z80A @ 4 MHz. The Gate Array steals cycles; every instruction rounds up to a multiple of 4 T-states. Budget **≈64 000 usable T-states per 50 Hz frame**. |
| RAM | 128 KB = 64 KB base + 4 × 16 KB banks paged into **&4000–&7FFF** (`OUT &7Fxx`, values &C0–&C7). |
| Screen | 16 KB at &C000. Non-linear: `addr = &C000 + (y AND 7)*&800 + (y>>3)*80 + (x>>2)`. |
| Mode 1 | 320×200, **4 colours**, 4 pixels/byte, 80 bytes per scanline. Chosen: 40-column text is non-negotiable for a strategy HUD. |
| Sound | AY-3-8912, 3 channels + noise. |
| Disc | 3", ~178 KB raw / ~169 KB usable under AMSDOS. |
| Firmware | Keep AMSDOS for disc I/O → RAM above **≈&A680** is reserved. Do our own rendering. |

## Memory map

```
&0000–&003F   RST vectors / interrupt stub
&0040–&00FF   fast scratch (tick temporaries, blitter vars)
&0100–&3FFF   ENGINE CODE            16 KB   (low ROM disabled)
&4000–&7FFF   PAGED WINDOW           16 KB   ← banks 4..7 = 64 KB of cold data
&8000–&A67F   RESIDENT DATA + tables  ~9.6 KB
&A680–&BFFF   AMSDOS + firmware      RESERVED — do not touch
&C000–&FFFF   SCREEN                 16 KB
```

**Paged through &4000–&7FFF:** text pack (all names + descriptions), tile/sprite sets, the save
buffer, the undo/autosave snapshot, music data. Nothing in the tick loop may live in a bank — the
tick only touches resident data, so we never page during simulation.

## Screen layout (Mode 1, 320×200)

```
┌───────────────────────────────┬────────┐
│ MAP  32 × 16 tiles            │ SELECT │   map: 256×128 px = 64 bytes/line
│ 8×8 px tiles, odd rows offset │ PANEL  │   panel: 64 px = 16 bytes/line
│ 4 px (= exactly 1 byte)       │ 8 cols │
├───────────────────────────────┴────────┤
│ HUD — 40 × 9 chars: resources, metrics,│   72 px
│ alerts, build/research menu            │
└────────────────────────────────────────┘
```

**The whole world is on screen at once — there is no scrolling and no camera.** That deletes an
entire subsystem. The half-tile offset that sells the "hex" read is 4 pixels, which in Mode 1 is
*exactly one byte*, so it costs a byte offset and **zero bit-shifting**.

| Cost | Value |
|---|---|
| Tile bitmap | 2 bytes × 8 rows = **16 bytes** |
| Tile blit (unrolled `LDI`) | ≈450 T-states |
| Full 512-tile redraw | ≈230 000 T ≈ **4 frames** (scene changes only) |
| Typical frame (dirty tiles only) | < 1 frame |

---

# Part III — Frugality decisions (PC → CPC)

| PC build | CPC 6128 | Why |
|---|---|---|
| 60×40 = 2 400 hexes | **32×16 = 512 tiles** | 512 B map, whole world visible, no scroll code |
| `double` everywhere | **8-bit / 16-bit fixed point** | Z80 has no FPU and no `MUL` |
| Real-time, 4 ticks/s, 19 systems every tick | **1 tick per 10 frames (5/s)**, systems round-robin | one system class per tick; full sweep every 8 ticks |
| Per-tile `double` pollution | **2 bits per tile** (0–3) | 128 B instead of 19 KB |
| Per-building `double` deposit | 1 byte in the building record (×16 units) | |
| Unbounded building list | **max 64 buildings**, 6 B each = 384 B | also caps tick cost |
| Full descriptions in RAM | **text pack on disc**, paged on demand | ~8 KB of prose never sits in RAM |
| Named colonists with health/morale | **8 crew slots**, 1 byte each (health) | specialty in the same byte |
| Hyperloop flood-fill over all buildings | recompute **only when a terminal changes state** | cached bitmask, not a per-tick graph walk |
| Vegetation spread scanning every tile | **amortised**: 64 tiles per tick | full pass every 8 ticks |
| Multiply-heavy efficiency chain | **shift/table lookups** (×0.5, ×0.85, ×1.5) | 256-byte tables beat a Z80 multiply |

## Number formats

| Quantity | Format | Notes |
|---|---|---|
| Resources (E/W/O/F/M/Si) | unsigned 16-bit | 0–65 535 |
| Credits | unsigned **24-bit** | costs reach 40 000; player banks far more |
| Production per building/tick | **signed byte, 1/8 units** | ±16.0 range; `E+12` → `+96`, `E-0.5` → `-4` |
| Planet metrics (T/P/O₂/H₂O) | byte 0–255 (= 0–100 %) + byte sub-accumulator | |
| Approval, plague, infestation, storm, seismic | **unsigned 16-bit, per-quantity scale** | scale chosen so the smallest per-tick delta is **≥ 4 units** (see Part V) |
| Population | unsigned 16-bit | capped at 65 535 |

---

# Part IV — Data formats (byte-exact)

### Map tile — 1 byte × 512 = **512 B**

| Bits | Field |
|---|---|
| 0–2 | terrain: 0 Flatland, 1 Lowland, 2 Highland, 3 Mountain, 4 Crater, 5 PolarIce, 6 Water, 7 Vegetation |
| 3–5 | deposit: 0 none, 1 Ice, 2 Iron, 3 Silicon, 4 Regolith |
| 6–7 | elevation band 0–3 (band 0 = floods first) |

Parallel arrays: `POLL[]` 2 bits/tile = **128 B**; `DIRTY[]` 1 bit/tile = **64 B**.

### Building instance — 6 bytes × 64 = **384 B**

| Byte | Field |
|---|---|
| 0 | tile index, low 8 bits |
| 1 | bit7 = tile index bit 8; bits 0–5 = building type (0–63) |
| 2 | bits 0–1 state (0 empty / 1 building / 2 operational / 3 disabled); bits 2–3 workers; bits 4–7 flags (automated, blackout, depleted, struck) |
| 3 | build progress |
| 4 | deposit remaining (×16 units) **or** repair countdown |
| 5 | spare / timer |

### Building definition — **20 bytes** × 33 = **660 B**

| Byte | Field |
|---|---|
| 0 | bits 0–3 category; bits 4–5 maxWorkers; bit6 solarPowered; bit7 shieldsAtmosphere |
| 1 | requiredTech index (255 = none) |
| 2 | cost Materials ÷ 4 |
| 3 | cost Credits ÷ 256 |
| 4–9 | 3 × production slot: `(resourceId, signed 1/8 value)`; resourceId 255 = unused |
| 10–18 | 3 × special slot: `(specialId, **16-bit** param)`; specialId 0 = unused |
| 19 | pad |

**Why a 16-bit param.** A 1-byte param cannot carry this content: `PopulationCapacity` runs
1 200 / 6 000 / 30 000 and `RequiresPopulation` runs 10 000 / 50 000. Dividing by 256 to squeeze
them into a byte quantises 1 200 → 4 (= 1 024) — a 15 % error on a threshold the player can see.
The extra 3 bytes per building costs 99 bytes total and makes every param exact.

Records are 20 bytes rather than 16, so indexing is `×16 + ×4` (four `ADD HL,HL`, copy, two more)
instead of a plain shift — about 20 T-states more per lookup, and lookups are not in the hot path.

### Technology — 4 bytes × 23 = **92 B** (+ 3 B researched bitmap)

| Byte | Field |
|---|---|
| 0 | cost ÷ 10 (15–100) |
| 1 | prerequisite A (255 = none) |
| 2 | prerequisite B (255 = none) |
| 3 | bits 0–2 tier (2–6); bit3 requiresPhase2 |

Unlocks are **not** stored: each building already names its `requiredTech`, so the palette is
derived by filtering. One direction only — cheaper and impossible to desync.

### Core state — **≈64 B resident**

Resources, planet metrics, population + peak, approvals, pollution/seismic/storm/infestation/plague
accumulators, Phase-2 flag byte, `phase2Ticks` (16-bit), RNG seed.

### Save game

`64 B state + 512 B map + 128 B pollution + 384 B buildings + 3 B tech ≈ **1.1 KB**` — four slots
plus three autosaves fit in 8 KB of disc.

---

# Part V — Content tables

## Special ability IDs

16-bit param, so every value below is **exact** — no quantisation anywhere.

| ID | Meaning | Param encoding | Values used |
|---|---|---|---|
| 1 | PopulationCapacity | raw | 1 200 / 6 000 / 30 000 |
| 2 | RequiresPopulation | raw | 10 000 / 50 000 |
| 3 | HousingCapacity | raw | 6 / 30 / 40 |
| 4 | PollutionPerTick | ×1000 | 20 / 30 |
| 5 | SeismicPerTick | ×1000 | 50 |
| 6 | AutomationCapacity | raw | 4 |
| 7 | StormShieldRadius | raw | 2 |
| 8 | EcosystemStability | ×10000 | 10 / 25 |
| 9 | HyperloopRange | raw | 6 |
| 10 | MedicalCapacity | ×1000 | 20 |
| 11 | ExtractionPerTick | ×8 | 8 / 12 |
| 12 | ExportPerTick | ×8 | 24 / 40 |
| 13 | SiliconExportPrice | raw | 350 |
| 14 | MaterialsExportPerTick | ×8 | 16 |
| 15 | VegetationSpread | ×1000 | 10 / 20 / 40 |
| 16 | ScrubsPollution | flag | 1 |
| 17–20 | PlanetEffect T / P / O₂ / H₂O | **signed** ×1000 | +35 … −25 |
| 21 | Storage | special-cased in code | only the capsule (4 pools) and the battery |
| 22 | RequiresDeposit | deposit id | 1–4 |

> **Storage** is the one field that does not fit the `(id, param)` shape — the landing capsule holds
> four different pools at once. Two buildings use it, so it is hardcoded rather than given a
> variable-length encoding that the other 31 buildings would pay for.

> **Reading the tables below:** the **Production** column is in stored form (signed 1/8 units, so
> `E−4` = −0.5 energy/tick). The **Specials** column is in *game* values — run them through the
> encoding table above to get the stored param.

## All 33 buildings

Production is signed 1/8 units (`E-4` = −0.5 energy/tick). `W` = max workers.

### Tier 1 — available from the start

| # | Name (CPC) | Cat | W | Mat | Cred | Production | Specials |
|---|---|---|---|---|---|---|---|
| 0 | LANDING CAPS | Habitat | 0 | — | — | E−8 | Storage E5000 W2000 O2000 F1500 |
| 1 | HABITAT MOD | Habitat | 0 | 70 | 5 000 | E−4 | PopCap 1200, Housing 6 |
| 2 | SOLAR PANEL | Power | 0 | 40 | 2 000 | E+32 | solarPowered |
| 3 | BATTERY | Power | 0 | 30 | 1 500 | — | Storage E3000 |
| 4 | O2 RECYCLER | LifeSup | 1 | 50 | 3 000 | O+10, E−4 | — |
| 5 | ICE DRILL | LifeSup | 1 | 50 | 2 500 | W+10, E−2 | Extract 1.0, needs Ice |
| 6 | HYDROPONICS | Food | 1 | 60 | 3 500 | F+7, W−2, E−3 | — |
| 7 | REGO PRINTER | Industry | 0 | 20 | 2 000 | M+4, E−3 | Extract 1.0, needs Regolith, Poll 0.02 |
| 8 | RESEARCH LAB | Research | 1 | 60 | 4 000 | R+12, E−4 | — |

### Tier 2 — Industrial Expansion

| # | Name | Cat | W | Mat | Cred | Production | Specials |
|---|---|---|---|---|---|---|---|
| 9 | FISSION RCTR | Power | 1 | 120 | 12 000 | E+96 | — |
| 10 | IRON MINE | Industry | 1 | 40 | 3 000 | M+10, E−4 | Extract 1.5, needs Iron, Poll 0.02 |
| 11 | SILICON MINE | Industry | 1 | 45 | 3 000 | S+8, E−4 | Extract 1.5, needs Silicon, Poll 0.02 |
| 12 | EXPORT TERM | Industry | 1 | 60 | 4 000 | E−5 | Export 3 |
| 13 | GHG FACTORY | Planet | 1 | 100 | 9 000 | E−12 | T +0.020, P +0.010, Poll 0.03 |

### Tier 3 — Planetary Engineering

| # | Name | Cat | W | Mat | Cred | Production | Specials |
|---|---|---|---|---|---|---|---|
| 14 | ORBIT MIRROR | Planet | 1 | 120 | 14 000 | E−16 | T +0.035 |
| 15 | MAGNETOSPHERE | Planet | 0 | 150 | 20 000 | E−32 | shieldsAtmosphere |
| 16 | COMET REDIR | Planet | 1 | 130 | 16 000 | E−20 | H₂O +0.030, P +0.015 |

### Tier 4 — Biosphere

| # | Name | Cat | W | Mat | Cred | Production | Specials |
|---|---|---|---|---|---|---|---|
| 17 | CYANO FARM | Biosphere | 1 | 70 | 6 000 | E−5 | O₂ +0.012, VegSpread 0.010 |
| 18 | GM FOREST | Biosphere | 1 | 90 | 8 000 | E−3 | O₂ +0.025, VegSpread 0.040 |
| 19 | FAUNA RESERVE | Biosphere | 1 | 80 | 7 000 | E−2, F+4 | O₂ +0.010, VegSpread 0.020, Ecosys 0.001 |
| 20 | DOMELESS CITY | Habitat | 0 | 200 | 25 000 | E−16 | PopCap 6000, Housing 30 |

### Tier 5 — Phase 2A: Society & Climate

| # | Name | Cat | W | Mat | Cred | Production | Specials |
|---|---|---|---|---|---|---|---|
| 21 | CRYO CAPTURER | Planet | 0 | 110 | 10 000 | E−14 | T −0.025, P −0.012 |
| 22 | TOWN HALL | Civic | 0 | 80 | 8 000 | E−4 | governance +0.15 (max +0.30) |
| 23 | ATM SCRUBBER | Civic | 0 | 70 | 6 000 | E−8 | ScrubsPollution |
| 24 | ISOLATION HOSP | Civic | **2** | 80 | 8 000 | E−24 | Medical 0.02 |
| 25 | ARCOLOGY | Habitat | 0 | 300 | 40 000 | E−48 | PopCap 30000, **needs pop 10 000**, Housing 40 |

### Tier 6 — Phase 2B: Infrastructure Boom

| # | Name | Cat | W | Mat | Cred | Production | Specials |
|---|---|---|---|---|---|---|---|
| 26 | QUANTUM PLANT | Industry | 0 | 90 | 12 000 | E−16 | Export 3, SiPrice 350 |
| 27 | STOCK EXCHANGE | Civic | 0 | 150 | 30 000 | E−64 | Export 5, MatExport 2, **needs pop 50 000** |
| 28 | DEEP CORE DRILL | Industry | 1 | 120 | 15 000 | M+16, S+12, E−32 | Seismic 0.05, Poll 0.03, **no deposit needed** |
| 29 | AI DRONE HIVE | Civic | 0 | 100 | 14 000 | E−40 | Automation 4 |
| 30 | SEA WALL | Civic | 0 | 90 | 7 000 | E−4 | StormShield 2 |
| 31 | GENETIC VAULT | Biosphere | 1 | 90 | 9 000 | E−8 | Ecosys 0.0025 |
| 32 | HYPERLOOP TERM | Civic | 0 | 70 | 6 000 | E−16 | HyperloopRange 6 |

## All 23 technologies

`Cost÷10` is the stored byte. Tier 5–6 require Phase 2.

| # | Tech | Tier | Cost | Byte | Prereq | Unlocks |
|---|---|---|---|---|---|---|
| 0 | RECLAIM | 2 | 150 | 15 | — | (dismantle action) |
| 1 | NUCLEAR FISSION | 2 | 300 | 30 | — | Fission Reactor |
| 2 | HEAVY METALLURGY | 2 | 250 | 25 | — | Iron Mine |
| 3 | ROVER NETWORKS | 2 | 200 | 20 | — | (survey bonus) |
| 4 | SILICON EXTRACTION | 2 | 220 | 22 | — | Silicon Mine |
| 5 | MASS DRIVER | 2 | 350 | 35 | #4 | Export Terminal |
| 6 | GREENHOUSE GAS | 2 | 400 | 40 | #1 | GHG Factory |
| 7 | ORBITAL MIRRORS | 3 | 600 | 60 | #6 | Orbital Mirror |
| 8 | ARTIF MAGNETOSPHERE | 3 | 700 | 70 | #1 | Magnetosphere |
| 9 | COMET REDIRECTION | 3 | 800 | 80 | #7 | Comet Redirector |
| 10 | CYANOBACTERIA | 4 | 500 | 50 | #7 | Cyano Farm |
| 11 | GM TREES | 4 | 700 | 70 | #10 | GM Forest, Fauna Reserve |
| 12 | DOMELESS CITIES | 4 | 1000 | 100 | #11 + #8 | Domeless City |
| 13 | ATMOSPHERE SINKS | **5** | 550 | 55 | #6 | Cryo Capturer |
| 14 | SOCIO-POLITICAL | **5** | 500 | 50 | — | Town Hall |
| 15 | ATM SCRUBBING | **5** | 450 | 45 | — | Atm Scrubber |
| 16 | MACRO-EPIDEMIOLOGY | **5** | 500 | 50 | — | Isolation Hospital |
| 17 | QUANTUM PROCESSING | **6** | 700 | 70 | #5 | Quantum Plant |
| 18 | CORE-MANTLE PEN | **6** | 800 | 80 | #2 | Deep Core Drill |
| 19 | AUTOMATED LABOR | **6** | 750 | 75 | — | AI Drone Hive |
| 20 | STORM ENGINEERING | **6** | 650 | 65 | — | Sea Wall |
| 21 | ECOLOGICAL ENG | **6** | 700 | 70 | #11 | Genetic Vault |
| 22 | MAGLEV PROPULSION | **6** | 750 | 75 | #5 | Hyperloop Terminal |

**Note:** technology #12 is the only one with two prerequisites — which is exactly why the tech
record carries two prereq bytes.

## Events — the four random ones

Rolled once per tick against the sponsor's chance byte.

| Event | Effect | Duration | Counter |
|---|---|---|---|
| **DUST STORM** | solar output ×0.2 | 250–600 ticks | batteries, fission |
| **SOLAR FLARE** | crew health −4/tick; disables one random non-Power building | 100–300 ticks | Magnetosphere **or** Cave shelter |
| **LIFE SUPPORT FAIL** | disables one `LifeSupport` building | until repaired | Engineer on site (×2 repair) |
| **CAVE DISCOVERY** | permanent radiation shelter flag | permanent | — (positive) |

## Events — the eleven Phase-2 crises

All are **deterministic state machines** on 16-bit accumulators, not dice. Scale column is the
fixed-point unit; deltas are per tick.

| # | Crisis | Accumulator (scale) | Rises by | Fires at | Effect | Counter |
|---|---|---|---|---|---|---|
| 1 | Runaway greenhouse | — (compares T/P to target) | — | T or P > target+4 | health −, wither veg, vaporise water | Cryo Capturer #21 |
| 2 | Urbanization | population | migration | **10 000** (latched) | unlocks Arcology | — |
| 3 | Industrial Shift | population | migration | **50 000** (latched) | unlocks Stock Exchange | — |
| 4 | Stagnation | — | — | pop > housing | production ×0.85, health − | Arcology #25 |
| 5 | Faction strike | approval 0–255 | drift ±3 | < 64, ends > 90 | halt all `Industry` **or** all `Biosphere` | Town Hall #22 |
| 6 | Pollution wither | 2 bits/tile | +1 per emitter | level 3 | adjacent vegetation dies | Scrubber #23 |
| 7 | Marsquake | 1/256 | +13 per drill, −3 decay | **3 840** | disable buildings radius 1, 200-tick repair | spread drills |
| 8 | Hurricane | 1/256 | +5…8 (air × ocean) | **3 072** | disable solar + elevation-band 0, 250-tick repair | Sea Wall #30 |
| 9 | Invasive species | 1/16384 | +20 × biomass | **4 915** (0.30) | food drain, wither 1–3 veg tiles | Genetic Vault #31 |
| 10 | Martian Plague | 1/256 | +1 when ocean ≥ 0.25, after 3-Sol grace | ramps to 256 | production ×(1 − sev/2), floor 0.5 | **staffed** Hospital #24 |
| 11 | Logistics blackout | — | — | `Industry` > 6 tiles from core, unlinked | that building ×0.5 | Hyperloop chain #32 |

**Scale rule:** every accumulator's unit is chosen so the *smallest* per-tick delta is ≥ 4 units.
That keeps all of it in 16-bit integer adds with no rounding death, and no multiply.

---

# Part VI — Implementation phases

Each phase is independently playable and independently shippable. Do not start the next until the
acceptance test passes.

### Phase 0 — Skeleton (no game yet)
* Mode 1 setup, palette, interrupt-driven 50 Hz timer, tick divider (1 tick / 10 frames).
* Tile blitter (unrolled, 16-byte tiles), dirty-tile list, screen address table (200 entries × 2 B = 400 B — never compute the interleave at runtime).
* Keyboard/joystick reader, cursor on the map grid.
* **RAM:** code 4 KB, tiles 256 B, address table 400 B.
* **Accept:** move a cursor over a 32×16 tile field at 50 fps with < 1 frame of blitting.

### Phase 1 — Map & economy
* Map generator (see note below), `MAP[512]`, terrain/deposit rendering, side panel.
* Resource ledger (16-bit + 24-bit credits), building list, place/demolish, construction progress, worker assignment.
* Production tick over ≤64 buildings, energy brownout gating.
* **RAM:** +512 B map, +384 B buildings, +660 B defs, +64 B state.
* **Accept:** place a solar panel + ice drill, watch water rise and energy balance; brownout when you over-build.

### Phase 2 — Terraforming
* Four planet metrics + sub-accumulators, `PlanetEffect` specials (IDs 17–20).
* Ice→water melt and vegetation spread, **amortised 64 tiles/tick**.
* Win check at 4×100 %.
* **Accept:** GHG Factory raises temperature; polar ice becomes water tiles; the win latch fires.

### Phase 3 — Research
* Tech table, researched bitmap, research points from labs, tech picker UI, palette filtering by `requiredTech`.
* **RAM:** +92 B techs, +3 B bitmap.
* **Accept:** research Nuclear Fission, Fission Reactor appears in the palette.

### Phase 4 — Classic events + save
* The four random events, sponsor difficulty byte, repair countdowns, alert line in the HUD.
* AMSDOS save/load of the 1.1 KB block; 4 slots + autosave rotation.
* **Accept:** survive a dust storm on batteries; save, reset the machine, load, identical state.

### Phase 5 — Phase 2A
* Phase-2 latch on win, population model (migration vs housing), stagnation, factions + strikes, per-tile pollution + scrubbers, plague + Doctors.
* Buildings #21–#25, techs #13–#16.
* **RAM:** +128 B pollution, +~16 B state.
* **Accept:** win the game, watch the greenhouse run away, build a Cryo Capturer and pull it back; provoke a strike and settle it with a Town Hall.

### Phase 6 — Phase 2B
* Seismic + marsquake, weather + hurricane + Sea Wall shielding, automation, ecosystem/invasive species, Hyperloop connectivity (cached bitmask, recomputed only on terminal state change).
* Buildings #26–#32, techs #17–#22.
* **Accept:** a hurricane knocks out a Hyperloop terminal and the mines it fed go dark until repaired.

### Phase 7 — Polish
* AY music + SFX, tutorial overlay, text pack paging from disc, title screen, sponsor select.
* **Accept:** full run from landing to a maintained Phase-2 planet without a reset.

### Note on map generation
Perlin/fBm is far too expensive. Generate on the **host** and ship 8–16 fixed maps in a bank
(512 B each = 8 KB for 16 maps), or use a cheap 16-bit LFSR + 3×3 smoothing pass at load time
(~50 ms, once). Fixed maps are the frugal answer and make seeds shareable.

---

# Part VII — Risks

| Risk | Mitigation |
|---|---|
| 64-building cap feels tight late game | it is the *point* — it also caps tick cost. Reclaim is the pressure valve. |
| 16-bit population caps at 65 535 | Industrial Shift is at 50 000; cap and display "65K+" |
| Tick cost spikes when many systems coincide | systems are round-robin, one class per tick; never all in one frame |
| Text pack paging stalls the tick | page only in the UI, never inside the tick loop |
| 4 colours makes terrain hard to read | terrain uses pattern *and* colour; deposits are a 2×2 corner glyph |

---

## Bottom line

The full content of the modern game — **33 buildings, 23 technologies, 4 random events and 11
Phase-2 crises** — encodes into **≈820 bytes of tables** (660 B building defs + 92 B techs + 3 B
researched bitmap + ~64 B event table), or ≈1.7 KB once short display names are added. A 6128 can
hold all of it comfortably, with the descriptive text paged from disc. What
actually has to be engineered away is the *real-time, floating-point, 2 400-hex* machinery around
it: a 512-tile world with no scrolling, integer accumulators scaled so nothing needs a multiply,
64 buildings, and a round-robin tick. That is a real game on a real 6128 — not a demo.

# Terraforming Mars — Complete Game Specification

**Purpose of this document.** This is a complete, self-contained specification of the *real-time Mars-colony simulation* implemented in this repository (a C#/MonoGame game). It is written so it can be handed to an AI (or a human) to **re-implement the game from scratch on an Amstrad CPC 6128 in Z80 assembly**. Every rule, constant, and formula below is taken from the actual source code and data files, not invented.

> ⚠️ **This is NOT the Fryxelius board game "Terraforming Mars".** It is an original real-time colony-builder that happens to share the theme. Do not implement card-drafting, corporations, or the board-game rules. Implement the simulation described here.

The document has two parts:

1. **Part A — Game Specification** (platform-independent ground truth). Sections 1–16.
2. **Part B — Amstrad CPC 6128 / Z80 Port Guide** (how to adapt Part A to the target). Sections 17–22.

An **Appendix** collects every numeric constant in one place.

---

# PART A — GAME SPECIFICATION

## 1. Concept and core loop

You are the operations director of humanity's first permanent Mars colony. You start with a landing capsule, a handful of buildings, and 4 crew. Real time flows in discrete **ticks**; buildings produce and consume **resources** each tick; you spend **credits + materials** to build more; you research **technologies** that unlock planetary-engineering machines; and those machines slowly push four **planet metrics** (Temperature, Pressure, Oxygen, Water) from Martian values toward habitable targets.

**You win** when all four metrics reach their targets simultaneously.
**You lose** if your entire crew dies of life-support failure.

The moment-to-moment loop:

1. Watch resource rates (top HUD). Fix shortfalls (build power, life support, food).
2. Expand: place buildings on suitable hexes (some need deposits, some need to touch a habitat).
3. Assign crew to buildings for efficiency bonuses.
4. Research the tech tree to unlock better power, mining, and terraforming.
5. Build planetary machines (GHG factories, orbital mirrors, magnetosphere, comet redirectors, biosphere) to raise the four metrics.
6. Survive random events (dust storms, solar flares, life-support failures).
7. Terraform to 100% on all four goals.

---

## 2. World model overview — three layers of "stuff"

It is critical to keep three distinct concepts separate; they use overlapping words (Oxygen, Water) but are **different things**:

| Layer | Examples | Where it lives | Notes |
|---|---|---|---|
| **Map ores** (raw) | Ice, Iron, Silicon, Regolith | In hex tiles, as finite deposits | Mined by buildings; deplete to zero |
| **Colony resources** (processed) | Energy, Water, Oxygen, Food, Materials, Silicon, Credits, Research | Global "ledger" (not per-tile) | Produced/consumed per tick; some capped by storage |
| **Planet metrics** (global) | Temperature, Pressure, Oxygen(atmosphere), Water(coverage), Biomass | Single global `PlanetState` | The terraforming victory values |

**Watch out for the collisions:**
- Colony **Oxygen** (a breathable resource consumed by crew, produced by the O₂ Recycler) is *not* the planet **Oxygen** metric (atmospheric %, raised only by biosphere buildings' *planet effects*).
- Colony **Water** (a resource from the Ice Drill, consumed by hydroponics/crew) is *not* the planet **Water** metric (surface coverage, produced by melting polar ice and comet impacts).
- **Silicon** is both a mined ore *and* a colony resource (mined → stored → sold for credits).
- **Materials** is a colony resource produced by processing Iron/Regolith ore; it is the build currency.

---

## 3. The map and coordinate system

- The game map is a **rectangular grid of pointy-top hexagons**, default **64 wide × 44 tall** (= 2816 tiles), generated deterministically from a seed (`424242` in the shipping game; the Core default is 60×40 / seed 1337 but the game overrides it).
- Internally hexes use **axial coordinates** `(Q, R)` with cube third coordinate `S = −Q − R`.
- The grid is laid out over a rectangle of **odd-r offset coordinates** `(col, row)` (pointy-top; odd rows shifted right), converted to axial:
  - offset → axial: `Q = col − (row − (row & 1)) / 2`, `R = row`
  - axial → offset: `col = Q + (R − (R & 1)) / 2`, `row = R`
- Hex neighbor directions (index 0 = east, clockwise): `(1,0) (1,−1) (0,−1) (−1,0) (−1,1) (0,1)`.
- Hex distance = `(|Q| + |R| + |S|) / 2`.

**Pointy-top pixel math** (only needed if you render true hexes; see §19 for the CPC simplification). With hex size `s` and origin `(ox, oy)`:
```
HexToPixel(Q,R):  x = s·(√3·Q + (√3/2)·R) + ox
                  y = s·(3/2·R)            + oy
PixelToHex(px,py): x = (px−ox)/s ; y = (py−oy)/s
                   q = (√3/3)·x − (1/3)·y
                   r = (2/3)·y
                   → round to nearest hex via cube rounding
```

### 3.1 Map generation (reference — you may pre-bake maps instead)

The reference generator uses layered Perlin/fractal noise. This is heavy for a Z80 (see §19.4 for alternatives). The algorithm:

1. **Elevation**: fractal noise (`Octaves=5, Lacunarity=2.0, Persistence=0.5, NoiseScale=0.08`) sampled per tile, normalized to ≈[−1, 1].
2. **Polar ice**: latitude `lat = |row − Height/2| / (Height/2)` (0 = equator, 1 = pole). Any tile with `lat ≥ 0.82` is forced to **PolarIce** (roughly the top ~4 and bottom ~3 rows).
3. **Terrain by elevation quantile** (thresholds computed from the actual non-polar elevation distribution, so the mix is stable):

   | Terrain | Cutoff quantile | Approx share of non-polar tiles |
   |---|---|---|
   | Canyon | 0.06 | 6% |
   | Lowland | 0.22 | 16% |
   | Flatland / Crater | 0.68 | 46% (12% of these become Crater) |
   | Highland | 0.88 | 20% |
   | Mountain | (top) | 12% |

4. **Deposits** rolled per tile with a coherent "vein" noise field (see §5).

---

## 4. Terrain types

Enum order (= numeric id):

| id | Terrain | Buildable? | Notes |
|---|---|---|---|
| 0 | Canyon | ✅ | low elevation |
| 1 | Lowland | ✅ | |
| 2 | Flatland | ✅ | landing spot is always Flatland |
| 3 | Crater | ✅ | subset of the flatland band |
| 4 | Highland | ✅ | |
| 5 | Mountain | ❌ | **blocks building** |
| 6 | PolarIce | ✅ | always carries a large Ice deposit |
| 7 | Water | ❌ | **blocks building**; appears only when polar ice melts |
| 8 | Vegetation | ✅ | spreads late-game as the planet becomes habitable |

**Buildability gate:** a tile is buildable unless its terrain is **Mountain or Water**. Individual buildings may further restrict terrain (`AllowedTerrain`) or require a deposit or a habitat link (§7).

---

## 5. Map ore deposits

Deposit types: `None, Ice, Iron, Silicon, Regolith`.

Each deposit has a **Type**, an integer **Amount** (remaining units), and a **Hidden** flag (invisible until surveyed by a Geologist). A tile's deposit depletes as it is mined (`RemainingDeposit`), and becomes `DepositDepleted` when it hits 0.

**Placement rules & amounts** (`_rng.Next(min, max)` is min-inclusive, max-exclusive; a `vein` value ∈[0,1] comes from a second noise field):

| Terrain | Condition | Deposit | Amount range |
|---|---|---|---|
| PolarIce | always | Ice | 800–1599 |
| Crater / Canyon | 33% chance | Ice | 200–599 |
| Mountain / Highland | `vein > 0.55` and 40% | Iron | 300–899 |
| Mountain / Highland | else 30% | Silicon | 200–699 |
| Flatland / Lowland | 30% | Regolith | 150–499 |
| Flatland / Lowland | else 18% | Ice | 100–399 |

**Hidden flag:** surface Ice is never hidden. A non-Ice deposit (Iron/Silicon/Regolith) is hidden with 35% probability.

**Extraction:** a mining building with `ExtractionPerTick = E` tries to mine `E × efficiency` units/tick from its tile; it takes `min(need, remaining)`. Its resource output is scaled by `factor = extracted / E`, so output tapers as the deposit runs dry, then the mine goes inert.

---

## 6. Time and the simulation tick

The simulation is a **fixed-timestep** loop driven by a real-time accumulator, so it is deterministic and framerate-independent.

| Constant | Value | Meaning |
|---|---|---|
| Ticks/second at ×1 | **4.0** | Normal speed = 4 ticks per real second |
| In-game minutes/tick | **10.0** | |
| Ticks/Sol | **144** | 1 Sol (Martian day) = 1440 in-game minutes |
| Max ticks/frame | **32** | anti "spiral of death" clamp |

**Speeds:** Paused = 0×, Normal = 1×, Fast = 2×, Ultra = 4× (→ 0 / 4 / 8 / 16 ticks per real second).

**Advance algorithm** each rendered frame, given real `dt` seconds:
```
if multiplier <= 0: accumulator = 0; run 0 ticks   // paused
accumulator += dt · multiplier · 4.0
ticks = floor(accumulator)          // clamp to 32
accumulator -= ticks
TotalTicks += ticks
run Tick() that many times
```
`Sol = TotalTicks/144 + 1` (1-based). Clock also exposes hour/minute of Sol for display.

### 6.1 Per-tick system order

Every `Tick()` runs these systems **in this exact order** (this ordering matters — e.g. Market runs after Production so freshly-mined silicon is sellable the same tick):

1. **EventSystem** (only if events enabled) — roll/advance random events, repair disabled buildings.
2. **ConstructionSystem** — advance buildings under construction (consume materials gradually).
3. **ProductionSystem** — production/consumption + energy brownout gating.
4. **MarketSystem** — sell surplus Silicon → Credits.
5. **ResearchSystem** — accumulate research points into the current tech.
6. **PlanetSystem** — apply planetary effects, atmosphere leak, ice-melt/flood tile conversion, recompute water coverage.
7. **BiosphereSystem** — spread vegetation tiles.
8. **PopulationSystem** — spawn new colonists when food + housing allow.
9. **LifeSupportSystem** — consume O₂/water/food per crew; handle starvation deaths & colony collapse.

Before/after each tick the engine snapshots every resource and records the **net rate/tick** for the HUD.

---

## 7. Buildings

### 7.1 Building definition fields (schema)

`Id, Name, Description, Category, Buildable(=true), BuildTimeTicks(=100), MaxWorkers(=0), OptimalSpecialty(=None), RequiresDeposit(=None), ExtractionPerTick(=0), ExportPerTick(=0), RequiredTech(=""), AllowedTerrain(list, empty=any buildable), Cost{resource→amount}, Production{resource→±/tick}, Storage{resource→capacity}, PlanetEffects{metric→±/tick}, ShieldsAtmosphere(bool), SolarPowered(bool), VegetationSpreadPerTick(0), HousingCapacity(0), RequiresHabitatLink(bool).`

`MaxWorkers = 0` means **automatic** (no crew needed, always runs at efficiency 1.0 when operational).

### 7.2 Full building catalog

Costs are **Materials / Credits**. Production is per tick (+ produce, − consume). "Tech" = required research to unlock.

| Id | Category | Build ticks | Workers / optimal | Cost (Mat/Cr) | Production /tick | Special |
|---|---|---|---|---|---|---|
| landing_capsule | Habitat | 0 | 0 | — (not buildable) | Energy −1.0 | Storage: E5000 W2000 O2000 F1500. Starting base. |
| habitat_module | Habitat | 110 | 0 | 70 / 5000 | Energy −0.5 | Needs habitat link; **Housing +6** |
| solar_panel | Power | 60 | 0 | 40 / 2000 | Energy +4.0 | **SolarPowered**; only Flatland/Lowland/Highland/Crater |
| battery | Power | 50 | 0 | 30 / 1500 | — | Storage: Energy 3000 |
| o2_recycler | LifeSupport | 80 | 1 / Engineer | 50 / 3000 | Oxygen +1.2, Energy −0.5 | |
| ice_drill | LifeSupport | 90 | 1 / Geologist | 50 / 2500 | Water +1.2, Energy −0.3 | Needs **Ice** deposit; Extract 1.0 |
| hydroponics | Food | 100 | 1 / Botanist | 60 / 3500 | Food +0.9, Water −0.3, Energy −0.4 | |
| regolith_printer | Industry | 80 | 0 | 20 / 2000 | Materials +0.5, Energy −0.4 | Needs **Regolith**; Extract 1.0 |
| research_lab | Research | 100 | 1 / Climatologist | 60 / 4000 | Research +1.5, Energy −0.5 | |
| fission_reactor | Power | 150 | 1 / Engineer | 120 / 12000 | Energy +12.0 | Tech: nuclear_fission |
| iron_mine | Industry | 90 | 1 / Geologist | 40 / 3000 | Materials +1.2, Energy −0.5 | Tech: heavy_metallurgy; needs **Iron**; Extract 1.5 |
| silicon_mine | Industry | 90 | 1 / Geologist | 45 / 3000 | Silicon +1.0, Energy −0.5 | Tech: silicon_extraction; needs **Silicon**; Extract 1.5 |
| export_terminal | Industry | 110 | 1 / Engineer | 60 / 4000 | Energy −0.6 | Tech: mass_driver; **ExportPerTick 3.0** |
| ghg_factory | Planetary | 140 | 1 / Climatologist | 100 / 9000 | Energy −1.5 | Tech: greenhouse_gas_production; **Temp +0.020, Pressure +0.010** |
| orbital_mirror | Planetary | 160 | 1 / Climatologist | 120 / 14000 | Energy −2.0 | Tech: orbital_mirrors; **Temp +0.035** |
| magnetosphere_station | Planetary | 200 | 0 | 150 / 20000 | Energy −4.0 | Tech: artificial_magnetosphere; **ShieldsAtmosphere** |
| comet_redirector | Planetary | 180 | 1 / Climatologist | 130 / 16000 | Energy −2.5 | Tech: comet_redirection; **Water +0.03, Pressure +0.015** |
| cyanobacteria_farm | Biosphere | 120 | 1 / Botanist | 70 / 6000 | Energy −0.6 | Tech: cyanobacteria; **Oxygen +0.012**; VegSpread 0.010 |
| gm_forest | Biosphere | 150 | 1 / Botanist | 90 / 8000 | Energy −0.4 | Tech: gm_trees; **Oxygen +0.025**; VegSpread 0.040 |
| fauna_reserve | Biosphere | 140 | 1 / Botanist | 80 / 7000 | Energy −0.3, Food +0.5 | Tech: gm_trees; **Oxygen +0.010**; VegSpread 0.020 |
| domeless_city | Habitat | 250 | 0 | 200 / 25000 | Energy −2.0 | Tech: domeless_cities; **Housing +30** |

> Note: `PlanetEffects` (Temp/Pressure/Oxygen/Water) are **planet-metric** deltas — they terraform. The O₂ Recycler's `Oxygen +1.2` is a **colony resource** (breathing), *not* a planet effect. Only the Planetary/Biosphere buildings have `PlanetEffects`.

### 7.3 Building states & runtime

**States:** `UnderConstruction → Operational → (Disabled ⇄ Operational)`.

- A building with `startOperational` or `Buildable=false` (the landing capsule) begins **Operational** at full progress.
- Otherwise it begins **UnderConstruction** with `BuildProgress = 0`.

**Construction** (per tick, ConstructionSystem):
- `speed = 2` if any assigned worker is an **Engineer**, else `1`.
- Materials are delivered gradually: `need = totalMaterials / BuildTimeTicks · speed`. If the colony can't afford `need` this tick → building is **Stalled** (no progress) until materials exist. Otherwise `MaterialsPaid += need`, `BuildProgress += speed`.
- When `BuildProgress ≥ BuildTimeTicks` → becomes **Operational** and its `Storage` is added to the global capacity.

**Worker efficiency** — the multiplier applied to *everything* a building does (production, planet effects, research, exports, vegetation). Range **0 … 1.5**:
```
if not Operational:            efficiency = 0
else if MaxWorkers == 0:       efficiency = 1.0        // automatic building
else if Workers.Count == 0:    efficiency = 0          // manned but empty → nothing
else:
   staffing      = min(1, Workers.Count / MaxWorkers)
   hasSpecialist = any worker.Specialty == OptimalSpecialty
   health        = average(worker.Health)
   efficiency    = staffing · (hasSpecialist ? 1.5 : 1.0) · health
```
So: fully staffed + matching specialist + full health = **1.5×**; an automatic building = **1.0×**; a manned building with nobody assigned = **0×**.

**Disabled / repair** (caused by events): a disabled building has `RepairTicksRemaining` counting down by 2/tick (if an Engineer is assigned) or 1/tick; at ≤0 it returns to Operational. While disabled, efficiency = 0.

---

## 8. Resources, production and energy

### 8.1 The ledger

Colony resources: **Energy, Water, Oxygen, Food, Materials, Silicon, Credits, Research** (Research is a *flow*, not stored — it goes straight into the tech tree).

- Each resource has an **amount** and a **capacity**. Capacity is **infinite by default**; a finite cap exists only for resources that some operational building declares `Storage` for (e.g. Energy is capped by the sum of capsule + batteries; Water/Oxygen/Food capped by the capsule).
- Every write **clamps to [0, capacity]**. Overflow above the cap is **silently discarded**; shortage floors at 0.
- Consumption (life support, construction materials) is **all-or-nothing** per resource: it only succeeds if the full amount is available.

### 8.2 Production / consumption per tick (ProductionSystem)

Two passes:

**Pass 1 — compute each operational building's factor and tally energy:**
- Mining buildings: `factor = extracted / ExtractionPerTick` (0…1 as the deposit runs out).
- Solar buildings: `factor ·= SolarEfficiency` (a global 0…1 dimming set by dust storms; normally 1.0).
- Sum positive `Production[Energy]·factor` into **energyProduction**, negative into **energyConsumption**.

**Pass 2 — energy brownout gating:**
```
available = storedEnergy + energyProduction
brownout  = (energyConsumption > available) ? available / energyConsumption : 1.0
PowerOutage = brownout < 0.999
```
Then for each active building and each `Production[kind]` (except Research):
- energy **consumers** are scaled by `brownout`; producers by 1.0.
- accumulate `net[kind] += perTick · factor · (consumer ? brownout : 1)`.
Finally apply all `net[kind]` to the ledger (clamped). **Effect:** if you don't have enough power, everything that consumes energy runs at reduced output that tick (a brownout), rather than hard-failing.

---

## 9. Colonists and population

**Specialties:** `None, Geologist, Engineer, Botanist, Climatologist`.
- **Geologist** → mine/drill yield (optimal for ice_drill, iron_mine, silicon_mine), surveys hidden deposits.
- **Engineer** → 2× construction/repair speed, optimal for reactors/recyclers/export.
- **Botanist** → food & biosphere, optimal for hydroponics and all biosphere buildings.
- **Climatologist** → optimal for research_lab and all planetary machines.

**Colonist stats:** `Name`, `Specialty`, `Health = 1.0`, `Morale = 1.0`, `Assignment` (the building worked at). Health scales worker efficiency.

**Housing:** `Housing = BaseHousing + Σ HousingCapacity of operational buildings`. `BaseHousing` is set by sponsor (easy 14 / normal 12 / hard 10).

**Population growth** (PopulationSystem, per tick):
- Skip if `crew ≥ Housing` (no room).
- Skip if `Food < 40 · (crew + 1)` (need a food surplus to feed the next settler).
- Else accumulate `+0.0025/tick`; at ≥1.0 spawn one colonist with a random specialty (Geologist/Engineer/Botanist/Climatologist), named `"Settler #n"`. (≈1 new colonist per 400 ticks under good conditions.)

**Life support** (LifeSupportSystem, per tick, per colonist):
- Consume `Oxygen 0.08`, `Water 0.05`, `Food 0.03` × crew (each all-or-nothing).
- `LifeSupportFailing` = crew>0 and any of the three could not be paid.
- Not failing → reset failure counters.
- Failing → increment a counter. **Grace = 200 ticks** before any death. After grace, accumulate deaths at **0.02/tick** (≈1 death per 50 ticks); each death removes the last colonist.
- When crew reaches **0** → colony **Collapsed = true** → game over.

**Health dynamics** (EventSystem):
- Regenerate `+0.0015/tick` toward 1.0 while no solar flare is active.
- During an unprotected solar flare, drain `−0.004/tick` (floored at 0). Health 0 does not itself kill — it just zeroes that colonist's work efficiency.

**Cave shelter:** a permanent flag granted by the CaveDiscovery event; makes the colony "protected" so solar flares can't disable buildings or drain health.

---

## 10. Planet metrics and terraforming

`PlanetState` holds the four victory metrics plus Biomass.

| Metric | Start (Mars) | Target | Clamp range | Display unit |
|---|---|---|---|---|
| Temperature | −60.0 | **0.0** | [−120, 60] | °C |
| Pressure | 0.6 | **10.0** | [0, ∞) | kPa |
| Oxygen (atmos) | 0.1 | **15.0** | [0, 100] | % |
| Water (coverage) | 0.0 | **0.30** | [0, 1] | shown ×100 = % |
| Biomass | 0.0 | — | [0, 1] | vegetation cover (indicator only, not a goal) |

**Per-metric progress** = `clamp((value − start) / (target − start), 0, 1)`.
**Overall progress** = average of the four progresses.

**Applying building planet effects** (per tick, PlanetSystem): for each operational planetary/biosphere building with efficiency `eff>0`, and each `(metric, delta)` in its `PlanetEffects`:
- If `metric == Water`: add `delta·eff` to a **flood accumulator** (comet water floods low tiles — it does not directly set coverage).
- Else: `planet.Add(metric, delta · eff · saturation(metric, value))`.

**Saturation / homeostasis** — effects taper off past the target so metrics stabilize instead of running away:
```
below target: factor = 1.0
above target: factor = clamp(1 − (value − target)/(softCap − target), 0, 1)
softCaps: Temperature 25, Pressure 30, Oxygen 30
```

**Atmosphere leak:** `PressureLeakPerTick = 0.004`. **Unless** an operational `ShieldsAtmosphere` building (the L1 Magnetosphere Station) exists, Pressure loses 0.004/tick (solar wind stripping). Only Pressure decays; Temperature/Oxygen/Water do not passively decay.

**Ice melt → Water tiles:** if `Temperature > −15`, accumulate `(Temp + 15)·0.04` tiles/tick; each whole unit converts one **PolarIce** tile (highest elevation first) into **Water**.
**Flood:** the flood accumulator (from comets) converts low Canyon/Lowland/Crater/Flatland tiles (lowest elevation first) into **Water**.
**Water coverage** is then recomputed as `waterTiles / totalTiles`. So the Water metric is **derived from the map**, driven by temperature (melting) and comets (flooding).

**Biosphere / vegetation** (BiosphereSystem): each tick sum `spread = Σ VegetationSpreadPerTick · eff`. It only grows if `spread>0 AND Temperature ≥ 0 AND WaterCoverage ≥ 0.05`. Accumulate; each whole unit converts a Flatland/Lowland tile (near water first) into **Vegetation**. `Biomass = vegetationTiles / totalTiles`. (Vegetation is a cosmetic/indicator layer; atmospheric oxygen comes from the biosphere buildings' `PlanetEffects[Oxygen]`, not from vegetation tiles.)

---

## 11. Research / technology tree

- Research points flow from `research_lab` buildings (`Research +1.5/tick × efficiency`) into the **currently selected** technology.
- A tech completes when accumulated points ≥ its **Cost**; completing it unlocks its buildings and any techs that list it as a prerequisite.
- **Phase 1 (survival)** tech is assumed known at landing — all the starter buildings are available immediately. Phases 2–4 are researched in-game.

| Id | Name | Phase | Cost | Prerequisites | Unlocks (buildings) |
|---|---|---|---|---|---|
| reclaim | Reclaim | 2 | 150 | — | (enables recycling buildings) |
| rover_networks | Rover Networks | 2 | 200 | — | — |
| silicon_extraction | Silicon Extraction | 2 | 220 | — | silicon_mine |
| heavy_metallurgy | Heavy Metallurgy | 2 | 250 | — | iron_mine |
| nuclear_fission | Nuclear Fission | 2 | 300 | — | fission_reactor |
| mass_driver | Mass Driver Export | 2 | 350 | silicon_extraction | export_terminal |
| greenhouse_gas_production | GHG Factories | 2 | 400 | nuclear_fission | ghg_factory |
| orbital_mirrors | Orbital Mirrors | 3 | 600 | greenhouse_gas_production | orbital_mirror |
| artificial_magnetosphere | Artificial Magnetosphere | 3 | 700 | nuclear_fission | magnetosphere_station |
| comet_redirection | Comet Redirection | 3 | 800 | orbital_mirrors | comet_redirector |
| cyanobacteria | Cyanobacteria & Lichen | 4 | 500 | orbital_mirrors | cyanobacteria_farm |
| gm_trees | GM Cold-Resistant Trees | 4 | 700 | cyanobacteria | gm_forest, fauna_reserve |
| domeless_cities | Domeless Cities | 4 | 1000 | gm_trees, artificial_magnetosphere | domeless_city |

A typical tech path to victory: `nuclear_fission → greenhouse_gas_production → orbital_mirrors → (comet_redirection, cyanobacteria) → gm_trees → …`, plus `artificial_magnetosphere` to stop pressure leaking away.

---

## 12. Market and economy

- **Only income sources:** selling Silicon, and reclaim refunds. There is **no passive income**.
- **Silicon export:** each `export_terminal` (operational, eff>0) sells `min(ExportPerTick·eff, storedSilicon)` per tick at **35 credits/unit**. Runs after production, so silicon mined this tick is sellable immediately.
- **Starting credits:** `100,000 × sponsorMultiplier`.

**Reclaim (recycling a building):**
- Only `Buildable` buildings can be reclaimed (not the landing capsule). Requires the `reclaim` tech to be available in-game.
- Refund fraction decays with age: `fraction = clamp(0.90 − 0.02·sols, 0.20, 0.90)` where `sols = (now − createdTick)/144`. Starts at **90%**, −2%/Sol, floor **20%**.
- Returns `Credits·fraction` **and** `MaterialsPaid·fraction` (only materials actually invested). Also frees workers and removes the building's storage capacity.

---

## 13. Random events

Enabled in the normal game. Each tick, with probability `EventChancePerTick` (sponsor-tuned; 0.0008 normal), one event fires. Type roll on a uniform [0,1):

| Roll | Event | Effect |
|---|---|---|
| < 0.40 | **Dust Storm** | Global `SolarEfficiency = 0.20` (solar output −80%) for a random **300–600** ticks. One at a time. |
| < 0.70 | **Life-Support Failure** | Disables a random operational **LifeSupport** building for **300** ticks (Engineer repairs 2×). |
| < 0.92 | **Solar Flare** | For **150–300** ticks. If **unprotected**: immediately disables one random non-Power building (300 ticks) and drains every colonist **−0.004 health/tick**; health regen is suppressed while active. |
| else (8%) | **Cave Discovery** (good) | Permanently sets the cave-shelter flag → colony becomes "protected" (immune to flare disabling & health drain). |

"Protected" = has cave shelter **or** an operational `ShieldsAtmosphere` building. Disabled buildings repair at 1/tick (2/tick with an Engineer). An event-notification ring buffer (max 6) feeds the UI.

---

## 14. Sponsors / difficulty

Chosen at new-game. Scales starting resources, housing, and event severity.

| Sponsor | Difficulty | Start ×mult | BaseHousing | EventChance/tick | DustStorm solar factor | DustStorm ticks | SolarFlare ticks | Repair ticks |
|---|---|---|---|---|---|---|---|---|
| Tech Billionaire | Easy | 2.0 | 14 | 0.0004 | 0.40 | 200–400 | 120–240 | 200 |
| United Space Agency | Normal | 1.0 | 12 | 0.0008 | 0.20 | 300–600 | 150–300 | 300 |
| Private Crowdfunding | Hard | 0.5 | 10 | 0.0016 | 0.15 | 500–1000 | 200–400 | 500 |

---

## 15. Starting colony (new game setup)

1. **Landing spot:** the Flatland tile nearest the map center `(Width/2, Height/2)`.
2. **Landing Capsule** placed there (operational; the storage hub).
3. **Ring buildings** placed on a spiral outward from the capsule (first free buildable, unoccupied hex each), all operational: `solar_panel, solar_panel, o2_recycler, hydroponics, research_lab`.
4. **Mines** on the nearest matching deposit: `ice_drill` (nearest Ice), `regolith_printer` (nearest Regolith).
5. **Starting resources** (× sponsor multiplier): Energy 2500, Water 800, Oxygen 800, Food 600, Materials 300, Credits 100,000.
6. **Starting crew (4):** Ada Reyes (Engineer)→o2_recycler, Boris Kane (Geologist)→ice_drill, Chen Liu (Botanist)→hydroponics, Dara Okafor (Climatologist)→research_lab.

---

## 16. Player interaction, HUD and controls

### 16.1 Screen layout (reference implementation)

- **Top-center resource bar:** icon + amount for Energy, Water, Oxygen, Food, Materials, Silicon, Credits, and Crew count. A resource's number turns **red** when its net rate is negative (falling). Hover shows `amount / capacity  ±rate/tick`.
- **Below the bar, top-center — terraforming goals:** four chips (Temperature, Pressure, Oxygen, Water) each showing **% progress**, plus an overall Terraforming % and a Biomass %. Hover shows current value vs target.
- **Bottom-left — colony status lines:** warnings such as power outage, life-support failing, cave shelter active, low crew health, buildings idle/unstaffed.
- **Bottom-right — research indicator:** current tech + % progress (grey if none selected).
- **Bottom bar — toolbar** of icon buttons: Buildings, Research, Speed, Save, Mute, **Center (crosshair)**, [Reclaim once unlocked], Menu, Help.
- **Right side — building info panel** (opens on right-clicking a building): name, category, state/build %, production, deposit remaining, workers + `[−]`/`[+]` crew buttons. This panel is draggable and remembers its position.
- **Popups** (open above their toolbar button): Build menu (palette of unlocked buildings), Speed menu (pause/×1/×2/×4), Research menu (available techs).
- **Modal dialogs** for confirmations (e.g. reclaim, delete save) and a **Help/how-to-play** overlay.

### 16.2 Controls (reference)

| Input | Action |
|---|---|
| Left-drag / WASD / arrows | Pan camera |
| Mouse wheel | Zoom |
| Right-click a hex/building | Select it (opens building panel) |
| Right-click (with a menu/mode open) | Cancel it |
| **B** | Toggle build menu |
| **T** | Toggle research menu |
| **Space / 1 / 2 / 3** | Pause / ×1 / ×2 / ×4 speed |
| **+ / −** | Assign / remove crew on selected building |
| **H** | Center camera on the landing module |
| **U** | Mute/unmute |
| **F5 / F9** | Save / load |
| **Esc** | Back / main menu / close overlay |
| Left-click | UI buttons; place building (in build mode); pick reclaim target (in reclaim mode) |

### 16.3 Save/load

State is serialized to JSON next to the executable (map, planet metrics, tick count, colony resources, buildings with progress/workers, colonists, tech, active events). The port only needs an equivalent binary snapshot to disk (§20.6).

---

# PART B — AMSTRAD CPC 6128 / Z80 PORT GUIDE

This part explains how to render Part A onto a 1984 8-bit machine. Treat Part A as the **rules ground truth**; this part is **implementation guidance** you may adapt.

## 17. Target hardware summary (Amstrad CPC 6128)

- **CPU:** Zilog Z80 @ 4 MHz (with ~1/3 lost to video DMA / wait states; effective ≈ 3.3 MHz). **No FPU, no multiply/divide instructions** — you write those yourself.
- **RAM:** 128 KB = 64 KB main + 64 KB expansion, paged as 16 KB banks into `&4000–&7FFF` via the Gate Array (`OUT &7Fxx`, RAM-config values `&C4…&CF`). Screen is 16 KB.
- **Video (CRTC + Gate Array):**
  - Mode 0: **160×200, 16 colours** (chunky).
  - Mode 1: **320×200, 4 colours**.
  - Mode 2: 640×200, 2 colours.
  - Palette: **27 possible colours** (RGB each off/half/full); up to 16 "inks" on screen (Mode 0).
  - Screen RAM `&C000–&FFFF`, **non-linear**: `addr = &C000 + (y>>3)*&50 + (y&7)*&800 + (x_byte)`.
  - Mode 0 byte packs **2 pixels**, bit-interleaved: pixelA = bits (7,3,5,1), pixelB = bits (6,2,4,0).
- **Sound:** AY-3-8912, 3 square-wave channels + noise.
- **Input:** keyboard + one/two joysticks. **No standard mouse** (the AMX mouse is rare — don't rely on it).
- **Storage:** 3″ floppy via AMSDOS (≈178 KB/side). Save/load with firmware cassette/disc calls.
- **Timing:** 50 Hz frame (`VSYNC`) interrupt is the natural clock.
- **Toolchain:** cross-assemble with RASM / pasmo / WinAPE; test in WinAPE or Caprice/CPCEC.

## 18. Recommended scope

The full game (2816-tile map, 21 buildings, Perlin generation, doubles) is large. Choose a tier:

- **MVP (recommended first target):** map **~32×24** shown through a scrolling viewport; the full building set but simplified art; events on; integer economy (§20); keyboard control; one pre-baked or simply-generated map; save/load one slot. This is a complete, winnable game.
- **Full port:** 64×44 map, hidden-deposit surveying, all sponsors, multiple save slots, nicer art/animation.

Everything below works for either tier; where it matters, MVP-friendly choices are marked.

## 19. Graphics & map rendering

### 19.1 Screen mode choice
- **Recommended: Mode 0 (160×200, 16 colours)** for the map — colour distinguishes terrain and resources at a glance, and chunky pixels suit tile art. Reserve a few inks for UI text.
- Alternative: **Mode 1 (320×200, 4 colours)** if you prefer crisp text and can encode terrain with patterns instead of colour. A Mode-1 UI with a Mode-0 map region is possible via CRTC split but is advanced — keep one mode for the MVP.

### 19.2 Draw hexes as pre-shifted tiles, not math
Do **not** compute `√3` pixel positions on a Z80. Instead:
- Use a **fixed tile stamp** per hex and an **odd-r brick offset**: even rows at `x = col·W`, odd rows at `x = col·W + W/2`, rows at `y = row·(H − overlap)`. This "brick" layout reads as hexes without per-pixel geometry.
- Suggested Mode-0 tile: **8×8 or 8×6 pixels** (4–8 bytes wide). Pre-draw every terrain/building/deposit tile as a byte blob; rendering = copy bytes to screen (fast `LDI` runs).
- The **viewport** shows only the tiles that fit (e.g. ~18×24 Mode-0 tiles). Keep a `camX,camY` tile offset; clamp to map bounds.

### 19.3 Scrolling
- **MVP:** redraw the visible tile window when the camera moves (dirty-rectangle: only redraw newly exposed rows/cols). Simple and fast enough at tile granularity.
- **Advanced:** CRTC hardware scroll (adjust start address / R2,R3,R7 tricks) for smooth pixel scroll — optional.

### 19.4 Map generation on Z80
Perlin fractal noise is too heavy. Options, cheapest first:
1. **Ship pre-baked maps** as data (a byte array of terrain+deposit per tile) — deterministic, zero CPU. Bundle 3–5 maps.
2. **Cheap value-noise / diamond-square** at load time using a seeded LCG, then apply the §3.1 quantile classification (sort a sampled subset for thresholds, or use fixed cutoffs). Good enough visually.
Either way, keep the **latitude→PolarIce** rule and the **deposit chances** from §5 so gameplay balance holds.

### 19.5 Tile data in memory
Per tile, ~2–3 bytes:
- byte 0: `terrain` (0–8, low nibble) + `depositType` (0–4, high nibble).
- byte 1–2: `remainingDeposit` (0–1599 → 16-bit, or scale to a byte with a shift if you accept coarser deposits).
- Building presence: keep a **separate building array** (each entry stores its tile index) rather than a per-tile pointer, to save map RAM. A tile→building lookup can be a small hash or linear scan (building count is low, dozens).
- A 32×24 map = 768 tiles × 3 B ≈ 2.3 KB; 64×44 = 2816 × 3 ≈ 8.5 KB. Both fit easily; put the map in a paged bank if convenient.

## 20. The economy in integer / fixed-point math

The reference uses `double`. On Z80 use **fixed-point integers**. Recommended scheme:

### 20.1 Number formats
- **Planet metrics & colony resources (accumulating quantities): signed 16.16 fixed-point in 32 bits** (`value_fixed = round(realValue × 65536)`).
  - This gives ~0.0000153 resolution — the tiny deltas (0.0015, 0.004) stay exact enough and accumulate without drift.
  - Ranges fit 32-bit signed (e.g. Temperature −60 → −3,932,160; capacity like Energy 8000 → 524,288,000, still < 2.1e9).
- **Credits: plain signed 32-bit integer** (drop fractions). Range to 100,000+ needs 17+ bits; 32-bit is safe. Round per-tick credit gains to whole credits.
- **Worker efficiency: 8.8 fixed in 16 bits** (`1.0 = 256`, `1.5 = 384`, `0 = 0`).
- **SolarEfficiency / brownout / saturation factors: 8.8 fixed** (`1.0 = 256`).

### 20.2 Core fixed-point ops you must implement
- `add32/sub32` (32-bit) — trivial.
- `mul_16_16_to_32` (unsigned 16×16→32) — standard shift-add; you'll need signed handling.
- **Scale a 16.16 value by an 8.8 factor:** `result = (value × factor) >> 8`, keeping 16.16. (value is 32-bit, factor 16-bit → 48-bit intermediate; in practice the operands are small — clamp/΄use 32-bit intermediate with care, or scale factor into the delta before widening.)
- `div` only where unavoidable (progress %, brownout ratio, reclaim fraction). Prefer precomputed reciprocals or repeated subtraction; these run at most once/tick or on UI events, so speed is not critical.

### 20.3 Applying a per-tick delta with efficiency
Most systems do `metric += delta × efficiency × factor`. Precompute per building each tick:
```
effDelta = (delta_16.16 × eff_8.8) >> 8          ; still 16.16
metric  += (effDelta × saturation_8.8) >> 8      ; planet metrics only
```
Accumulate all buildings' contributions into a per-resource/metric net (32-bit), then apply once and clamp. This matches the reference's "net then clamp" behavior.

### 20.4 Clamping
After each apply: clamp resources to `[0, capacity]`; clamp planet metrics to their ranges (Temp [−120,60], Pressure ≥0, Oxygen [0,100], Water [0,1]). All in 16.16.

### 20.5 Constant conversion
Convert every real constant in the Appendix to your fixed format at build time (assembler `EQU`s). Example: `GHG_TEMP EQU 0.020×65536 = 1311`; `LEAK EQU 0.004×65536 = 262`; `HEALTH_REGEN EQU 0.0015×65536 = 98`.

### 20.6 Timing on 50 Hz
The reference runs 4 ticks/s at ×1. Drive ticks from the **50 Hz frame interrupt**:
- Keep a frame counter. **Normal = 1 sim tick every 12–13 frames** (≈4/s). Fast = every 6, Ultra = every 3, Paused = never. (Or decouple entirely and let the player hold a key to advance.)
- Do at most **one sim tick per frame**; never batch many (the Z80 can't). If you want faster "game speed," reduce frames-per-tick, not ticks-per-frame.
- Budget: a full tick iterates buildings (dozens) and colonists (tens) with a few 32-bit adds each — comfortably < one frame at tile counts in the MVP range. Rendering is the cost; only redraw changed tiles/HUD numbers.

### 20.7 Save/load
Serialize a compact binary snapshot to disc via AMSDOS: map bytes, planet metrics (4×4 B + biomass), tick count (4 B), resources (7×4 B + credits), tech bitmask, building array (id, tile, state, progress, workers, materialsPaid, createdTick), colonist array (name-index, specialty, health, assignment), active events. A few KB total. Firmware: open/read/write via `CAS`/`BIOS` disc entries (AMSDOS `&BC77`-area vectors) or a small AMSDOS wrapper.

## 21. Controls remap (no mouse)

Map the reference's mouse UI to keyboard + joystick:

| Reference | CPC suggestion |
|---|---|
| Camera pan | Cursor keys / joystick (moves a **tile cursor**; camera follows at edges) |
| Right-click select | **COPY** or **ENTER** on the cursor tile (select building/tile) |
| Build menu (B) | **B** → list; cursor to choose, ENTER to enter place-mode, then move cursor + ENTER to place; ESC cancels |
| Research menu (T) | **T** → list of available techs; ENTER selects target |
| Speed (Space/1/2/3) | **Space** pause toggle; **1/2/3** = ×1/×2/×4 |
| Crew +/− | **+ / −** (or `[`/`]`) on selected building |
| Center on base | **H** |
| Reclaim | **R** (enter reclaim-mode; ENTER on target; confirm) |
| Save/Load | **F5-equivalent** — map to e.g. **S / L** with confirm dialogs |
| Help / Menu | **F1** help, **ESC** menu/back |

A blinking tile **cursor sprite** replaces the mouse pointer. The draggable building panel becomes a fixed side/bottom panel (simpler on a fixed layout).

## 22. Suggested build milestones

1. **Boot + Mode 0 + tile renderer:** show a static pre-baked map through a scrolling viewport with a cursor.
2. **Data tables:** encode the building catalog, tech tree, sponsors, and constants (Appendix) as assembler data.
3. **Tick engine (integer):** ledger, production + brownout, construction, storage caps — no UI yet; verify resource numbers advance like the spec.
4. **HUD:** resource bar, goal %s, status lines, research indicator; redraw only changed numbers.
5. **Placement + crew:** build menu, placement validation (§7), worker assignment & efficiency.
6. **Planet systems:** planet effects, saturation, pressure leak, ice-melt/flood → water coverage, biosphere; wire up win check.
7. **Population + life support:** growth, consumption, grace/death, lose check.
8. **Events + sponsors.**
9. **Save/load, sound (AY blips), menus, help.**
10. **Polish:** hardware scroll, animation, extra maps, difficulty tuning.

Verify each milestone against the Appendix numbers — the balance depends on them.

---

# APPENDIX — Master constant reference

**Time:** 4 ticks/s @×1; 10 in-game min/tick; 144 ticks/Sol; speeds 0/1/2/4×; ≤32 ticks/frame.

**Planet start → target (clamp) [softcap]:**
- Temperature −60.0 → 0.0 (−120..60) [sat softcap 25]
- Pressure 0.6 → 10.0 (≥0) [softcap 30]
- Oxygen(atmos) 0.1 → 15.0 (0..100) [softcap 30]
- Water coverage 0.0 → 0.30 (0..1)
- Pressure leak (if unshielded): −0.004/tick
- Ice melt: starts at Temp > −15; rate (Temp+15)·0.04 tiles/tick
- Progress = clamp((v−start)/(target−start),0,1); Overall = mean of 4; **Win = all four ≥ 1**.

**Life support (per colonist per tick):** O₂ 0.08, Water 0.05, Food 0.03. Grace 200 ticks; death 0.02/tick after grace; **Lose = crew reaches 0**.

**Population:** growth 0.0025/tick; needs `Food ≥ 40·(crew+1)` and `crew < Housing`. Housing = BaseHousing(14/12/10) + Σ HousingCapacity.

**Health:** regen +0.0015/tick (no flare); flare drain −0.004/tick (unprotected).

**Worker efficiency:** 0…1.5 = staffing·(specialist?1.5:1)·avgHealth; automatic building = 1.0; empty manned = 0.

**Construction:** materials/tick = totalMat/BuildTimeTicks·speed; speed 2 (Engineer) else 1; stalls if unaffordable.

**Market:** Silicon 35 credits/unit; export = min(ExportPerTick·eff, stock). No passive income.

**Reclaim:** fraction = clamp(0.90 − 0.02·sols, 0.20, 0.90); refunds Credits·f and MaterialsPaid·f.

**Events (normal):** chance 0.0008/tick; roll <0.40 DustStorm(solar 0.20, 300–600t) / <0.70 LifeSupportFailure(disable LS 300t) / <0.92 SolarFlare(150–300t, disable non-Power 300t, health −0.004/t) / else CaveDiscovery(permanent shelter). Repair 1/tick (2 w/ Engineer).

**Deposits:** PolarIce Ice 800–1599; Crater/Canyon Ice 33% 200–599; Mtn/Highland Iron (vein>0.55 &40%) 300–899 or Silicon 30% 200–699; Flat/Lowland Regolith 30% 150–499 or Ice 18% 100–399; non-ice hidden 35%.

**Starting resources (×sponsor mult 2.0/1.0/0.5):** Energy 2500, Water 800, Oxygen 800, Food 600, Materials 300, Credits 100000. Crew 4 (Engineer/Geologist/Botanist/Climatologist).

**Building & tech tables:** see §7.2 and §11 (all costs, production, planet effects, build times, tech costs/prerequisites).

*End of specification.*

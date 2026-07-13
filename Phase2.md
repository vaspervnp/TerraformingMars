# Terraforming Mars — Phase 2: The Living Planet (Game Design Specification)

This document serves as a comprehensive Game Design Specification (GDS) for implementing Phase 2. It is structured for direct ingestion by an AI programming assistant to generate code, data schemas, and state-machine logic.

---

## System 1: Advanced Colonist Recruitment & Population Scaling

Phase 2 shifts from basic colony survival to complex macro-societal management. The player transition involves moving away from passive colonist arrivals to an active, targeted Orbital Recruitment Pipeline.

### 1. Recruitment Mechanics
*   **The Request Interface:** A dedicated UI panel allowing players to spend Credits to order targeted transport shuttles from Earth[cite: 1].
*   **Cooldown & Capacity:** Shuttles have fixed travel countdown timers and a maximum capacity. High-tier specialties cost significantly more Credits and require higher baseline colony happiness[cite: 1].

### 2. New & Expanded Character Specialties
In addition to Geologists, Engineers, Botanists, and Climatologists[cite: 1], Phase 2 introduces:

| Specialty | Resource/System Impact | Specialized Building Assignment |
| :--- | :--- | :--- |
| **Theoretical Physicist** | +25% Research Point generation[cite: 1]; unlocks quantum/antimatter tech trees. | Quantum Research Matrix, Singularity Core |
| **Medical Doctor** | -15% Colony Health decay rate[cite: 1]; completely mitigates "Martian Plague" progression speed. | Planetary Hospital, Cloning Center |
| **Industrial Worker** | +10% Construction speed; +15% output to heavy factories (Materials/Silicon)[cite: 1]. | Deep Core Drill, Automated Assembler |
| **Ecologist** | Increases biome stability; prevents biomass decay from invasive species. | Wildlife Sanctuary, Genetic Vault[cite: 1] |
| **Diplomat / Politician** | +10% Faction Approval; lowers planetary unrest; reduces Earth resource tribute taxes. | Senate Dome, Embassy Terminal |

### 3. Population Triggers & Scaling Industry
As the total population increases, the game unlocks specialized, space-intensive buildings. If these infrastructure thresholds are unmet, a Systemic Stagnation penalty occurs (dropping colony health and production efficiency[cite: 1]).

*   **Threshold 1: 10,000 Colonists (The Urbanization Era)**
    *   *Unlocks:* **High-Density Residential Arcology** (Replaces standard housing with vertical space-saving structures[cite: 1]).
    *   *Demand:* Requires a permanent 2x increase in Food and Water loops[cite: 1].
*   **Threshold 2: 50,000 Colonists (The Industrial Shift)**
    *   *Unlocks:* **Interplanetary Stock Exchange** (Passively converts excess Silicon and Materials into continuous Credits[cite: 1]).
    *   *Demand:* Massive baseline Energy grid strain (+100 MW drain)[cite: 1].
*   **Threshold 3: 250,000 Colonists (The Autonomous Sovereignty)**
    *   *Unlocks:* **Planetary Senate Dome** (Enables independent law-making and independent currency minting).
    *   *Demand:* Triggers the political faction mechanic and Earth-tension escalation.

---

## System 2: Phase 2 Implementation Roadmap (The 3 Sub-Phases)

Phase 2 initiates the moment all four baseline terraforming tracks (Temperature, Pressure, Oxygen, Water) hit 100%[cite: 1]. The gameplay state transitions from an Extinction Survival Loop to a Civilization Simulation Loop.

### Phase 2A: The Stabilizing Cradle (Focus: Society & Equilibrium)
The atmosphere is breathable[cite: 1], but highly volatile. The goal here is stabilizing the planet while handling the social friction of the first massive wave of human migration.

#### 🛠️ Tech Tree & Structure Architecture
*   **Tech: Atmosphere Sink Arrays**
    *   *Structure:* **Cryo-Carbon Capturer** (Sucks excess CO2 out of the air to freeze it into dry ice blocks, reversing runaway greenhouse effects).
*   **Tech: Macro-Epidemiology**
    *   *Structure:* **Planetary Isolation Hospital** (High-tech medical centers staffed by Doctors to neutralize mutated alien pathogens).
*   **Tech: Socio-Political Synthesis**
    *   *Structure:* **District Town Halls** (Allows the player to set tax rates and regional policies to balance Faction demands).

#### 📜 Detailed Mechanics
*   **Runaway Terraforming:** If greenhouse gas factories or orbital mirrors aren't disabled or modified[cite: 1], the global temperature and pressure keep rising past 100%, causing oceans to vaporize[cite: 1]. Players must build *Cryo-Carbon Capturers* to actively maintain the sweet spot.
*   **The Great Migration:** A massive wave of automated Earth shuttles lands without warning. This strains housing and food systems[cite: 1]. Players must quickly transition to *High-Density Residential Arcologies*.
*   **Faction Politics:** Crew choices mutate into political blocks[cite: 1]. Industrial Workers want more mining[cite: 1], Ecologists want zero pollution. Low approval ratings from any block trigger strikes in corresponding buildings (e.g., miners stop producing Iron/Silicon[cite: 1]).
*   **The Martian Plague:** A bio-hazard event that triggers when surface liquid water hits 100%[cite: 1]. It reduces the active workforce efficiency by 2% every cycle unless staffed with *Doctors*.
*   **Pollution Management:** Heavy industry produces localized pollution on hexes[cite: 1]. If pollution counts get too high, adjacent biomass tiles wither and die[cite: 1], forcing players to clean up or move their heavy factories.

---

### Phase 2B: The Infrastructure Boom (Focus: Global Automation & Macro-Economics)
With the society stabilized, the colony expands across the entire map, automating basic labor to build a powerhouse economy.

#### 🛠️ Tech Tree & Structure Architecture
*   **Tech: Maglev Vacuum Propulsion**
    *   *Structure:* **Hyperloop Terminal** (Built on hex edges; allows instantaneous movement of materials and workers between distant nodes[cite: 1]).
*   **Tech: Core-Mantle Penetration**
    *   *Structure:* **Deep Core Plasma Drill** (Requires Industrial Workers; extracts vast, infinite nodes of Iron and Silicon directly from the mantle[cite: 1]).
*   **Tech: Automated Labor Swarms**
    *   *Structure:* **AI Drone Hive** (Deploys autonomous drones to completely replace human Worker requirements in heavy industrial hexes[cite: 1]).

#### 📜 Detailed Mechanics
*   **Ecosystem Balancing:** Introducing complex animals. Players must maintain a precise ratio of herbivores to carnivores in *Wildlife Sanctuaries*[cite: 1]. Imbalances destroy local plant biomes, reducing planetary oxygen output[cite: 1].
*   **Extreme Weather:** Thick air and oceans generate super-storms[cite: 1]. Hurricanes wipe out solar arrays and flood low-lying hex cities[cite: 1], forcing players to construct flood gates and hard-shelled structural shielding.
*   **Invasive Earth Species:** Cargo imports introduce Earth pests. They consume 10% of local crop and biomass yields per cycle until the player researches targeted *Bio-Weapons* or deploys *Ecologists*.
*   **Hyperloop Network:** Connects disparate mining outposts[cite: 1]. If a node is broken by extreme weather, the linked bases instantly suffer an energy/resource blackout until fixed[cite: 1].
*   **Advanced Automation:** Deploying *AI Drone Hives* frees up the human population. Human workers become "Unemployed Civilians" who require high-tier entertainment buildings unless retrained as Doctors or Physicists.
*   **Deep Core Extraction:** Mining infinite resource veins deep down creates seismic instability[cite: 1]. This triggers localized marsquakes that crack nearby buildings.
*   **The Silicon Monopoly:** The player stops selling raw Silicon to Earth and builds high-tech processing loops[cite: 1]. Turning Silicon into *Quantum Processors* multiplies trade income tenfold[cite: 1].

---

### Phase 2C: The System Sovereign (Focus: Mega-Engineering & Independence)
Mars becomes the crown jewel of the solar system. The player builds planetary-scale mega-structures, claims political independence from Earth, and reaches for deep space.

#### 🛠️ Tech Tree & Structure Architecture
*   **Tech: Carbon-Nanotube Weaving**
    *   *Structure:* **The Space Elevator Base** (A massive multi-hex project requiring 50,000 Materials, 20,000 Silicon, and 100,000 Credits to construct over multiple phases[cite: 1]).
*   **Tech: Orbital Tethering & Grappling**
    *   *Structure:* **Asteroid Catchment Rig** (Moored to Phobos/Deimos; dynamically pulls asteroids into stable orbit for automated strip-mining).
*   **Tech: Quantum Climate Synchronization**
    *   *Structure:* **Weather Control Grid Tower** (Staffed by Theoretical Physicists; permanently neutralizes all negative weather events in a 5-hex radius).

#### 📜 Detailed Mechanics
*   **The Space Elevator:** Once fully built, it removes all shuttle travel cooldowns and cuts import/export costs to zero, allowing instant planetary trading[cite: 1].
*   **Martian Independence:** Earth's sponsors demand an exponential increase in resource quotas[cite: 1]. The player can comply (losing resources/Credits) or declare independence[cite: 1]. Declaring independence requires a *Senate Dome* and triggers an economic blockade from Earth.
*   **Asteroid Wrangling:** Players redirect rare-metal asteroids directly into orbit. Missed calibration values (calculated by Theoretical Physicists) run a small risk of kinetic impact catastrophes on the surface.
*   **Moons of Mars Expansion:** Unlocks a secondary strategic map layer for Phobos and Deimos. Players build zero-G industrial processing complexes to ship materials down to the surface via the Space Elevator.
*   **Interplanetary Tourism:** Transforming scenic locations (like Valles Marineris oceans) into resort complexes[cite: 1]. This generates massive, continuous passive Credit streams based on the planet's overall safety and beauty ratings[cite: 1].
*   **Weather Control Grid:** Towers use orbital mirrors to flash-fry storm fronts before they land[cite: 1]. This turns unpredictable weather manipulation into an active energy-management minigame.
*   **Cultural Wonders:** Buildings like the *Olympus Academy of Sciences* grant a permanent +50% boost to research across the board, finalizing Mars' transition into the intellectual and cultural capital of human civilization[cite: 1].

---

## Technical Pseudocode Spec for AI Coder: State & Condition Transitions

```csharp
// State Management for Phase Transition
public class GameManager : MonoBehaviour {
    public float temperature, pressure, oxygen, water; // Scaled 0 to 100
    public bool isPhaseTwoActive = false;

    void Update() {
        if (!isPhaseTwoActive && temperature >= 100f && pressure >= 100f && oxygen >= 100f && water >= 100f) {
            TriggerPhaseTwo();
        }
        
        if (isPhaseTwoActive) {
            RunPhaseTwoSimulation();
        }
    }

    void TriggerPhaseTwo() {
        isPhaseTwoActive = true;
        UIController.Instance.ShowNotification("TERRAFORMING COMPLETE: Welcome to Phase 2: The Living Planet.");
        UnlockTechTree(TechTreeTier.Phase2A);
        PopulationManager.Instance.EnableAdvancedRecruitment();
    }

    void RunPhaseTwoSimulation() {
        // Runaway Terraforming Check
        if (temperature > 105f || pressure > 105f) {
            GlobalEcosystem.Instance.ApplyGreenhouseDamage();
        }
        // Random Event Selector Pool includes Plagues, Migrations, and Hurricanes
        EventSystem.Instance.SetEventPool(EventPoolType.Phase2Global);
    }
}
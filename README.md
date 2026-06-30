# Terraforming Mars

Open-source base-building / simulation game σε **C# / .NET 9**, με procedural hex χάρτη και real-time σιμουλασιόν. Στόχος: εξόρυξη πόρων, υποδομές, επιβίωση πληρώματος και σταδιακή terraforming (θερμοκρασία, ατμόσφαιρα/O₂, νερό).

## Stack
- **.NET 9**, καθαρό domain layer ανεξάρτητο από engine
- **MonoGame (DesktopGL)** για rendering/input
- Hex grid (pointy-top, axial/cube coordinates), procedural generation με Perlin noise (fBm + quantile classification)
- Real-time simulation με fixed-timestep (pause / ×1 / ×2 / ×4)

## Δομή Solution
```
src/TerraformingMars.Core   — domain & simulation (engine-agnostic)
    Grid/        Hex, OffsetCoord, HexLayout
    Map/         TerrainType, ResourceType, ResourceDeposit, HexTile, HexMap
    Generation/  INoiseSource, PerlinNoise, MapGenerator, MapGenerationSettings
    Simulation/  GameClock, ResourceLedger, World, Colony, systems
src/TerraformingMars.Game   — MonoGame viewer (Camera2D, HexMapRenderer, MarsGame)
tests/TerraformingMars.Core.Tests — xUnit
```

## Build & Run
```bash
dotnet test                                    # unit tests
dotnet run --project src/TerraformingMars.Game # ο viewer
```

### Controls (viewer)
| Πλήκτρο | Δράση |
|---|---|
| drag (αριστερό/μεσαίο) | pan |
| ροδέλα | zoom (γύρω από κέρσορα) |
| WASD / βελάκια | κίνηση κάμερας |
| F | fit χάρτη στην οθόνη |
| N | νέος τυχαίος χάρτης |
| δεξί κλικ | επιλογή hex |
| Space | pause/resume σιμουλασιόν |
| 1 / 2 / 3 | ταχύτητα ×1 / ×2 / ×4 |
| Esc | έξοδος |

Πληροφορίες tile και κατάσταση αποικίας (Sol/ώρα, πόροι, rates) εμφανίζονται στον **τίτλο του παραθύρου** (font-based HUD έρχεται σε επόμενη φάση).

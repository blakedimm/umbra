# Aether — Procedural Dungeon Generator & World Simulator

**Aether** (`aether.umb`) is a self-contained procedural cavern generator and world simulation engine written entirely in pure **Umbra (`v1.0.0`)**.

---

## Core Algorithms

1. **Deterministic 32-bit LCG PRNG (`nextRand`, `randRange`)**:
   - Implements the classic Numerical Recipes Linear Congruential Generator:
     $$X_{n+1} = (1664525 \cdot X_n + 1013904223) \bmod 2^{32}$$
   - Uses logarithmic binary doubling/halving (`mod` and `floor`) to perform exact integer modulo and floor operations in $O(\log N)$ steps on Umbra's 64-bit IEEE 754 numbers.

2. **2D Nested List Matrix (`createNoiseMap`)**:
   - Allocates a $52 \times 20$ (`1040` cells) 2D grid (`grid[y][x]`) enclosed by solid bedrock borders (`#`) and seeded with a `45%` initial rock wall density.

3. **Cellular Automata 4-5 Rule via Pipeline (`|>`)**:
   - Applies 4 consecutive smoothing passes chained with Umbra's functional pipeline operator:
     ```umbra
     let worldMap = createNoiseMap(mapWidth, mapHeight, initialWallPercent)
         |> smoothStep
         |> smoothStep
         |> smoothStep
         |> smoothStep;
     ```
   - **Rule 4-5**: A rock wall cell (`1`) survives if at least `4` of its 8 Moore neighbors are walls; an open floor cell (`0`) solidifies into a wall if at least `5` of its 8 neighbors are walls.

4. **Procedural Entity Spawning**:
   - Places the Player (`@`) in the upper-left cavern sector and the Exit Portal (`>`) in the lower-right cavern sector.
   - Procedurally populates open cavern floors with Treasure Chests (`$`) and Hostile Mobs (`M`).

---

## Map Legend

| Symbol | Tile Code | Description |
| :---: | :---: | :--- |
| `#` | `1` | Solid cavern rock wall / bedrock border |
| `.` | `0` | Open traversable cavern floor |
| `@` | `2` | Player spawn point |
| `$` | `3` | Loot / Treasure chest |
| `M` | `4` | Hostile dungeon mob |
| `>` | `5` | Dungeon exit portal |

---

## Running Aether

```powershell
./publish/umbra.exe projects/aether/aether.umb
```

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

See [GameDesign.md](GameDesign.md) for the game concept. In short: a city-builder/roguelike where you manage baby turtles that hatch from a nest on an island, collecting resources to protect their unhatched siblings from ocean-washed trash.

## Tech stack

- Unity **6000.5.2f1** (Unity 6), 2D project using the Universal Render Pipeline (URP 2D Renderer).
- New Input System (`UnityEngine.InputSystem`) — all scripts read `Mouse.current`/`Keyboard.current` directly, not the legacy `Input` class.
- TextMeshPro for UI text.
- Notable packages (see `Packages/manifest.json`): 2D Animation, Aseprite importer, PSD importer, Sprite Shape, Tilemap (+ extras), Timeline, Visual Scripting, Unity Test Framework.

## Working with this repo

- This is a Unity project opened and run through the Unity Editor — there's no CLI build script or package.json here yet.
- Unity's Test Framework package is installed but no tests have been written; when they exist, they run via the Editor's Test Runner (Window > General > Test Runner) or Unity's `-runTests -batchmode` CLI mode.
- `Library/`, `Temp/`, `Logs/`, and `UserSettings/` are Unity-generated caches, not source — never edit or rely on their contents.
- Scenes live in `Assets/Scenes`; render pipeline / input / volume config lives in `Assets/Settings`; all gameplay code is under `Assets/Scripts`.
- Only write/edit `.cs` scripts. Scene, prefab, and Inspector wiring (new GameObjects, assigning serialized fields, adding components in the Editor) must be described as steps for the user to do themselves — don't attempt to edit `.unity`/`.prefab` YAML directly.

## Architecture

### Scene-wide singletons, no dependency injection

Turtles, trash, and upgrade cards are all runtime-instantiated (spawned from prefabs, not placed in the scene), so they can't hold a pre-wired scene reference the way hand-placed objects can. Instead, core systems expose themselves as `public static X Instance` set in `Awake`/`OnEnable` and cleared in `OnDestroy`/`OnDisable`, and everything else reaches them via the static accessor: `ResourceManager.Instance`, `UpgradeManager.Instance`, `PathfindingManager.Instance`, `BuildModeController.Instance`, `TurtleNest.Instance`, `FoodBuilding.Instance`. When adding a new manager-style system that runtime-spawned objects need to reach, follow this same pattern rather than introducing a new wiring mechanism.

### Static flags for cross-system gating

Instead of an event bus, mutually-exclusive "modes" are coordinated by static bools/flags that every interested system polls each frame: `DayStormCycle.IsStorming` (day vs. storm phase — gates harvesting, resource respawn, aggro), `BuildModeController.IsActive`/`IsForced` (Shift-held build mode), `UpgradeSelectionUI.IsActive` (end-of-storm card picker). `TurtleSelectionController` and `CameraController` both stand down while build mode or the upgrade picker is active; `TurtleAgent` reads `IsStorming` directly rather than being told about transitions via a callback.

### Island generation → downstream systems

`IslandGenerator` procedurally builds three tilemaps (Water fills everything, ShallowWater rings the coast, Sand is land-only) from `IslandNoiseMap`'s blob-chain noise field, forces the map center to land, and discards every landmass not connected to it — so there's always exactly one island, however abstract its shape. It fires `IslandGenerated` once tiles are painted and the nest is spawned at the origin; `PathfindingManager` (invalidates its deep-water cache), `DayStormCycle` (starts the first day), and the various scenery/resource spawners all subscribe to this event rather than being called in a fixed order from a bootstrapper.

### Day/storm loop

`DayStormCycle` drives the core game loop: a fixed-length day phase, then a storm where `TrashSpawner.SpawnRound` scatters trash in open deep water using an exponentially-growing "rating budget" (each `TrashDefinition` costs a rating; the budget determines both volume and which trash tiers can afford to appear). The storm ends once `TrashSpawner.AnyTrashAlive()` is false, at which point buildings heal and `UpgradeSelectionUI` offers a card pick before the next day begins — `IsStorming` deliberately stays `true` through that pick so turtles don't resume daytime behavior while the player is still choosing.

### TurtleAgent: standing objective + transient detours

`TurtleAgent` (`Assets/Scripts/Turtle/TurtleAgent.cs`) is the largest and most central script. Its model: a **target resource objective** (a resource *type*, set only by the player via `MoveToResource`) is a standing order nothing else may clear. Every other behavior — `MoveToPoint`, `MoveToBuilding`, a storm-triggered aggro chase against nearby trash, parking at a Watchtower — is a transient detour layered on top; once it ends, the turtle falls back through `Update()`'s own day/night default (seek the objective by day, nest-guard/wander by night) evaluated fresh rather than restored from a snapshot. Read the file's class-level doc comment before modifying turtle behavior — it explains why no interruption needs save/restore logic.

Turtles physically pass through non-interactable buildings but switch onto a dedicated `TurtleInteracting` physics layer while targeting an interactable one (a Rune, a Watchtower), so a "bump to interact" collision can register without turtles generally getting stuck on walls.

### Data-driven extensibility: definitions on prefabs, not new code

New building types and new upgrade cards are meant to be added as prefabs, not new manager code:
- `BuildableDefinition` (name, resource cost, price-scaling-per-placement) attaches to any building prefab; `BuildModeController` reads it generically to show the ghost, check affordability, and instantiate on placement.
- `UpgradeCardDefinition` is an abstract base with an `Apply()` override; concrete cards live under `Assets/Scripts/Upgrades/` (e.g. `CritChanceUpgradeCard`, `JellyfishUpgradeCard`) and are referenced by `UpgradeSelectionUI`'s upgrade pool array. Cards that grant a food item implement `IGrantsFoodItem`, which `UpgradeSelectionUI.Select` checks to force-trigger first-time Food Building placement via `BuildModeController.EnsureFoodBuildingPlaced`.

### Resources: dual routing to Nest vs. Food Building

`ResourceManager.ResourceType` covers both material resources (Wood, Rock) and food resources (Seaweed, Coconut, JellyfishGuts); `ResourceManager.IsFoodType` is the single source of truth for which is which. `TurtleAgent` carries and delivers the two kinds through fully parallel, independent state (separate carry lists, capacities pooled together, separate delivery coroutines/targets) — materials go to `TurtleNest`, food goes to `FoodBuilding` — so a turtle can carry a mix of both at once.

### Pathfinding

`PathfindingManager` wraps `AStarPathfinder` with this project's grid conventions: it always avoids `ResourceNode` obstacles (rebuilt fresh per call — infrequent, cheap) and, only for turtles (`avoidDeepWater: true`), also avoids deep water (cached, since it never changes after generation). Trash ignores deep water since it must cross open ocean to reach shore. Aggro-chasing turtles skip pathfinding via `HasLineOfSight` when a target is directly visible, since trash moves too fast for a stored path to stay accurate.

### Input handling

Mouse/keyboard polling is spread across a few independent `Update()` loops that coordinate via the static flags above rather than a central input router: `CameraController` (drag-to-pan, scroll-to-zoom), `TurtleSelectionController` (click a turtle to select, click elsewhere to order it), `BuildModeController` (Shift to enter build mode, scroll to cycle buildables, click to place). Click-vs-drag disambiguation (a screen-pixel distance threshold plus `CameraController.WasDragging`) is duplicated between `TurtleSelectionController` and `BuildModeController` rather than shared — keep both in sync if that threshold logic changes.

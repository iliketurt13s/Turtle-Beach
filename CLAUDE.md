# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

See [GameDesign.md](GameDesign.md) for the game concept. In short: a city-builder/roguelike where you manage baby turtles that hatch from a nest on an island, collecting resources to protect their unhatched siblings from ocean-washed trash.

## Tech stack

- Unity **6000.5.2f1** (Unity 6), 2D project using the Universal Render Pipeline (URP 2D Renderer).
- Notable packages (see `Packages/manifest.json`): 2D Animation, Aseprite importer, PSD importer, Sprite Shape, Tilemap (+ extras), new Input System, Timeline, Visual Scripting, Unity Test Framework.

## Current state

This is an early-stage project scaffold — there are no C# scripts yet. Content so far is limited to the default sample scene and URP/2D settings assets under `Assets/Settings`. There is no established build/lint/test tooling in the repo; everything is driven through the Unity Editor for now.

- Scenes live in `Assets/Scenes`.
- Render pipeline / input / volume config lives in `Assets/Settings`.

## Working with this repo

- This is a Unity project opened and run through the Unity Editor (version above) — there's no CLI build script or package.json here yet.
- Unity's Test Framework package is installed but no tests have been written; when they exist, they run via the Editor's Test Runner (Window > General > Test Runner) or Unity's `-runTests -batchmode` CLI mode.
- `Library/`, `Temp/`, `Logs/`, and `UserSettings/` are Unity-generated caches, not source — never edit or rely on their contents.

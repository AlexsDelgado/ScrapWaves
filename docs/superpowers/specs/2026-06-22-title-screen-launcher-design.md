# Title Screen Launcher Design

## Goal

Add a dedicated title screen that becomes the first scene in the build and lets the player choose between the three current runtime destinations:

- `Play` -> `SampleScene`
- `Weapon Sandbox` -> `WeaponTestingSandbox`
- `Enemies Testing` -> `enemiesTesting`

The same feature must also provide a consistent way to return to the title screen from gameplay and from both debug scenes.

## User Flow

### Boot Flow

When the game starts, Unity loads a new `TitleScreen` scene first. The scene shows a simple menu with one title label and three buttons. Pressing any button loads its destination scene immediately.

The debug entries are intentionally visible in normal builds for now. There is no hidden developer-only gating in this version.

### Return Flow

From `SampleScene`:

- `Escape` opens the existing pause menu.
- The pause menu offers `Resume` and `Back to Title`.
- The run end screen offers `Retry` and `Back to Title`.

From `WeaponTestingSandbox` and `enemiesTesting`:

- `Escape` opens a minimal pause menu.
- That pause menu offers `Resume` and `Back to Title`.

There is no always-visible return button in debug scenes. `Escape` is the entry point everywhere.

## Architecture

### Shared Scene Navigation

Introduce one small shared scene-navigation helper that owns the scene names and exposes intent-level methods rather than raw scene string usage scattered across UI scripts.

Minimum responsibilities:

- Define the canonical scene names for `TitleScreen`, `SampleScene`, `WeaponTestingSandbox`, and `enemiesTesting`.
- Expose methods such as `LoadTitle()`, `LoadPlay()`, `LoadWeaponSandbox()`, and `LoadEnemiesTesting()`.
- Reset `Time.timeScale` to `1f` before changing scenes so paused scenes do not leak a frozen timescale into the next load.

This helper is the only place that should know the exact destination scene strings.

### Title Screen Scene

Add a new dedicated `TitleScreen` scene and place it first in `ProjectSettings/EditorBuildSettings.asset`. The enabled runtime order should be:

1. `TitleScreen`
2. `SampleScene`
3. `WeaponTestingSandbox`
4. `enemiesTesting`

The title scene stays intentionally light:

- one controller component that builds the UI at runtime
- an `EventSystem`, created automatically if the scene does not already have one
- a basic camera only if the scene setup requires one

The UI should follow the project's existing pattern of constructing simple runtime UI from code instead of relying on a manually authored canvas hierarchy.

### Gameplay Integration

`SampleScene` already uses the gameplay HUD stack, so the title return hooks should extend the existing components rather than adding a second navigation system.

- `PauseMenuUI` gains a `Back to Title` button.
- `RunEndScreenUI` gains a `Back to Title` button alongside the existing retry action.

Both buttons call the shared scene-navigation helper.

### Debug Scene Integration

The debug scenes do not currently share the full gameplay HUD stack, so they should get a small reusable pause overlay component instead of pulling in unrelated gameplay UI.

That component should:

- listen for `Escape`
- toggle a simple overlay
- show `Resume`
- show `Back to Title`
- restore time correctly on close or scene exit

The same component can be attached in both `WeaponTestingSandbox` and `enemiesTesting`.

## Components

### `SceneNavigation`

Static or lightweight shared utility responsible for scene routing and timescale reset.

### `TitleScreenController`

Runtime UI builder for the title scene. It creates:

- a full-screen overlay or panel background
- a title text element
- three buttons with the exact labels `Play`, `Weapon Sandbox`, and `Enemies Testing`

It is intentionally minimal and does not need options, animation, save slots, or nested menus.

### `PauseMenuUI`

Extend the existing gameplay pause menu with one additional button for title return. Existing resume and settings behavior remains unchanged.

### `RunEndScreenUI`

Extend the existing run-end overlay with a second action for title return. `Retry` continues to reload the active scene; `Back to Title` leaves the scene through the shared helper.

### `SimpleScenePauseMenu`

Minimal reusable pause overlay for the two debug scenes only. It should not take dependencies on gameplay-only systems unless they are already optional.

## Data Flow

1. Unity boots into `TitleScreen`.
2. `TitleScreenController` creates the runtime UI and wires button callbacks.
3. A menu button calls the shared scene-navigation helper.
4. The helper resets timescale and loads the requested scene.
5. In gameplay or debug scenes, `Escape` opens the relevant pause UI.
6. `Back to Title` from any pause or run-end UI calls the same shared helper.

## Failure Handling

- If a target scene is missing from build settings, the navigation helper should log a clear error and avoid silent failure.
- If `Escape` is pressed while a pause UI is already open, it should close cleanly and restore the previous timescale.
- If a scene has no `EventSystem`, title and debug pause UI should ensure one exists so buttons remain usable.
- Title return must always restore `Time.timeScale` even if the source scene was paused when the action was triggered.

## Verification

Automated checks should cover:

- build settings register `TitleScreen` as the first enabled scene
- the shared helper resolves the canonical scene names consistently
- title-return actions reset timescale before requesting the scene load

Manual verification should cover:

- game boot opens `TitleScreen` first
- each title button loads the correct destination scene
- `Escape` opens pause in `SampleScene`, `WeaponTestingSandbox`, and `enemiesTesting`
- `Back to Title` works from gameplay pause
- `Back to Title` works from the gameplay run-end screen
- `Back to Title` works from both debug-scene pause overlays
- returning to title after pausing does not leave the next scene frozen

# Balance authoring

- **Weapon source of truth:** manually authored `WeaponData` assets in `Assets/ScriptableObjects/WeaponSO` (and the corresponding sandbox assets). Tune base fields, weapon-specific tuning, `LevelData`, and Path A/B data in the Inspector. Existing `BalanceStats` and `UpgradeSpecificStats` rows remain serialized and are preserved.
- `balance_weapon_stats.csv` is retained as a design reference. It is not imported at runtime, on editor initialization, or by the balance import menu. The obsolete weapon CSV parser/importer has been removed; no CSV numbers are used to regenerate weapon values.
- Startup preserves authored weapon values. Upgrade previews continue using the runtime weapon calculations, including current player stats and heat.
- **Material economy:** `balance_material_usage.csv` supplies ordinary upgrade material roles and totals. Explicit import: Unity menu `ScrapWaves/Balance/Import Material Usage CSV`. It saves only `MaterialUsageBalance.asset` and preserves its eight authored basic, advanced and rejected-offer tinkering costs. Tune those costs in the same asset Inspector; see `docs/tinkering-cost-authoring.md`. Existing prices remain the default for legacy/missing assets. The existing runtime fallback for missing/empty material balance remains; there is no editor auto-import or automatic drop reset.
- **Legacy manual tools:** `Tools/ScrapWaves/Apply Weapon Balance Sheet Snapshot` is a separate hardcoded, destructive snapshot command, not the supported weapon authoring workflow. It still overwrites weapon values if explicitly invoked. `ScrapWaves/Balance/Create Default Drop Configs` likewise replaces authored drop settings. Neither runs automatically; neither was changed in this CSV cleanup.
- **Base = level 1** in both reference files.
- **Variant spawn (resolved in code):** orbital roulette adds `floor(runTime / 120s) * 3` weight to variant entries.
- **Rare drops on base enemies:** 5% rare / 95% common per enemy family.
- **`XX` material role:** second principal (`PrincipalExtra`), used by Blades A for Sheet metal.

## Verification

Run `WeaponBalanceStartupTests`, `EconomyBalanceTests`, `CraftingGameplayPreviewTests`, and `WeaponDiagnosticsTests` in the Unity EditMode Test Runner. The startup fixture enters Play Mode in an empty scene and compares weapon data before/during/after startup without saving production assets. Material-import tests use disposable destinations.

For a manual check, record an authored weapon's base stats, a non-unit level multiplier, both advanced paths, and any balance/upgrade-specific rows. Enter and exit Play Mode, then reload the editor and confirm those values and asset dirty states are unchanged. Check the upgrade preview against the weapon's runtime diagnostics. Review the asset diff before saving; do not use the legacy snapshot command to initialize or validate manually authored balance.

## Concurrent enemy caps (#170)

- Tune `Assets/Data/Balance/SpawnBalanceProfile.asset` in the Inspector: **Max Active Enemies By Cycle**. Element 0 applies from run start through the first Overheat; the index follows `HeatManager.CompletedOverheatCycles`, including the existing interrupted-phase behavior. The last entry repeats. The supplied entry is 300, preserving the previous cap. Empty lists use **Max Active Enemies**, which also remains the ceiling for every entry; all effective limits are at least 1.
- The cap limits concurrent living orbital enemies plus required boss/elite objectives. **In-world ZoneSpawner enemies are excluded and their spawning is independent.** Objective counting preserves their prior contribution; objective admission always bypasses the cap so pending Base-supported batches cannot be blocked by a full population. Total living enemies can therefore exceed the cap.
- This is not a spawn budget or a camera-visibility test. Emergence, temporary untargetability/burrowing and dormancy retain counted occupancy. Death, pool return, root disable/destruction free it. Each successful spawn records its current source; failed placement/prewarming consumes no slot, and pool lifecycle resets prevent a reused zone instance retaining orbital membership (or vice versa).
- Surviving counted enemies remain counted when the cycle changes. A lower cap pauses new orbital spawns without culling. Permanent Overheat keeps using the current cycle value; explicit heat reset selects the first entry, and a fresh run rebuilds the population. A heat reset alone does not reset boss/elite parity or despawn enemies.
- The BalanceTuning Inspector shows **Enemigos para cap** against the effective cycle cap and a separate registered total including zones. Required objective admission, pending/spawning cancellation and the post-Overheat decay pause keep their existing rules.
- Focused checks: `OverheatEnemyCapTests`, `OverheatCycleSpawnScalingTests`, `HeatSpawnScalingTests`, `OverheatDeferredObjectiveTests`, `EnemyPoolLifecycleTests`, `EnemyDormancyTests`, and the enemy-emergence suites.

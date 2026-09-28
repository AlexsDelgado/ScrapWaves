# Herramientas de editor y tests — alpha 23-09-2026

Inventario de lo que hay en `main` para construir assets, validar y probar sin jugar una run entera. Ninguna de estas piezas es autoridad de gameplay: generan prefabs y escenas, fuerzan valores o leen estado. Complementa `06-QA-y-debug.md` con rutas de menú, conteo de tests y el mapa real de teclas.

## Scripts

| Carpeta | Contenido |
|---|---|
| `Assets/Scripts/Editor/` (+ `MainMenu/`) | Builders de animación del jugador, wearables, rear threat, UI de run, escena de juego, título |
| `Assets/Scripts/Balance/Editor/` | `BalanceTuningHubEditor` (inspector en vivo del hub) |
| `Assets/Scripts/Economy/Editor/` | Import de CSV de balance, setup de economía, `BalanceAutoImporter` |
| `Assets/Scripts/Enemy/Editor/` | `DestroyerPrefabMenu` |
| `Assets/Scripts/Level/Editor/` | Compactor, salida, flecha guía, niebla, modelos de la escena |
| `Assets/Scripts/Spawning/Editor/` | Asset de ruleta por defecto |
| `Assets/Scripts/Testing/` + `Editor/` | `BalanceTestingController` y su builder de escena |
| `Assets/Scripts/Weapon/Editor/`, `Weapon/Testing/Editor/` | Builders de game feel por arma, HUD, sandbox, snapshot de balance, inspectores |
| `Assets/Tests/Editor/` | Suite NUnit, 82 archivos |

Hooks automáticos (`[InitializeOnLoad]`): `BalanceAutoImporter` (Economy/Editor/BalanceAutoImporter.cs:5) importa los CSV solo si falta `MaterialUsageBalance.asset` (:25); `PlayerAnimationValidation` (Editor/PlayerAnimationValidation.cs:9) y `RearThreatValidation` (Editor/RearThreatValidation.cs:15) solo re-registran el callback del Test Runner si quedó una corrida pendiente en `SessionState`.

Inspectores custom: `BalanceTuningHubEditor`, `DebugMonitorEditor`, `WeaponDataEditor`, `WeaponPresentationProfileEditor`. `ReadmeEditor` es el de la plantilla de Unity (`TutorialInfo`).

## Menús de editor

54 `[MenuItem]` en `Assets`, todos del proyecto. No hay menús de terceros (`TextMesh Pro` y `TutorialInfo` no aportan). Conviven cuatro raíces: `ScrapWaves/`, `Tools/ScrapWaves/`, `Tools/Scrap Waves/` (con espacio) y `Tools/Scenes/`.

| Ruta | Archivo:línea | Qué hace |
|---|---|---|
| ScrapWaves/Balance/Import All CSV | Economy/Editor/BalanceImportMenu.cs:13 | CSV de `Assets/Data/Balance` → `MaterialUsageBalance` + `WeaponSO` |
| ScrapWaves/Balance/Create Default Drop Configs | BalanceImportMenu.cs:30 | Crea configs de drop por defecto |
| ScrapWaves/Economy/Apply Drop Art Updates (CellBattery + PlasticV2) | Economy/Editor/DropAssetsUpdateMenu.cs:10 | Cambia modelos de dos pickups |
| ScrapWaves/Economy/Add Economy To Player Prefab | Economy/Editor/EconomySceneSetupMenu.cs:7 | Agrega componentes de economía a `player.prefab` |
| ScrapWaves/Economy/Create Crafting Station In Scene | EconomySceneSetupMenu.cs:60 | Estación de crafteo en la escena abierta |
| ScrapWaves/Economy/Wire Material Drop Art Models | EconomySceneSetupMenu.cs:74 | Llena `MaterialDropVisualCatalog` |
| ScrapWaves/Economy/Create Material Orb Prefab | EconomySceneSetupMenu.cs:138 | Prefab de orbe de material |
| ScrapWaves/Economy/Create Material Pool In Scene | EconomySceneSetupMenu.cs:181 | Pool de materiales en la escena |
| Tools/ScrapWaves/Fix Enemy Skinned Animations (Chaser+Drone) | Editor/EnemySkinnedAnimBuilder.cs:42 | Rehace controllers skinned de enemigos |
| Tools/ScrapWaves/Build Chaser Walk Animation | EnemySkinnedAnimBuilder.cs:55 | Solo chaser |
| Tools/ScrapWaves/Build Slime Hop Animation | EnemySkinnedAnimBuilder.cs:65 | Solo slime |
| Tools/ScrapWaves/Build Drone Fly Animation | EnemySkinnedAnimBuilder.cs:75 | Solo drone |
| Tools/Scenes/Create Gameplay Scene (Level 1) | Editor/GameplaySceneBuilder.cs:15 | Copia `SampleScene` si falta `GameplayScene`, cablea pools y Build Settings |
| ScrapWaves/UI/Organize Current Scene Under UI | Editor/GameplayUiSceneMigration.cs:18 | Reordena la UI de la escena abierta |
| ScrapWaves/UI/Migrate All Player Scenes Under UI | GameplayUiSceneMigration.cs:26 | Lo mismo en todas las escenas con jugador |
| Tools/Scrap Waves/Validate Authored Title Screen | Editor/MainMenu/TitleScreenAuthoringValidator.cs:31 | Valida la escena de título |
| Tools/ScrapWaves/Build Placeholder Player Animation | Editor/PlaceholderPlayerAnimationBuilder.cs:29 | Clips, controller y attachments del jugador |
| Tools/ScrapWaves/Refresh Player Animation Controller | PlaceholderPlayerAnimationBuilder.cs:113 | Solo clips y controller |
| Tools/ScrapWaves/Refit Animated Wearable Sockets | PlaceholderPlayerAnimationBuilder.cs:149 | Reajusta sockets de wearables |
| Tools/ScrapWaves/Render Placeholder Player Animation | PlaceholderPlayerAnimationBuilder.cs:322 | Renders de preview a `.utmp/player-animation` |
| Tools/ScrapWaves/Validate Placeholder Player Animation | Editor/PlayerAnimationValidation.cs:39 | Corre 13 fixtures EditMode y guarda XML + resumen |
| ScrapWaves/UI/Create Missing Rear Threat Indicator Assets | Editor/RearThreatIndicatorAuthoring.cs:35 | Assets del indicador |
| ScrapWaves/UI/Add Rear Threat Indicator To Current Scene | RearThreatIndicatorAuthoring.cs:78 | Lo agrega a la escena abierta |
| ScrapWaves/UI/Refresh Rear Threat Prefab Preview Meshes | RearThreatIndicatorAuthoring.cs:81 | Regenera meshes de preview |
| ScrapWaves/UI/Add Rear Threat Indicator To All Player Scenes | RearThreatIndicatorAuthoring.cs:203 | Migración masiva |
| ScrapWaves/Validation/Rear Threat/Compile and Render | Editor/RearThreatValidation.cs:63 | Compila player y renderiza a `Library/CodexValidation/RearThreat` |
| ScrapWaves/Validation/Rear Threat/Render PC and Mobile Previews | RearThreatValidation.cs:88 | Solo previews |
| ScrapWaves/UI/Create Missing Run Menu Assets | Editor/RunMenuPrefabBuilder.cs:35 | Assets de menús de run |
| Tools/ScrapWaves/Audit Animated Wearable Clearance | Editor/WearableAnimationClearanceAudit.cs:31 | Hornea la piel en una preview scene y mide choques con wearables |
| Tools/ScrapWaves/Build Wearable Weapon Mounts | Editor/WearableWeaponMountBuilder.cs:62 | Cinco fire points y catálogo del jugador |
| Tools/ScrapWaves/Render Wearable Weapon Mount Preview | WearableWeaponMountBuilder.cs:160 | Preview de montajes |
| ScrapWaves/Enemies/Create Destroyer Missile Prefab | Enemy/Editor/DestroyerPrefabMenu.cs:17 | Prefab de misil |
| ScrapWaves/Enemies/Create Destroyer Prefab | DestroyerPrefabMenu.cs:49 | `Destroyer_Boss.prefab` con Mouth/WeakPoint placeholder |
| ScrapWaves/Enemies/Assign Destroyer As Second Boss In Scene | DestroyerPrefabMenu.cs:105 | Lo pone en el `BossManager` de la escena |
| ScrapWaves/Level/Create Below Level Fog Prefab | Level/Editor/BelowLevelFogPrefabBuilder.cs:16 | Plano de niebla |
| Tools/ScrapWaves/Open Compactor Escape Test | Level/Editor/CompactorEscapeSetupEditor.cs:20 | Abre `WeaponTestingSandbox` y enfoca la puerta |
| Tools/ScrapWaves/Setup Compactor Escape Test | CompactorEscapeSetupEditor.cs:33 | Importa modelo, crea prefab y lo mete en los dos sandboxes |
| Tools/ScrapWaves/Replace Gameplay Placeholder Models | Level/Editor/GameplayModelsSetupEditor.cs:15 | Reemplaza puerta y workbench en `GameplayScene` |
| ScrapWaves/Level/Create Guide Arrow In Scene | Level/Editor/GuideArrowSetupMenu.cs:14 | Flecha guía |
| ScrapWaves/Level/Setup Exit System | Level/Editor/LevelExitSetupEditor.cs:17 | Sistema de salida, bosses y HUD V2 |
| ScrapWaves/Spawning/Create Default Enemy Spawn Roulette | Spawning/Editor/EnemySpawnRouletteAssetUtility.cs:9 | `DefaultEnemySpawnRoulette.asset` |
| Tools/ScrapWaves/Build Balance Testing Scene (test_balance) | Testing/Editor/BalanceTestingSceneBuilder.cs:25 | Genera la escena de balance (ver abajo) |
| Tools/ScrapWaves/Open Balance Testing Scene (test_balance) | BalanceTestingSceneBuilder.cs:202 | La abre |
| Tools/ScrapWaves/Game Feel/Rebuild Combat Text Assets | Weapon/Editor/CombatTextAssetBuilder.cs:20 | Assets de texto de combate |
| Tools/ScrapWaves/Game Feel/Rebuild Flamethrower Production Assets | Weapon/Editor/FlamethrowerAssetBuilder.cs:34 | Presentación del lanzallamas |
| Tools/ScrapWaves/Game Feel/Rebuild Cannon Production Assets | Weapon/Editor/GameFeelFoundationAssetBuilder.cs:38 | Presentación del cañón |
| Tools/ScrapWaves/Game Feel/Rebuild Mortar Production Assets | Weapon/Editor/MortarAssetBuilder.cs:35 | Mortero |
| Tools/ScrapWaves/Game Feel/Rebuild Rocket Launcher Production Assets | Weapon/Editor/RocketLauncherAssetBuilder.cs:35 | Lanzacohetes |
| Tools/ScrapWaves/Game Feel/Rebuild Rotating Blade Production Assets | Weapon/Editor/RotatingBladeAssetBuilder.cs:35 | Sierra rotante |
| ScrapWaves/UI/Build GameplayHud Prefab | Weapon/Editor/GameplayHudPrefabBuilder.cs:21 | Prefab de HUD |
| ScrapWaves/UI/Rebuild BottomStrip In Prefab | GameplayHudPrefabBuilder.cs:49 | Franja inferior del HUD |
| ScrapWaves/UI/Wire SampleScene GameplayHud | GameplayHudPrefabBuilder.cs:259 | Cablea el HUD en `SampleScene` (legacy) |
| Tools/ScrapWaves/Apply Weapon Balance Sheet Snapshot | Weapon/Editor/WeaponBalanceSheetApplier.cs:25 | Pisa `WeaponData` con valores hardcodeados |
| Tools/ScrapWaves/Build Weapon Testing Sandbox | Weapon/Testing/Editor/WeaponTestingSandboxSceneBuilder.cs:19 | Escena sandbox de armas y dummy |

`BalanceTuningHubEditor` no es menú: en Play muestra intervalo, intensidad y estado de spawn en vivo, con botones de heat 0/40/80/99 % (Balance/Editor/BalanceTuningHubEditor.cs:79-86) y "Disparar Overheat" (:89).

## Escena de balance

`BalanceTestingController` (Testing/BalanceTestingController.cs:10) es un panel IMGUI para probar 1×1.

1. `Build Balance Testing Scene` crea una escena vacía y le agrega luz, suelo, `HeatManager`, pool de proyectiles, jugador, cámara, EventSystem, dificultad, estación de crafteo, puntos de power-up y el controller (BalanceTestingSceneBuilder.cs:26-46).
2. Guarda en `Assets/Scenes/Testing/test_balance.unity` y la agrega **habilitada** a Build Settings (:48-49).
3. En Play: F1 muestra u oculta el panel y F2 alterna el mouse entre UI y cámara (BalanceTestingController.cs:81-85).
4. Botones: spawn +1/+10 por `EnemySpawnKind`, Destroyer y Stalker, 999 de todo o +50 por material, reset al arma inicial (`AutomaticCannon`), abrir crafteo, power-ups, limpiar enemigos y curar.

## Suite de tests

- Todo vive en `Assets/Tests/Editor/`. No hay ningún `.asmdef` en `Assets`: los tests compilan en `Assembly-CSharp-Editor` y solo aparecen en la pestaña **EditMode** (`com.unity.test-framework` 1.6.0). No hay assembly de PlayMode.
- 840 atributos: 598 `[Test]`, 233 `[TestCase]` y 9 `[UnityTest]`. Un `[TestCase]` cuenta como un caso. Los `[UnityTest]` entran a Play con `EnterPlayMode` desde EditMode (6 archivos).

| Área | Archivos (atributos) | Total |
|---|---|---|
| Armas: lógica y upgrades | WeaponUpgradeEffect 93, WeaponUpgradeMath 34, AutomaticWeaponMount 26, WearableWeaponFiring 21, SandboxWeaponUpgradeData 21, WeaponDiagnostics 13, AutomaticCannonFireLogic 5, MortarTrajectory 5, WeaponDamageAttribution 5, ManualWeaponFireCooldown 4, WeaponManagerSandboxParity 4, ManualProjectileTargetDamage 3, MortarTerrainFilter 2, ActiveAbilityAmmo 2 | 238 |
| Armas: presentación / game feel | AutomaticCannonPresentation 27, MortarPresentation 21, FlamethrowerPresentation 11, GameFeelFoundation 10, RocketLauncherPresentation 9, RotatingBladePresentation 9 | 87 |
| Texto de combate y accesibilidad (`GameFeel/CombatText`) | CombatTextPresentationCore 16, BurnStatusCombatText 9, PresentationAccessibility 8, DamageApplicationResult 6, DamageFeedbackSequenceCore 6, CombatTextAggregateCore 6, CombatTextSpatialDirector 5, CombatTextViewMotion 5, PresentationAccessibilityConsumer 5, CombatTextCleanStyleAsset 2 | 68 |
| Jugador: animación, wearables, movimiento y stats | PlayerAnimationIntegration 17, StatDisplayFormat 12, PlayerDirectionalDash 10, StatAttribution 9, PlayerStatConsumer 6, PlayerWearableMountFit 6, PlayerTorsoSkinning 5, PlayerWearableSurfaceFollower 5, PlayerXpFlow 3, PlayerWearableClearance 2, PlayerAnimationSandbox 1, PlayerRunPreview 1 | 77 |
| Apuntado y retícula | GameplayAim 23, ReticlePresentationLogic 10, ReticleHudAuthoring 8, ReticleAimProvider 2 | 43 |
| Rear threat | RearThreatSensor 39, RearThreatPresentation 15, RearThreatAuthoring 10, RearThreatPlayModeValidation 1 | 65 |
| Enemigos | EnemyReaction 36, EnemyDormancy 10, EnemyHealthDot 2, EnemyPoolLifecycle 2 | 50 |
| Overheat / spawn / dificultad | OverheatCycleSpawnScaling 7, HeatSpawnScaling 6, DifficultyManager 2 | 15 |
| Economía, meta y pasivos | AdvancedTinkering 26, EconomyBalance 6, PassiveItemInventory 6, PassiveItemTestingController 6, ObjectivesMenuUIPresenter 6, PassiveItemManager 5, SandboxPassiveItemData 4, AchievementToastAuthoring 3, AchievementUiIconCatalog 2, PickupGroundFall 1 | 65 |
| UI, menús y navegación | RunMenuUi 27, PauseMenuUI 19, TitleScreenController 12, SceneNavigation 12, GameplayPause 8, GameplayHudAuthoring 7, SupportUiAuthoring 7, ScrapSceneTransition 7, GameplayUiScene 6, UserSettingsService 5, TitleScreenScreenStack 4, WeaponUiIconCatalog 2 | 116 |
| Nivel | CompactorDoorPresentation 6, CompactorEscapeIntegration 6 | 12 |
| Audio | BgmTrackSelector 4 | 4 |

Sin ningún test que los referencie (búsqueda por nombre de clase en `Assets/Tests`):

- `OverheatManager`: cierre de ciclo y paridad elite/boss. `HeatManager` solo aparece como fixture de escalado de spawn.
- `BossManager`, `DestroyerBehavior`, `GigaWormBehavior`, `HellfireSlimeBehavior`, `BomberDroneBehavior`, `ChargerEnemyBehavior`, `FlyingRangedBehavior` y `BossKeyDrop`.
- `OrbitalSpawner`, `ZoneSpawner` y `ExitSpawnPressure`. `OverheatEliteWaveSpawner` y `EnemySpawnRoulette` solo aparecen de pasada (EnemyDormancyTests.cs:202, PlayerStatConsumerTests.cs:86).
- Crafteo: `CraftingStation` sale en una integración del compactor; de `CraftingUI` y `CraftingMenuView` solo se testea el cableado de UI (RunMenuUiTests, GameplayUiSceneTests). La transacción de crafteo no tiene test.
- `AudioManager` aparece una sola vez (UserSettingsServiceTests.cs:149).
- La dormancia sí está cubierta (`EnemyDormancyTests`, 10).

## Mapa de teclas de QA

Todo usa el Input System nuevo (`Keyboard.current`). `activeInputHandler: 1` en ProjectSettings. La única rama legacy es `Input.GetKeyDown(KeyCode.Q)` en DebugMonitor.cs:62 bajo `#else`.

| Tecla | Script | Archivo:línea | Qué hace |
|---|---|---|---|
| F3 (default) | `DebugUI` | Weapon/UI/DebugUI.cs:21, :104 | Muestra u oculta el overlay |
| P / Numpad− / Numpad+ (default) | `DebugUI` | DebugUI.cs:22-24, :113-120 | Pausa y escala de tiempo, solo con el overlay visible |
| F2 | `QaRuntimeTweaker` | Spawning/QaRuntimeTweaker.cs:126 | Panel `Balance` |
| F3 | `QaCoreLoopMenu` | Spawning/QaCoreLoopMenu.cs:75 | Panel `CoreLoop` |
| F1 | `EnemiesTestingHarness` → `QaPanels` | Spawning/EnemiesTestingHarness.cs:149 | Panel `Qa`; `QaPanels` (Spawning/QaPanels.cs:22) solo guarda qué panel está activo, sin input propio |
| Numpad 1 / 4 / 0 | `EnemiesTestingHarness` | EnemiesTestingHarness.cs:152-158 | Tirar ruleta, loguear stats y limpiar |
| Ctrl+Numpad 9 | `DebugCrafting` | Debug/DebugCrafting.cs:65-66 | 999 de cada material |
| Numpad 0 | `DebugInfiniteHealth` | Debug/DebugInfiniteHealth.cs:35 | Vida infinita on/off |
| Q | `DebugMonitor` | Weapon/DebugMonitor.cs:60 | Solo loguea el uso de la habilidad |
| F1 / F2 | `BalanceTestingController` | Testing/BalanceTestingController.cs:81-85 | Panel y modo de mouse en `test_balance` |

Qué hay en `Assets/Scenes/GameplayScene.unity` (GUID del `.meta` buscado en el YAML):

| Componente | GameObject | m_IsActive / m_Enabled | Nota |
|---|---|---|---|
| `DebugUI` | `DebugUI` | 1 / 1 (línea 11585) | `_visibleOnPlay: 0`; teclas serializadas 96/30/31/19 |
| `DebugCrafting` | `DEBUGTESTINVENTORY` | 1 / 1 (15554) | 999 |
| `DebugInfiniteHealth` | `DEBUG_InfiniteHealth` | 1 / 1 (27953) | `_startEnabled: 0` |
| `BalanceTuningHub` | `BalanceTuning` | 1 / 1 (17910) | — |
| `DebugMonitor` | vía `player.prefab` | habilitado | Override de escena `_logStateChanges: 0` |
| `QaRuntimeTweaker`, `QaCoreLoopMenu`, `EnemiesTestingHarness` | — | ausentes | Solo están en `Testing/enemiesTesting.unity`, que no tiene `DebugUI` |

**F3:** hoy no hay conflicto en ninguna escena, porque `DebugUI` y `QaCoreLoopMenu` nunca están juntos. Si se agrega `QaCoreLoopMenu` a `GameplayScene`, o `DebugUI` a `enemiesTesting`, F3 abre los dos a la vez.

## Deuda / puntos abiertos

- Con el enum `Key` de Input System 1.19, los valores serializados de `DebugUI` en `GameplayScene` son F3=96 y P=30, pero `_timeSlowerKey: 31` es **Q** y `_timeFasterKey: 19` es **E**, no Numpad−/+. Con el overlay abierto, Q (habilidad, WeaponManager.cs:389) baja el time scale y E (interactuar, CraftingStation.cs:69 y ExitDoor.cs:177) lo sube. Además `06-QA-y-debug.md` no menciona P ni la escala de tiempo.
- `DebugUI`, `DebugCrafting` y `DebugInfiniteHealth` están activos en `GameplayScene` y no tienen ninguna guarda `isDebugBuild`, `UNITY_EDITOR` ni `DEVELOPMENT_BUILD`. Un build de release trae F3, Ctrl+Numpad 9 y Numpad 0. El propio summary de `DebugInfiniteHealth` dice que era para `SampleScene` y para borrar antes de release (DebugInfiniteHealth.cs:9-10).
- Build Settings tiene habilitadas `WeaponTestingSandbox`, `enemiesTesting` y `test_balance`. Además, el builder de balance vuelve a habilitar `test_balance` cada vez que se corre (BalanceTestingSceneBuilder.cs:49).
- Hay dos fuentes que escriben `WeaponData`: `Import All CSV` (BalanceImportMenu.cs:24) y `Apply Weapon Balance Sheet Snapshot`, que tiene valores hardcodeados (WeaponBalanceSheetApplier.cs:36+). La última que se corre gana.
- No existe una assembly de PlayMode ni asmdefs de tests. Los 9 `[UnityTest]` dependen de `EnterPlayMode` dentro de EditMode, y las validaciones por menú exigen escenas guardadas (PlayerAnimationValidation.cs:72-74).
- El núcleo del loop no tiene tests: `OverheatManager`, `BossManager`, spawners orbital, de zona y de oleada, behaviors de variantes y bosses, y la transacción de crafteo.
- Las raíces de menú están partidas (`ScrapWaves/`, `Tools/ScrapWaves/`, `Tools/Scrap Waves/`, `Tools/Scenes/`). `Wire SampleScene GameplayHud` todavía apunta a la escena legacy.

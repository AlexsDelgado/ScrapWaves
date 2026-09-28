# UI y HUD — alpha 23-09-2026

Toda la UI del juego es uGUI (`com.unity.ugui` 2.0.0) con texto TextMeshPro (`com.unity.textmeshpro` 3.2.0) y navegación por `InputSystemUIInputModule` (`com.unity.inputsystem` 1.19.0). No hay UI Toolkit en runtime: no hay `.uxml`, `.uss` ni `UIDocument`. El criterio actual es "vista autorada en escena o prefab, presenter que solo bindea": los presenters no construyen jerarquía en runtime. Los builders de jerarquía existen, pero son editor-only (`#if UNITY_EDITOR`, métodos `AuthorUi`). El indicador trasero es un mesh en mundo y no usa Canvas: ver `44-Rear-threat.md`.

## Canvas

Todos los canvas raíz son `Screen Space Overlay` con `CanvasScaler` en `Scale With Screen Size`, 1920×1080, match 0,5. Orden en `GameplayScene` (valores serializados):

| Canvas | Sorting | Origen |
|---|---|---|
| `PauseRoot` (canvas anidado, override sorting) | 32760 | Agregado en escena sobre la instancia HUD V2 (`GameplayScene.unity:31240`) |
| `CraftingMenu` | 5100 | `Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab:3157` |
| `LevelUpMenu` / `WeaponSelectionMenu` | 5000 | Prefabs RunMenus (:1937 / :2135) |
| `LevelUpStatFeedbackCanvas` | 4900 | Escena (:2045) |
| `AchievementUnlockToastCanvas` | 750 | Escena (:32930) |
| `ReticleHUD_Canvas` | 650 | Escena (:17621); default de `ReticleHud._sortingOrder` |
| `GameplayHudCanvas` | 600 | `GameplayHud V2.prefab`; lo fuerza `GameplayHudRoot._sortingOrder` en Awake |
| `SurvivorHUD_Canvas` | 500 | Legacy, dentro de un GameObject inactivo |
| `GameplayUI_Canvas` | 400 | Legacy (`UIManager`), dentro de un GameObject inactivo |
| `MaterialInventoryHUDCanvas` | 100 | Escena (:4386) |

Todo cuelga de la raíz `UI` (`GameplayScene.unity:30161`), igual que el `EventSystem` (:23511). En `TitleScreen`: `MainMenuCanvas` (1000) y `FeedbackCanvas` (1500). `_includeTestingButtons` está en 0 (`TitleScreen.unity:19819`).

## HUD de gameplay: prefabs

| Prefab | Estado |
|---|---|
| `Assets/Prefabs/UI/GameplayHud V2.prefab` | El que usan `GameplayScene` (`GameplayScene.unity:9730`) y `SampleScene`. Suma `LevelExitHud` y 2 `Image` respecto de V1 |
| `Assets/Prefabs/UI/GameplayHud.prefab` | Sin referencias en escenas ni prefabs. Es el destino de `GameplayHudPrefabBuilder` |
| `Assets/Prefabs/UI/PlayerBarsHUD.prefab` | Sin referencias por GUID. Solo lo lee el builder por path (`GameplayHudPrefabBuilder.cs:17`) |
| `Assets/Prefabs/UI/RunMenus/*.prefab` | LevelUp (3 cartas), WeaponSelection (2 cartas), Crafting (3 slots). Los instancia la escena bajo `UI` |

Contenido de V2, bajo `GameplayHudCanvas`: `PlayerCombatFeedback`, `BossHealthBarHud`, `OverheatObjectiveHud`, `OffscreenObjectiveIndicators`, `BottomStrip` (`ColumnLeft` con barras, `ColumnCenter` con pasivos, `ColumnRight` con armas), `PauseMenuUI`, `RunEndScreenUI` (con `RunEndRoot`) y `LevelExitHud`.

La jerarquía visual de la pausa (`PauseRoot` con 8 hijos) no está en el prefab. Es un objeto agregado en escena sobre la instancia (`GameplayScene.unity:9727-9728`), con `_root` de `PauseMenuUI` sobrescrito (:9053-9055). `SampleScene` tiene sus propias copias.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/Weapon/UI/GameplayHudRoot.cs` | Raíz del HUD V2. Solo fija el sorting (600) del canvas hijo |
| `Assets/Scripts/Weapon/UI/GameplayHudHierarchyBuilder.cs` | Editor-only. Arma canvas, capas y BottomStrip, y la jerarquía de RunEnd |
| `Assets/Scripts/Weapon/Editor/GameplayHudPrefabBuilder.cs` | Menús *Build GameplayHud Prefab* (:21), *Rebuild BottomStrip In Prefab* (:49), *Wire SampleScene GameplayHud* (:259). Escriben `GameplayHud.prefab` (:15) |
| `Assets/Scripts/Editor/GameplayUiSceneMigration.cs` | *Organize Current Scene Under UI* / *Migrate All Player Scenes Under UI*. Mueve canvases y EventSystem bajo `UI`, cablea menús RunMenus y llama `AuthorUi` de cada presenter (:82-148) |
| `Weapon/UI/HudUiFactory.cs`, `HudUiWire.cs`, `HudBottomStripLayouts.cs`, `TmpUiHelper.cs` | Helpers de construcción y búsqueda por nombre |
| `Weapon/UI/PlayerBarsHud.cs` | Esfera = heat/overheat, barra superior = HP, inferior = XP. Por eventos |
| `Weapon/UI/PassiveLoadoutHud.cs` | 6 slots de pasivos. `PassiveItemManager.OnInventoryChanged` |
| `Weapon/UI/WeaponClusterHud.cs` | 3 slots de arma, munición, cooldown de rotación, habilidad y cargas de dash |
| `Weapon/UI/ReticleHud.cs` (+ `ReticlePresentationLogic.cs`) | Retícula del arma manual, marcador de mortero, flash de punto débil |
| `Weapon/UI/BossHealthBarHud.cs`, `OverheatObjectiveHud.cs`, `OffscreenObjectiveIndicators.cs` | Jefe, objetivo de overheat, flechas fuera de pantalla hacia jefe/élite |
| `Weapon/UI/PlayerCombatFeedback.cs` | Flash de daño y overlays de invulnerabilidad, stun y quemadura |
| `Weapon/UI/PauseMenuUI.cs` | Pausa, ajustes in-run y stats. Tabs de `StatAttributionPanel` solo en editor/dev (:58, :80) |
| `Weapon/UI/StatAttributionPanel.cs`, `LastHitRecorder.cs` | Panel dev: fuente de cada stat y último golpe |
| `Weapon/UI/RunEndScreenUI.cs` | Pantalla de victoria o derrota. Singleton `Instance` |
| `Weapon/UI/DebugUI.cs` | Overlay dev (F3): FPS, heat y log. P pausa y numpad ± cambia el time scale |
| `Weapon/UI/UIManager.cs`, `SurvivorHud.cs` | Legacy. Ver abajo |
| `Assets/Scripts/Level/LevelExitHud.cs` | Llaves, carga de puerta y presión de salida |
| `Assets/Scripts/XP/LevelUpChoiceUI.cs` | Controller de cartas de level-up y de primera arma. Vive en `player.prefab` |
| `Assets/Scripts/Economy/CraftingUI.cs` | Controller del menú de crafting. Vive en `player.prefab` |
| `Assets/Scripts/UI/RunMenus/*` | `ChoiceMenuView` / `ChoiceCardView` (cartas), `CraftingMenuView` / `CraftingWeaponSlotView`, `RunMenuContent` (SO con copy opcional por arma) |
| `Assets/Scripts/UI/WeaponUiIconCatalog.cs`, `AchievementUiIconCatalog.cs` | SO en `Resources/UI/...`, con carga lazy y setter para tests |
| `Assets/Scripts/TitleScreenController.cs` | Botones del título, sección DEV y ruteo a escenas |
| `Assets/Scripts/UI/MainMenu/*` | `TitleScreenScreenStack`, `SettingsScreenUI`, `MainMenuPresentationController` (+Profile, ItemView, audio, punch, fondo), `ScrapSceneTransition`, vistas de objetivos y unlocks |
| `Assets/Scripts/GameplayPause.cs` | Contador estático de locks de pausa de UI |

## Legacy: UIManager y SurvivorHud

En `GameplayScene` los dos están apagados por partida doble. `UIManager` tiene el componente en `m_Enabled: 0` (`GameplayScene.unity:24734`) y su GameObject `UIManager` inactivo. `SurvivorHud` también tiene `m_Enabled: 0` (:22780) y su GameObject `StatHud` inactivo. Sus canvases (400 y 500) siguen serializados.

Referencias vivas: `GameplayUiSceneMigration.cs:113-122` les llama `AuthorUi` en cada migración; `GameplayHudPrefabBuilder.cs:307` desactiva `SurvivorHud`; `SupportUiAuthoringTests.cs:17-18` los testea. En runtime no los referencia nada.

## Cómo pausan los menús

`GameplayPause` (`Assets/Scripts/GameplayPause.cs`) es solo un contador (`Push`/`Pop`/`SetHeld`), con reset en `SubsystemRegistration`. No toca `Time.timeScale`. Lo consultan `PlayerMovement.cs:314`, `HeatManager.cs:290` y `WeaponPresentationController.cs:448`. Cada menú pone `Time.timeScale = 0`, guarda su propio valor previo y toma el lock:

| Quién | Pausa | Restaura |
|---|---|---|
| `PauseMenuUI` | `SetPauseState(true, 0)` (:248-267) | Valor guardado si > 0,001, si no 1 |
| `LevelUpChoiceUI` | :112-114, si `_pauseWhileChoosing` (1 en `player.prefab:917`) | Previo si > 0, si no 1 (:143) |
| `CraftingUI` | :107-109 | Previo si > 0, si no 1 (:124) |
| `GameManager.EnterEndState` | :101-102 | Solo al recargar (`ResetTimeScaleForReload`, :118-122) |
| `HitStopController` | `timeScale = 0` sin lock | Su propio valor guardado |
| `DebugUI` | P con el panel visible, sin lock (:213-224) | Su propio valor guardado |

Todos los menús llaman `ThirdPersonCamera.SetLookBlockedByUi`, que desbloquea y muestra el cursor.

## Flujo: pausa

1. `PauseMenuUI.Update` (:114) detecta Escape (Input System). Si `_root` es nulo, no hace nada.
2. `CanPause` (:164-182) vuelve a resolver refs. Rechaza si hay level-up o crafting visibles (y deja el cursor libre) o si `GameManager` no está en `Playing`.
3. `ShowPause`: guarda el time scale, `SetPauseState(true, 0)` (activa `PauseRoot`, cursor libre, cámara bloqueada, lock), sincroniza ajustes y stats, y foco en Resume.
4. Mientras está abierto, `RefreshRunStats` corre cada frame.
5. Escape cierra en orden: primero la tab dev de atribución, después el panel de ajustes, y recién entonces hace Resume (:216-230).
6. *Quit* → `ReturnToTitle`: despausa y va a `SceneNavigation.LoadTitle()`. Si falla, vuelve a pausar.

## Flujo: carta de level-up

1. `PlayerXP.OnLevelUp` → `LevelUpOrchestrator.HandleLevelUp` encola el nivel. Los niveles se procesan uno por uno (`LevelUpOrchestrator.cs:43-61`).
2. `PassiveItemLevelUpHandler.PresentAndApplyCoroutine` filtra el pool por desbloqueos, arma entre 2 y 3 ofertas (`_choicesOffered` = 3 en `player.prefab:947`) y llama `LevelUpChoiceUI.PresentCoroutine`.
3. `ShowChoice` (:87-116) valida que la vista tenga cartas suficientes (`CanPresent`), bindea las cartas (`ChoiceMenuView.Show`: nombre, resumen/descripción de `RunMenuContent` si es arma, icono), bloquea la cámara y pausa.
4. Click en una carta → `CompleteChoice` → `Hide` (restaura time scale y suelta el lock) → callback con el índice.
5. El handler aplica el pasivo. Después, `PlayerStatsLevelUpHandler` aplica las stats automáticas y `LevelUpStatFeedback.Show` las muestra (canvas 4900).
6. Si el controller se desactiva con la carta abierta, se completa con −1 (:148-154).

La elección de primera arma usa el mismo controller con `WeaponSelectionMenu` (`RunStartWeaponChoice.cs:53`).

## Flujo: fin de run

1. `PlayerHealth.OnPlayerDied` → `GameManager.OnPlayerDied`, o `TriggerVictory` desde la puerta de salida.
2. `EnterEndState` (:99-115): estado, `timeScale = 0`, lock, `ReportRunToSaveSystem`. Después, `RunEndScreenUI.Instance.Show`, o error si no hay pantalla autorada.
3. `Show` (:43): título y color según victoria/derrota; stats (tiempo, kills, nivel, jefes); cámara bloqueada; `RunEndRoot` activo.
4. *Retry* (:134-143): `ResetTimeScaleForReload` y `SceneManager.LoadScene(buildIndex actual)`. *Main menu*: `SceneNavigation.LoadTitle()`.

## Título

`TitleScreenController` cablea Play, Objectives, Settings, Quit (oculto en WebGL) y la sección DEV (`_developerRoot`, apagada salvo `_includeTestingButtons`). Arma la navegación explícita vertical (:276-299). Play usa `ScrapSceneTransition.TryLoad` (persistente, `DontDestroyOnLoad`, `ScrapSceneTransition.cs:164`) y, si falla, carga directo.

`TitleScreenScreenStack` maneja una sola pantalla local por vez (Objectives, Settings, QuitConfirmation): animación con `unscaledDeltaTime` (0,22 s de apertura / 0,18 s de cierre, o lo que diga el profile), cancel por `ICancelHandler` y `InputActionReference`, foco de retorno. `SettingsScreenUI` tiene tres categorías (Controls, Audio, Feedback), reset con confirmación (armado de dos pasos) y escribe `UserSettingsService`.

## Tests (Assets/Tests/Editor)

`GameplayUiSceneTests` (6 escenas: una raíz `UI`, menús y retícula autorados bajo ella, ningún canvas de pantalla fuera de `UI`), `GameplayHudAuthoringTests`, `SupportUiAuthoringTests`, `PauseMenuUITests` (navegación, Escape, sorting sobre DebugUI, bloqueos modales, ajustes), `GameplayPauseTests`, `RunMenuUiTests` (cartas 3/2, crafting 3 slots/6 materiales, sin crear UI en runtime), `ReticleHud*Tests`, `TitleScreenControllerTests`, `TitleScreenScreenStackTests`, `ScrapSceneTransitionTests`, `ObjectivesMenuUIPresenterTests`, `AchievementToastAuthoringTests`, `*UiIconCatalogTests`, `StatAttributionTests`, `SceneNavigationTests`, `UserSettingsServiceTests`.

## Deuda / puntos abiertos

- `GameplayHudPrefabBuilder` construye y reconstruye `GameplayHud.prefab` (`GameplayHudPrefabBuilder.cs:15`), que no está en ninguna escena. Las escenas usan `GameplayHud V2.prefab`. El warning de `GameplayHudRoot.cs:20` manda a ese menú. Hay que decidir si se borra V1 y se apunta el builder a V2, o si se borra el builder.
- La pausa no está en el prefab. `PauseRoot` y sus hijos son objetos agregados en cada escena sobre la instancia V2 (`GameplayScene.unity:9727-9728`; `SampleScene` tiene dos `PauseRoot`). Una instancia nueva de V2 queda con `_root` nulo, y `PauseMenuUI.Update` sale en silencio (:116). Hay que aplicar el override al prefab.
- Hay dos almacenes de accesibilidad. El título escribe `UserSettingsService.ScreenShake/ScreenFlash` (`SettingsScreenUI.cs:278-279`). La pausa escribe `ReducedShake/ReducedFlash` en `SaveManager.PresentationAccessibility` (`PauseMenuUI.cs:546-547`). Solo `ReducedMotion` se sincroniza (:334-344, :539-544). Temblor y flash quedan con valores distintos según desde dónde se cambien.
- El time scale no tiene dueño: cada menú guarda su propio previo. `DebugUI` está activo en `GameplayScene` (:11587), no tiene gate de dev build (F3 funciona en release, `DebugUI.cs:99-120`) y su P pone el time scale sin tomar el lock. Con una carta de level-up o el crafting abiertos, lo vuelve a 1 y el mundo corre debajo del menú, mientras `GameplayPause` sigue bloqueando solo al jugador.
- `LevelUpChoiceUI.PresentCoroutine` ignora `title` (:51-56). `PresentWeaponSelectionCoroutine` pasa `null` como título. Los títulos en castellano de los llamadores (`PassiveItemLevelUpHandler.cs:60`, `WeaponLevelUpHandler.cs:77`) y el de `RunStartWeaponChoice.cs:53` nunca se muestran. `Show` hardcodea textos en inglés (:84).
- `UIManager` y `SurvivorHud` están muertos en runtime, pero la migración les sigue llamando `AuthorUi`, y sus canvases (400 y 500) siguen en la escena. Son candidatos a borrarse junto con `SupportUiAuthoringTests:17-18`.
- `GameplayHudRoot._playerBarsContent` (:8-9) no se lee nunca. Su tooltip promete una jerarquía placeholder en Awake que no existe.
- Desde gameplay no se usa `ScrapSceneTransition`: Retry carga con `SceneManager.LoadScene` directo (`RunEndScreenUI.cs:142`), y Quit y Main menu usan `SceneNavigation.LoadTitle()`. La transición solo existe en el camino título → juego. Además, el crafting solo se cierra con su botón: Escape queda bloqueado por `CanPause` (:169-177) y `CraftingUI` no maneja cancel.

# Escenas y navegación — alpha 23-09-2026

Carga de escenas sin additive ni async: todo es `SceneManager.LoadScene` síncrono por build index. `SceneNavigation` es la única tabla de destinos. `ScrapSceneTransition` pone una cortina animada encima y sobrevive a los cambios de escena, pero solo el menú de título la usa. La pausa mezcla dos mecanismos: `Time.timeScale` y un contador de locks de UI (`GameplayPause`).

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/SceneNavigation.cs` | Enum `SceneDestination`, nombres y paths, `Load`, `QuitApplication` |
| `Assets/Scripts/UI/MainMenu/ScrapSceneTransition.cs` | Overlay de transición persistente (singleton, `DontDestroyOnLoad`) |
| `Assets/Scripts/TitleScreenController.cs` | Botones del título, destinos DEV, foco, bloqueo de input durante la transición |
| `Assets/Scripts/GameplayPause.cs` | Contador estático de locks de pausa de UI |
| `Assets/Scripts/Weapon/UI/PauseMenuUI.cs` | Menú de pausa (Escape), settings in-game, volver al título |
| `Assets/Scripts/Weapon/UI/RunEndScreenUI.cs` | Pantalla de fin de run: Retry y menú principal |
| `Assets/Scripts/GameManager.cs` | Estado de la run. Congela el tiempo al terminar |
| `Assets/Prefabs/UI/MainMenu/ScrapSceneTransition.prefab` | Overlay authored |

## Build settings

`ProjectSettings/EditorBuildSettings.asset`:

| Índice | Escena | En build | Destino en `SceneNavigation` |
|---|---|---|---|
| 0 | `Assets/Scenes/TitleScreen.unity` | Sí | `Title` |
| 1 | `Assets/Scenes/GameplayScene.unity` | Sí | `Play` |
| — | `Assets/Scenes/SampleScene.unity` | **No** | — |
| 2 | `Assets/Scenes/Testing/WeaponTestingSandbox.unity` | Sí | `WeaponSandbox` |
| 3 | `Assets/Scenes/Testing/enemiesTesting.unity` | Sí | `EnemiesTesting` |
| 4 | `Assets/Scenes/Testing/test_balance.unity` | Sí | ninguno |

`SceneNavigation.Load` (`SceneNavigation.cs:37-51`) resuelve el path fijo (`:84-94`) a build index con `SceneUtility.GetBuildIndexByScenePath`. Si la escena no está habilitada en Build Settings, loguea un error y devuelve `false` sin cargar. Antes de cargar llama `PrepareForSceneChange`, que solo pone `Time.timeScale = 1` (`:32-35`). `QuitApplication` sale de Play Mode en editor y llama `Application.Quit()` en build (`:73-82`).

## Raíz persistente

`TitleScreen` tiene un root `PersistentSystemsRoot` (`TitleScreen.unity:11293`) con `UserSettingsService`, `UserSettingsApplier` y, como hijo, la instancia de `ScrapSceneTransition.prefab` (`:10117`).

- `UserSettingsService.Awake` hace `DontDestroyOnLoad` de su GameObject, que es el root.
- `ScrapSceneTransition.Awake` hace `DontDestroyOnLoad(transform.root)` (`ScrapSceneTransition.cs:163-164`).
- Al volver al título se carga otra copia del root. `UserSettingsService.Awake` (orden −220) ve que ya hay instancia y desactiva y destruye la copia entera, transición incluida (`UserSettingsService.cs:82-97`). Si igual quedara otra transición viva, su `Awake` loguea el duplicado y la oculta (`ScrapSceneTransition.cs:147-156`).
- `TitleScreenController.Awake` reemplaza sus referencias authored por las instancias vivas (`TitleScreenController.cs:54-57`).

`GameplayScene` no tiene servicio de settings ni transición. Si se arranca directo en el editor no hay cortina ni settings aplicados, y `UserSettingsApplier` no existe.

## Transición

Timings en unscaled time (prefab `ScrapSceneTransition.prefab:313-318`, iguales al script):

| Fase | Duración | Reduced motion |
|---|---|---|
| `Warning` (cuchilla de aviso) | 0,1 s | Se saltea |
| `Covering` (placas) | 0,38 s, curva con overshoot 1,035 | Fade de alpha de 0,1 s con las placas ya cerradas |
| `WaitingForScene` | ≥ 0,08 s tras `sceneLoaded`. Timeout 15 s | Igual |
| `Revealing` | 0,3 s | Fade de 0,1 s |

Clips: `UI_1.wav` (aviso), `UI_2.wav` (impacto) y `UI_0.wav` (reveal) (`ScrapSceneTransition.prefab:307-309`), escalados por el volumen SFX de settings. Con reduced motion, el impacto baja de 14 a 4 partículas.

Flujo (`ScrapSceneTransition.cs:195-420`):

1. `TryLoad(destino)`. Rechaza si ya hay una transición en curso, si el componente está inactivo o si faltan referencias authored. Guarda el nombre esperado y emite `TransitioningChanged(true)`.
2. `Update` avanza la máquina de estados con `unscaledDeltaTime`. Puede cruzar varias fases en un frame, con guarda de 8 iteraciones (`:254-316`).
3. Al cubrir del todo, invoca `SceneNavigation.Load` una sola vez (`:369-405`). Si falla o tira excepción, destapa la escena actual.
4. `HandleSceneLoaded` marca la carga solo si el nombre coincide con el esperado (`:430-439`). Sin ese evento, a los 15 s loguea un error y destapa igual (`:340-367`).
5. Reveal y `TransitioningChanged(false)`. El título bloquea input de menú y del screen stack mientras dura (`TitleScreenController.cs:206-210`).

## Título

`TitleScreenController`:

- **Botones de producción.** Play, Objetivos, Settings y Salir, este último con confirmación. En WebGL, Salir se oculta (`:355-362`).
- **Destinos DEV.** Weapon Sandbox y Enemies Testing, detrás de `_includeTestingButtons`, que en la escena está en 0 (`TitleScreen.unity:19819`). Se activan con `SetTestingButtonsVisible`.
- **Carga.** `RequestSceneLoad` intenta la transición. Si no hay transición, o no arrancó y no hay otra en curso, loguea un error y carga directo (`:159-171`).
- **Navegación.** Arma navegación explícita vertical solo con los botones visibles e interactuables (`:276-297`). El foco inicial va a Play.
- **Preferencias.** Aplica reduced motion, shake y flash de `UserSettingsService` a la presentación del menú y al screen stack (`:212-221`).
- **Cursor.** Lo libera en `Awake`.

## Pausa

`GameplayPause` (`GameplayPause.cs:7-48`) es un contador. `SetHeld(ref bool, bool)` garantiza un push/pop por dueño. Se resetea en `SubsystemRegistration`, no por escena.

| Dueño del lock | Cuándo | `timeScale` |
|---|---|---|
| `PauseMenuUI` | Escape en run activa (`PauseMenuUI.cs:114-134`) | 0. Al reanudar, restaura el valor guardado |
| `LevelUpChoiceUI` | Elección de mejora | 0. Restaura el anterior |
| `CraftingUI` | Crafteo | 0. Restaura el anterior |
| `GameManager` | Victoria o derrota (`GameManager.cs:98-103`) | 0 |

Todos sueltan el lock en `OnDisable`, así que un cambio de escena no deja locks colgados. El hit-stop también pone `timeScale` en 0, pero no toma lock.

Lectores de `IsUiPaused`: `HeatManager.cs:290`, `PlayerMovement.cs:314` y `WeaponPresentationController.cs:448`.

`CanPause` (`PauseMenuUI.cs:163-182`) no abre la pausa sobre level-up o crafteo, ni con la run terminada. Escape cierra primero el popup de más arriba (panel de settings, tab DEV) y recién después reanuda.

## Salidas desde gameplay

| Acción | Código | Ruta |
|---|---|---|
| Pausa → Título | `PauseMenuUI.cs:232-246` | `SceneNavigation.LoadTitle()` directo. Si falla, vuelve a pausar |
| Fin de run → Menú | `RunEndScreenUI.cs:145-148` | `SceneNavigation.LoadTitle()` directo |
| Fin de run → Retry | `RunEndScreenUI.cs:134-143` | `GameManager.ResetTimeScaleForReload()` y `SceneManager.LoadScene(buildIndex activo)` |

## Deuda / puntos abiertos

- Las salidas desde gameplay no usan `ScrapSceneTransition`, aunque la instancia sigue viva en `DontDestroyOnLoad` si el juego arrancó en el título. Título → juego tiene cortina. Juego → título y Retry cortan en seco.
- Retry salta `SceneNavigation` (`RunEndScreenUI.cs:142`). Queda un segundo punto de carga que resetea `timeScale` por su cuenta y que, si se agrega lógica a `SceneNavigation.Load` (transición, limpieza), no la va a heredar.
- `test_balance.unity` está habilitada en el build sin destino ni botón. Sandbox y Enemies Testing también se empaquetan aunque sus botones estén ocultos. Hoy todo build de release las incluye.
- `GameplayPause.Reset()` (`GameplayPause.cs:44-47`) no tiene llamadores.
- Arrancar `GameplayScene` desde el editor no crea `UserSettingsService`, así que la sensibilidad y los volúmenes quedan en los valores de escena. No hay bootstrap que lo cubra.

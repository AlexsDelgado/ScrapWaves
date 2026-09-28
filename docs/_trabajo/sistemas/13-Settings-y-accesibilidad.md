# Settings y accesibilidad — alpha 23-09-2026

Hay **dos almacenes de preferencias**, que se solapan:

1. **`UserSettingsService`.** Controles, audio y feedback básico. Lo guarda en `PlayerPrefs` y `UserSettingsApplier` lo empuja a los componentes vivos.
2. **`PresentationAccessibilityRuntime`.** Reduced motion, reduced shake y reduced flash, más el texto de combate. Es un estado estático. Lo persiste `SaveManager` dentro del save de meta-progresión (JSON).

Reduced motion existe en los dos, y el título y la pausa escriben en almacenes distintos.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/Settings/UserSettingsData.cs` | DTO serializable, defaults, rangos, flags `UserSettingsChange` |
| `Assets/Scripts/Settings/UserSettingsService.cs` | Singleton persistente: carga, set, reset, guardado con debounce |
| `Assets/Scripts/Settings/UserSettingsApplier.cs` | Aplica a cámara, audio, presentación de armas, feedback de combate y reacciones de enemigos |
| `Assets/Scripts/Settings/Accessibility/PresentationAccessibilitySettings.cs` | DTO y snapshot inmutable `PresentationAccessibilityState` |
| `Assets/Scripts/Settings/Accessibility/PresentationAccessibilityRuntime.cs` | Estado global y evento `Changed` |
| `Assets/Scripts/Meta/SaveManager.cs` | Persiste la accesibilidad en `scrapwaves_save.json` |
| `Assets/Scripts/UI/MainMenu/SettingsScreenUI.cs` | Pantalla de settings del título |
| `Assets/Scripts/Weapon/UI/PauseMenuUI.cs` | Settings dentro de la pausa |

## Opciones

| Opción | Almacén | Default | Rango | Título | Pausa |
|---|---|---|---|---|---|
| Sensibilidad horizontal | UserSettings | 0,12 | 0,02–0,4 | Sí | Sí |
| Sensibilidad vertical | UserSettings | 0,12 | 0,02–0,4 | Sí | Sí |
| Invertir Y | UserSettings | No | — | Sí | Sí |
| Volumen SFX | UserSettings | 0,2 | 0–1 | Sí | Sí |
| Volumen música | UserSettings | 0,2 | 0–1 | Sí | Sí |
| Reduced motion | **Ambos** | No | — | Sí (UserSettings) | Sí (escribe en los dos) |
| Screen shake | UserSettings | Sí | — | Sí | No |
| Screen flash | UserSettings | Sí | — | Sí | No |
| Reduced shake | Accesibilidad | No | — | No | Sí |
| Reduced flash | Accesibilidad | No | — | No | Sí |
| Texto de combate | Accesibilidad | `Full` | Off / ImportantOnly / Full | No | Off o Full. `ImportantOnly` no se puede elegir (`PauseMenuUI.cs:548`) |
| Escala del texto de combate | Accesibilidad | 1 | 0,75–1,25 | No | Sí |

Defaults y rangos en `UserSettingsData.cs:7-19` y `PresentationAccessibilitySettings.cs:18-27`. El título tiene reset por categoría (Controles, Audio, Feedback) y reset total (`UserSettingsService.cs:210-228`). La pausa no tiene reset.

## Persistencia

**UserSettings.** Clave `ScrapWaves.UserSettings.v1.Data` de `PlayerPrefs`, con JSON vía `JsonUtility` (`UserSettingsService.cs:8,254-284,351-366`).

1. **Carga perezosa.** La primera lectura de cualquier propiedad dispara `EnsureInitialized`. Si falla la lectura o el JSON, usa los defaults con un warning único.
2. **Migración.** `MigrateLegacyAudioDefaults` (`:286-302`) reemplaza SFX = 1,0 y música = 0,45 (los defaults viejos) por 0,2 y guarda.
3. **Sanitize.** Clampa los rangos y cambia NaN o infinito por el default (`UserSettingsData.cs:47-88`).
4. **Set.** Cada setter sanitiza, compara, emite `Changed(flag)` y agenda el guardado a 0,2 s en unscaled time (`:333-349`). `FlushPendingSave` se fuerza en pausa de app, al salir, en `OnDestroy`, al cerrar el panel de settings de la pausa y al reanudar.

**Accesibilidad.** `SaveManager` (bootstrap automático `BeforeSceneLoad`, `SaveManager.cs:34-43`) carga `Application.persistentDataPath/scrapwaves_save.json` y llama `PresentationAccessibilityRuntime.Apply`. `SetPresentationAccessibility` (`:229-249`) sanitiza, aplica y guarda solo si cambió. `ResetProgress` conserva la accesibilidad (`:210-222`).

`PresentationAccessibilityRuntime.Apply` (`PresentationAccessibilityRuntime.cs:28-42`) emite `Changed` solo si el snapshot cambió. El estado se resetea en `SubsystemRegistration`.

## Aplicación

`UserSettingsApplier` vive junto al servicio en `PersistentSystemsRoot` del título (`TitleScreen.unity:11293-11317`) y viaja con él por `DontDestroyOnLoad`.

1. Se suscribe a `BecameAvailable` de `ThirdPersonCamera`, `AudioManager`, `WeaponPresentationController` y `PlayerCombatFeedback`, y a `sceneLoaded` (`UserSettingsApplier.cs:17-28`).
2. En cada `sceneLoaded` redescubre targets con `FindObjectsByType` y aplica todo (`:99-110`).
3. En `Changed` aplica solo el grupo afectado (`:80-97`).

| Target | Qué recibe | Código |
|---|---|---|
| `ThirdPersonCamera` | Sensibilidades, Invert Y, `ScreenShakeEnabled = shake && !reducedMotion` | `:234-240` |
| `AudioManager` | `SfxVolume`, `MusicVolume` | `:242-246` |
| `WeaponPresentationController` | Reduced motion, shake, flash | `:248-254` |
| `PlayerCombatFeedback` | Reduced motion, flash (apaga el flash de daño) | `:256-261` |
| `EnemyReactionRuntime` | Reduced motion, flash | `:227-232` |

Del lado de accesibilidad, `WeaponPresentationController` se suscribe a `PresentationAccessibilityRuntime.Changed` y copia los cinco campos a sus `GameFeelRuntimeOptions` (`WeaponPresentationController.cs:645-654`). `CombatTextDirector` lee `Current` para modo y escala. Reduced shake multiplica los impulsos de cámara por 0,25 (`CameraFeedbackController.cs:11`).

Otros lectores directos de `UserSettingsService.Instance`:

- `ScrapSceneTransition`: reduced motion y volumen SFX.
- `MenuAudioFeedback`: volumen SFX.
- `TitleScreenController`: preferencias de presentación del menú.

### Pausa y reduced motion

- El toggle de la pausa escribe en los dos almacenes (`PauseMenuUI.cs:539-544`).
- Al abrir la pausa, `SyncReducedMotionToPresentationRuntime` copia el valor de UserSettings a Accesibilidad si difieren (`:334-344`).
- El título no hace esa copia.

## Deuda / puntos abiertos

- **Un volumen SFX al 100 % se pierde al reiniciar.** La migración legacy compara contra 1,0 y 0,45 en cada carga, sin versión ni marca de "ya migrado" (`UserSettingsService.cs:286-302`). Un usuario que sube el SFX al máximo, o deja la música en 45 %, vuelve a 20 % en el próximo arranque.
- Hay dos fuentes de verdad para reduced motion. Si se cambia en el título, `PresentationAccessibilityRuntime` queda desfasado hasta que se abre la pausa. En `WeaponPresentationController` gana el último que escribió (`WeaponPresentationController.cs:418-430` contra `:645-654`).
- Shake y flash están partidos: "Screen shake / flash" (on/off, solo en el título) y "Reduced shake / flash" (atenuar, solo en la pausa). Son cuatro toggles en dos pantallas distintas para dos conceptos parecidos.
- El título no expone texto de combate ni reduced shake/flash. La pausa no expone screen shake/flash ni reset.
- `CombatTextMode.ImportantOnly` existe y está soportado por `CombatTextVisibilityPolicy`, pero la UI lo mapea a Off/Full (`PauseMenuUI.cs:548`).
- `UserSettingsService` solo existe si la sesión pasó por `TitleScreen`. `SaveManager` sí tiene bootstrap. Al arrancar `GameplayScene` directo, la accesibilidad se aplica y los settings no.
- Con reduced motion, el applier apaga todo el shake de cámara (`UserSettingsApplier.cs:239`). Los factores de reduced motion de `ThirdPersonCamera` (0,2 / 0,35 / 0) quedan casi sin uso. Ver `11-Camara-y-aim.md`.

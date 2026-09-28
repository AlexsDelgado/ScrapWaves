# Audio — alpha 23-09-2026

Audio sin `AudioMixer`: no hay assets `.mixer` en el proyecto y todas las `AudioSource` de `TitleScreen` y `GameplayScene` tienen `OutputAudioMixerGroup` vacío. El volumen se controla multiplicando en código. Cada escena tiene su propio `AudioManager` (no persiste) con una playlist de BGM y un canal de SFX genérico. El audio de armas tiene su propio pool de voces 3D dentro de la presentación de armas, y el menú y la transición reproducen sus clips por su cuenta.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/Audio/AudioManager.cs` | Singleton por escena: SFX genéricos, playlist de BGM, capa de Overheat |
| `Assets/Scripts/Audio/BgmTrackSelector.cs` | Elección de pista: `AlternateTwo` o `ShuffleBag` |
| `Assets/Scripts/GameFeel/WeaponAudioDirector.cs` + `Weapon/Presentation/WeaponAudioVoicePool.cs` | SFX de armas por cue, con voces 3D |
| `Assets/Scripts/UI/MainMenu/MenuAudioFeedback.cs` | Navegación y confirmación del menú de título |
| `Assets/Scripts/UI/MainMenu/ScrapSceneTransition.cs` | Clips de aviso, impacto y reveal de la cortina |
| `Assets/Scripts/Settings/UserSettingsApplier.cs` | Empuja los volúmenes SFX y música de settings al `AudioManager` |

## AudioManager

`[DefaultExecutionOrder(-60)]`. `Instance` se asigna en `OnEnable` y se limpia en `OnDisable`, sin chequeo de duplicados (`AudioManager.cs:56-66`). Emite `BecameAvailable` para que `UserSettingsApplier` le aplique los volúmenes.

Tres fuentes:

| Fuente | Uso |
|---|---|
| `_sfx` | `PlayOneShot(clip, _sfxVolumeScale)` (`:278-284`) |
| `_musicNormal` | Playlist, sin loop. El volumen es el de música |
| `_musicOverheatLayer` | Loop opcional. Volumen 0 fuera de Overheat |

En `Start` se suscribe a `PlayerXP.OnLevelUp` y a `OverheatManager.OnOverheatStarted/Finished` (búsqueda por `FindAnyObjectByType`, `:81-93`). Después arranca la playlist y la capa de Overheat en idle (`:68-73`).

Helpers estáticos: `TryPlayShoot`, `TryPlayEnemyHit`, `TryPlayEnemyDeath` y `TryPlayPlayerHurt` (`:287-293`). Hurt tiene un cooldown de 1 s en unscaled time, así los golpes seguidos no apilan el SFX (`:244-251`).

### Valores por escena

| Campo | Script | `GameplayScene` (`:29126-29148`) | `TitleScreen` (`:9106-9125`) |
|---|---|---|---|
| `_sfx` | — | asignado | **vacío** |
| `_musicOverheatLayer` | — | asignado | vacío |
| `_shoot` | — | `shoot.wav` | — |
| `_enemyHit` / `_enemyDeath` | — | **vacíos** | — |
| `_levelUp` | — | `level_up.wav` | — |
| `_overheatStart` / `_overheatEnd` | — | `010.wav` los dos | — |
| `_playerHurt` | — | `player_hurt.wav` | — |
| `_bgmTracks` | — | Gameplay v1–v4 (`.ogg`) | Title Screen v1–v2 (`.ogg`) |
| `_bgmMode` | ShuffleBag | ShuffleBag (1) | AlternateTwo (0) |
| `_bgmOverheatLayer` | — | **vacío** | vacío |
| Pausa entre pistas | 10–30 s | 10–30 s | 10–30 s |
| `_sfxVolumeScale` / `_musicMainVolume` | 0,2 / 0,2 | 0,2 / 0,2 | 0,2 / 0,2 |
| `_musicOverheatVolume` | 0,1556 | 0,1556 | 0,1556 |

Las `AudioSource` de la escena quedan en volumen 1, sin loop, prioridad 128. La de música no tiene PlayOnAwake.

## Playlist

1. `StartMainBgm` (`:124-138`): si hay al menos una pista no nula, pone `loop = false` y el volumen de música, y crea el selector. `AlternateTwo` con un número de pistas distinto de 2 cae a `ShuffleBag` (`:140-147`).
2. `RunPlaylist` (`:149-180`):
   - Pide un índice. Si el clip es nulo, reintenta hasta N veces.
   - Hace `Play` y espera `!isPlaying` frame a frame.
   - Espera un silencio aleatorio entre min y max con `WaitForSecondsRealtime`.
   - Repite.
3. `BgmTrackSelector` (`BgmTrackSelector.cs:36-88`):
   - **`AlternateTwo`.** Arranca con una pista al azar y después alterna 0/1.
   - **`ShuffleBag`.** Fisher-Yates. Al rellenar la bolsa, si la primera es la última que sonó, la intercambia con otra, así nunca repite dos seguidas entre bolsas.
   - Con una sola pista, siempre la 0.

### Capa Overheat

`SetOverheatLayerActive` (`AudioManager.cs:217-230`) sube la capa a:

```
clamp01(0,1556 × volumenMúsica / 0,2)
```

Queda proporcional al slider de música, con referencia en el default 0,2 (`:272-276`). Cambiar el volumen de música reescala la capa solo si está sonando (`:259-270`). En `GameplayScene` no hay clip de capa, así que al entrar en Overheat solo suena el SFX `010.wav`.

## Volumen: quién aplica qué

| Emisor | Factor de volumen | Fuente del valor |
|---|---|---|
| `AudioManager` SFX | `_sfxVolumeScale` | Settings, vía applier |
| `AudioManager` música | `_musicMainVolume` en `AudioSource.volume` | Settings, vía applier |
| Armas (`WeaponAudioVoicePool`) | `cue.Volume × SfxVolume × intensidad × capa` | `AudioManager.SfxVolume`, o 1 si no hay manager (`WeaponPresentationController.cs:616-619`) |
| Menú de título | `volumenAuthored × SfxVolume` (navegación 0,55, impacto 0,9) | `UserSettingsService` directo (`MenuAudioFeedback.cs:71-75`) |
| Transición | 0,65 / 0,95 / 0,7 × SfxVolume | `UserSettingsService` directo |

Voces de armas: 16 voces con spatial blend 1 (`player.prefab:1041-1042`, iguales al script).

## Assets

`Assets/Audio`:

- **BGM.** Seis `.ogg`: Gameplay v1–v4 y Title Screen v1–v2. Están en Streaming y Vorbis (`loadType: 2`). Cada uno tiene un `.m4a` duplicado sin referencias.
- **SFX.** `.wav` en Decompress On Load: `shoot`, `level_up`, `player_hurt`, `010`, `UI_0..2` y `click` (reject del menú).
- `Junkyard-Pulse-Loop.ogg` solo lo referencia `SampleScene`, que no está en el build.

## Deuda / puntos abiertos

- **El SFX de disparo no suena nunca.** El único llamador de `TryPlayShoot` es `PlayerAutoAttack` (`PlayerAutoAttack.cs:64`), deshabilitado en el prefab (`player.prefab:619`). El clip `shoot.wav` está asignado sin uso. Las armas reales suenan por su propio pool.
- `_enemyHit` y `_enemyDeath` están vacíos en `GameplayScene`. Las llamadas de `EnemyHealth` (`EnemyHealth.cs:159,167`) no hacen nada. Hay que confirmar si el audio de impacto de armas lo cubre a propósito.
- `_bgmOverheatLayer` está vacío. La capa de Overheat y su `AudioSource` están cableadas, pero en la run no suena nada.
- Overheat start y end usan el mismo clip (`010.wav`), que también es el `_localOpenClip` del menú.
- Sin mixer: no hay ducking, ni bus de SFX o UI, ni snapshots de pausa. La música y los loops siguen sonando con `timeScale = 0`: nada llama `AudioListener.pause` ni pausa las fuentes.
- Tres caminos leen el volumen SFX de fuentes distintas (`AudioManager`, `UserSettingsService` directo, fallback 1). En `GameplayScene` arrancada desde el editor, sin servicio de settings, el menú no aplica y las armas usan el 0,2 serializado del `AudioManager`.
- `Instance` es el último `AudioManager` habilitado, sin guarda de duplicados (`AudioManager.cs:56-60`). Si se deshabilita uno, `Instance` queda nulo aunque haya otro activo.
- Seis `.m4a` duplicados de la BGM sin referencias en `Assets/Audio/BGM`.
- 10 a 30 s de silencio entre pistas (`_bgmGapMinSeconds/_bgmGapMaxSeconds`) en las dos escenas. En una run corta es mucho tiempo sin música; conviene confirmarlo con diseño.

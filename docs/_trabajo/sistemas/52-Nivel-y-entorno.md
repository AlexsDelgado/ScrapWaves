# Nivel y entorno — alpha 23-09-2026

Qué hay en `GameplayScene` hoy y qué scripts arman el objetivo de salida, los pickups de mundo y el entorno visual. El nivel sigue siendo un greybox (`Map`, cubos y planos) con una sola estación de crafteo y una puerta de salida tipo compactador. La salida se abre con llaves que dropean los bosses. Ver `04-Game-Loop.md` para la fase de escape y `03-Spawning-y-enemigos.md` para la presión de spawn.

## Scripts

| Script | Ubicación | Rol |
|---|---|---|
| `LevelExitObjective` | `Level/` | Cuenta llaves. Singleton `Instance`, `[DefaultExecutionOrder(-40)]` |
| `KeyPickup` | `Level/` | `IPickable`: registra la llave y se destruye |
| `WorldPickup` | `Level/` | Caída al suelo, bobbing, imán y recogida por radio |
| `BossKeyDrop` | `Level/` | En la muerte del boss instancia la llave (CellBattery) |
| `ExitDoor` | `Level/` | Máquina de estados de la puerta, input [E], victoria |
| `CompactorDoorPresentation` | `Level/` | Muestrea el clip de apertura según la carga de la puerta |
| `LevelExitPressure` | `Level/` | Con todas las llaves: Overheat permanente y escalones ×2/×3/×4 |
| `ExitSpawnPressure` | `Level/` | Estático. Pasa el multiplicador a `OverheatSwarmBoost` |
| `LevelExitHud` | `Level/` | Texto de estado (llaves, carga, presión). Vive en `GameplayHud V2.prefab` |
| `GuideArrow` / `GuideArrowController` | `Level/` | Flecha sobre el jugador hacia la estación y luego hacia la puerta |
| `BelowLevelFogFade` | `Level/` | Plano de niebla falsa con fade según la altura del jugador |
| `PickupGroundFall` | raíz de `Assets/Scripts/` | Caída por gravedad sin Rigidbody para drops |
| `SpawnGroundUtility` | raíz de `Assets/Scripts/` | Snap al suelo + depenetración de un `CharacterController` al spawnear |
| `CraftingStation` | `Economy/` | Abre `CraftingUI` con [E] dentro del radio |

`PickupGroundFall` y `SpawnGroundUtility` no están en `Level/` aunque son utilidades de entorno. Tools de editor en `Level/Editor/` (`LevelExitSetupEditor`, `CompactorEscapeSetupEditor`, `GuideArrowSetupMenu`, `BelowLevelFogPrefabBuilder`, `GameplayModelsSetupEditor`): se documentan aparte.

## Objetivo de salida

1. Cada boss con `BossKeyDrop` instancia la llave en `posición + 0.75 Y` al morir, salvo que ya estén todas (`BossKeyDrop.cs:26-39`). No hace raycast: la caída la resuelve `WorldPickup`.
2. `WorldPickup` cae con `PickupGroundFall` y, al aterrizar, pasa a bobbing + imán (`WorldPickup.cs:102-139`). Radio base 1.5, imán 5, velocidad 10; el radio efectivo sale de `PlayerStatMath.GetPickupRange` (`WorldPickup.cs:150-157`).
3. `KeyPickup.OnPickedUp` → `LevelExitObjective.RegisterKey()` (`KeyPickup.cs:13`). En escena `_keysRequired: 2` (`GameplayScene.unity:34379`).
4. Con la última llave dispara `OnAllKeysCollected` (`LevelExitObjective.cs:36-48`). Escuchan la puerta, la presión, el HUD y la flecha.
5. `LevelExitPressure.ActivatePressure`: `BossManager.SetExitPhaseActive(true)`, elites deshabilitados, `EnterPermanentOverheat()` y `ExitSpawnPressure.SetActive(true, 2)` (`LevelExitPressure.cs:78-96`).
6. Cada `_minutesPerTier` (1 min) sube el escalón hasta el último del array `{2, 3, 4}` (`LevelExitPressure.cs:58-76`; escena `:34428-34432`).

## Puerta de salida

Estados (`ExitDoor.cs:7-19`): `Locked → AwaitingActivation → Charging → Ready → Used`.

| Paso | Qué pasa | Código |
|---|---|---|
| Llaves completas | `Locked → AwaitingActivation`, `OnDoorUnlocked` | `ExitDoor.cs:122-129` |
| [E] en radio | `TryStartCharging`, `OnChargeStarted` | `ExitDoor.cs:111-113, 132-146` |
| Carga | Cuenta regresiva, `OnChargeProgress` cada frame | `ExitDoor.cs:158-172` |
| Fin de carga | `Ready`, `OnDoorReady` | `ExitDoor.cs:169-171` |
| [E] otra vez | `Used` → `GameManager.TriggerVictory()` | `ExitDoor.cs:115-118` |

- No reacciona con `CraftingUI` o `LevelUpChoiceUI` abiertas (`ExitDoor.cs:90-91`).
- La distancia es solo XZ (`flat.y = 0`, `ExitDoor.cs:103-106`).
- Valores: el prefab trae 5 s de carga y radio 3 (`CompactorExitDoor.prefab:214-215`). La escena los pisa a **radio 20 y 20 s** (`GameplayScene.unity:12892-12899`).
- `ResetCharge()` vuelve a `AwaitingActivation` o `Locked` sin perder llaves (`ExitDoor.cs:149-156`).

`CompactorDoorPresentation` arma un `PlayableGraph` manual (sin Animator Controller) y hace `SetTime(progreso × largo del clip)` (`CompactorDoorPresentation.cs:140-144, 146-170`). El collider de puerta cerrada se apaga en `Ready`/`Used`; el `_Activity` del renderer de succión va de 0.2 a 1 durante la carga (`:130-133`). Resincroniza en `LateUpdate`.

## Crafteo y flecha guía

- Hay **una** `CraftingStation` en escena (`GameplayScene.unity:30110`), en `(-163.5, 56.51, 68.85)` (`:30143`), con el `Workbench.fbx` como hijo y un `Workbench Collision`. Radio de interacción pisado a 10 (`:30128`; default 3 en `CraftingStation.cs:11`).
- La estación mide distancia 3D (`CraftingStation.cs:50`); la puerta, solo XZ.
- No abre si otra UI de crafteo o de level-up está visible (`CraftingStation.cs:42-43`). Al abrir dispara `OnInteracted` (`:62`).
- `GuideArrowController` (sobre la instancia de `Arrow_Guide`, `GameplayScene.unity:25737`): a los 20 s de run apunta a la estación; con todas las llaves apunta a la puerta. Cada aparición dura 5 s o hasta interactuar (`OnInteracted` / `OnChargeStarted`) (`GuideArrowController.cs:58-93`; escena `:25825-25826`).
- `GuideArrow` flota 2.2 sobre el jugador y apunta en 3D con pulso de escala (`GuideArrow.cs:15, 64-88`).

## Utilidades de suelo

**`PickupGroundFall.Tick`** (`PickupGroundFall.cs:20-36`): gravedad −20, raycast desde +5 con alcance 85, ignora triggers. Lo usan `WorldPickup.cs:105`, `XPDrop.cs:64` y `MaterialDrop.cs:129`.

**`SpawnGroundUtility.TryResolveFootPosition`** (`SpawnGroundUtility.cs:18-71`):

1. Raycast hacia abajo en XZ. Entre todos los hits elige el más cercano a la Y de referencia, con umbral opcional. Si ninguno entra en el umbral, igual toma el más cercano (`:73-130`).
2. Si la máscara primaria falla, prueba la de fallback (`:43-49`).
3. Hasta N iteraciones de depenetración de la cápsula del `CharacterController` (`:54-67`).

La usan `OrbitalSpawnPlacement.cs:141`, `BossManager.cs:208`, `SwarmSpawner.cs:162` y `BalanceTestingController.cs:363`.

## GameplayScene: layout

Objetos raíz (transform sin padre), agrupados:

| Grupo | Objetos |
|---|---|
| Managers | `GameManager`, `DifficultyManager`, `OverheatSystem` (`HeatManager` + `BossManager`), `AudioManager`, `BalanceTuning`, `LevelExitObjective`, `LevelExitPressure` |
| Spawn | `OrbitalSpawner`, `EliteWaveSpawner`, `SpawnPoint` (`SwarmSpawner` deshabilitado, `m_Enabled: 0`, `:25409`), `SwarmEnemyPool` |
| Pools | `GameplayPools`, `XP Pool`, `MaterialPool`, `ProjectilePool` (prefab) |
| Nivel | `Map` (greybox, escala 15, 27 hijos `Cube`/`Plane`, `:30839`), `CraftingStation`, `ExitDoor` (prefab), `BelowLevelFogPlane` (prefab), `Lights`, `Global Volume` |
| Jugador / cámara | `player` (prefab), `Main Camera` (`ThirdPersonCamera`), `Arrow_Guide` (prefab) |
| UI | `UI` (14 hijos: HUD V2, menús de run, `EventSystem`, canvases de stats/inventario/logros) |
| Debug | `DebugUI`, `DEBUGTESTINVENTORY` (`DebugCrafting`), `DEBUG_InfiniteHealth` |
| Sueltos | `EnemyPro` (prefab, activo, `:10807`), `Enemy` (prefab, inactivo, `:30689`) |

Posiciones clave:

| Qué | Posición | Línea |
|---|---|---|
| Spawn del jugador (`player.prefab`) | (-150.9, 0.3, -50.7) | `:34695` |
| `ExitDoor` (`CompactorExitDoor.prefab`, escala 15.04) | (-169.8, -4.2, -243.4) | `:12913` |
| `CraftingStation` | (-163.5, 56.51, 68.85) | `:30143` |
| `ZoneSpawner_Slime` → `Slime (variant)` | (-259.7, 11.1, -179.5) | `:27751` |
| `ZoneSpawner_Drone` → `Drone (variant)` | (-29.7, 10.2, 73.3) | `:27800` |
| `BelowLevelFogPlane` (300 × 300, rotado 90° en X) | (-121, 38.4, -35.5) | `:29314` |

Los dos `ZoneSpawner` son instancias de prefab activas: 12 enemigos, radio 6, `_rearmOnOverheat: 1` (`ZoneSpawner_Slime.prefab:70-76`). En la escena se pisa `_enemyPrefab` y `_groundRaycastMask` (bits 129).

## Niebla de piso

`BelowLevelFogFade` pone `_Visibility` por `MaterialPropertyBlock`: 1 si el jugador está por encima de `planoY − 0.35`, 0 si está por debajo, con fade de 0.75 s. En 0 apaga el renderer (`BelowLevelFogFade.cs:71-105`). Crea o ajusta un `BoxCollider` trigger de 4 unidades de grosor que solo mira al jugador (`:107-121`). La niebla de `RenderSettings` está apagada; este plano es la única niebla.

## Iluminación y URP

**Escena** (`GameplayScene.unity:17-97`):

- Fog de Unity off (`m_Fog: 0`). Ambient desde el skybox (`m_AmbientMode: 0`), skybox por defecto (`Default-Skybox`, fileID 10304), sin `m_Sun`.
- Lightmaps horneados on, realtime GI off, Mixed = Shadowmask. `LightingSettings` = `Assets/New Lighting Settings.lighting` (bake res 40, atlas 1024, sin AO). Hay `LightingDataAsset` horneado.
- **No hay luz direccional.** `Lights` agrupa 4 luces **Point** (aunque se llaman "Spot Light"), naranjas `(1, 0.58, 0)`, intensidad 1000, rango 49.4, sombras hard, modo **Baked** (`m_Lightmapping: 2`) (`:3255`, `:3943`, `:15042`, `:23873`).
- `Global Volume`: global, prioridad 0, peso 1, perfil `SampleSceneProfile` (`:13219-13223`).

**`SampleSceneProfile`**: Bloom (threshold 1, intensidad 0.25, scatter 0.5), Vignette 0.2, Tonemapping Neutral, MotionBlur desactivado. El mismo perfil está también como volume por defecto del RP asset de PC (`PC_RPAsset.asset:95`).

**Pipeline**: `GraphicsSettings` usa `PC_RPAsset` (`GraphicsSettings.asset:43`). Hay dos calidades: `Mobile` → `Mobile_RPAsset` y `PC` → `PC_RPAsset`. La actual es PC (`m_CurrentQuality: 1`); Standalone usa PC y Android usa Mobile.

| Setting | PC | Mobile |
|---|---|---|
| Render scale | 1 | 0.8 |
| MSAA | off (1x) | off (1x) |
| HDR | on | on |
| Shadow distance | 50 | 50 |
| Cascadas / resolución main light | 4 / 2048 | 1 / 1024 |
| Luces adicionales | per-pixel, 4 por objeto, con sombras | per-pixel |
| Rendering path | Forward+ (`PC_Renderer.asset:56`) | Forward |
| Renderer features | SSAO (intensidad 0.4, radio 0.3, fuente depth-normals) | ninguna |

## Deuda / puntos abiertos

- **`ZoneSpawner` sí está en la escena.** `03-Spawning-y-enemigos.md` dice que no hay instancias, pero hay dos activas (Slime y Drone, 12 cada una, rearme en Overheat). Hay que corregir ese doc o sacarlas.
- **No hay luz direccional ni `m_Sun`.** El RP de PC reserva sombras de main light (4 cascadas, 2048) que no tienen luz que las use. La iluminación son 4 point lights horneadas: nada dinámico proyecta sombra real salvo lo que haga SSAO.
- **La escena pisa la puerta a radio 20 y 20 s de carga.** El prefab trae 3 y 5 s. Como el chequeo ignora Y (`ExitDoor.cs:103-106`), con radio 20 se puede activar desde otro nivel de altura sobre o bajo la puerta.
- **`GuideArrowController` resuelve una sola estación** con `FindAnyObjectByType<CraftingStation>` (`GuideArrowController.cs:35`), aunque `CraftingStation` ya contempla que haya varias (`CraftingStation.cs:40-41`). Hoy hay una; si se agregan más, la flecha apunta a una arbitraria.
- **Hay objetos sueltos en la escena**: `EnemyPro` activo en la raíz en (8.7, 0.6, -13.8), `Enemy` inactivo, `SpawnPoint` con el `SwarmSpawner` obsoleto deshabilitado y `SwarmEnemyPool` activo.
- **Nombres de plantilla.** El volume usa `SampleSceneProfile`, los lighting settings son `New Lighting Settings`, y las luces "Spot Light" en realidad son point lights.
- **El trigger de `BelowLevelFogFade` sobra**: `Update` ya recalcula el target cada frame (`BelowLevelFogFade.cs:44-54`). `OnTriggerEnter/Exit` solo repiten ese cálculo.
- **`PickupGroundFall` y `SpawnGroundUtility` están en la raíz de `Assets/Scripts/`**, fuera de `Level/`.

# Game loop — alpha 22-09-2026

El GDD del 21-7 cita `DumpsterFire-DEV-GameLoop`. Ese archivo no estaba en `docs/`. Esto es el loop que corre en `GameplayScene`.

## Scripts

`GameManager`, `DifficultyManager`, `RunSessionStats`, `RunCombatStats`, `LevelExitObjective`, `BossKeyDrop`, `KeyPickup`, `WorldPickup`, `ExitDoor`, `LevelExitPressure`, `ExitSpawnPressure`, `LevelExitHud`, `GuideArrowController`, `CompactorDoorPresentation`, `PlayerXP`.

## Estados de la run

`GameManager` distingue juego en curso, game over por muerte y victoria. `OnBossDefeated` cuenta para las stats de la run. No gana la partida. El campo de “bosses necesarios para victoria” está obsoleto.

La victoria solo sale de `ExitDoor` en estado Ready, que llama a `TriggerVictory()`. La derrota es vida del jugador a 0.

Al cerrar, `RunEndScreenUI` muestra tiempo, kills, nivel y bosses, con reintentar o volver al menú. El estilo de esa pantalla se rehízo el 21-09. `SaveManager` recibe el reporte para scrap, desbloqueos y logros.

## Llaves y puerta

1. Un boss del Overheat par muere.
2. `BossKeyDrop` en el prefab instancia el pickup. `WorldPickup` lo mueve y lo atrae. `PickupGroundFall` corrige la caída (el fix del boss pickup es del 21-09).
3. `KeyPickup` llama a `LevelExitObjective.RegisterKey()`.
4. Con `_keysRequired = 2` en la escena, `OnAllKeysCollected` pasa la puerta de Locked a AwaitingActivation.

`ExitDoor`: el jugador interactúa, la puerta carga (5 s por defecto en el componente, radio 3) y, al quedar Ready, otra interacción termina la run.

`GuideArrowController` apunta a una estación de crafting durante una ventana inicial (20 s) y, al juntar las llaves, apunta a la puerta. `CompactorDoorPresentation` es la presentación visual de esa puerta.

## Fase de salida

`LevelExitPressure.ActivatePressure` al completar las llaves:

- `BossManager.SetExitPhaseActive(true)` — no salen más bosses de Overheat.
- `OverheatEliteWaveSpawner.SetExitPhaseDisabled(true)`.
- `OverheatManager.EnterPermanentOverheat()` — buff de cadencia, la fase no se cierra por objetivo, el heat queda clavado en el clímax.
- `ExitSpawnPressure` arranca en ×2 y sube a ×3 y ×4, un escalón por minuto.

Esa presión entra al batch orbital y a la velocidad de los enemigos. No acorta el intervalo de `OrbitalSpawner` (detalle en `02-Overheat.md`).

`LevelExitHud` muestra llaves, carga de la puerta y el escalón de presión.

## Tiempo y dificultad

`DifficultyManager` escala cantidad y stats de lo que nace según los minutos de run. Se multiplica con la escala de heat y con la escala de ciclos de Overheat ya cerrados. No sustituye a ninguna de las dos.

La pausa de gameplay (`GameplayPause`) la usan el level-up, el crafting y el menú de pausa. Con timescale en 0 el heat que decae con `unscaledDeltaTime` hay que tenerlo presente al probar el residual: el decay del heat está pensado para seguir en tiempo no escalado.

## Powerups de run

`TemporaryPowerupController` y los pickups de `Assets/Scripts/Powerups/` aplican efectos temporales encima de las stats. Entran en el mismo `PlayerStats` como modificadores de fuente temporal. No sustituyen al level-up ni al crafting.

## Qué no es el cierre de la run

- Llenar el heat.
- Matar dos bosses sin recoger las llaves y usar la puerta.
- Un temporizador de Overheat. Ese campo ya no corta nada.

# Spawners de las islas superiores

Actualización del 03-11-2026. Alcance: las tres emboscadas `ZoneSpawner_Drone`, `ZoneSpawner_Slime` y `ZoneSpawner_Chaser` de `GameplayScene`. La plataforma de fabricación es la base del spawner que reemplaza a `ZoneMarker`; no es la mesa `CraftingStation` ni cambia sus reglas.

## Estado y refresco

`ZoneSpawner` conserva la autoridad de gameplay. Una entrada válida al trigger consume la zona y dispara la emboscada. `ArmedChanged` actualiza la presentación inmediatamente; `IsAvailable` expresa si la zona armada se puede usar ahora, independientemente de la distancia al jugador.

| Estado | Holograma | Cuatro lámparas |
|---|---|---|
| Armada y disponible | Visible | Encendidas de forma continua |
| Armada, temporalmente bloqueada | Visible | Apagadas |
| Consumida | Oculto | Apagadas |
| Spawner deshabilitado o run terminada | Oculto | Apagadas |

Los bloqueos temporales incluyen preparación de Overheat, tutorial, cinemática inicial, pausa y jugador muerto; no consumen la zona. El movimiento del holograma se congela con la pausa. La preparación conserva la entrada diferida existente y puede procesarla al acabar la preparación.

Con `_rearmOnOverheat` activo, `OnOverheatFinished` rearma la zona salvo si el motivo es `Interrupted`. El comienzo de Overheat no refresca; el Overheat permanente tampoco ofrece un cierre normal de ciclo. El rearme no dispara una emboscada por sí solo: si el jugador ya está dentro al terminar, debe salir y volver a entrar. Tampoco borra enemigos previamente generados por la zona.

## Prefabs y edición

El menú explícito **ScrapWaves > Level > Author Upper Island Spawners** ejecuta `ZoneSpawnerAuthoring.Run`. Requiere Edit Mode y escenas sin cambios pendientes. Genera assets guardados, actualiza los tres prefabs y migra `GameplayScene`; no construye geometría durante la partida ni se ejecuta automáticamente al importar.

- `Assets/Art/Spawners/FabricationPlatform.prefab`: base compartida de metal reparado, cama de proyección y cuatro lámparas de esquina. Se anida en los tres prefabs.
- `Assets/Art/Spawners/Materials/`: materiales propios de plataforma, lámparas y holograma. No se modifica el `Bullet.mat` compartido con otros objetos.
- `Hologram/UprightPreviewPivot`: pivote visual separado del trigger y del origen de spawn. Su hijo conserva la orientación de importación del modelo; el preview no lleva IA, colliders ni Animator.
- `ZoneSpawnerPresentation`: referencias serializadas a spawner, raíz/pivote del holograma, cuatro renderers de lámparas y materiales encendido/apagado. El Inspector permite ajustar giro (24 grados/s por defecto), amplitud vertical (0,3 m) y período (3 s).

Editar el prefab compartido para cambiar todas las bases; editar cada prefab de spawner para su preview. Mantener los transforms de la raíz, colliders, cantidades, radios y referencias de gameplay de las instancias. La migración compensa la escala no uniforme solo en el holograma para que el giro no deforme la silueta. Regenerar con el menú reconstruye los hijos visuales conocidos y reaplica los materiales de autoría: revisar o conservar las personalizaciones antes de volver a ejecutarlo.

El shader URP `Assets/Art/Spawners/Shaders/SpawnerHologram.shader` aplica el aspecto de proyección únicamente al preview. El pedido actual del diseñador autoriza este holograma aunque la guía estética anterior lo desaconsejara. Las lámparas usan emisión y materiales compartidos de encendido/apagado; no requieren cuatro luces dinámicas con sombras.

## Relación con bosses y revisión

No cambia ninguna regla de bosses: `PlayerPlatformState.CanSpawnObjectives` exige soporte real en Base; `BossManager` y `OverheatEliteWaveSpawner` respetan ese requisito y rechazan colocaciones superiores. `MapLaunchPad` bloquea nuevos lanzamientos durante Overheat. Eso sostiene el recorrido normal previsto. La muerte del último boss o la salida del último elite completa el objetivo sin comprobar la posición del jugador; la entrada nueva tras refresco cubre ese caso defensivo sin alterar los encuentros.

La revisión debe comprobar los tres prefabs y sus instancias: consumo, cierre normal e interrupción, preparación y pausa, entrada nueva después del refresco, cuatro lámparas coherentes con la disponibilidad, silueta legible durante giro/oscilación y transforms de gameplay preservados. **ScrapWaves > Spawners > Capture Fabrication Platforms** (`ZoneSpawnerPreview.Capture`) prepara capturas deterministas en una escena de preview, con gráficos habilitados. Este documento no certifica resultados de pruebas ni de revisión visual; esos resultados deben registrarse después de ejecutar la migración y validarla en Unity.

# Dormancia, pools y rendimiento — alpha 23-09-2026

La idea es que una run larga no se llene de GameObjects ni haga Instantiate/Destroy por cada enemigo. Hay tres capas. Pools por prefab para enemigos, proyectiles, áreas y VFX. Un techo global de activos. Y la dormancia por altura, que congela al enemigo que quedó en un piso de abajo en vez de borrarlo. Lo que sigue describe el código y los valores de `GameplayScene` al 23-09.

## Scripts

| Script | Rol |
|---|---|
| `EnemyPoolRegistry` | Singleton, un `EnemyPrefabPool` por prefab. También crea los pools de FX si faltan |
| `EnemyPrefabPool` | Cola de inactivos de un prefab, prewarm, crecimiento hasta `MaxSize` |
| `SwarmPooledEnemy` | Marca de pertenencia. Resetea al sacar/devolver y enruta `Despawn()` |
| `IEnemySpawnLifecycle` | Hook `OnPoolSpawn` / `OnPoolDespawn` (`IEnemySpawnLifecycle.cs:4-8`) |
| `EnemyLifecycleCoordinator` | Limpieza de fin de Overheat (solo elites) y "Clear all" de QA |
| `EnemyVerticalEngagement` | Desenganche y dormancia por altura |
| `EnemyDormancyRegistry` | Lista de dormidos y techo con expulsión |
| `EnemyRegistry` / `EnemyRegistryMember` | Lista estática de activos. Targeting, separación y cap |
| `EnemyMovementSteering` | Punto de persecución en anillo, weave y separación muestreada |
| `EnemyProjectilePool`, `EnemyTimedAreaPool`, `ExplosionRadiusVfxPool` | Pools de apoyo |
| `EnemyPoolProfiler` / `EnemyPoolProfilerHud` | Contadores de QA y HUD IMGUI |
| `SwarmSpawner` + `SwarmEnemyPool` | Legacy, `[Obsolete]` (`SwarmSpawner.cs:3`) |

## Pool de enemigos

En la escena, el registry vive en `GameplayPools` (`GameplayScene.unity:29250-29254`) con `_useEnemyPool: 1`, `_entries: []`, `_allowPoolGrowth: 1` y la ruleta `DefaultEnemySpawnRoulette`. Corre con `DefaultExecutionOrder(-42)` (`EnemyPoolRegistry.cs:10`).

| Origen del pool | Prewarm | Máximo | Dónde |
|---|---|---|---|
| Entrada manual (`Entry`) | 16 | 128 | `EnemyPoolRegistry.cs:17-18` (en escena no hay) |
| Prefab de la ruleta | `max(8, BatchSize × 2)` | 256 | `EnemyPoolRegistry.cs:115-116` |
| Prefab no registrado, primer `TryGet` | 8 | 128 | `EnemyPoolRegistry.cs:173` |

La ruleta tiene seis prefabs: `EnemyPro`, `Drone`, `Chaser` y sus tres variantes. Los batch son 4/4/3/2/2/2, así que todos quedan en el piso de 8. Al cargar se instancian 48 enemigos inactivos. Los elites de la oleada entran por el autoregistro (8/128). `BossManager` no pasa por el pool: hace `Instantiate` (`BossManager.cs:189`).

`EnemyPrefabPool.TryGet` saca de la cola. Si está vacía y hay crecimiento, crea hasta `_maxSize`. Si no, devuelve `null` (`EnemyPrefabPool.cs:42-64`). El prewarm se recorta a `maxSize` (`:30`).

## Ciclo de vida de un enemigo orbital

1. `OrbitalSpawner.SpawnRouletteWave` corta el batch si `EnemyRegistry.ActiveCount >= MaxActiveEnemies` (`OrbitalSpawner.cs:231`).
2. `OrbitalSpawnPlacement` pide `EnemyPoolRegistry.TryGet`. Si falla, hace `Instantiate` sin pool (`OrbitalSpawnPlacement.cs:118-126`).
3. `EnemyPrefabPool.ActivateInstance`: `SetActive(true)` (entra a `EnemyRegistry` por `EnemyRegistryMember.OnEnable`), `_leasedCount++` y `NotifySpawned()` (`EnemyPrefabPool.cs:119-124`).
4. `SwarmPooledEnemy.ResetForPoolSpawn`: `EnemyHealth.PrepareForPoolSpawn`, `PrepareForSpawn` de los followers, agrega los drops meta si faltan, `EnemyVerticalEngagement.EnsureOn` + `ResetEngagement`, y llama `OnPoolSpawn` en cada `IEnemySpawnLifecycle` (`SwarmPooledEnemy.cs:59-75`).
5. Se posiciona sobre el suelo. Si no encuentra pie válido, vuelve al pool o se destruye (`OrbitalSpawnPlacement.cs:158-163`).
6. Vive. Puede desengancharse y dormirse (ver abajo).
7. Muerte: `EnemyHealth.CompleteDeath` dispara `OnDied`, stats y `FinalizeDeath`. Si está bound, `Despawn()`; si no, `Destroy` (`EnemyHealth.cs:165-185`).
8. `Despawn()` va al pool legacy, al registry o a `Destroy` (`SwarmPooledEnemy.cs:41-57`).
9. `EnemyPrefabPool.Release`: `NotifyDespawned()` (followers `OnDespawned` + `OnPoolDespawn`, que despierta a un dormido), después `SetActive(false)` (sale de `EnemyRegistry`), reparent, `_leasedCount--` y vuelta a la cola (`EnemyPrefabPool.cs:66-81`).

Implementan `IEnemySpawnLifecycle`: `EnemyBehaviorBase`, `EliteObjectiveMarker` y `EnemyVerticalEngagement`. El orden importa. `OnPoolDespawn` corre antes del `SetActive(false)` porque desactivar no restaura los `.enabled` que tocó la dormancia (`EnemyVerticalEngagement.cs:396-399`).

`EnemyLifecycleCoordinator.OnOverheatEnded` solo limpia elites de la oleada; el swarm orbital queda (`EnemyLifecycleCoordinator.cs:13-22`). `ClearAllForQa` libera todos los pools, el legacy, el orbital, las zonas y los elites, y resetea los contadores (`:25-56`).

## Techo de activos

`MaxActiveEnemies` sale del perfil si hay uno y, si no, del fallback serializado (`OrbitalSpawner.cs:101`). `BalanceTuningHub` inyecta `SpawnBalanceProfile.asset`, que tiene `_maxActiveEnemies: 300` (línea 113 del asset). El fallback de escena también es 300 (`GameplayScene.unity:27720`). El recorte es por enemigo dentro del batch: el loop hace `break` apenas se llega al techo.

`EnemyRegistry.ActiveCount` cuenta todo lo registrado, **dormidos incluidos** (`EnemyRegistry.cs:10`, comentario en `:59-61`). El profiler separa `AwakeActiveCount = ActiveCount − DormantCount` (`EnemyPoolProfiler.cs:19`).

## Vertical engagement y dormancia

| Parámetro | Valor | Dónde |
|---|---|---|
| `_belowThreshold` | 40 u (`player.y − enemy.y`) | `EnemyVerticalEngagement.cs:29` |
| `_dormancyAfterSeconds` | 10 s | `:32` |
| Sonda de suelo al dormir | +2 u hacia arriba, 60 u hacia abajo, capas Terrain+Default | `:14-15`, `:389-394` |
| Techo de dormidos | 150 | `EnemyDormancyRegistry.cs:16` |

Ningún prefab ni la escena serializan el componente: se agrega en runtime con `EnsureOn`, así que rigen los defaults.

1. **Desenganchado.** Si el enemigo queda más de 40 u por debajo del jugador, su id entra a `s_DisengagedIds` (`:169-179`). `EnemyRegistry` lo saltea para targeting, succión y separación (`EnemyRegistry.cs:62`, `:440`). Si está arriba o en el mismo piso, sigue enganchado y puede caer hacia el jugador.
2. **Dormido.** Después de 10 s seguidos abajo, `EnterDormancy` (`:213-276`): destruye el `WeaponMovementFreezeStatus`, apaga behaviors antes que followers, pone el rigidbody kinematic, lo apoya en el piso, limpia knockback e invencibilidad, y apaga CharacterController, colliders, renderers y `EnemyHitFeedback`. Queda en el mundo y se registra en `EnemyDormancyRegistry`.
3. **Despertar.** Cuando el jugador baja a menos de 40 u, `ExitDormancy` restaura el estado cacheado. Si tiene behavior, reactiva solo el behavior y deja que su `OnEnable` decida los followers (`:279-316`).
4. **Techo.** Con más de 150 dormidos, expulsa primero los de prefabs cuyo pool no tiene stock y después los más lejanos. Los pooled vuelven con `Despawn()`; el resto se destruye (`EnemyDormancyRegistry.cs:75-124`).

Exenciones: cualquier `GigaWormBehavior` (`EnsureOn` devuelve null, `Awake` fuerza exento; `:92-106`). Los elites de la oleada se marcan después de obtener la instancia (`OverheatEliteWaveSpawner.cs:317-321`). Los bosses, en `BossManager.cs:245-251`. `ResetEngagement` devuelve el valor de fábrica al reciclar, así un elite reciclado no queda exento para siempre (`:72-79`, `:148-154`).

## Steering

`EnemyMovementSteering` no asigna slots únicos. Cada enemigo sortea un ángulo de slot y una fase de weave (`EnemyMovementSteering.cs:222-226`). Persigue un punto en un anillo alrededor del jugador, de radio `orbitRadius`, que se achica cerca para que el melee cierre el contacto (`:194-201`). La separación usa un caché de posiciones que se refresca una vez por frame desde `EnemyRegistry.CollectActive`, sin desenganchados (`:114-138`). Toma `maxSeparationSamples` vecinos con stride, con costo O(muestras) y no O(n) (`:141-178`). El perfil `MeleeSwarm` usa orbit 1.8, separación 0.7 con radio 1.25 y 8 muestras (`:18-26`).

## Pools de apoyo

`EnemyPoolRegistry.EnsureExists` crea los tres si faltan (`EnemyPoolRegistry.cs:77-79`). En escena ya están sobre `GameplayPools`.

| Pool | Usuarios | Escena | Tope efectivo |
|---|---|---|---|
| `EnemyProjectilePool` | `FlyingRangedBehavior`, `BomberDroneBehavior` | prefab asignado, 32 iniciales, máximo 256 por prefab (`:29267-29271`) | Sí, cuenta el total por prefab (`EnemyProjectilePool.cs:158-161`) |
| `EnemyTimedAreaPool` | `FireArea` (Hellfire), `EnemyC4` (Bomber), `CorrosiveSlimeArea` | 4 / 32 por prefab (`:29284-29285`) | **No**. Ver deuda |
| `ExplosionRadiusVfxPool` | `ExplosionRadiusVfx.Spawn` | 8 / 32 (`:29298-29299`) | **No**. Ver deuda |

Los tres, si no hay instancia o se llega al tope, caen a `Instantiate` y el objeto se destruye al terminar. El comentario "Pooling = TODO futuro" de `EnemyProjectile.cs:9` ya no describe el runtime: `Consume()` devuelve al pool (`:109-110`).

## Profiler y HUD

`EnemyPoolProfiler` cuenta Instantiate, Destroy, Get y Release del pool, más `RegistryActiveCount`, `DormantCount`, `AwakeActiveCount` e `InactiveEnemyObjects`. Este último se calcula con `FindObjectsByType<EnemyHealth>` (`EnemyPoolProfiler.cs:8-46`). Los contadores de alloc los alimentan también los pools de proyectiles, áreas y VFX: no son solo de enemigos.

`EnemyPoolProfilerHud` está en `GameplayPools` con `_visible: 0` (`GameplayScene.unity:29312`). Solo dibuja si `_visible` y `QaPanels.Active == Qa`, o sea con el panel F1 (`EnemyPoolProfilerHud.cs:28`). No tiene hotkey propio ni setter, así que en `GameplayScene` nunca se ve. Tampoco muestra los dormidos. Ese dato está en el menú F3, `QaCoreLoopMenu.cs:182`.

## Legacy

`SwarmSpawner` está en `SpawnPoint` con `m_Enabled: 0` (`GameplayScene.unity:25409`). Su `_maxActiveEnemies: 200` no aplica. En cambio, `SwarmEnemyPool` está activo y habilitado (`:34234`) con `Enemy.prefab`, 64 iniciales y máximo 512, y su `Awake` hace el prewarm completo (`SwarmEnemyPool.cs:83-87`).

## Tests

- `Assets/Tests/Editor/EnemyDormancyTests.cs`: dormancia que deja el objeto inerte y restaura el estado exacto, `OnPoolDespawn` que despierta, exento que nunca duerme, `ResetEngagement` que limpia el exento de un elite reciclado, techo que expulsa al más lejano, y `EliteObjectiveMarker` / contador de elites cuando uno sale sin morir.
- `Assets/Tests/Editor/EnemyPoolLifecycleTests.cs`: `PrepareForPoolSpawn` limpia invencibilidad y rellena vida. `Despawn` enruta al registry cuando está bound.
- Sin cobertura: la prioridad por pool sin stock en la expulsión, los topes de `EnemyPrefabPool` y los pools de FX.

## Deuda / puntos abiertos

- **`SwarmEnemyPool` legacy instancia 64 `Enemy.prefab` al cargar la escena** aunque `SwarmSpawner` está deshabilitado. Son objetos muertos en memoria. Sacar el componente o bajarle `_initialPoolSize`.
- **El tope de `EnemyTimedAreaPool` no funciona.** `CountForPrefab` devuelve `queue.Count`, que es solo lo inactivo (`EnemyTimedAreaPool.cs:108-112`). Con la cola vacía siempre da 0 < 32 y crea otra. Pasa lo mismo en `ExplosionRadiusVfxPool.TotalCount() => _inactive.Count` (`:87`). El crecimiento no tiene techo.
- **Con el pool agotado, el orbital spawnea sin pool.** Los 256 por prefab caen a `Instantiate` (`OrbitalSpawnPlacement.cs:123-126`). Esas instancias no pasan por `NotifySpawned`, así que no reciben `EnemyVerticalEngagement` ni los drops meta, salvo que el prefab ya los traiga. Nunca se duermen y mueren con `Destroy`. El comentario de `EnemyDormancyRegistry.cs:10-12` dice que el tipo sin stock "deja de spawnear", y no es así.
- **Los dormidos cuentan para el techo.** 150 dormidos sobre 300 de `MaxActiveEnemies` dejan como mínimo 150 despiertos en el piso del jugador. Hay que decidir si el techo debería mirar `AwakeActiveCount`.
- **`EnemyPoolRegistry.Release` no hace nada si el prefab no está registrado** (`:191`). La instancia queda activa y nadie se entera.
- **El HUD del profiler es inalcanzable en `GameplayScene`** (`_visible: 0`, sin toggle) y no muestra dormidos. Los contadores de Instantiate/Destroy mezclan enemigos con proyectiles, áreas y VFX.
- **Comentarios viejos.** El summary de `EnemyVerticalEngagement.cs:4-8` todavía dice que el enemigo "vuelve al pool" tras el sleep; hoy queda congelado. `EnemyProjectile.cs:9` dice "Pooling = TODO".
- **`BossManager` usa `Instantiate` sin pool** (`BossManager.cs:189`) y no llama a `EnemyPoolProfiler.RegisterInstantiate`, así que los bosses no aparecen en los contadores. Es poco volumen.

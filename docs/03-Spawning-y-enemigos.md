# Spawning y enemigos — alpha 22-09-2026

Reemplaza `DumpsterFire-DEV-Spawning.docx` en lo que el código hace hoy. La intención (no vaciar la arena entre Overheats, tres capas de presión) se mantiene. El spawner principal ya no instancia y destruye sin pool.

## Capas de presión

1. **Constante.** `OrbitalSpawner` sigue al jugador en un anillo y tira de una ruleta de tipos.
2. **Puntuada.** Oleada de elites o boss según la paridad del Overheat. `ZoneSpawner` existe para emboscadas por trigger y se rearma en cada Overheat; en `GameplayScene` no hay instancias.
3. **Terminal.** `ExitSpawnPressure` mientras el jugador va a la puerta. Ver `04-Game-Loop.md`.
4. **Acumulada, desde el 22-09.** Cada Overheat cerrado acorta el intervalo orbital, sube el batch y sube la vida de lo que nace, con techo. Ver `02-Overheat.md`. Eso queda aunque el heat residual ya haya llegado a 0.

El swarm común no se borra al cerrar el Overheat. La única pausa de spawn es el decay residual mientras el heat sigue en el primer tramo o por encima.

## Quién spawnea

| Componente | Rol | Estado |
|---|---|---|
| `OrbitalSpawner` | Presión constante. Ruleta, anillo, cap global | Spawner de la run |
| `EnemySpawnRoulette` + `EnemySpawnRouletteConfig` | Pesos, batch y variantes | Asset `DefaultEnemySpawnRoulette` |
| `OrbitalSpawnPlacement` | Punto en el anillo, snap al suelo | Activo |
| `EnemyPoolRegistry` / `EnemyPrefabPool` / `SwarmPooledEnemy` | Pool por prefab de la ruleta | Activo. El DEV del 21-7 decía Instantiate/Destroy sin pool |
| `OverheatEliteWaveSpawner` | Oleada impar | Activo. En escena: 9 elites |
| `BossManager` | Boss en ciclo par, Instantiate (sin pool) | Activo |
| `ZoneSpawner` | Emboscada por zona | Código listo, sin uso en la escena principal |
| `SwarmSpawner` + `SwarmEnemyPool` | Un solo prefab, pool viejo | Obsoleto. Solo QA legacy |
| `DifficultyManager` | Escala cantidad y stats por tiempo de run. No es la cadencia | Activo, se compone con heat y ciclos |

En `GameplayScene` el anillo orbital está en radio 25–35. El intervalo base que manda es el del perfil: 15 s, luego × escala de heat × escala de ciclos completados, con piso de 0.05 s.

Batch de cada tick:

```
round(batchDeLaRuleta × escalaDeDificultad × max(escalaDeHeat, presionDeSalida) × escalaDeCiclos)
```

Se recorta si `EnemyRegistry.ActiveCount` llega a `MaxActiveEnemies` (300 en el perfil).

## Ruleta

`EnemySpawnKind` cubre slimes, drones, chasers y variantes. Las entradas marcadas variante arrancan con peso base 0 en el asset por defecto.

Bonus de variante en el asset vivo: cada 120 s, +4 de peso por paso (`_variantWeightBonusPerStep: 4`). `BALANCE_NOTES.md` todavía dice +3; el asset y la ruleta usan +4.

Esas variantes (Hellfire, Bomber, Shocker, etc.) son spawn orbital. No cierran el Overheat. Los elites que cierran el ciclo impar son los prefabs de la oleada (`Slime_Elite`, `Drone_Elite`, `Chaser_Elite`).

## Comportamientos

Infra compartida: `EnemyHealth`, `EnemyFollow` / `SimpleFollow`, `EnemyBehaviorBase`, `EnemyRegistry`, `EnemyMovementSteering` (slots para no apilarse), `EnemyKnockbackReceiver`.

| Behavior | Rol |
|---|---|
| `FlyingRangedBehavior` | Drone a distancia, proyectiles con pool |
| `ChargerEnemyBehavior` | Embestida. Modo Shocker opcional |
| `HellfireSlimeBehavior` | Variante: lanzamiento, explosión, área de fuego |
| `BomberDroneBehavior` | Variante: dash, C4, fase drone |
| `DestroyerBehavior` | Boss: caza y succión en umbrales de vida, punto débil en la boca |
| `GigaWormBehavior` | Boss: subterráneo, telegraph, salto, escupitajo. Exento del vertical engagement |
| `EnemySeekingMissile`, `EnemyC4`, `FireArea`, áreas corrosivas | Soporte de esos behaviors |

El slime base no usa `EnemyBehaviorBase`; persigue con follow. Hay animación skinned y hop de slime cableados en commits del 21-09 (`EnemySkinnedAnimBuilder`, `EnemyAnimPhaseOffset`).

`Boss.prefab` y `Boss_2.prefab` alternan en los ciclos pares y llevan `BossKeyDrop`. `Destroyer_Boss` y `Stalker` también tienen drop de llave en el prefab. El `BossManager` de la escena referencia los bosses genéricos, no al Stalker. El manager fuerza la vida al spawnear (1500 en escena), añade drops de scrap/powerup y marca exención de vertical engagement. No añade `BossKeyDrop` en runtime: la llave depende del prefab.

## Vertical engagement

`EnemyVerticalEngagement`: si el enemigo queda muy por debajo del jugador (umbral por defecto 40 unidades), pasa a desenganchado y, tras unos 10 s, a dormancia. La dormancia congela y oculta; no devuelve la instancia al pool. Bosses, elites de objetivo y `GigaWormBehavior` van exentos. `EliteObjectiveMarker` evita que un elite que desaparece sin morir deje el Overheat colgado.

## Pools de apoyo

`EnemyPoolRegistry.EnsureExists()` también deja listos pools de proyectiles de enemigo, áreas temporales y VFX de explosión. Un comentario viejo de “pooling TODO” en `EnemyProjectile` ya no describe el runtime.

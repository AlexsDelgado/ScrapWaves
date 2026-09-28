# Overheat — alpha 22-09-2026

Reemplaza `DumpsterFire-DEV-Overheat.docx` (entrega del 21-7). La intención de diseño de aquel documento se mantiene. Los números de `SpawnBalanceProfile` y la presión permanente por ciclos completados son posteriores.

## Intención

El Heat es el reloj que controla el jugador: matar llena la barra, llenarla obliga a una fase de riesgo con un objetivo. También es la curva de poder (el kit de las armas lee el heat) y la fuente de las llaves (los bosses solo nacen dentro de un Overheat par).

Al juntar las llaves, la salida es un Overheat permanente con presión creciente.

Al cerrar un Overheat normal el swarm común no se borra. Queda heat residual, el spawn se pausa mientras ese residual sigue alto, y después el loop sigue. Desde el 22-09, cada Overheat ya cerrado deja el spawn más denso aunque la barra haya decaído.

## Scripts

`HeatManager`, `OverheatManager`, `OverheatSwarmBoost`, `EnemyHeatOnKill`, `EnemyLifecycleCoordinator`, `ExitSpawnPressure`, `LevelExitPressure`, `BossManager`, `OverheatEliteWaveSpawner`, `OrbitalSpawner`.

El perfil que pisa los defaults está en `Assets/Data/Balance/SpawnBalanceProfile.asset`, inyectado por `BalanceTuningHub` en `GameplayScene`.

## Modelo de puntos

Dos tramos de igual esfuerzo, mapeados de forma no lineal a la barra:

```
barra = (heat / A) * 0.8                  si heat <= A
barra = 0.8 + ((heat - A) / B) * 0.2      si heat > A
```

`NormalizedHeat` es esa barra (0–1). El escalado de spawn usa otro eje: el ratio lineal `heat / capacidad`. Con tramos iguales, ratio 0.5 es el 80 % visual.

Valores que corren en `GameplayScene` (perfil), no los defaults del script:

| Parámetro | Perfil vivo | Default del script (21-7 / fallback) |
|---|---|---|
| Tramo A (0–80 % visual) | 60 | 50 |
| Tramo B (80–100 % visual) | 60 | 50 |
| Heat por kill | 1 | 5 |
| Escalado del requisito al cerrar un Overheat | ×1.4 | ×1.12 |
| Decay residual | 25 puntos/s (unscaled) | 12 |
| Intervalo base de spawn orbital | 15 s | el spawner de escena tiene otro fallback |
| Tope de enemigos activos | 300 | — |

Primer Overheat del perfil: 120 puntos, 1 por kill, 120 kills. El segundo tramo sigue siendo solo el 20 % visual de la barra.

Al cerrar un Overheat, `ApplyEscalationAfterOverheat` multiplica `_heatRequirementEscalation` por 1.4 y suma un ciclo completado. Los dos tramos crecen con ese multiplicador. `ResetHeatProgressAndEscalation()` vuelve el requisito a 1 para menú o partida nueva.

Durante el decay posterior, `AddHeat` no suma. Los kills del swarm que quedó no recargan la barra.

## Fases

**Normal.** Spawn orbital y heat por kills.

**Intermedia.** `CurrentHeat >=` tramo A y todavía no está al máximo. `OverheatSwarmBoost` pone velocidad de swarm ×2. El spawn orbital sigue (salvo que aún esté el decay de un Overheat anterior).

**Overheat.** Sin temporizador. El heat queda al máximo, así que las sinergias de arma están al tope, más un buff de cadencia ×1.5 (`OverheatManager._fireRateMultiplier` en el prefab del jugador). El escalado de spawn por heat se suprime durante la fase. El campo `_overheatDuration` (75 en el prefab) está deprecado y no corta la fase.

**Permanente.** `EnterPermanentOverheat()`, disparado al juntar las llaves. Mismo buff de cadencia. `NotifyOverheatObjectiveCleared` no cierra la fase. Solo un `EndOverheat(Interrupted)` (al desactivar el manager) sale de ella. La presión de esta fase la lleva `ExitSpawnPressure`, no el boost intermedio.

## Objetivo según la paridad

| Ciclo | Objetivo | Quién lo cierra |
|---|---|---|
| Impar (1, 3, 5…) | Oleada de elites | Muere el último elite trackeado |
| Par (2, 4, 6…) | Boss | Muere el último boss y suelta llave |

En `GameplayScene` la oleada impar son 9 elites: 3 slime, 3 drone, 3 chaser. Esos elites van exentos del vertical engagement.

`BossManager` en la escena: solo ciclos pares, HP 1500 al spawnear, distancia 35. El default del script sigue en 400 HP y distancia 12; no es lo que corre la escena.

`_multiBossOverheatCycle = 3` con `_spawnOnlyOnEvenCycles` sigue siendo una config muerta: el ciclo 3 es impar y los bosses solo salen en pares. El multi-boss (2) no se dispara con los valores de escena.

En la fase de salida, bosses y oleada de elites quedan inhibidos.

## Cierre, residual y presión de los ciclos siguientes

`_clearSwarmOnOverheatEnd` está en false. Al cerrar:

1. `EnemyLifecycleCoordinator.OnOverheatEnded()` limpia los elites de la oleada, no el swarm orbital.
2. Se aplica la escalada del requisito y se apaga el boost de overheat.
3. Heat residual: si `_heatAfterOverheat` es 0 (así está el prefab), residual = tramo A + 50 % del tramo B, cerca del 90 % de la barra. Si el campo es mayor que 0, se usa ese valor.
4. Decay a 25 puntos/s hasta 0.
5. `OrbitalSpawner.CanSpawnByHeat()` pausa el spawn solo mientras el decay está activo y el heat sigue en el primer tramo o por encima. Un heat alto por kills normales no pausa el spawn.

Además, cada ciclo ya completado modifica el spawn para el resto de la run (`HeatManager`, valores del perfil):

| Escala | Paso por ciclo cerrado | Límite |
|---|---|---|
| Intervalo orbital | −0.25 (se multiplica, piso 0.6) | no baja de 0.6× |
| Tamaño de batch | +0.25 | techo 2× |
| Vida al spawnear | +0.25 | techo 2× |

Ejemplo de intervalo: base 15 s × escala de heat × esta escala. Tras un ciclo cerrado el piso de la escala de ciclo es 0.75; tras dos, 0.6, y ahí se queda.

## Presión de salida

Al juntar las llaves, `LevelExitPressure` enciende `ExitSpawnPressure` con escalones ×2, ×3 y ×4, un minuto entre cada uno.

`OverheatSwarmBoost.SpeedMultiplier` es el máximo entre el boost intermedio (×2 si está activo) y la presión de salida. El batch orbital usa `max(escala de heat, ExitPressureSpawnMultiplier)`.

`ExitSpawnPressure.SpawnRateMultiplier` acorta el intervalo en `SwarmSpawner`, que está obsoleto. `OrbitalSpawner.EffectiveSpawnInterval()` no lo lee. En la salida, la presión sube el batch y la velocidad de los enemigos. La cadencia entre ticks orbitales no se acelera por ese multiplicador.

## Sinergias de arma con el heat

Siguen alimentadas por `NormalizedHeat`. Durante cualquier Overheat (normal o permanente) la barra está al 100 % y el buff de cadencia ×1.5 está activo. Los umbrales concretos por arma viven en cada `WeaponData` / tuning y en `referencia-codigo/DumpsterFire-WeaponSystem.docx`. El cuadro del DEV del 21-7 (cañón +15 % por tramo, cohetes, mortero, lanzallamas, blade) es la intención de diseño; después de los pases de balance 0.3 y 0.4 hay que leer el asset, no esa tabla, antes de citar un porcentaje.

## Anti-softlock

| Situación | Cobertura |
|---|---|
| Muere el último boss o elite | Sí, handlers por instancia |
| No hay prefab u objetivo spawneable | Sí, cierre si no hubo objetivo |
| Overheat permanente | No debe terminar por objetivo |
| Objetivo vivo pero inalcanzable | Sigue abierto. El vertical engagement y `EliteObjectiveMarker` reducen el caso del elite que se va del mapa sin morir |
| Manager desactivado a mitad de fase | `Interrupted` sigue aplicando la escalada ×1.4 |

## Deuda que el DEV del 21-7 ya marcaba y sigue abierta

- El boost de velocidad de fase llega por `OverheatSwarmBoost` a quien lo lee (`EnemyFollow`). Un follower que no lo consulte no acelera.
- No hay watchdog de objetivo inalcanzable.
- `Interrupted` escala el próximo ciclo.
- Quedan restos de temporizador y la config de multi-boss en ciclo impar.

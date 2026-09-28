# Deltas y huecos — 22-09-2026

Qué hay que actualizar respecto de cada generación de documentos, y qué no conviene dar por cerrado.

## Desde la entrega del 21-7

El diseño de loop de esa entrega (heat residual, swarm que no se borra, level-up sin armas, crafting, Overheat sin timer, salida permanente) es el juego actual. Estos puntos de esos docs ya no describen `main`:

| Tema | Doc del 21-7 | Código / escena al 22-09 |
|---|---|---|
| Puntos de heat A/B | 50 / 50 | 60 / 60 en el perfil |
| Heat por kill | 5 (20 kills al primer Overheat) | 1 (120 kills al primero) |
| Escalada al cerrar | ×1.12 | ×1.4, y además escala de spawn por ciclos cerrados |
| Decay residual | ~12 / s | 25 / s |
| Vida y distancia del boss | 400 HP, 12 u (defaults) | 1500 HP, 35 u en `GameplayScene` |
| Pool del orbital | Instantiate/Destroy, sin pool | `EnemyPoolRegistry` |
| `SwarmSpawner` | Spawner legacy todavía descrito como vía | Marcado obsolete |
| Presión tras cada Overheat | No estaba | Intervalo, batch y vida permanentes por ciclo cerrado |
| Meta, logros, iconos, menú de objetivos | Fuera de esos DEV | Implementado en agosto–septiembre |
| Rear threat, combat text en mundo, animación Playables, firepoints | No estaban en el paquete del 21-7 | Specs en `referencia-codigo/` |
| StatSystem de julio | `CurrentValue = base + bonus` sumados | `(base + aditivos) × producto de multiplicadores` |
| Game loop y QA | Citados, sin archivo en la descarga | `04` y `06` de esta carpeta |

El GDD de esa misma entrega, en la parte de diseño (islas arriba, scrapyard abajo, bajar al Overheat, enemigos que caen al piso de abajo), no es el loop de `GameplayScene`. La parte técnica de ese GDD sí apunta a los scripts correctos, con dos documentos que no se descargaron.

## `docs/old/`

`Dumpster Fire - Game Treatment`, `Scrap Waves HC`, `Spec Doc - Combat Design` y `Spec Doc - Player & Progression System` son la generación anterior. Sirven para ver intención de mapa, fantasía y el primer diseño de overheat. No uses sus tablas de XP, de armas por level-up ni de spawn como spec del alpha.

## `docs/New/` — vigentes, con estos añadidos

Esos ocho docx están copiados en `referencia-codigo/` porque describen el runtime y el código les coincide. No los reescribí. Desde que se escribieron, `main` sumó o movió esto:

| Doc | Sigue siendo la referencia de | Añadir al leerlo |
|---|---|---|
| Weapon System | Cinco armas, daño, munición manual, habilidades, riesgos | Balance 0.3–0.4 tocó assets (cohete, blade, daño). El bug de crítico al 100 % que el propio doc describe sigue siendo el defecto a mirar. El ciclo manual sin cooldown que el doc marca también sigue en el código |
| Automatic Firepoint | Monturas, wearables, orden pose-antes-de-disparar | Sin cambio de arquitectura en los commits de septiembre |
| Movement | Máquina Rigidbody: salto, dash, crouch, slide, stun, knockback | El commit `53ef237` no cambia la máquina. Cambia datos: `MovementSpeed` base 6.8, `JumpHeight` base 3, y ajustes de física del proyecto / escena |
| Player Animation | Playables del jugador, tres capas, aim de huesos | El 21-09 se sumó animación skinned de enemigos y hop de slime. Eso no entra en este doc; está del lado enemigo (`03`) |
| Combat Feedback | Director, números, muerte, hit-stop, accesibilidad | El 22-09 se corrigió feedback de muerte y la detección de golpe / aim de la espada. Conviene revalidar la sección de reacciones si se cita un caso de blade |
| Rear Threat | Sensor, snapshot, mesh en la cadera | El widget de sensor de enemigos del 14-09 encaja con este sistema |
| UI Presentation | Título, settings, HUD authored, menús de run | Después: restyle de fin de run, playlists de BGM, iconos de logros y de armas, `StatAttributionPanel` en la pausa (dev), logros en inglés |
| Stat System (el de New, no el de julio) | `StatType`, modificadores, ruleta, pasivos, XP | El panel de atribución en pausa es un consumidor nuevo. No cambia la ecuación |

## Huecos de producto, no de documentación

Cosas que el código deja abiertas y un spec “final” no debería pintar como cerradas:

1. **Config muerta de multi-boss.** Ciclo 3, bosses solo en ciclos pares. Nunca spawnea el segundo boss de ese ciclo con los valores de escena.
2. **Presión de salida y cadencia orbital.** El batch y la velocidad suben. El intervalo de `OrbitalSpawner` no usa `SpawnRateMultiplier`.
3. **`ZoneSpawner` sin colocar** en la escena principal. El diseño de emboscadas regionales no está en el mapa que se juega.
4. **Dos pisos del GDD.** Dirección de nivel, no regla del loop.
5. **Crítico saturado** por la definición de `CriticalChance` y por el powerup de daño extra. Documentado por el spec de armas como defecto de corrección.
6. **Cooldown de ciclo manual** no se aplica en `EndManualMode`. Con una sola arma, la munición se rellena y el manual vuelve a empezar enseguida.
7. **Cooldown de habilidad** solo avanza mientras esa arma está en manual.
8. **`WeaponLevelUpHandler` huérfano.** Si alguien lo vuelve a cablear al orquestador, las armas volverían a salir del level-up y chocarían con el crafting.
9. **HUD viejo.** `PlayerBarsHud` es el HUD de la run. `UIManager` y `SurvivorHud` siguen en el proyecto como vía alternativa.
10. **Objetivo de Overheat inalcanzable** todavía puede dejar la fase abierta. Vertical engagement y el marker de elites cubren una parte.

## Commits que mueven documentación (21-07 → 22-09)

No hace falta recontar el arte ni los FBX. Estos commits cambian lo que un spec tiene que decir:

- 02-08 a 18-08: cañón, cohete, lanzallamas, blade, mortero, VFX, save y desbloqueos, advanced tinkering, flecha guía.
- 18-08 a 25-08: combat text en mundo, menú principal, sandbox de pasivos.
- 01-09 a 08-09: bosses, comportamientos nuevos, challenges y meta, auto fire point, pausa, balance 0.2.x.
- 14-09: UI y widget de sensor.
- 20-09 a 21-09: balance, elites y vertical engagement, fin de run y BGM, iconos, animaciones de enemigos, audio, muerte y aim de la blade.
- 22-09: balance 0.3 → 0.4, presión de spawn por Overheats cerrados, panel de atribución de stats, logros en inglés, botón dev de max upgrades, valores de movimiento.

Último commit de esta foto: `53ef237` en `main`.

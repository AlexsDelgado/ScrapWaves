# Documentación alpha — 22-09-2026

Snapshot de diseño y de implementación para Scrap Waves (nombre de producto en los docs históricos: Dumpster Fire). La rama de referencia es `main`, commit `53ef237` (22-09-2026, “Movement changes”).

Esta carpeta reemplaza, para el día a día, la mezcla de tres generaciones que está en `docs/`:

| Origen | Qué es | Qué hacer con ella |
|---|---|---|
| `docs/old/` | Treatment, high concept y spec docs de diseño (combate, progresión). | Histórico. El loop de islas / underlevel y varias reglas de XP ya no son el juego que corre. |
| `docs/21-7 ultima documentacion entregada/` | Última entrega de diseño + specs DEV (21-07-2026). | Base correcta del loop (heat residual, crafting, level-up sin armas). Los números de balance y el spawning quedaron atrás. |
| `docs/New/` | Specs de código escritos por el otro programador. | Siguen siendo la referencia profunda de armas, movimiento, animación, firepoints, feedback, rear threat, UI y stats. Copia en `referencia-codigo/`. |
| Esta carpeta | Lo que hay que leer como estado alpha del 22-09. | Empieza por este índice y por `01`–`06`. Los huecos vivos están en `07`. |

## Qué faltaba en la descarga

El GDD del 21-7 nombra estos documentos en la sección técnica:

| Documento citado | En la carpeta `docs/` |
|---|---|
| DumpsterFire-DEV-Overheat | Sí, en la entrega del 21-7. Reescrito aquí: `02-Overheat.md`. |
| DumpsterFire-DEV-Spawning | Sí. Reescrito aquí: `03-Spawning-y-enemigos.md`. |
| DumpsterFire-DEV-Player_Progression | Sí. Actualizado aquí: `05-Progresion-crafting-y-meta.md`. |
| DumpsterFire-StatSystem | Hay dos. El de julio describe la fórmula vieja. El de `docs/New/` describe el runtime actual y está copiado en `referencia-codigo/`. |
| DumpsterFire-WeaponSystem | Sí, en `docs/New/`. |
| DumpsterFire-Movement System | Sí, en `docs/New/`. |
| DumpsterFire-DEV-GameLoop | No está el archivo. Reconstruido desde código: `04-Game-Loop.md`. |
| DumpsterFire-DEV-QA | No está el archivo. Reconstruido desde código: `06-QA-y-debug.md`. |

El GDD también enlaza un Figma, un tablero de Miro y varios Google Docs (UI, arte, combat design, player progression y otros). Esos enlaces no traen el archivo local correspondiente con el mismo nombre. Los únicos spec docs de diseño descargados en `docs/old/` son Combat Design y Player & Progression.

## Archivos de esta carpeta

1. `01-GDD.md` — concepto y loop como están implementados.
2. `02-Overheat.md` — heat, overheat, residual y presión por ciclos. Reemplaza el DEV del 21-7.
3. `03-Spawning-y-enemigos.md` — orbital, ruleta, pools, elites, bosses, vertical engagement.
4. `04-Game-Loop.md` — llaves, puerta, presión de salida, victoria.
5. `05-Progresion-crafting-y-meta.md` — XP, pasivos, crafting, meta y logros.
6. `06-QA-y-debug.md` — escenas, overlays y herramientas de balance.
7. `07-Deltas-y-huecos.md` — qué cambió desde el 21-7, qué sigue vigente de `docs/New/`, y qué no cerrar en diseño.
8. `08-Backlog-playtest.md` — backlog de las 7 mejoras hacia el playtest, repartido por rol y estimado en días. El Gantt imprimible está en `impresion/Gantt-playtest.html`.
9. `09-Coherencia-estetica.md` — reglas visuales scrap punk sacadas de `Personajes.blend` (Slime, Drone, Chaser, Stalker) y capturas en `estetica/capturas/`.

## Regla de números

Si un markdown de diseño y un asset discrepan, manda el asset que usa `GameplayScene`:

- Heat y spawn: `Assets/Data/Balance/SpawnBalanceProfile.asset`
- Armas y materiales: CSV importados (`balance_weapon_stats.csv`, `balance_material_usage.csv`). `Assets/Data/Balance/BALANCE_NOTES.md` es una nota corta; el bonus de variantes que cita (`+3`) ya no coincide con la ruleta viva (`+4`).
- Defaults en los scripts (`HeatManager` 50/50 y 5 de heat por kill, `BossManager` 400 HP) son fallback. En la escena de juego no son los valores que corren.

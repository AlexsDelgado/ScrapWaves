# QA y debug — alpha 22-09-2026

El GDD del 21-7 cita `DumpsterFire-DEV-QA`. Ese archivo no estaba en `docs/`. Esto es el inventario que hay en `Assets/Scripts` hoy.

Ninguna de estas herramientas es autoridad de gameplay. Leen estado o fuerzan overrides en escenas de prueba.

## Durante una run

| Entrada | Script | Qué hace |
|---|---|---|
| F3 | `DebugUI` | Overlay: FPS, heat, nivel, logs. Pausa y escala de tiempo |
| F2 | `QaRuntimeTweaker` | Ajusta en vivo stats de enemigos sin tocar el prefab |
| — | `QaCoreLoopMenu`, `QaPanels`, `QaBalanceReport` | Menús y reporte del loop de spawn / balance |
| — | `DebugMonitor` | OnGUI del estado de armas: manual, munición, habilidad |
| — | `WeaponDebugGizmos` | Gizmos de aim, rango y proyectil |
| Ctrl+Numpad 9 | `DebugCrafting` | 999 de cada material |
| — | `DebugInfiniteHealth` | Vida infinita |
| Pausa, solo editor / development | `StatAttributionPanel` | Desglose de stats y último golpe |
| Menú de objetivos, solo dev | `ObjectivesMenuUI` | Reset de progreso y max upgrades de meta |

`BalanceTuningHub` en la escena de juego inyecta `SpawnBalanceProfile` y expone lecturas de QA, incluido un disparo manual de Overheat.

## Escenas y sandboxes

| Herramienta | Uso |
|---|---|
| `WeaponTestingSandboxManager` + `WeaponSandboxDebugUI` | Tres slots, dummies, overrides de heat y de stats, modos y paths |
| `WeaponStatOverride`, `WeaponHeatOverride` | Fuerzan stats y heat en el sandbox |
| `WeaponDummyEnemy`, `WeaponDummySpawner` | Blancos del sandbox |
| `PassiveItemTestingController` | Prueba de pasivos aislada |
| `EnemiesTestingHarness` | Spawn controlado de enemigos |
| `BalanceTestingController` | Escena mínima de balance |
| `CompactorEscapeTestController` | Prueba de la puerta / escape |
| `QaBalanceReport` | Vuelca la ruleta, el bonus de variantes y el perfil |

Los tests de editor viven en `Assets/Tests/Editor/`. Cubren, entre otros, matemática de armas, monturas automáticas, overheat por ciclos (`OverheatCycleSpawnScalingTests`), rear threat, XP, animación, HUD de retícula, pool de enemigos y texto de combate.

## Builds

Los botones de dev y el panel de atribución de stats están guardados para que no aparezcan en un player de release. El resto de overlays de QA hay que confirmar que la escena de juego que se buildea no los deje activos.

## Escenas de rama que no son el alpha de `main`

En el remoto hay ramas de mapa, IA, cámara y optimización (`Map_V2`, `IA-Behaviour`, `Progressive`, `Optimization`, `Debug-QA`). El documento alpha describe `main`. Una feature que solo esté en esas ramas no forma parte de este snapshot.

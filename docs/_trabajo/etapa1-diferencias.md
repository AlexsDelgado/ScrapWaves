# Etapa 1 — Diferencias entre docs/ y el proyecto (23-09-2026)

Base: `main` @ `53ef237`. Revisión de los 8 `.md` de `docs/` contra código, escenas, prefabs, assets y git. ~120 afirmaciones verificadas OK; las diferencias se listan abajo.

Decisión del usuario: las `.docx` de `referencia-codigo/` y carpetas históricas (`docs/old/`, `docs/21-7…/`, `docs/New/`) se ignoran; no son necesarias para esta documentación.

## A. Referencias a archivos inexistentes (ignoradas por decisión)
1. `docs/old/`, `docs/21-7 ultima documentacion entregada/`, `docs/New/`, `referencia-codigo/` y los 8 `.docx` `DumpsterFire-*`: no están en el repo ni en el historial. `docs/` no está versionado.
2. Los docs de diseño viejos estaban en `docs/design docs/` (borrados en `bdbf89d`, 20-09), que incluía también `UI Design Document.docx`.
3. La escena de menú se llama `TitleScreen`, no `Title`.

## B. Valores / comportamientos distintos
| # | Doc | Dice | Realidad (evidencia) |
|---|---|---|---|
| 4 | 03 | Bosses `Boss`/`Boss_2` alternan; Stalker no se usa | `GameplayScene.unity:32800-32801`: `_bossPrefab` = Stalker (ciclos 2, 6…), `_secondBossPrefab` = Destroyer_Boss (4, 8…). Boss/Boss_2 no se usan. |
| 5 | 03, 07 | `ZoneSpawner` sin instancias en la escena | 2 instancias activas con `_rearmOnOverheat`: ZoneSpawner_Slime → Hellfire (`:27778`, pos -259.7, 11.1, -179.5) y ZoneSpawner_Drone → Bomber (`:27827`, pos -29.7, 10.2, 73.3). |
| 6 | 03 | Variantes salen del orbital con +4 cada 120 s | `EnemySpawnRoulette.cs:198` descarta `BaseWeight <= 0` antes del bonus; variantes tienen 0 (`DefaultEnemySpawnRoulette.asset:33,38,43`). Nunca salen del orbital; +4 y `ApplyExtraEliteChance` sin efecto. Shocker no aparece en ningún lado. |
| 7 | 02 | Overheat permanente con barra al 100 % | `OverheatManager.cs:126` `EnterPermanentOverheat` solo corta el decay; barra queda en el residual y sube por kills. |
| 8 | 05 | XP común 4 / raro 12 | `MaterialCatalog.cs:54-55`: 1 / 5. |
| 9 | 05 | `CriticalChance` suma ~1.0 por level-up | Corregido en `dd551fe` (Balance 0.3.2): `LevelUpgradeBaseAmount 0.01`. Sigue el bug del powerup: `TemporaryPowerupController.cs:109` suma Lerp(50,100) → crítico 100 % (clamp en `WeaponDamageResolver.cs:414`). |
| 10 | 04 | Puerta: carga 5 s, radio 3 | Defaults del componente; escena `CompactorExitDoor` override 20 s / radio 20 (`GameplayScene.unity:12892-12897`). |
| 11 | 04 | Flecha guía a crafting durante los primeros 20 s | Aparece a los 20 s y dura 5 s o hasta interactuar (`GuideArrowController.cs:19-22, 58-72`). |
| 12 | 05 | Advanced Tinkering: elegir 1 de 2 paths | `WeaponCraftingService.cs:114-186`: 1 path al azar; Path B requiere desbloqueo meta; rechazar una vez cuesta +50 % y garantiza el alternativo. |
| 13 | 04 | Heat decae en pausa (unscaled) | `HeatManager.cs:282-291`: unscaled por hit-stop, pero el decay se corta con `GameplayPause.IsUiPaused`. |
| 14 | 06 | `DebugInfiniteHealth` sin tecla; F3 = DebugUI | Numpad 0 (`DebugInfiniteHealth.cs:35`). F3 también en `QaCoreLoopMenu.cs:75`; F1 en `QaPanels.cs:7` sin documentar. |
| 15 | 07 | `WeaponLevelUpHandler` huérfano | En `player.prefab`, habilitado, pool de 5 armas; `LevelUpOrchestrator.cs:24` tiene la referencia pero no lo llama (63-73). |
| 16 | 05 | Script `MaterialUsageBalance` | Clase `MaterialUsageBalanceSO`; el asset sí se llama `MaterialUsageBalance.asset`. |
| 17 | 07 | Balance entre 20-09 y 21-09 | El único commit del 20-09 es `bdbf89d` (borrado de docs); balance es del 21-09. |
| 18 | 02 | Escala de intervalo por ciclo "se multiplica" | Es lineal: `max(piso, 1 − paso·n)` (`HeatManager.cs:204`). |

## C. Presente en el proyecto y ausente en docs
- **Riesgo de release:** en `GameplayScene` activos sin guarda de compilación: `DebugUI` (F3, pausa P, velocidad Numpad ±), `DebugCrafting` (999 materiales), `DebugInfiniteHealth` (Numpad 0), `EnemyPoolProfilerHud`, `BalanceTuningHub` (Overheat manual). Build Settings incluye 3 escenas de test: `WeaponTestingSandbox`, `enemiesTesting`, `test_balance`.
- Level-up: 5 a 20 tiradas de stats según nivel (`PlayerStatsLevelUpHandler.GetUpgradeCountForLevel`); meta aplica `GetMetaStatGrowthMultiplier` a cada tirada.
- Scrap de fin de run: segundos/10 + 25 por boss + materiales sobrantes (raros ×5) en `GameManager.CalculateScrapEarned`; no se muestra en la pantalla final.
- Scavenging inconsistente: `EnemyMaterialDrop.cs:29` usa `chance*(1+S)`; `PlayerStatMath.cs:73` usa `S/100`.
- `SpecMetaBootstrap` registra 13+ challenges por código; en `Resources/Meta/Achievements` hay solo 2 assets (BossHunter, RustyMarathoner). Agrega `TemporaryPowerupController` y `MetaProgressionApplier` al jugador.
- Botón DEV max upgrades también da 9999 de scrap.
- Al cerrar un Overheat, la capacidad sube a 168 con el heat en 120: el boost ×2 de fase intermedia se reenciende ~1.7 s durante el decay (que es también toda la pausa de spawn).
- Fallbacks de `HeatManager` en la escena (100/100, ×1.5, 2/kill, decay 12; ciclos 0.1/0.3) distintos del script y del perfil; solo corren sin perfil.
- Contadores de ciclo separados en `BossManager`, `OverheatEliteWaveSpawner` y `HeatManager`; `EnterPermanentOverheat` dispara `OnOverheatStarted` y suma ciclo. Elites cierran con `OverheatEndReason.BossDefeated`; `TimeExpired` sin uso.
- `EnemyPoolRegistry.EnsureExists` sale temprano si ya existe y no crea pools de apoyo.
- `UpgradeManager` legacy sin atributo `[Obsolete]`.
- `53ef237` además de movimiento (5→6.8, salto 2→3) sube `_baseMoveAcceleration` a 75 y la gravedad de -9.81 a -18.
- Sistemas sin documentar: ver `docs/_trabajo/sistemas/`.

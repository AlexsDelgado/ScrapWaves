# Meta, challenges y logros — alpha 23-09-2026

La progresión fuera de la run: el save en JSON, los challenges y logros que desbloquean pasivos y Path B, la tienda de upgrades meta pagada con Scrap y el menú de objetivos. Todo pasa por `SaveManager`. Los demás scripts reportan eventos o leen multiplicadores.

## Scripts

- `Meta/SaveManager.cs` + `Meta/SaveData.cs`: persistencia, evaluación de logros y compras.
- `Meta/SpecMetaBootstrap.cs`: registra los challenges de la Spec y las cartas de Path B, y agrega componentes al jugador.
- `Meta/ChallengeProgressTracker.cs`: escucha daño, muertes y kills, y reporta progreso.
- `Meta/MetaProgressionApplier.cs`: aplica los upgrades meta de stats al empezar la run.
- `Meta/MetaStatUpgradeCosts.cs` (`Resources/Meta/MetaStatUpgradeCosts.asset`): precios.
- `Meta/AchievementDefinition.cs`, `AchievementConditionType.cs`, `UnlockCatalog.cs`, `UnlockRequirement.cs`, `WeaponPathUnlockData.cs`, `IUnlockable.cs`.
- `Meta/UI/ObjectivesMenuUI.cs`, `MetaUpgradeShopUI.cs`, `ObjectivesFilterChipBar.cs`, `AchievementUnlockToast.cs`, `AchievementUnlockToastView.cs`.

## SaveManager

Se autocrea antes de cargar la escena (`SaveManager.cs:34-43`), con orden −150 y `DontDestroyOnLoad`.

Archivo: `Application.persistentDataPath/scrapwaves_save.json` (`:15`, `:55`). En Windows: `%USERPROFILE%\AppData\LocalLow\Initial Dreams\Dumpster FIre\`. Se serializa con `JsonUtility` en modo pretty print (`:547`). Si falla la lectura, arranca un save vacío y loguea un warning (`:525-541`).

`SaveData` (versión 3, `SaveData.cs:41-78`):

| Campo | Tipo | Uso |
|---|---|---|
| `Version` | int | 3. `Sanitize` solo la sube, sin migración |
| `Scrap` | int | Moneda meta |
| `UnlockedIds` | string[] | Pasivos, armas y `WeaponPath_<id>_PathB` desbloqueados |
| `UnlockedAchievementIds` | string[] | Logros completados |
| `PresentationAccessibility` | objeto | Motion, shake, flash y combat text. Sobrevive al reset |
| `TotalBossKills`, `TotalRunsCompleted`, `TotalEnemiesKilled` | int | Contadores cross-run (se cargan al terminar la run) |
| `BestSurvivalTimeSeconds` | float | Mejor tiempo de una run |
| `HighestPlayerLevel` | int | Se actualiza en cada level-up que supera el récord |
| `TotalEliteOrBossKills`, `TotalDropsLooted` | int | Contadores de challenges |
| `WeaponLevels` | {WeaponId, HighestLevel}[] | Sin call site (`ReportWeaponLevelReached` no se llama) |
| `CustomProgress` | {Key, Value}[] | Custom y RunChallenge: guarda el máximo |
| `WeaponKills` | {WeaponId, Kills}[] | Kills por arma (crédito al último golpe) |
| `MetaStatLevels` | {StatTypeName, Level}[] | Nivel meta 0–10 por stat |
| `MetaItemUpgrades` | {UnlockId, Level}[] | Nivel meta 0–3 por pasivo |

Evaluación (`SaveManager.cs:499-523`): recorre el catálogo y marca como completo todo logro con `GetProgress + 0.0001 ≥ TargetValue`. Suma `ScrapReward`, desbloquea cada `RewardUnlockId` y dispara `OnAchievementUnlocked`. La llaman todos los `Report*`.

Catálogo: `Resources.LoadAll("Meta/Achievements")` (`:69`) más los logros runtime de `SpecMetaBootstrap`, deduplicados por id (`:269-295`).

## Challenges registrados

Todos se crean en `SpecMetaBootstrap.EnsureRegistered` (`SpecMetaBootstrap.cs:23-77`).

| Id | Condición | Objetivo | Recompensa |
|---|---|---|---|
| `first_kill` | Custom `first_kill` (cualquier kill) | 1 | 10 Scrap |
| `first_death` | Custom `first_death` | 1 | 10 Scrap |
| `first_extraction` | RunsCompletedTotal (victorias) | 1 | 10 Scrap |
| `thaw_them_out` | WeaponKillsTotal, Flamethrower | 2500 | 50 Scrap + Path B Flamethrower |
| `to_little_pieces` | WeaponKillsTotal, RocketLauncher | 2500 | 50 + Path B RocketLauncher |
| `make_it_rain` | WeaponKillsTotal, Mortar | 2500 | 50 + Path B Mortar |
| `go_for_the_head` | WeaponKillsTotal, AutomaticCannon | 2500 | 50 + Path B AutomaticCannon |
| `studied_the_blade` | WeaponKillsTotal, RotatingBlade | 2500 | 50 + Path B RotatingBlade |
| `high_value_targets` | EliteOrBossKillsTotal | 250 | 40 + `Shop_Head_BountyHunterModule` |
| `resourcefulness` | DropsLootedTotal | 10000 | 40 + `Shop_Head_ScavengerModule` |
| `not_how_you_heal` | RunChallenge: de 100 % a 0 % de vida en ≤ 5 s | 1 | 30 + `Shop_Core_ScrapReassembly` |
| `survival_adept` | RunChallenge: 120 s sin recibir daño | 120 | 30 + `Shop_Core_ElectromagneticShield` |
| `one_shot_one_kill` | RunChallenge: un golpe de ≥ 1000 de daño | 1000 | 30 + `Shop_Arm_AdvancedTargetingModule` |

Assets en `Resources/Meta/Achievements`:

| Asset | Condición | Objetivo | Recompensa |
|---|---|---|---|
| `BossHunter` | BossKillsTotal | 3 | 50 Scrap, `_rewardUnlockIds: []` |
| `RustyMarathoner` | SurviveTimeSingleRun | 600 s | 50 Scrap, `_rewardUnlockIds: []` |

Los 5 pasivos con `_unlockedFromStart: 0` son justo los 5 que se ganan por challenge. Los otros 12 arrancan desbloqueados. Todos tienen `ScrapPrice: 0` y ningún `RequiredAchievement`.

Path B: `RegisterPathUnlockCards` (`SpecMetaBootstrap.cs:97-114`) crea un `WeaponPathUnlockData` runtime por arma, con **50 Scrap** de precio y sin logro requerido. Se compra en la tienda o se gana con el challenge del arma. `SaveManager.IsPathUnlocked` siempre deja pasar PathA (`SaveManager.cs:444-451`).

## ChallengeProgressTracker

Singleton autocreado `AfterSceneLoad` (`ChallengeProgressTracker.cs:149-150`).

| Evento | Llamador | Qué reporta |
|---|---|---|
| `NotifyEnemyKilled` | `EnemyHealth.cs:168` | Kill al `LastDamagingWeaponId`, elite/boss, `first_kill` (`:106-121`) |
| `NotifyDamageInstance` | `WeaponDamageApplier.cs:38` | Máximo de la run. Reporta si ≥ 1000 (`:91-104`) |
| `NotifyPlayerDamaged` | `PlayerHealth.cs:299/313` | Resetea el contador sin daño. Chequea 100→0 en ≤ 5 s (`:64-79`) |
| `NotifyPlayerDied` | `PlayerHealth.cs:248/322` | `first_death` |
| `NotifyDropLooted` | `MaterialPickupReceiver.cs:45` | `TotalDropsLooted += amount` |
| `Update` | — | Si `now − últimoDaño ≥ 120` → `survival_adept` (`:36-62`) |

## MetaProgressionApplier

Lo agrega `SpecMetaBootstrap` al jugador y aplica en `Start` (`MetaProgressionApplier.cs:24-67`).

- Stats: DamageMultiplier, AttackSpeedMultiplier, ProjectileAreaSize, CriticalChance, CriticalDamage, MaxHealth, HealthRegeneration, PickupRange (`:10-20`).
- Cada nivel es un **multiplicador** `1 + 0.05 × nivel` (hasta ×1.5 en nivel 10), con fuente `StatUpgradeSource.Base` (`SaveManager.cs:359-363`).
- Crecimiento en level-up: `GetMetaStatGrowthMultiplier` da ×1.15 desde el nivel meta 5 y ×1.3225 en el 10 (`SaveManager.cs:366-375`). Lo consume `PlayerStatsLevelUpHandler` (doc 23).
- Pasivos: `GetMetaItemPowerMultiplier = 1.1^nivel` (0–3, hasta ×1.331) sobre cada bonus del pasivo (`PassiveItemManager.cs:236-237`). Quedan excluidos los pasivos con ShieldCharges, AirJumps o DashCharges (`MetaUpgradeShopUI.cs:742-754`).
- Después de aplicar, ajusta la vida actual con el delta de vida máxima.

## Tienda de upgrades meta

`MetaUpgradeShopUI` (pestaña Upgrades). Muestra las 8 stats de arriba y los pasivos desbloqueados que se pueden mejorar.

`MetaStatUpgradeCosts.asset`: base 25, crecimiento 1.35; ítem base 40, crecimiento 1.5 (iguales a los defaults). La fórmula es `round(base × crecimiento^(n−1))` (`MetaStatUpgradeCosts.cs:14-24`).

| Nivel | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | Total |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Stat | 25 | 34 | 46 | 62 | 83 | 112 | 151 | 204 | 276 | 372 | 1365 |
| Pasivo | 40 | 60 | 90 | | | | | | | | 190 |

Llevar las 8 stats al máximo cuesta 10 920 Scrap. Los topes (10 y 3) se aplican en `SaveData` (`SaveData.cs:86`, `:116`).

## UI

- `ObjectivesMenuUI` (menú principal): pestañas Objectives, Unlocks y Upgrades. Se navega con A/D o con los hombros del gamepad (`ObjectivesMenuUI.cs:188-216`) y recuerda la última pestaña en la sesión. En Unlocks se compra con `SaveManager.TryPurchase` (`:956`).
- Botones DEV, solo en `UNITY_EDITOR || DEVELOPMENT_BUILD` (`:31-38`, `:447-510`):
  - "DEV: Reset progress": `SaveManager.ResetProgress`. Borra todo menos accesibilidad.
  - "DEV: Max upgrades": `MetaUpgradeShopUI.DevMaxOutAllUpgrades` (stats a 10 y pasivos desbloqueados a 3) más Scrap a 9999, con una sola escritura (`DevCommit`).
- `AchievementUnlockToast`: singleton `DontDestroyOnLoad` autocreado (`AchievementUnlockToast.cs:362`). Encola los desbloqueos y muestra cada uno 3 s, con fade de 0.25 s en tiempo unscaled (`:20-21`). En `TitleScreen` construye su propio panel. En gameplay usa el `AchievementUnlockToastView` de la escena y espera si no puede presentar.

## Deuda / puntos abiertos

- **Escritura a disco por kill.** `ReportWeaponKill` y `ReportEliteOrBossKill` llaman a `Save()` en cada kill (`SaveManager.cs:297-317`), igual que `ReportDropsLooted` en cada pickup. Con cientos de kills por minuto, es IO sincrónico en el hilo principal.
- `ResetRunScratch` no tiene llamadores. `_lastPlayerDamageTime` y `_runMaxDamageInstance` pasan de una run a la siguiente, así que `survival_adept` puede completarse al arrancar una run si pasaron 120 s de `Time.time` desde el último daño (tiempo de menú incluido).
- Las descripciones de `BossHunter` y `RustyMarathoner` prometen "Overheated Core" y "Jet Boots". Ninguno de los dos pasivos existe y `_rewardUnlockIds` está vacío.
- Path B se puede comprar por 50 Scrap sin hacer el challenge de 2500 kills. Hay que decidir si el challenge es un atajo o un requisito (`RequiredAchievement` es null).
- CriticalChance recibe el meta como multiplicador sobre una base de 0, así que solo escala lo que aportan los otros aditivos. CriticalDamage y HealthRegeneration no son `UpgradeableByLevel`, y el crecimiento meta de los niveles 5 y 10 no tiene efecto en ellos.
- `WeaponLevelReached` y `ReportWeaponLevelReached` no tienen call site. `SaveData.Version` no migra datos.
- `first_extraction` cuenta victorias (`TotalRunsCompleted` solo sube con `victory`). Hay que confirmar que "extraction" y victoria son lo mismo.

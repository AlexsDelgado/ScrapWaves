# Armas: arquitectura — alpha 23-09-2026

El jugador lleva hasta tres armas. En cada momento, una está en manual (la dispara el jugador, con munición y habilidad en Q) y las otras disparan solas en automático. Al vaciar la munición del manual, esa arma vuelve a automático y la siguiente pasa a manual. El heat de la run alimenta la curva de poder de cada arma. El catálogo por arma está en `31-Armas-catalogo.md` y los firepoints en `32-Firepoints-automaticos.md`.

## Scripts

| Script | Rol |
|---|---|
| `Managers/WeaponManager` | Slots, modo manual/automático, input, munición y ciclo. Vive en `player.prefab` |
| `Managers/WeaponBehaviourFactory` | `WeaponType` → clase concreta (`WeaponBehaviourFactory.cs:13-21`) |
| `Base/WeaponBehaviourBase` (`BasicProjectileWeapon`) | Base de las cinco armas: ticks, gasto de munición, fire interval, spawn de proyectiles |
| `Types/*Weapon` | `AutomaticCannon`, `RocketLauncher`, `Flamethrower`, `Mortar`, `RotatingBlade` |
| `Base/WeaponData` + `WeaponModels` | ScriptableObject y datos de runtime (`WeaponInstance`) |
| `Managers/WeaponMath` | Munición máxima, cooldown de habilidad, knockback, multiplicador de cadencia |
| `Managers/WeaponDamageResolver` | `WeaponDamageContext`: cadena de daño y crítico |
| `Managers/PlayerWeaponMountController`, `AutomaticWeaponMount` | Montajes y firepoints (doc 32) |
| `Projectiles/ProjectilePool` (+ `ProjectilePoolMember`, `Projectile`) | Pool de proyectiles |
| `Projectiles/FlamethrowerManualAreas` | Pool de áreas manuales independientes; movimiento y crecimiento en LateUpdate, colisión central con sliding 3D y un reloj compartido de daño |
| `Economy/WeaponCraftingService` | Subida de nivel y Advanced Tinkering (rutas A/B) |
| `Economy/WeaponStatsParser`, `Weapon/Editor/WeaponBalanceSheetApplier` | Dos caminos para llevar el balance a los assets |
| `Managers/WeaponLevelUpHandler` | Armas por level-up. Está en el prefab, pero `LevelUpOrchestrator` no lo llama (`LevelUpOrchestrator.cs:63-73`) |

## WeaponManager

- `MaxWeaponSlots = 3` (`WeaponManager.cs:8`). `AddWeapon` crea el `WeaponInstance` y el behaviour, lo registra en el mount controller y, si es la primera arma, la pone en manual (`:106-123`).
- `_startingWeapons` está vacío en `player.prefab` (`player.prefab:795`). Si hay un `RunStartWeaponChoice` activo, el manager no equipa nada; el arma inicial sale de ese menú (`:249-262`).
- `HeatManager.GetInstance()` se resuelve una sola vez, en `Awake` (`:51`). Si el HeatManager aparece después, las armas quedan sin heat (`Heat == null` → sin bonus).

### Orden de `Update` (`:57-79`)

1. Sale si `timeScale ≤ 0` o si `GameManager` no está en `IsPlaying`.
2. Evalúa la pose del esqueleto (`PlayerAnimationDriver.EvaluatePoseForWeapons`) y vuelve a resolver el aim con el muzzle ya ubicado. Ver doc 32.
3. `RefreshWeaponModes` en los mounts.
4. `TickAutomatic` de cada arma en estado `Automatic` (`:306-315`).
5. `UpdateManualWeapon`: input (click izquierdo = fuego, Q = habilidad, cableado a `Mouse`/`Keyboard`, `:366-411`), tick del cooldown de habilidad, `TickManual` y la habilidad. Las armas con `IHoldActiveAbilityBehaviour` (el cohete) reciben begin/tick/release (`:336-348`).
6. Si la munición del manual llegó a 0, llama a `EndManualMode` (`:350-351`).
7. `UpdateManualCycle` (`:414-425`).

### Munición y ciclo manual

- `StartManualMode(i)` pone el arma `i` en `Manual`, el resto en `Automatic`, y **rellena la munición** del arma entrante con `WeaponMath.GetMaxManualAmmo` (`:428-450`).
- La munición máxima es `BaseManualAmmo × AmmoMultiplier × LevelData.ManualAmmoMultiplier`. Si la ruta define `ManualAmmoOverride ≥ 0`, ese valor la pisa (`WeaponMath.cs:49-67`).
- `TrySpendManualAmmo` casi siempre se llama con `requireFullAmount:false`: un disparo o una habilidad salen enteros aunque quede menos munición que el costo (`WeaponBehaviourBase.cs:198-215`).
- **Bug: el cooldown de cambio de manual no existe.** `EndManualMode` pasa directo a `StartManualMode(next)` (`:453-465`), y `StartManualMode` deja `_manualCooldownTimer = 0` (`:435`). Nadie asigna un valor positivo a ese timer, así que `UpdateManualCycle` nunca avanza. `_manualCycleCooldown` (1.25) y `_singleWeaponCycleCooldown` (2.5) están serializados y la UI los lee (`GetManualCooldownNormalized`, `:208-214`), pero no hacen nada.
  - Con una sola arma, al vaciarse la munición se rellena en el mismo frame: el manual es infinito.
  - Con varias armas, el cambio es instantáneo.
- **El cooldown de habilidad solo avanza en manual.** `TickAbilityCooldown` se llama solo para el arma manual (`:333`). Un arma que usó Q y pasó a automático conserva el cooldown congelado hasta volver a manual.
- El estado `WeaponState.Cooldown` (`WeaponModels.cs:65`) no se usa.
- `RefillManualAmmoAndResetActiveCooldown` (powerup Full Heal) recarga todas las armas y pone en 0 los cooldowns (`:274-287`).

### Habilidad (Q)

- `CanUseAbility` exige que el arma esté en manual, que el cooldown sea 0 y que haya munición > 0 si el costo es > 0 (`:229-238`).
- El cooldown es `SkillCooldown × (1 − AbilityCooldownReduction)`, con la reducción acotada entre 0 y 0.95 (`WeaponMath.cs:81-110`). Es fijo por asset: no baja con el nivel.
- El costo es `ActiveAbilityAmmoCost`, salvo en AutomaticCannon ruta A, que cuesta 80 fijo (`WeaponMath.cs:91-102`).

## WeaponData (ScriptableObject)

`Assets/ScriptableObjects/WeaponSO/<Arma>.asset`, menú `ScrapWaves/Weapon Data` (`WeaponData.cs:158`).

| Grupo | Campos |
|---|---|
| Identidad | `WeaponId`, `DisplayName`, `WeaponType`, `Icon`, `PresentationProfile` |
| Targeting | `AutoTargetingMode` (`ClosestInRange`, `RandomInRange`, `IgnoreCameraClosest`), `AutomaticAimConstraint` (`CameraCone`, `Full360`, `BodyForward180`), `ManualMode` |
| Base | `BaseDamage`, `BaseAttackRate`, `BaseRange`, `BaseKnockback`, `BaseManualAmmo`, `ActiveAbilityAmmoCost`, `SkillCooldown` |
| Tuning por tipo | `_specificTuning` (`SerializeReference`): `AutomaticCannonTuning`, `RocketLauncherTuning`, etc. (`WeaponData.cs:6-156`). `OnValidate` fuerza el tipo que corresponde a `WeaponType` |
| Niveles | `LevelData` (1–10), `PathA` / `PathB` (`WeaponUpgradePathData`: nombre, multiplicadores, `ManualAmmoOverride`, `LevelData` 6–10) |
| Balance crudo | `BalanceStats`, `UpgradeSpecificStats`. Están vacíos en los cinco assets |
| Meta | `UnlockedFromStart`, `Requirement` (`IUnlockable`) |

`WeaponLevelData` tiene tres multiplicadores: daño, cadencia y munición (`WeaponModels.cs:33-40`). Rangos, radios, cooldowns y crítico no escalan por nivel en ningún lado.

## Resolver de daño

`WeaponDamageContext` (`WeaponDamageResolver.cs:100-372`) se crea una vez por disparo (`BasicProjectileWeapon.CreateDamageContext`, `WeaponBehaviourBase.cs:327-338`):

1. `BaseDamage` × `LevelData.DamageMultiplier` × `PathData.DamageMultiplier` × stat `DamageMultiplier` (`:170-180`).
2. Si es daño de habilidad, × `AbilityDamageMultiplier` (`:182-186`).
3. **Tirada de crítico, una por contexto** (`:191-197`). Todos los objetivos de una explosión o de un tick comparten el resultado.
   - Probabilidad: `Clamp01(stat CriticalChance)` (`:412-416`). Sale solo del stat del jugador: la fila "Critical chance %" del CSV de armas no se lee.
   - Multiplicador: `max(1, CriticalDamage × CritMultiplierOverride)` (`:195`). Solo el cañón define override (×2).
4. Por objetivo: × elite (stat `EliteDamageMultiplier`, si `WeaponEnemyClassifier` dice elite o boss) × rango (Sharpshooter/CQB: > 15 m o < 10 m, `:348-363`) × `DamageScale` × escala adicional (`:306-346`).
5. Redondea con un mínimo de 1 (`:286`). `OnDamageResolved` alimenta el `StatAttributionPanel` y los diagnósticos.

Knockback: `BaseKnockback × stat Knockback × escala × falloff × (1 + √daño·0.25)` (`WeaponMath.cs:113-122`). Con `BaseKnockback` 0 (el lanzallamas), el knockback es 0 sea cual sea la escala.

El clamp de crítico en 0–1 es lo que hace que un stat `CriticalChance` inflado (ver `05-Progresion-crafting-y-meta.md`) quede en crítico al 100 %, en vez de romper la tirada.

## Sinergia con el heat

- `HeatManager.NormalizedHeat` va de 0 a 1 y es por tramos: 0–0.8 lineal en el primer tramo de puntos y 0.8–1 en el segundo (`HeatManager.cs:220-236`). Durante un Overheat la barra está al 100 %, o sea `h = 1`.
- Cada arma lee `Heat.NormalizedHeat` en su propio código. No hay una tabla central: los umbrales están en el tuning del asset o hardcodeados (detalle por arma en el doc 31).
- Hook genérico: `GetFireInterval = GetFireIntervalWithoutHeat / GetHeatFireRateMultiplier` (`WeaponBehaviourBase.cs:176-192`). El default es `1 + 0.25·h`. Cohete y cañón lo pisan con 1; el mortero lo pisa con su curva por encima de 0.5. Lanzallamas y blade no pasan por `GetFireInterval`.
- Aparte del heat, durante el Overheat `OverheatManager` suma +0.5 a `AttackSpeedMultiplier` con `SetRuntimeFireRateMultiplier(1.5)` (`OverheatManager.cs:36`, `:133`, `:158`). Al terminar lo vuelve a 1 (`:186`). Ese método borra **todos** los modificadores `TemporaryEffect` antes de aplicar el nuevo (`PlayerStats.cs:146-151`).
- El VFX y el feedback reciben `NormalizedHeat` en `WeaponPresentationContext` / `WeaponFeedbackContext`. Es solo cosmético.

## Niveles y Advanced Tinkering

- `WeaponInstance.Level` va de 1 a 10 (`WeaponManager.cs:133`). `HasAdvancedPath` es `Level ≥ 6` (`WeaponModels.cs:63`). El CSV define los niveles 1–5 (básico) y 6–10 por ruta.
- `WeaponMath.GetLevelData` busca primero en `PathX.LevelData` y, si no encuentra, cae al `LevelData` básico (`WeaponMath.cs:6-17`).
- **Subida de nivel:** `WeaponCraftingService.TryUpgradeWeapon`, de a un nivel desde `CraftingUI` (`CraftingUI.cs:262`). Bloquea el paso 5→6 si no hay ruta (`WeaponCraftingService.cs:79-80`).
- **Advanced Tinkering**, solo en nivel 5 sin ruta (`:114-186`):
  1. Se ofrece una ruta. Si B está desbloqueada en meta (`SaveManager.IsPathUnlocked`), sale al 50 %; si no, siempre A (`:127-128`). Reabrir la estación no vuelve a tirar (`:122-130`).
  2. Aceptar: gasta materiales, sube a 6 y llama a `ApplyUpgradePath` (`:171-178`).
  3. Rechazar: gasta materiales, marca el rechazo y garantiza la otra ruta con +50 % de costo. Solo se puede rechazar una vez, y solo si la otra ruta está desbloqueada (`:135-140`, `:181-185`).
- `ApplyUpgradePath` ignora la llamada si `Level < 6` (`WeaponManager.cs:173-180`).
- Riesgo latente: `WeaponLevelUpHandler` → `TryAddOrUpgradeWeapon` → `UpgradeWeapon` sube de nivel sin gate de ruta (`WeaponLevelUpHandler.cs:84`). Si se vuelve a cablear, un arma puede pasar a nivel 6 con `SelectedPath None`. Advanced Tinkering exige nivel 5 exacto, así que esa arma quedaría sin ruta para siempre.

### De dónde salen los números de nivel

| Camino | Qué hace | Estado |
|---|---|---|
| `WeaponStatsParser.ImportAll`: en editor por menú y por `BalanceAutoImporter`; en Play por `EconomyBootstrap` (`EconomyBootstrap.cs:60-61`) | Busca `"Basic"` en la columna 1 y `"Total"` en la columna 0 (`WeaponStatsParser.cs:80`, `:120`). El CSV tiene los dos en la otra columna, así que `ApplyBlock` limpia `BalanceStats` y sale (`:77-82`). Después, `SyncLegacyLevelData` reescribe el `LevelData` básico con todos los multiplicadores en 1 (`:209-231`) | **Roto.** Cada Play en editor deja el `LevelData` básico en 1 |
| `WeaponBalanceSheetApplier` (menú `Tools/ScrapWaves/Apply Weapon Balance Sheet Snapshot`) | Snapshot hardcodeado del CSV: daño y munición de los niveles 1–5 y de las rutas 6–10 (`WeaponBalanceSheetApplier.cs:36-111`) | Las `PathX.LevelData` actuales coinciden con este snapshot. El `BaseDamage` de los assets no (cañón 5 contra 20) |

Resultado: en los niveles 1–5 el arma no mejora, y las rutas escalan sobre un `BaseDamage` que no es el del CSV. Si el parser se arregla, `:203` carga `ActiveAbilityAmmoCost` desde la fila **"Ability damage"**, y `:289` mapea "ability cooldown" a `StatType.BaseFireInterval`. Los dos son bugs latentes.

## Pool de proyectiles

- `ProjectilePool` vive en `GameplayScene`. El prefab de jugador deja `_projectilePool` en null y la escena lo asigna por override. El manager lo pasa a cada behaviour por la factory (`WeaponManager.cs:296-302`).
- `ProjectilePool.prefab`: 64 prewarm en `Awake`, crecimiento permitido hasta 1024 y vida de 3 s (`ProjectilePool.cs:89-93`).
- Cuando se agota, `TryGet` devuelve null y el disparo no sale; no recicla el proyectil más viejo (`:108-124`).
- `TrySpawnProjectile` configura, aplica el contexto de daño y lanza. `TrySpawnExplosiveProjectile` suma explosión, velocidad y viaje máximo **después** de `Launch`, porque `Launch` resetea esos campos (`:265-337`, `Projectile.cs:255-298`).
- Vuelve al pool por vida, por viaje máximo, por golpe de sweep o por trigger (`Projectile.cs:418-540`), vía `DespawnOrDestroy` → `ProjectilePoolMember` → `Release`.
- VFX: `ExplosionRadiusVfxPool` (8 iniciales, 32 máx. en escena) y `WeaponVfxPool` por cue (`MaxSimultaneous` 8 por defecto).

## Deuda / puntos abiertos

- Cooldown del ciclo manual muerto (`WeaponManager.cs:435`, `:453-465`). Con una sola arma, el manual es infinito.
- El cooldown de habilidad solo corre en manual (`:333`).
- El parser del CSV no importa nada y pisa el `LevelData` básico en cada Play. `BaseDamage` de los assets ≠ CSV. Rangos, cooldowns y crítico por nivel no tienen consumidor.
- `WeaponStatsParser.cs:203` (costo ← "Ability damage") y `:289` (cooldown → `BaseFireInterval`) quedan listos para romper el día que el parser funcione.
- El crítico sale solo del stat del jugador y se tira una vez por disparo. La columna de crítico por arma del CSV se ignora.
- `SetRuntimeFireRateMultiplier` borra todos los `TemporaryEffect`, no solo el del Overheat.
- `HeatManager` se toma una sola vez en `Awake`.
- Si se recablea `WeaponLevelUpHandler`, puede dejar armas en nivel ≥ 6 sin ruta.
- `ExplosionRadiusVfxPool.TotalCount()` cuenta solo los inactivos (`ExplosionRadiusVfxPool.cs:87`), así que el tope de 32 no se aplica.
- `ProjectilePool` no tolera instancias destruidas: si desencola un null, `ActivateInstance` tira excepción (`ProjectilePool.cs:116`, `:625`).
- La vida única de 3 s puede cortar tiros lentos de rango largo antes del viaje máximo.

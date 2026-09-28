# Catálogo de enemigos — alpha 23-09-2026

Ficha de cada prefab de enemigo que la run puede instanciar hoy en `GameplayScene`. Los valores salen del YAML de prefabs, assets y escena. No salen del diseño. Son valores **base**. En runtime se escalan con `DifficultyManager` / `SpawnBalanceProfile` (vida ×6, velocidad ×3, daño ×1.35 al tope, `SpawnBalanceProfile.asset:61-65`) y con los ciclos de Overheat cerrados. Ver `03-Spawning-y-enemigos.md`.

## Scripts

| Script | Qué aporta a la ficha |
|---|---|
| `EnemyHealth` | `_maxHealth` (`EnemyHealth.cs:5`), `_damageTakenMultiplier` |
| `SimpleFollow` | Velocidad de persecución `_speed` (`SimpleFollow.cs:8`). Es el follower activo en los prefabs comunes |
| `EnemyFollow` | `_moveSpeed` (`EnemyFollow.cs:10`). **Deshabilitado** (`m_Enabled: 0`) en todos los prefabs comunes; activo solo en `Destroyer_Boss` |
| `EnemyContactDamage` | `_contactDamage`, escalado por `EnemyOutgoingDamageScale` (`EnemyContactDamage.cs:21`) |
| `EnemyMaterialDrop` + `MaterialDropConfig` | Chance y pool de material (`EnemyMaterialDrop.cs:6-8`) |
| `EnemyScrapDrop` | Scrap: 0.1 % base en comunes, 5–10 directo al save en bosses (`EnemyScrapDrop.cs:7,26`). Se agrega en runtime (`SwarmPooledEnemy.cs:81`, `BossManager.cs:239`) |
| `EnemyDropExperience` | Legacy, no hace nada (`EnemyDropExperience.cs:3-6`). La XP sale del pickup de material: común +1, rara +5 (`MaterialCatalog.cs:54-55`) |
| `BossKeyDrop` | Instancia `CellBattery.prefab` al morir (`BossKeyDrop.cs:39`) |
| Behaviors | `FlyingRangedBehavior`, `ChargerEnemyBehavior`, `HellfireSlimeBehavior`, `BomberDroneBehavior`, `GigaWormBehavior`, `DestroyerBehavior` |

## Fuentes de spawn en `GameplayScene`

| Fuente | Referencia en escena | Prefabs |
|---|---|---|
| `OrbitalSpawner` | `GameplayScene.unity:27708`, `_config` = `DefaultEnemySpawnRoulette` | Ver ruleta |
| `OverheatEliteWaveSpawner` | `GameplayScene.unity:27880` | `Slime_Elite` ×3, `Drone_Elite` ×3, `Chaser_Elite` ×3 (9 por ola, ciclos impares) |
| `BossManager` | `GameplayScene.unity:32796` | `_bossPrefab` = `Stalker`, `_secondBossPrefab` = `Destroyer_Boss`, `_bossMaxHealth: 1500` |
| `ZoneSpawner_Slime` (instancia) | `GameplayScene.unity:27741`, pos (-259.7, 11.1, -179.5) | Override `_enemyPrefab` → `Slime (variant)`, `_spawnCount: 12` |
| `ZoneSpawner_Drone` (instancia) | `GameplayScene.unity:27790`, pos (-29.7, 10.2, 73.3) | Override `_enemyPrefab` → `Drone (variant)`, `_spawnCount: 6` |
| `SwarmSpawner` | `GameplayScene.unity:25411`, `m_Enabled: 0` | Legacy, apagado |

Los `ZoneSpawner` disparan por `OnTriggerEnter` y se rearman en cada Overheat (`_rearmOnOverheat: 1`, `ZoneSpawner.cs:99,147`). `ZoneSpawner_Chaser.prefab` no está en la escena.

### Ruleta orbital (`DefaultEnemySpawnRoulette.asset`)

| Kind | Prefab (GUID) | BaseWeight | Batch | IsVariant |
|---|---|---|---|---|
| 0 JunkSlime | `EnemyPro` (`e5ce45a0…`) | 60 | 4 | 0 |
| 1 VigilanceDrone | `Drone` (`aefb6009…`) | 25 | 4 | 0 |
| 2 ChaserBot | `Chaser` (`1780b9ab…`) | 25 | 3 | 0 |
| 3 HellfireSlime | `Slime (variant)` (`f341ba25…`) | **0** | 2 | 1 |
| 4 BomberDrone | `Drone (variant)` (`35733be1…`) | **0** | 2 | 1 |
| 5 ShockerBot | `Chaser (variant)` (`e7399044…`) | **0** | 2 | 1 |

Bonus de variante del asset: +4 cada 120 s. **No aplica a entradas con peso 0.** `BuildEffectiveEntries` descarta `BaseWeight <= 0` antes de sumar el bonus (`EnemySpawnRoulette.cs:198` vs `:203`). `ApplyExtraEliteChance` (stat `ExtraEliteChance`) también sale sin hacer nada si el peso variante total es 0 (`EnemySpawnRoulette.cs:231`). Con el asset actual el orbital solo tira `EnemyPro`, `Drone` y `Chaser` (55 / 23 / 23 %), sin importar el tiempo de run ni las stats.

## Fichas

Velocidad = `SimpleFollow._speed`, salvo que el behavior tome el movimiento. Daño = contacto / ataque del behavior. Drop = `MaterialDropConfig` × `_dropAmount`.

### Comunes (orbital)

| Prefab | HP | Vel. | Daño | Behavior | Drop |
|---|---|---|---|---|---|
| `EnemyPro` (Junk Slime) | 30 | 2.2 | contacto 12 | Solo follow | `JunkSlimeDrops` ×1 |
| `Drone` (Vigilance) | 15 | 5 (`FlyingRangedBehavior._moveSpeed`) | contacto 10; bala 15, vel 12 | Vuela a altura 4, se acerca hasta 15 m, lockea 2 s, dispara, cooldown 1.5 s | `VigilanceDroneDrops` ×1 |
| `Chaser` | 50 | 3 | contacto 18; embestida 25 | A 6 m carga 2 s y dashea (30 u/s, máx. 12 m); si no pega en 2 s, overheatea | `ChaserBotDrops` ×1 |

### Variantes (solo por `ZoneSpawner`, o nunca)

| Prefab | HP | Vel. | Daño | Behavior | Drop | ¿Spawnea? |
|---|---|---|---|---|---|---|
| `Slime (variant)` (Hellfire) | 60 | 5 | contacto 15; explosión 25 (r 3.5), burn 2 dps × 3 s, `FireArea` 4/tick c/0.5 s × 4 s | A 4 m se vuelve invencible, se lanza, explota y muere | `HellfireSlimeDrops` ×1 | Sí, zona Slime (12) |
| `Drone (variant)` (Bomber) | 25 | 5.5 | contacto 10; bala 20, vel 15; C4 20 (`EnemyC4.prefab`) | A 10 m hace 2 dashes con 2 C4 cada uno, recarga 2 s, entre tanto dispara | `BomberDroneDrops` ×1 | Sí, zona Drone (6) |
| `Chaser (variant)` (Shocker) | 100 | 3.5 | contacto 20; embestida 12 + descarga 10, stun 1 s | Charger con `_overcharged: 1`, dash 38 u/s, máx. 14 m | `ShockerBotDrops` ×1 | **No.** Solo en `Testing/enemiesTesting.unity` |

### Elites (oleada de Overheat impar)

| Prefab | HP | Vel. | Daño | Behavior | Drop |
|---|---|---|---|---|---|
| `Slime_Elite` | 450 | 5 | contacto 25; explosión 25, burn 4 dps | Hellfire (muere al explotar) | `JunkSlimeDrops` ×2 |
| `Drone_Elite` | 125 | 5.5 | contacto 10; bala 35, vel 8; C4 20 | Bomber, 4 C4 por dash, recarga 5 s | `VigilanceDroneDrops` ×2 |
| `Chaser_Elite` | 300 | 3.5 | contacto 35; embestida 12 + descarga 10 | Shocker (`_overcharged: 1`), empuje 50 | `ChaserBotDrops` ×2 |

El modo Shocker sí está en juego, pero vía `Chaser_Elite`. El daño de embestida es `_hitDamage + _dischargeDamage` (`ChargerEnemyBehavior.cs:179`).

### Bosses (Overheat par)

| Prefab | HP prefab → spawn | Vel. | Daño | Behavior |
|---|---|---|---|---|
| `Stalker` (`GigaWormBehavior`) | 1500 → 1500. `_damageTakenMultiplier: 0.2`; `EnemyDamageHitZone` ×1.2 | 5 bajo tierra; salto 25, caída 25 | contacto 60; emerge 80 (r 14); escupitajo → `CorrosiveSlimeArea` 2/tick c/0.5 s × 5 s, slow 0.45 | Caza bajo tierra, telegraph 1.2 s, salta (apex 50), cae donde estaba el jugador, escupe 2 proyectiles c/0.5 s |
| `Destroyer_Boss` (`DestroyerBehavior`) | 1000 → **1500**. Punto débil de boca 80 HP | 1.6 (`EnemyFollow`) | contacto 10; misil 12 c/1.2 s; tragar 40 | Hunt con misiles guiados; succión al 75/50/25 %: inmune, cura 2 % por enemigo comido y 15 % si traga al jugador; romper el punto débil le saca 20 %; corte a los 15 s |

`BossManager.cs:237` pisa la vida del prefab con `_bossMaxHealth`. Orden (`BossManager.cs:313`): ciclo 2 → Stalker, 4 → Destroyer, 6 → Stalker… Ninguno de los dos tiene `EnemyMaterialDrop`. Sueltan: llave `CellBattery` (`BossKeyDrop` en prefab), 5–10 de scrap al save y powerup temporal (los dos últimos, agregados en `BossManager.cs:239-242`).

## Drop tables (assets en `ScriptableObjects/Economy/Drops/`)

| Asset | `_dropChance` | Pool |
|---|---|---|
| `JunkSlimeDrops` | 0.65 | Chapa 71, Caños 12, Engranajes 12, Combustible gelificado 5 |
| `VigilanceDroneDrops` | 0.65 | Engranajes 95, Explosivo plástico 5 |
| `ChaserBotDrops` | 0.75 | Caños 95, Cableado 5 |
| `HellfireSlimeDrops` | 0.9 | Chapa 40, Combustible 60 |
| `BomberDroneDrops` | 0.9 | Engranajes 40, Explosivo 60 |
| `ShockerBotDrops` | 0.9 | Caños 40, Cableado 60 (solo lo usa el Shocker, que no spawnea) |

## CSV y `BalanceAutoImporter`

1. Los CSV (`Assets/Data/Balance/balance_material_usage.csv`, `balance_weapon_stats.csv`) no tienen datos de enemigos. No hay CSV de HP, velocidad ni daño; esos valores viven solo en los prefabs.
2. `BalanceAutoImporter` corre en cada domain reload, pero sale temprano si existe `MaterialUsageBalance.asset` (`BalanceAutoImporter.cs:25`). Hoy existe, así que no importa nada. Esto contradice la nota de memoria de que "cada reload pisa assets", al menos en el HEAD actual.
3. Si ese asset falta, además llama `CreateDefaultDropConfigs` (`BalanceImportMenu.cs:31-46`), que **reescribe** los 6 drop configs con valores hardcodeados distintos a los del repo. Por ejemplo, Junk 0.45 con 95/5 (hoy 0.65, 4 materiales), Chaser 0.55 (hoy 0.75), variantes 0.55 (hoy 0.9).

## Deuda / puntos abiertos

- **Variantes con peso 0 no salen nunca del orbital.** El bonus de +4/120 s está muerto porque se descartan antes (`EnemySpawnRoulette.cs:198`). Pasa lo mismo con `ExtraEliteChance`. `BALANCE_NOTES.md` todavía describe el bonus como activo (+3).
- **`Chaser (variant)` / ShockerBot sin uso** en `GameplayScene`: ruleta con peso 0 y sin `ZoneSpawner`. Tampoco se usa `ShockerBotDrops`.
- **`03-Spawning-y-enemigos.md` desactualizado.** Dice que no hay `ZoneSpawner` en la escena (hay 2, con Hellfire y Bomber) y que el `BossManager` usa `Boss`/`Boss_2` (usa `Stalker` y `Destroyer_Boss`).
- **Scavenging con escala mezclada.** `EnemyMaterialDrop.cs:29` hace `chance × (1 + Scavenging)`, con Scavenging base 50 (`Scavenging.asset:17`) → chance siempre 1. `EnemyScrapDrop` y `PlayerDropMath` lo tratan como porcentaje (÷50, ÷100). Si `GetStat` devuelve 50, `_dropChance` de los configs no tiene efecto. No se verificó en play.
- **Elites con drop de su familia base.** `Slime_Elite` usa `JunkSlimeDrops`, no `HellfireSlimeDrops`, aunque se comporta como Hellfire. Lo mismo `Drone_Elite` y `Chaser_Elite`.
- **Destroyer 1000 HP en prefab, 1500 en run.** El valor del prefab engaña si se lo testea suelto.
- **Comentario viejo en `BomberDroneBehavior.cs:3-6`.** Dice 3 dashes × 5 C4; los prefabs usan 2×2 (variante) y 2×4 (elite).
- **`BossKeyDrop` depende del prefab.** Un boss nuevo sin el componente no suelta llave.
- **`CreateDefaultDropConfigs` pisaría los drops afinados** si se borra `MaterialUsageBalance.asset` o se usa el menú.

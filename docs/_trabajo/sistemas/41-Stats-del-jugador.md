# Stats del jugador — alpha 23-09-2026

Cada stat del jugador es un `StatDefinition` (ScriptableObject) con valor base. En runtime se le apilan modificadores aditivos y multiplicativos de level-up, pasivos, powerups, meta y efectos. `PlayerStats` es la única fuente del valor actual y los consumidores (movimiento, armas, vida, drops) lo leen bajo demanda. La vida actual, los escudos y los i-frames viven aparte, en `PlayerHealth`.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/Player/PlayerStats/StatsCore.cs` | Enums (`StatType`, `StatCategory`, `StatUpgradeSource`, `StatModifierType`), `StatDefinition`, `StatModifier`, `RuntimeStat` |
| `Assets/Scripts/Player/PlayerStats/PlayerStats.cs` | Mapa `StatType → RuntimeStat`, alta y baja de modificadores, evento `OnStatChanged` |
| `Assets/Scripts/Player/PlayerStats/PlayerStatMath.cs` | Clamps de consumo: resistencia, regen, lifesteal, pickup, elite, drops |
| `Assets/Scripts/Player/PlayerStats/PlayerStatsLevelUpHandler.cs` | Ruleta automática de stats por nivel (`StatMath`) |
| `Assets/Scripts/Player/PlayerStats/StatDisplayFormat.cs` | Formato y detección de saturación para UI |
| `Assets/Scripts/Player/PlayerHealth.cs` | Vida, daño, resistencia, regen, escudo, burn, i-frames, muerte |
| `Assets/Scripts/Meta/MetaProgressionApplier.cs` | Multiplicadores de meta al arrancar la run |
| `Assets/Scripts/Weapon/UI/StatAttributionPanel.cs` | Vista dev en la pausa: atribución por fuente y último golpe |
| `Assets/Scripts/Weapon/UI/LastHitRecorder.cs` | Guarda el último `WeaponDamageRoll` resuelto |

## StatType

`StatsCore.cs:5-17`. 30 valores, en orden: `MovementSpeed`, `JumpHeight`, `AirJumps`, `DashCharges`, `DashSpeed`, `DamageMultiplier`, `DamageFlat`, `EliteDamageMultiplier`, `AttackSpeedMultiplier`, `ProjectileAreaSize`, `CriticalChance`, `CriticalDamage`, `Knockback`, `AmmoMultiplier`, `MaxHealth`, `HealthRegeneration`, `Lifesteal`, `DamageResistance`, `PickupRange`, `ExtraEliteChance`, `Scavenging`, `DoubleDrop`, `BaseFireInterval`, `AbilityDamageMultiplier`, `AbilityCooldownReduction`, `LongRangeDamageMultiplier`, `CloseRangeDamageMultiplier`, `ShieldCharges`, `ShieldRechargeDelay`, `HealthRegenerationDelayReduction`.

`StatCategory`: Mobility, Offensive, Defensive, Miscellaneous (`:19`).

## Fórmula

`RuntimeStat.CurrentValue` (`StatsCore.cs:123-141`):

`valor = (base + Σ aditivos) × Π multiplicativos`, con `floor` si `IsInteger`.

- La base es `BaseValue` del asset, o un override. `PlayerStats.Awake` pisa la base de `MaxHealth` con `PlayerHealth._maxHealth` (`PlayerStats.cs:25-33`).
- Un multiplicativo de 1 es neutro. Un multiplicativo afecta también a todos los aditivos, no solo a la base.
- `RuntimeStat` no aplica clamps. Los aplica cada consumidor:

| Stat | Clamp | Dónde |
|---|---|---|
| `DamageResistance` | 0–0,95; daño mínimo 1 | `PlayerStatMath.cs:5-12` |
| `Lifesteal` | 0–1 | `PlayerStatMath.cs:27` |
| `ExtraEliteChance` | 0–0,95 | `PlayerStatMath.cs:41` |
| `HealthRegeneration`, `PickupRange` | ≥ 0 | `PlayerStatMath.cs:14-37` |
| `Scavenging` | `/100`, 0–1 = prob. de drop | `PlayerStatMath.cs:73` |
| `DoubleDrop` | `(DoubleDrop + (Scavenging − base)) / 100`, 0–1 | `PlayerStatMath.cs:76-87` |
| `CriticalChance` | 0–1 | `WeaponDamageResolver.cs:414` |
| `AbilityCooldownReduction` | 0–0,95 | `WeaponMath.cs:109` |
| `MovementSpeed` | ≥ 0,1 | `PlayerStats.cs:141` |
| `BaseFireInterval` / `AttackSpeedMultiplier` | intervalo ≥ 0,05 / mult ≥ 0,01 | `PlayerStats.cs:133-138` |

`GetStat` devuelve 0 y loguea un warning si falta la definición (`PlayerStats.cs:54-61`). `GetStatInt` hace `floor`.

## Fuentes de modificadores

`StatUpgradeSource` (`StatsCore.cs:20`):

| Fuente | Quién la usa | Tipo | SourceReference |
|---|---|---|---|
| `Base` | `MetaProgressionApplier` | Multiplicativo 1 + 0,05 × nivel meta | objeto privado |
| `LevelUp` | `PlayerStatsLevelUpHandler`; `PlayerStats.ApplyUpgrade` (legado) | Aditivo | `null` / `Upgrade` |
| `PassiveItem` | `PassiveItemManager` | Según el pasivo | instancia del pasivo |
| `Weapon` | Nadie en el código actual | — | — |
| `TemporaryEffect` | `OverheatManager` → `SetRuntimeFireRateMultiplier` | Aditivo (mult − 1) sobre `AttackSpeedMultiplier` | `PlayerStats` |
| `TemporaryPowerup` | `TemporaryPowerupController` | Ambos | `_buffSource` |

Las bajas se hacen por referencia (`RemoveModifiersFromSource`) o por categoría (`ClearModifiersFromSourceType`) (`PlayerStats.cs:105-120`). Los dos métodos vuelven a emitir `OnStatChanged` para todos los stats.

## Assets de stats

`Assets/ScriptableObjects/PlayerSO/Stats/*.asset`. Los 30 están en `_statDefinitions` del prefab (`player.prefab:705+`) y la escena no los sobreescribe. Cat: M/O/D/X = Mobility/Offensive/Defensive/Misc. Nivel = `UpgradeableByLevel`. Items = `UpgradeableByItems`. Monto = `LevelUpgradeBaseAmount`.

| Asset | StatType | Cat | Base | Nivel | Items | Monto | % | Int |
|---|---|---|---|---|---|---|---|---|
| MovementSpeed | 0 | M | 6,8 | Sí | Sí | 0,04 | | |
| JumpHeight | 1 | M | 3 | Sí | Sí | 0,0025 | | |
| AirJumps | 2 | M | 0 | | Sí | 0 | | Sí |
| DashCharges | 3 | M | 1 | | Sí | 0 | | Sí |
| DashSpeed | 4 | M | 10 | Sí | Sí | 0,025 | | |
| DamageMultiplier | 5 | O | 1 | Sí | Sí | 0,04 | | |
| DamageFlat | 6 | M | 0 | | | 0 | | |
| EliteDamageMultiplier | 7 | O | 1,25 | | Sí | **1,2** | | |
| AttackSpeedMultiplier | 8 | O | 1 | Sí | Sí | 0,03 | | |
| ProjectileAreaSize | 9 | O | 1 | Sí | Sí | 0,03 | | |
| CriticalChance | 10 | O | 0 | Sí | Sí | 0,01 | Sí | |
| CriticalDamage | 11 | O | 2,5 | | Sí | 0 | | |
| Knockback | 12 | O | 1 | | Sí | 0 | | |
| AmmoMultiplier | 13 | O | 1 | Sí | Sí | 0,01 | | |
| MaxHealth | 14 | D | 100 | Sí | Sí | 5 | | |
| HealthRegeneration | 15 | D | 0,5 | | Sí | **1,2** | | |
| Lifesteal | 16 | D | 0 | | Sí | 0 | | |
| DamageResistance | 17 | D | 0 | Sí | | 0,006 | Sí | |
| PickUpRange | 18 | X | 5 | Sí | | 0,2 | | |
| ExtraEliteChance | 19 | X | 0 | | Sí | 0 | | |
| Scavenging | 20 | X | 50 | | Sí | 0 | | |
| DoubleDrop | 21 | X | −30 | | Sí | 0 | | |
| BaseFireInterval | 22 | M | 0 | | | 0 | | |
| AbilityDamageMultiplier | 23 | O | 1 | | Sí | 0 | | |
| AbilityCooldownReduction | 24 | O | 0 | Sí | Sí | 0,02 | Sí | |
| LongRangeDamageMultiplier | 25 | O | 1 | | Sí | 0 | | |
| CloseRangeDamageMultiplier | 26 | O | 1 | | Sí | 0 | | |
| ShieldCharges | 27 | D | 0 | | Sí | 0 | | Sí |
| ShieldRechargeDelay | 28 | D | 0 | | Sí | 0 | | |
| HealthRegenerationDelayReduction | 29 | D | 0 | | Sí | 0 | | |

Entran en la ruleta 12 stats. `CriticalChance` estaba en 1 por nivel y pasó a 0,01 en `Balance 0.3.2` (dd551fe): el bug de datos del level-up está corregido. `DoubleDrop` −30 junto con `Scavenging` 50 significa que no hay doble drop hasta tener Scavenging por encima de 80.

## Level-up automático

`PlayerStatsLevelUpHandler`, tope 36 (`player.prefab:981`).

1. Al iniciar, cada stat con `UpgradeableByLevel` recibe peso 5 (`:29`).
2. Por nivel se hacen N tiradas: 5 (niveles 1–5), 6 (6–10), 7 (11–15), 8 (16–20), 9 (21–25), 10 (26–29), 11 (30–35), 20 (36) (`:117-127`). Un mismo stat puede salir más de una vez.
3. Monto por tirada = `LevelUpgradeBaseAmount × (1 + nivel/18) × U(0,9; 1,1) × crecimiento meta` (`:84-89`, `:132-139`). El crecimiento meta es ×1,15 desde el nivel meta 5 y ×1,15² en el 10 (`SaveManager.cs:366-375`).
4. Se agrega como aditivo `LevelUp` con label `"LvN roll (base X)"` (`:99`). Si el stat es `MaxHealth`, además suma `round(monto)` a la vida máxima de `PlayerHealth` (`:101-102`).
5. La elegida baja 1 de peso por tirada (mínimo 1). Las que no salieron en el nivel suben 1 (`:49`, `:107-115`).

## PlayerHealth

Valores: `_maxHealth` 100 (prefab); `_hitInvulnerabilitySeconds` default 1,5, prefab 1,5, **escena 0,5**; `_regenerationDelaySeconds` 5 (`player.prefab:654-658`).

Daño directo, `TakeDamage` (`PlayerHealth.cs:282-325`):

1. Ignora si está muerto, con invulnerabilidad de powerup o dentro de los i-frames.
2. Si hay carga de escudo, consume una, da i-frames y no quita vida.
3. Aplica resistencia (`round(daño × (1 − res))`, mínimo 1), resta, da i-frames, suena el hurt, emite `OnHitDamageTaken` y `OnHealthChanged`.
4. Con 0 HP: `_isDead`, `ChallengeProgressTracker.NotifyPlayerDied`, `OnPlayerDied` (lo escucha `GameManager`).

Resto:

| Mecánica | Comportamiento | Línea |
|---|---|---|
| i-frames | Mientras duran, `Physics.IgnoreCollision` contra todos los colliders de enemigos. Se re-sincroniza cada `Update` | `:359-395` |
| Burn | DoT en ticks de 0,5 s. Ignora i-frames y escudo; aplica resistencia. Refresca duración y conserva el dps mayor | `:196-251` |
| Regen | `HealthRegeneration` HP/s tras `5 − HealthRegenerationDelayReduction` s sin daño. Acumula fracciones | `:327-351` |
| Escudo | `SetShieldConfig` desde `PassiveItemManager` (`ShieldCharges`, `ShieldRechargeDelay`). Recarga todas las cargas juntas tras el delay sin daño | `:118-129,184-194` |
| Vida máx. | `ApplyMaxHealthDelta`: si sube, cura lo ganado; si baja, recorta | `:90-105` |
| `OnEnable` | Resetea vida al máximo, burn e i-frames | `:150-160` |

## Meta

`MetaProgressionApplier` lo agrega en runtime `SpecMetaBootstrap.cs:93-94`, porque no está en el prefab. En `Start`, para `DamageMultiplier`, `AttackSpeedMultiplier`, `ProjectileAreaSize`, `CriticalChance`, `CriticalDamage`, `MaxHealth`, `HealthRegeneration` y `PickupRange` agrega un multiplicativo `1 + 0,05 × nivel` (0–10, tope ×1,5). Después ajusta la vida máxima por la diferencia (`MetaProgressionApplier.cs:10-20,29-66`). La tienda de meta lo re-aplica (`MetaUpgradeShopUI.cs:613`).

## Herramientas dev

Solo compilan en `UNITY_EDITOR || DEVELOPMENT_BUILD`.

- `StatAttributionPanel`: `PauseMenuUI.cs:80-91` lo monta en `PlayerStatsPanel` al abrir la pausa. Agrega pestañas **STATS** y **LAST HIT** junto al resumen. STATS lista todos los stats por categoría con base, subtotal de level-ups y de pasivos (con cantidad), otros, multiplicador y final. Al elegir uno muestra modificador por modificador con su label. Los stats saturados se pintan en naranja con `[!] consumers clamp this to …` (`StatAttributionPanel.cs:611-625,702-710`). LAST HIT desarma el último golpe en factores (base, nivel de arma, path, stat de daño, habilidad, crítico, elite, rango, escalas) y enlaza al stat correspondiente. Escape cierra primero la pestaña dev.
- `LastHitRecorder`: estático. Se suscribe una vez a `WeaponDamageResolver.OnDamageResolved` en `SubsystemRegistration` y guarda el último roll, para que se pueda ver después de pausar (`LastHitRecorder.cs:18-35`).

## BalanceAutoImporter

`Assets/Scripts/Economy/Editor/BalanceAutoImporter.cs` no toca los stats del jugador. Es `[InitializeOnLoad]`, pero solo corre `ImportAll` y `CreateDefaultDropConfigs` si falta `MaterialUsageBalance.asset` (`:17-30`). Esos pasos escriben assets de economía y armas desde `balance_material_usage.csv` y `balance_weapon_stats.csv`. Ningún importer ni CSV escribe `PlayerSO/Stats`.

## Deuda / puntos abiertos

- El powerup ExtraDamage suma 50–100 **aditivos** a `CriticalChance` (`TemporaryPowerupController.cs:109`), y el resolver clampéa a 0–1 (`WeaponDamageResolver.cs:414`). Durante el buff el crítico queda en 100 % a cualquier nivel. El valor debería ser 0,5–1,0 o, mejor, un aditivo chico.
- El meta de `CriticalChance` es multiplicativo sobre una base 0 (`MetaProgressionApplier.cs:51-57`): solo escala lo que dieron los level-ups (por ejemplo 0,05 → 0,075 en meta 10). Es un upgrade de tienda casi sin efecto.
- `PlayerHealth._maxHealth` se desincroniza de `GetMaxHealthTotal()`. El level-up suma `round(monto)` a la vida (`PlayerStatsLevelUpHandler.cs:101-102`), pero el stat sube `monto × multiplicador meta` y con fracciones. `PassiveItemManager` sincroniza por delta del stat, así que no corrige la deriva acumulada.
- Los modificadores de level-up tienen `SourceReference = null` (`PlayerStatsLevelUpHandler.cs:99`). `RemoveModifiersFromSource(null)` (`StatsCore.cs:150`) borraría todos los level-ups y no hay guarda en `PlayerStats.cs:105`.
- Datos muertos: `EliteDamageMultiplier` y `HealthRegeneration` tienen monto de nivel 1,2 con `UpgradeableByLevel` = 0. `UpgradeableByItems` no se lee en ningún script. `StatUpgradeSource.Weapon` no se usa.
- Montos de nivel casi nulos: `JumpHeight` 0,0025 (base 3) y `DashSpeed` 0,025 (base 10). Se llevan tiradas de la ruleta sin efecto perceptible.
- `StatDisplayFormat.GetEffectiveMaximum` (`:74-84`) no incluye `AbilityCooldownReduction` (clamp 0,95 en `WeaponMath.cs:109`). El panel dev nunca la marca como saturada.
- `ShieldRechargeDelay` base 0: con escudo equipado, sin delay de pasivo, las cargas vuelven todas en el frame siguiente al golpe (`PlayerHealth.cs:184-194`). Además, la escena baja los i-frames a 0,5 s contra 1,5 s en código y prefab: hay que decidir cuál es el valor de diseño.

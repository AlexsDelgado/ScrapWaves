# Economía de materiales y balance CSV — alpha 23-09-2026

Cómo se generan, se recogen y se gastan los materiales dentro de la run, y de dónde salen los números de balance (CSV, ScriptableObjects y el hub de spawn). Los valores serializados en assets y prefabs mandan sobre los defaults de script. Cuando difieren, van los dos.

## Scripts

- `Economy/EconomyBootstrap.cs`: singleton `DontDestroyOnLoad` que se autocrea antes de cargar la escena. Carga el balance de materiales y reimporta el CSV de armas.
- `Economy/CsvReader.cs`: parser CSV propio (comillas, `""`, saltos de línea dentro de comillas). Lee con UTF-8 y descarta el BOM.
- `Economy/MaterialUsageParser.cs`, `Economy/WeaponStatsParser.cs`: traducen los CSV a datos.
- `Economy/MaterialCatalog.cs`: categoría (común o rara), nombres, colores y XP de cada material.
- `Economy/MaterialDropConfig.cs` + `Economy/EnemyMaterialDrop.cs`: tirada de drop por enemigo.
- `Economy/MaterialPool.cs`, `MaterialDrop.cs`, `MaterialPickupReceiver.cs`, `MaterialInventory.cs`: orbe, recogida e inventario.
- `Economy/WeaponCraftingService.cs` + `WeaponCraftingCostCalculator.cs`: mejora, Tinkering y Advanced Tinkering.
- `Economy/MaterialUsageBalanceSO.cs`: roles por columna de arma y totales por nivel.
- Editor: `Economy/Editor/BalanceImportMenu.cs`, `BalanceAutoImporter.cs`, `MaterialUsageCsvImporter.cs`, `WeaponStatsCsvImporter.cs`, `Weapon/Editor/WeaponBalanceSheetApplier.cs`.
- `Balance/BalanceTuningHub.cs` + `Balance/SpawnBalanceProfile.cs` (+ `Balance/Editor/BalanceTuningHubEditor.cs`).

## Materiales

| Material | Categoría | XP al recoger |
|---|---|---|
| SheetMetal, MetalPipe, Gears | Común | 1 |
| JellifiedFuel, PlasticExplosive, Wiring | Rara | 5 |

`MaterialCatalog.cs:6-14` y `:54-55`. La XP es `GetPickupXpValue × amount` (`MaterialPickupReceiver.cs:39-43`), así que un doble drop también duplica la XP. El doc 05 cita el DEV del 21-7 (común = 4, rara = 12). El código da **1 y 5**.

## Drop de materiales

Flujo en `EnemyMaterialDrop.OnEnemyDied` (`EnemyMaterialDrop.cs:19-54`):

1. `chance = DropChance × (1 + Scavenging)`, con clamp a 0–1 (`:27-29`).
2. `Random.value > chance`: no hay drop.
3. `MaterialDropConfig.TryRoll`: ruleta por `WeightPercent`, acumulada sobre 100 (`MaterialDropConfig.cs:21-46`).
4. `amount = _dropAmount`. Se duplica si `Random.value < clamp01(DoubleDrop)` (`:56-61`).
5. `MaterialPool.TrySpawn`. El pool de `GameplayScene` serializa 128 iniciales, con crecimiento hasta **612** (el default de script es 2048).

Configuración serializada (`Assets/ScriptableObjects/Economy/Drops/`):

| Asset | DropChance | Pool | Prefabs (`_dropAmount`) |
|---|---|---|---|
| JunkSlimeDrops | 0.65 | SheetMetal 71 / MetalPipe 12 / Gears 12 / JellifiedFuel 5 | EnemyPro (1), Slime_Elite (2) |
| HellfireSlimeDrops | 0.9 | SheetMetal 40 / JellifiedFuel 60 | Slime (variant) (1) |
| ChaserBotDrops | 0.75 | MetalPipe 95 / Wiring 5 | Chaser (1), Chaser_Elite (2) |
| ShockerBotDrops | 0.9 | MetalPipe 40 / Wiring 60 | Chaser (variant) (1) |
| VigilanceDroneDrops | 0.65 | Gears 95 / PlasticExplosive 5 | Drone (1), Drone_Elite (2) |
| BomberDroneDrops | 0.9 | Gears 40 / PlasticExplosive 60 | Drone (variant) (1) |

`BalanceImportMenu.CreateDefaultDropConfigs` (`BalanceImportMenu.cs:30-48`) escribe otros valores (0.45 / 0.65 / 0.55 y 95/5 o 40/60). Si se ejecuta, pisa los assets de arriba.

### Inconsistencia de Scavenging

Hay dos fórmulas para el mismo stat:

| Dónde | Drop | Doble drop |
|---|---|---|
| `EnemyMaterialDrop.cs:29` / `:60` (runtime real) | `DropChance × (1 + Scavenging)` | `clamp01(DoubleDrop)` |
| `PlayerStatMath.cs:73` / `:86` (`PlayerDropMath`, solo `PassiveItemTestingController`) | `Scavenging / 100` | `(DoubleDrop + Scavenging − base) / 100` |

`Scavenging.asset` tiene base **50** y `DoubleDrop.asset` tiene base **−30**. Con la fórmula del runtime, la chance es `clamp01(0.65 × 51) = 1`: **todo kill dropea**, y la `DropChance` de los assets no tiene efecto. El doble drop arranca en 0, y cualquier bonus positivo neto lo lleva directo a 100 %. La herramienta de sandbox muestra 50 % y otra chance de doble drop. Queda confirmado: las dos fórmulas no coinciden.

## Crafting

`WeaponCraftingService` vive en el prefab `player.prefab`. Serializa `_materialBalance = MaterialUsageBalance.asset` y un `_weaponPool` de 5 armas.

- **Mejora** (`TryUpgradeWeapon`, `WeaponCraftingService.cs:68-90`): hasta nivel 10. El paso 5→6 se bloquea sin path (`:79-80`). El coste sale de `GetUpgradeCost(targetLevel)`. `CraftingUI` siempre pide `Level + 1` (`CraftingUI.cs:262`).
- **Tinkering** (`:92-112`): se desbloquea un arma al azar del pool que no esté equipada y que esté desbloqueada en el save.
- **Advanced Tinkering** (`:149-186`): solo en nivel 5. Ofrece PathA, o PathB si está desbloqueado (50/50). La oferta se guarda y reabrir la estación no la vuelve a tirar (`:122-130`). Rechazar la oferta cobra y garantiza el path alternativo. Aceptarla sube a 6 y aplica el path.

Coste de mejora (`WeaponCraftingCostCalculator.cs:27-51`): para cada material con rol en la columna del arma (base o `FlameA…BladesB` desde nivel 6, `MaterialUsageBalanceSO.cs:61-82`), se toma el total del rol en ese nivel.

| Nivel destino | Principal (X) | Secundario (x) | Terciario (y) | Principal extra (XX) |
|---|---|---|---|---|
| 2 | 15 | 0 | 0 | 0 |
| 3 | 25 | 10 | 0 | 0 |
| 4 | 50 | 20 | 0 | 0 |
| 5 | 100 | 40 | 0 | 0 |
| 6 | — (Advanced Tinkering) | — | — | — |
| 7 | 100 | 50 | 10 | 20 |
| 8 | 125 | 60 | 20 | 40 |
| 9 | 150 | 80 | 40 | 80 |
| 10 | 225 | 120 | 100 | 275 |

Valores tomados de `MaterialUsageBalance.asset` (`_roleTotals`), que coinciden con el CSV. La columna "Base" del CSV se importa como nivel 1 (15/10/10/10), pero ninguna acción la usa.

Roles (CSV, 39 asignaciones):

| Columna | Principal | Secundario | Terciario / extra |
|---|---|---|---|
| Flamethrower | Gears | MetalPipe | — |
| Rocket launcher | MetalPipe | SheetMetal | — |
| Mortar | MetalPipe | Gears | — |
| Autocannon | Gears | SheetMetal | — |
| Rotating Blades | SheetMetal | Gears | — |
| Flame A / B | Gears | MetalPipe | y: JellifiedFuel / Wiring |
| Rocket A / B | MetalPipe | SheetMetal | y: PlasticExplosive / Gears |
| Mortar A / B | MetalPipe | Gears | y: SheetMetal / PlasticExplosive |
| Auto A / B | Gears | SheetMetal | y: JellifiedFuel / MetalPipe |
| Blades A | — (SheetMetal es XX) | Gears | XX: SheetMetal |
| Blades B | SheetMetal | Gears | y: Wiring |

Costes fijos (`WeaponCraftingCostCalculator.cs:53-93`):

| Acción | Slot 1 | Slot 2 | Slot 3 |
|---|---|---|---|
| Tinkering (cada común) | gratis | 5 | 15 |
| Advanced Tinkering (cada rara) | 5 | 15 | 30 |
| Advanced tras rechazo | 8 | 22 | 45 |

El "slot" de Advanced es la cantidad de armas que ya tienen path + 1 (`WeaponCraftingService.cs:222-237`).

## Pipeline CSV

| CSV | Qué alimenta | Estado |
|---|---|---|
| `balance_material_usage.csv` | Roles X/x/y/XX por columna de arma y filas "Total …" por nivel → `MaterialUsageBalance.asset` | Funciona |
| `balance_weapon_stats.csv` | Bloques por arma: filas Total/Added de Damage, rangos, munición, habilidad, crítico, knockback, columna "Scales with" y stats específicas de path | **No importa nada** (ver deuda) |

Entradas:

1. Menú `ScrapWaves/Balance/Import All CSV` (`BalanceImportMenu.cs:13-28`): reescribe `MaterialUsageBalance.asset` y los 5 `WeaponSO`.
2. `BalanceAutoImporter` (`BalanceAutoImporter.cs:17-30`): en cada carga del editor, **solo si `MaterialUsageBalance.asset` no existe**. En ese caso llama a `ImportAll` y a `CreateDefaultDropConfigs`. El asset existe, así que hoy no dispara. La nota de memoria "reimporta en cada domain reload" ya no aplica a este script.
3. `EconomyBootstrap.LoadBalanceData` (`EconomyBootstrap.cs:41-62`): en cada Play, si el SO no tiene roles, parsea el CSV de materiales. **Siempre** corre `WeaponStatsParser.ImportAll` sobre los `WeaponSO` reales (cargados por `AssetDatabase`, `:76-84`). En build, las rutas `Assets/...` no existen y no se importa nada. `WireCraftingServices` corre antes de cargar la escena y no encuentra servicios, así que queda la referencia serializada del prefab.
4. `WeaponBalanceSheetApplier` (menú `Tools/ScrapWaves/Apply Weapon Balance Sheet Snapshot`): snapshot hardcodeado (`WeaponBalanceSheetApplier.cs:36-111`) que escribe `BaseDamage`, munición, cooldown y `LevelData` base y de paths.

## Balance de spawn

`BalanceTuningHub` (`GameplayScene`, orden −60) inyecta `Assets/Data/Balance/SpawnBalanceProfile.asset` en `DifficultyManager`, `HeatManager` y `OrbitalSpawner`, solo en los que no tienen profile propio (`BalanceTuningHub.cs:47-58`). También expone lecturas en vivo y dos acciones de debug: fijar el heat por % de barra y forzar el Overheat (`:131-159`).

| Campo | Asset | Default script |
|---|---|---|
| Delay antes del escalado (s) | 30 | 30 |
| Curva intensidad (min → valor) | 0→0, 5→0.2, 10→0.5, 20→1 | 0, 5→0.21, 10→0.40, 19.46→1 |
| Velocidad de rampa | 0.95 | 1 |
| Máx. multiplicador de cantidad (tiempo) | 3 | 4 |
| Máx. vida / velocidad / daño enemigo | 6 / 3 / 1.35 | 10 / 4 / 1.35 |
| Cantidad a heat lleno / intervalo a heat lleno | 2.5 / 0.5 | 2 / 0.45 |
| Ciclo: paso y piso de intervalo | 0.25 / 0.6 | 0.1 / 0.3 |
| Ciclo: paso y techo de batch y de vida | 0.25 / 2 | 0.1 / 2 |
| Heat: 0→80 % / 80→100 % (puntos) | 60 / 60 | 100 / 100 |
| Escalada por Overheat | 1.4 | 1.5 |
| Heat por kill / decay post-Overheat | 1 / 25 | 5 / 12 |
| Intervalo base spawner (s) / máx. activos | 15 / 300 | 5 / 300 |

## Deuda / puntos abiertos

- **El CSV de armas no se parsea.** `WeaponStatsParser.ApplyBlock` busca "Basic" en la columna 1 (`WeaponStatsParser.cs:80`) y "Total" en la columna 0 (`:120`), pero el CSV los tiene en las columnas 0 y 1. Entonces vacía `BalanceStats` y `UpgradeSpecificStats` y sale. Después `SyncLegacyLevelData` (`:208-230`) reescribe `LevelData` 1–10 con multiplicador 1. Como `EconomyBootstrap` lo corre en cada Play sobre los assets reales, los `WeaponSO` commiteados tienen `BalanceStats: []` y `LevelData` base plano en 1.0. Los niveles 1–5 no escalan daño. Los `LevelData` de path conservan el snapshot.
- `BaseDamage` commiteado (Flamethrower 5, RocketLauncher 25, Mortar 40) no coincide ni con el CSV (25/70/…) ni con el snapshot del applier. Hay que decidir una sola fuente.
- `ApplyDerivedBaseStats` asigna `ActiveAbilityAmmoCost` desde "Ability damage" (`WeaponStatsParser.cs:203`). Hoy está muerto, pero es un bug latente.
- Las dos fórmulas de Scavenging y DoubleDrop (arriba) no coinciden. Con la del runtime, la `DropChance` de los assets de drop no tiene efecto.
- `TryTinkerRandomWeapon` cobra antes de comprobar que haya candidatos (`WeaponCraftingService.cs:101-107`). Si no quedan armas desbloqueadas, se pierden los materiales.
- El mensaje de rechazo dice "+50 %", pero los costes reales son +60 % / +47 % / +50 % (`WeaponCraftingService.cs:185` vs `WeaponCraftingCostCalculator.cs:77-82`). El parámetro `advancedRejected` de `GetTinkeringSlotCost` no se usa.
- `CreateDefaultDropConfigs` tiene valores viejos y pisa los drops si se regenera el asset de balance.
- `MaterialCatalog` da 1/5 de XP, contra el 4/12 del DEV citado en el doc 05.

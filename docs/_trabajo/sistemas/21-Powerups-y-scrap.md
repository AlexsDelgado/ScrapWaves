# Power-ups temporales y Scrap — alpha 23-09-2026

Cubre los power-ups que caen durante la run (buffs cortos, curación, nuke) y el Scrap, la moneda meta que se gana en la run y se gasta fuera de ella. Los números de los power-ups escalan con el nivel del jugador. El Scrap entra por tres vías: pickups raros, kills de boss y liquidación al final de la run.

## Scripts

- `Powerups/TemporaryPowerupController.cs`: aplica los efectos en el jugador. `SpecMetaBootstrap` lo agrega en runtime si falta (`SpecMetaBootstrap.cs:91-92`). Solo está serializado en `Scenes/Testing/test_balance.unity`.
- `Powerups/EnemyTemporaryPowerupDrop.cs`: tirada de power-up al morir un enemigo.
- `Powerups/TemporaryPowerupPickup.cs`, `TemporaryPowerupPool.cs`, `TemporaryPowerupVisualCatalog.cs`: pickup, pool y visuales.
- `Powerups/EnemyScrapDrop.cs`, `ScrapPickup.cs`, `ScrapPickupPool.cs`: Scrap dentro de la run.
- `GameManager.cs` (`CalculateScrapEarned`, `ReportRunToSaveSystem`): liquidación al final de la run.
- `Enemy/BossManager.cs`, `Enemy/SwarmPooledEnemy.cs`: agregan los componentes de drop en runtime.

Ningún prefab de enemigo tiene serializados `EnemyScrapDrop` ni `EnemyTemporaryPowerupDrop`. Se agregan en runtime: `SwarmPooledEnemy.EnsureMetaDropComponents` (`SwarmPooledEnemy.cs:77-83`) para los enemigos del pool y `BossManager` (`BossManager.cs:239-242`) para cada boss.

## Drop de power-ups

`EnemyTemporaryPowerupDrop.OnEnemyDied` (`EnemyTemporaryPowerupDrop.cs:32-45`):

1. Base 1 % para normales y 5 % si `WeaponEnemyClassifier.CountsAsEliteOrBoss` da verdadero (`:9-10`). Elite incluye los nombres con "Elite" o "variant".
2. `chance = base × (Scavenging / 50)`, con clamp a 0–1 (`:39`). Con el Scavenging base (50), la chance queda en 1 % y 5 %.
3. El tipo es uniforme entre los 6 (`Random.Range(0, 6)`, `:43`). No hay pesos.
4. `TemporaryPowerupPool.TrySpawn`. El pickup se recoge caminando: `WorldPickup.ConfigureForGameplayCollection(1.5, 6, 12)` (`TemporaryPowerupPickup.cs:33`).

El pool se autocrea (`TemporaryPowerupPool.cs:60-71`), con 8 iniciales y 48 como máximo. Carga los prefabs `Assets/Prefabs/Pickups/powerups/PowerUp_*.prefab` **solo en editor**, vía `AssetDatabase` (`:214-229`). En build no hay referencias serializadas, así que se usa la esfera de fallback, tinteada por tipo, con la malla de `Resources/TemporaryPowerupVisualCatalog.asset`.

## Efectos

`t = clamp01(nivel / LevelCap)`, con LevelCap 36 (`TemporaryPowerupController.cs:289-294`). Los rangos van de `Lerp(min, max, t)`: en nivel 1, t ≈ 0.03; en nivel 36, t = 1.

| Tipo | Duración | Efecto (`TemporaryPowerupController.cs:99-152`) |
|---|---|---|
| ExtraDamage | 15 s | DamageMultiplier ×1.5–2.5, AttackSpeedMultiplier ×1.75–3, **CriticalChance +50–100 (aditivo)** |
| ExtraSpeed | 15 s | MovementSpeed ×2–4, JumpHeight ×2–3. DashCharges y AirJumps suman 2× lo que ya se tenía (solo si es > 0) |
| ExtraScavenging | 7.5 s | PickupRange ×2–4, Scavenging +25–50, DoubleDrop +25–75 |
| Invulnerability | 7.5 s | `PlayerHealth.GrantInvulnerability` más el flag propio `IsPowerupInvulnerable`, que consulta `PlayerHealth.cs:287` |
| FullHeal | instantáneo | Vida al máximo, recarga de munición manual y reset del cooldown de la habilidad activa (`:154-167`) |
| Nuke | instantáneo | 50–500 de daño en un radio de 20–40 m. Daño pleno hasta r/2, caída lineal hasta 0 en r. Knockback 18 × falloff (`:169-191`) |

Reglas:

- Solo hay **un buff temporizado activo**. `ApplyTimedBuff` llama primero a `ClearBuff` (`:195`), así que agarrar ExtraSpeed durante ExtraDamage cancela el daño. La invulnerabilidad corre en un timer aparte y convive con un buff.
- Los modificadores entran como `StatUpgradeSource.TemporaryPowerup` con una fuente única (`_buffSource`) y se sacan en bloque con `RemoveModifiersFromSource`.
- Hay FX de partículas creadas por código, con el color del power-up. Bajan la emisión en los últimos 2.5 s.
- En `test_balance`, `LogStatDeltas` loguea el antes y el después de cada stat que tocó.

## Scrap dentro de la run

`EnemyScrapDrop.OnEnemyDied` (`EnemyScrapDrop.cs:22-44`):

| Enemigo | Resultado |
|---|---|
| Boss (`WeaponEnemyClassifier.GetKind == Boss`) | +5–10 Scrap directo al save (`Random.Range(5, 11)`) y `SaveNow()`. No hay pickup |
| Resto | Chance `0.001 × (Scavenging / 50)`, o sea 0.1 % con la base. Spawnea `ScrapPickup` |

`ScrapPickup.OnPickedUp` suma 1 Scrap y guarda el archivo en el acto (`ScrapPickup.cs:24-25`). El Scrap recogido persiste aunque la run termine en derrota. El pool (`ScrapPickupPool`) se autocrea con 8 instancias.

`BossManager` no tiene lógica de drop propia. Solo engancha los componentes de arriba y su handler de muerte (`OnBossInstanceDied`, `BossManager.cs:316-328`), que avisa `OnBossDefeated`.

## Liquidación al terminar la run

`GameManager.EnterEndState` → `ReportRunToSaveSystem(victory)` (`GameManager.cs:98-104`, `:129-140`) → `SaveManager.ReportRunEnded`. Corre tanto en victoria como en derrota.

`CalculateScrapEarned` (`GameManager.cs:142-155`):

```
scrap = round(segundosSobrevividos / 10) + bossKills × 25
      + Σ materiales comunes en inventario × 1
      + Σ materiales raros en inventario × 5
```

Ejemplo: 15 min, 2 bosses, 120 comunes y 10 raros sin gastar dan 90 + 50 + 120 + 50 = **310 Scrap**. El comentario del código lo marca como fórmula placeholder.

Resumen de fuentes de Scrap por boss: 5–10 al morir (`EnemyScrapDrop`) más 25 en la liquidación.

Otras fuentes: recompensas de logros (ver doc 22) y el botón DEV, que fija 9999.

## Deuda / puntos abiertos

- **ExtraDamage suma +50 a +100 a CriticalChance**, un stat que va de 0 a 1 (`TemporaryPowerupController.cs:109`). Durante 15 s el crítico queda en 100 %. Es el mismo bug de escala que el doc 05. La definición de level-up ya se corrigió (0.01), pero el power-up no.
- ExtraScavenging usa la misma escala "de a 100" (+25–50 Scavenging, +25–75 DoubleDrop). Con la fórmula real de `EnemyMaterialDrop` (ver doc 20), los materiales ya caen al 100 %. El DoubleDrop pasa de −30 a positivo y se clampea a 100 %: el buff termina siendo "doble drop garantizado" más imán. Además sube la chance de power-up y de Scrap (×1.5–2).
- Nuke llama a `enemy.ApplyDamage` directo, sin pasar por `WeaponDamageApplier`: no hay críticos, multiplicadores de daño ni `NotifyDamageInstance`. La muerte igual dispara `OnDied`, así que el nuke genera drops (y power-ups en cadena).
- `ScrapPickup` y el Scrap de boss escriben el JSON en cada pickup. Junto con los reportes por kill del doc 22, son escrituras a disco frecuentes en el hilo principal.
- En build, los power-ups se ven como esferas: los prefabs authored solo se resuelven en editor.
- El tipo de power-up es uniforme. FullHeal y Nuke salen con la misma frecuencia que los buffs, sin control de diseño.
- El boss suma Scrap por dos caminos (5–10 inmediato + 25 al final). Hay que confirmar si eso es intencional.

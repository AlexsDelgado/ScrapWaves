# Level-up y stats automáticas — alpha 23-09-2026

Cómo se gana XP, cuánto pide cada nivel y qué pasa en cada level-up. En cada nivel hay una carta de pasivos a elegir y una ruleta automática de stats, que se resuelven en cola. Las armas no se ganan por level-up: salen de la estación de crafting (doc 20).

## Scripts

- `Player/PlayerXP.cs`: XP acumulada, curva, tope y evento `OnLevelUp`.
- `XP/LevelUpOrchestrator.cs`: cola de niveles pendientes. Coordina los handlers.
- `Player/Passives/PassiveItemLevelUpHandler.cs` + `PassiveItemRoulette.cs` + `XP/DynamicRoulette.cs`: carta de pasivos.
- `Player/PlayerStats/PlayerStatsLevelUpHandler.cs` (incluye `StatMath`): ruleta automática de stats.
- `XP/LevelUpChoiceUI.cs`: UI de la carta (pausa con `timeScale = 0`).
- `XP/LevelUpStatFeedback.cs`: textos flotantes con las stats que subieron.
- `Weapon/Managers/WeaponLevelUpHandler.cs`: carta de armas. Está en el prefab, pero **no se invoca**.
- Legacy: `XP/UpgradeManager.cs` (vacío, marcado como reemplazado) y `XP/XPDrop.cs`, `XPPickup.cs`, `XPPool.cs` (orbes). `GameplayScene` tiene un `XPPool`, pero ningún enemigo lleva `XPDrop` y ningún script llama al pool.

Todos los componentes están en `Assets/Prefabs/player.prefab`, y el orquestador tiene sus cuatro referencias serializadas.

## Entrada de XP

La única fuente es recoger un material: `MaterialPickupReceiver.GrantMaterial` (`MaterialPickupReceiver.cs:39-43`) suma `GetPickupXpValue × amount`. Un común vale 1 y un raro vale 5 (`MaterialCatalog.cs:54-55`). Un doble drop duplica la XP.

`PlayerXP.AddExperience` (`PlayerXP.cs:58-84`) resta el requisito en un loop y dispara un `OnLevelUp` por cada nivel. Un solo pickup puede dar varios niveles. Al llegar al tope, la XP sobrante se descarta.

## Curva de XP

Valores serializados en `player.prefab`, iguales a los defaults: primer requisito 10, multiplicador 1.2, tope 36. `GameplayScene` no los sobreescribe.

Requisito para pasar del nivel L al L+1: `req(1) = 10`, `req(L) = ceil(req(L−1) × 1.2)` (`PlayerXP.cs:86-96`). La cuenta es en float de 32 bits, igual que en Unity. Por redondeo float, del nivel 21 en adelante difiere en +1 de la cuenta exacta.

| L→L+1 | XP | Acum. | L→L+1 | XP | Acum. | L→L+1 | XP | Acum. |
|---|---|---|---|---|---|---|---|---|
| 1→2 | 10 | 10 | 13→14 | 101 | 538 | 25→26 | 925 | 5 446 |
| 2→3 | 12 | 22 | 14→15 | 122 | 660 | 26→27 | 1 110 | 6 556 |
| 3→4 | 15 | 37 | 15→16 | 147 | 807 | 27→28 | 1 332 | 7 888 |
| 4→5 | 18 | 55 | 16→17 | 177 | 984 | 28→29 | 1 599 | 9 487 |
| 5→6 | 22 | 77 | 17→18 | 213 | 1 197 | 29→30 | 1 919 | 11 406 |
| 6→7 | 27 | 104 | 18→19 | 256 | 1 453 | 30→31 | 2 303 | 13 709 |
| 7→8 | 33 | 137 | 19→20 | 308 | 1 761 | 31→32 | 2 764 | 16 473 |
| 8→9 | 40 | 177 | 20→21 | 370 | 2 131 | 32→33 | 3 317 | 19 790 |
| 9→10 | 48 | 225 | 21→22 | 445 | 2 576 | 33→34 | 3 981 | 23 771 |
| 10→11 | 58 | 283 | 22→23 | 534 | 3 110 | 34→35 | 4 778 | 28 549 |
| 11→12 | 70 | 353 | 23→24 | 641 | 3 751 | 35→36 | 5 734 | 34 283 |
| 12→13 | 84 | 437 | 24→25 | 770 | 4 521 | | | |

Llegar al 36 pide 34 283 XP, que son unos 34 000 comunes o 6 900 raros. Con el drop al 100 % que da la fórmula real de Scavenging (doc 20), la XP cae en casi cada kill.

## Orquestador

`LevelUpOrchestrator` (`LevelUpOrchestrator.cs:43-74`):

1. `OnLevelUp(n)` encola `n`. Si no hay proceso en curso, arranca la corrutina.
2. Por cada nivel en la cola, en orden:
   1. `PassiveItemLevelUpHandler.PresentAndApplyCoroutine(n)`: la carta pausa el juego y espera la elección.
   2. `PlayerStatsLevelUpHandler.ApplyLevelUpStats(n)`: la ruleta de stats es inmediata.
   3. `LevelUpStatFeedback.Show(resultados)`: encola un lote de textos flotantes (1.4 s por mensaje).
3. `_weaponHandler` está serializado y resuelto en `Awake`, pero no se usa.

## Carta de pasivos

`PassiveItemLevelUpHandler`: `_choicesOffered = 3` en el prefab. El pool tiene los 17 pasivos, filtrados por `SaveManager.IsUnlocked` (`:28-38`).

- Elegibles por slot (`PassiveItemManager.cs:174-218`): si el slot está lleno, solo las mejoras de los equipados con `Level < MaxLevel` (6 en los 17 assets; el default de script es 5). Si no está lleno, los ítems del slot que no estén equipados, más las mejoras de los equipados.
- Ruleta (`PassiveItemRoulette.cs`): cada clave (ítem nuevo o mejora de una instancia) arranca con peso 5. Se sacan 3 distintos por peso. El elegido baja 1 (mínimo 1) y los otros dos ofrecidos suben 1. Lo no ofrecido no cambia.
- Si no hay elegibles, se loguea un warning y el nivel sigue solo con stats.

## Ruleta automática de stats

`PlayerStatsLevelUpHandler` (`_levelCap = 36` serializado, duplicado del de `PlayerXP`).

Stats elegibles: las `StatDefinition` con `UpgradeableByLevel = 1`. Todas arrancan con peso 5 (`:20-31`).

Tiradas por nivel (`GetUpgradeCountForLevel`, `:117-127`):

| Nivel alcanzado | 2–5 | 6–10 | 11–15 | 16–20 | 21–25 | 26–29 | 30–35 | 36 |
|---|---|---|---|---|---|---|---|---|
| Tiradas | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 20 |

En total son 296 tiradas del nivel 2 al 36.

Pesos (`:33-54`, `:107-115`): cada tirada sortea por peso y la stat elegida baja 1 en el acto (mínimo 1). La misma stat puede salir varias veces en un nivel. Al cerrar el nivel, las que no salieron suben 1.

Monto por tirada (`:78-105`, `StatMath`, `:130-141`):

```
monto = LevelUpgradeBaseAmount × (1 + nivel / 18) × U(0.9, 1.1) × crecimientoMeta
```

`1 + nivel/18` va de 1.11 (nivel 2) a 3.0 (nivel 36). `crecimientoMeta` es ×1, ×1.15 desde el nivel meta 5 de esa stat y ×1.3225 en el 10 (`SaveManager.cs:366-375`). Entra como modificador **aditivo** `StatUpgradeSource.LevelUp`. Si la stat es MaxHealth, además suma `round(monto)` a la vida actual.

| Stat (asset) | Base | Base stat | Por tirada nv 2 / nv 36 | Esperado al 36* |
|---|---|---|---|---|
| MaxHealth | 5 | 100 | 5.6 / 15 | +273 |
| DamageMultiplier | 0.04 | 1 | 0.044 / 0.12 | +2.19 |
| AttackSpeedMultiplier | 0.03 | 1 | 0.033 / 0.09 | +1.64 |
| ProjectileAreaSize | 0.03 | 1 | 0.033 / 0.09 | +1.64 |
| MovementSpeed | 0.04 | 6.8 | 0.044 / 0.12 | +2.19 |
| PickUpRange | 0.2 | 5 | 0.22 / 0.6 | +10.9 |
| CriticalChance (0–1) | 0.01 | 0 | 0.011 / 0.03 | +0.55 |
| DamageResistance (0–1) | 0.006 | 0 | 0.007 / 0.018 | +0.33 |
| AbilityCooldownReduction (0–1) | 0.02 | 0 | 0.022 / 0.06 | +1.09 |
| AmmoMultiplier | 0.01 | 1 | 0.011 / 0.03 | +0.55 |
| DashSpeed | 0.025 | 10 | 0.028 / 0.075 | +1.37 |
| JumpHeight | 0.0025 | 3 | 0.003 / 0.0075 | +0.14 |

\* Estimación: las 296 tiradas se reparten parejo entre las 12 stats (los pesos tienden a igualarse), sin meta y con factor aleatorio medio 1. Suma de escalas ≈ 656 / 12 ≈ 54.7 × base.

El crecimiento meta solo tiene efecto en las 6 stats de la tienda que además son de level-up: DamageMultiplier, AttackSpeedMultiplier, ProjectileAreaSize, CriticalChance, MaxHealth y PickupRange.

## WeaponLevelUpHandler

Implementado: ofrece 2 o 3 cartas de armas ("Elige un arma"), con pool, pesos y descripciones. Está en `player.prefab` y tiene referencia en el orquestador, pero `ProcessSingleLevelUp` no lo llama. Queda por diseño: el loadout se decide al inicio de la run y en crafting (doc 05).

## Deuda / puntos abiertos

- La XP por material es 1/5, no 4/12 como en el DEV del 21-7. Con 34 283 XP para el tope, hay que medir el tiempo-a-nivel real antes de tocar la curva.
- **AbilityCooldownReduction** suma ≈ +1.09 esperado al nivel 36 en un stat de 0 a 1. El consumidor lo clampéa a 0.95 (`WeaponMath.cs:109`), así que cerca del tope de nivel la habilidad queda con el 5 % del cooldown. DamageResistance (clamp 0.95, `PlayerStatMath.cs:10`) llega a ≈ 0.33 solo con level-ups, antes de sumar pasivos.
- `_levelCap` está duplicado en `PlayerXP` y en `PlayerStatsLevelUpHandler`. `TemporaryPowerupController` usa el de `PlayerXP`. Si divergen, el escalado se desalinea.
- MaxHealth agrega el monto sin redondear al stat, pero a la vida actual le suma `round(monto)`. Puede quedar un desfase fraccional entre vida y máximo.
- El título de la carta está en español ("Elige un objeto" / "Elige un arma"), con el resto de la UI de meta en inglés.
- Hay restos legacy: `UpgradeManager`, y `XPPool` en `GameplayScene` junto con el prefab `xp drop`, sin uso.
- El salto de 11 a 20 tiradas en el nivel 36 es el único nivel con ese volumen. Como el nivel 36 es el tope, esas 20 tiradas pasan una sola vez, al final.

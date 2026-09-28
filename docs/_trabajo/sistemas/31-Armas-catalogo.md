# Armas: catálogo — alpha 23-09-2026

Una sección por arma de producción. Cada una cubre el rol, el automático, el manual, la habilidad (Q), las rutas A/B y la sinergia con el heat. La arquitectura común (slots, munición, resolver, niveles) está en `30-Armas-arquitectura.md`.

## Cómo leer los números

- **CSV** = `Assets/Data/Balance/balance_weapon_stats.csv`. Es la intención de balance por nivel.
- **Asset** = `Assets/ScriptableObjects/WeaponSO/<Arma>.asset`. Es lo que corre hoy.
- Las dos fuentes no coinciden en daño base, cadencia ni rangos. El importador del CSV no carga ningún valor (ver `30-Armas-arquitectura.md`, "Niveles"). Consecuencias en runtime:
  - Niveles 1–5: `DamageMultiplier` 1 en todo `LevelData`, así que el daño es el `BaseDamage` del asset en los cinco niveles.
  - Niveles 6–10: `PathA/PathB.LevelData` sí escalan. El multiplicador sale de dividir el daño del CSV por el daño base del CSV, pero se aplica sobre el `BaseDamage` del asset.
  - La munición sí coincide, porque `BaseManualAmmo` del asset es igual a la base del CSV en las cinco armas.
  - El cooldown de habilidad es el `SkillCooldown` fijo del asset. Ningún nivel lo baja.
- En las tablas, "A6→A10" es el rango del nivel 6 al 10 por la ruta A. La fila **Runtime** es lo que calcula el código hoy.
- `h` = `HeatManager.NormalizedHeat` (0–1, por tramos; 0.8 = fin del primer tramo).

| Arma | Daño base CSV | `BaseDamage` asset | `BaseAttackRate` | `BaseRange` | Munición | Costo Q | CD Q |
|---|---|---|---|---|---|---|---|
| AutomaticCannon | 20 | 5 | 5 (no se lee) | 25 | 200 | 20 | 6 |
| RocketLauncher | 70 | 25 | 0.9 | 20 | 40 | 10 | 8 |
| Flamethrower | 25 | 5 | 1 (no se lee) | 7 | 100 | 40 | 14 |
| Mortar | 100 | 40 | 0.75 | 22 | 15 | 5 | 10 |
| RotatingBlade | 60 | 25 | 0.8 (no se lee) | 4.4 | 50 | 8 | 5 |

## AutomaticCannon

**Rol.** DPS de línea con crítico. El automático tira ráfagas cortas; el manual, ráfagas sostenidas.

**Automático.** Busca objetivo dentro de `BaseRange` 25 con `BodyForward180` (`AutomaticCannonWeapon.cs:145-146`, `:1403-1414`). Tira una ráfaga de 3 balas (`CannonAutoBurstCount`) separadas 0.1 s (`:1338-1351`). Antes de cada bala revisa el ángulo y corta la ráfaga si el objetivo salió del frente (`:1370-1375`). La cadencia sale de `CannonAutoBurstsPerSecond` 0.5 × AttackSpeed × multiplicador de nivel (`:422-437`). Eso es una ráfaga cada 2 s. No usa `GetFireInterval`, así que `BaseAttackRate` 5 no se lee.

**Manual.** Mientras se mantiene el click, dispara ráfagas de hasta 5 balas (`clamp(ceil(ammo),1,5)`), a 1 de munición por bala y 1 ráfaga/s (`:238-240`, `AutomaticCannonFireLogic.cs:6-15`). La dirección sigue la retícula durante la ráfaga (`:1423-1425`). La escala de daño es 1 plano: el manual no recibe el bonus de heat (`:263`). No existe un rango manual; la fila "Manual range" del CSV no se usa.

**Habilidad.** Se activa al presionar, no hay que mantener. Tira una escopeta de `20 + floor(h·100/5)` balas, de 20 a 40 (`:321-324`), con dispersión de 11° (`CannonAbilityScatterRadius`, que pese al nombre es un ángulo). Gasta 20 de munición y acepta munición parcial (`:302`).

**Crítico.** El multiplicador es `CriticalDamage × CannonCriticalDamageMultiplierOverride` 2, y × 1.5 más en ruta B (`:339-345`).

| Stat (CSV) | 1 | 2 | 3 | 4 | 5 | A6→A10 | B6→B10 |
|---|---|---|---|---|---|---|---|
| Daño por bala | 20 | 25 | 30 | 35 | 40 | 45→100 | 300→600 |
| Cadencia auto (ráfagas o balas/s) | 0.75 | 0.85 | 0.95 | 1.05 | 1.25 | 3.5→10 | 0.5→1.9 |
| Cadencia manual | 1.0 | 1.1 | 1.2 | 1.3 | 1.5 | 6→11 | 0.75→2 |
| Munición manual | 200 | 220 | 240 | 260 | 280 | 400→600 | 40→80 |
| CD habilidad (s) | 6 | 5.5 | 5 | 4.5 | 4 | 3.75→2 | 3.75→2 |
| Crítico % | 5 | 10 | 15 | 20 | 25 | 30→60 | 35→90 |
| Específico | | | | | | 40 balas/s (80 en 10) | Punto débil ×2.5→×6 |
| **Runtime daño** | 5 | 5 | 5 | 5 | 5 | 11.25→25 | 75→150 |

**Ruta A — Continuous Fire.**
- Automático: bala única, con intervalo dividido por `(1.25 + floor(h·100/2)·0.01) × 3`. Da 1.875 balas/s en frío y 2.625 en caliente (`:428-432`, `:387-395`).
- Manual: `(1.25 + bonus de heat) × 5`, de 6.25 a 8.75 balas/s. El 1.25 está hardcodeado aunque exista el campo del tuning (`:377-385`, `:397-406`).
- Habilidad: ráfaga de 40 balas/s (constante, `:46`) durante `2 + floor(h·2)` s, entre 80 y 160 balas (`:455-469`). Cuesta 80 de munición, fijo en `WeaponMath.cs:96-99`.

**Ruta B — Head Hunter.**
- Automático: una línea perforante, hasta 10 objetivos, con −10 % de daño por cada objetivo extra (`:512-520`) y rango de 1000 (`:527-530`). El intervalo se divide por `HeadHunterAutoAttackSpeedMultiplier` 0.2 (`:434-435`): **un tiro cada 10 s**.
- Manual: cadencia × 1.75 (`:412-413`). El daño es ×2 contra elites y ×3 contra bosses, hardcodeado (`:502-510`). El punto débil solo cuenta en tiros manuales (`:254`).
- Habilidad: carga de 1 s que frena el movimiento (`:623-636`) y después un disparo con perforación ilimitada. Ese disparo aplica siempre el multiplicador de punto débil, pegue o no en uno (`:546-547`).

**Heat.**

| Uso | Fórmula | Línea |
|---|---|---|
| Daño automático (no en Head Hunter) | `1 + clamp(floor(h·100/25),0,3)·0.15` → 1.00 / 1.15 / 1.30 / 1.45 en 25/50/75 % | `:348-359` |
| Balas de la habilidad | +1 cada 5 %, sin tope (+20 al 100 %) | `:362-369` |
| Cadencia ruta A | +0.01 cada 2 % (hasta +0.50) | `:382-393` |
| Duración de la habilidad A | +1 s en h ≥ 0.5 y otro en h = 1 | `:457` |
| Punto débil | `clamp(5 + floor(h/0.2), 5, 10)` | `:481-483` |

`GetHeatFireRateMultiplier` devuelve 1 (`:336`), pero da igual: el cañón nunca llama a `GetFireInterval`.

**Problemas conocidos.**
- La habilidad de la ruta A gasta 80 y puede dejar la munición en 0. Entonces `EndManualMode` saca el arma del manual (`WeaponManager.cs:350-351`), la habilidad deja de tickear y `_continuousFireActive` queda en true. La ráfaga pendiente sigue la próxima vez que el arma entre en manual.
- El automático de Head Hunter (un tiro cada 10 s) queda muy lejos del CSV, que pide 0.5→1.9 balas/s.
- `GetContinuousFireBonus` (`:486-492`, que incluye el bonus del nivel 10) no se llama nunca. Tampoco se usan `FireLineBurst` (`:1243`) ni `GetRemainingHealth` (`:1767`). El ×1.25 de la ruta B en la habilidad base (`:325`) es inalcanzable.
- Valores hardcodeados que el CSV quiere escalar: el punto débil (5→10 en código, 2.5→6 en el CSV), las 40 balas/s (el CSV pide 80 en nivel 10) y el rango de Head Hunter.

## RocketLauncher

**Rol.** Área media con knockback. Cohetes curvos en automático; el lock-on múltiple es la habilidad.

**Automático.** Elige un objetivo en `BaseRange` 20 y guarda su punto de impacto al empezar la volea; el cohete va a ese punto fijo (`RocketLauncherWeapon.cs:115`, `:126`, `:422`). La volea tiene `RocketAutoBaseRocketCount` 1 + los cohetes de heat, con 0.11 s entre cohetes (`:421`, `:443-447`). Explosión de radio 2.5, falloff 1.0 y velocidad ×0.3 (asset). El cohete sube primero hasta el tope del jugador + 0.6 y sigue una Bézier cuadrática (`:494`, `AutomaticRocketTrajectory.cs:22-45`). El heat no acelera el automático (`GetHeatFireRateMultiplier` → 1, `:327`). Con 0.9 de cadencia, sale una volea cada ~1.11 s.

**Manual.** Un cohete recto por disparo, a 1 de munición, hacia `Spawn + aim × BaseRange` (`:149`, `:155`). Explosión de radio 4, falloff 0.65, velocidad ×0.6. Intervalo = `GetFireInterval / (1 + h)` (`:330-335`).

**Habilidad (mantener Q).** Implementa `IHoldActiveAbilityBehaviour`.
- Cono de 90° en `BaseRange` (`:531-537`). Arranca con 5 locks y suma uno cada 0.15 s mientras se mantiene (`:233-238`, `:511-516`).
- Máximo de locks: `10 + floor(h·10)`, de 10 a 20 (`:497-501`). Cuando todos los enemigos ya tienen lock, un boss acepta hasta 5 y un elite 2 (`:548-556`).
- Al soltar: un cohete por lock, con daño ×1.5, radio 2 y velocidad ×1.15 (`:298-306`).
- Cuesta 10 de munición, y solo se cobra si hubo al menos un lock (`:277-289`).

| Stat (CSV) | 1 | 2 | 3 | 4 | 5 | A6→A10 | B6→B10 |
|---|---|---|---|---|---|---|---|
| Daño | 70 | 80 | 90 | 100 | 110 | 130→250 | 120→175 |
| Explosión auto | 1.0 | 1.125 | 1.25 | 1.375 | 1.5 | 2→3.5 | 0.75→1.25 |
| Explosión manual | 2.0 | 2.25 | 2.5 | 2.75 | 3.0 | 3.5→5 | 1.5→2 |
| Rango auto / manual | 20 / 40 | | | | | 25 / 50 | 25 / 50 |
| Munición | 40 | 45 | 50 | 55 | 60 | 70→100 | 70→100 |
| CD habilidad | 8 | 8 | 8 | 8 | 7 | 7→4 | 7→4 |
| Knockback | 1.5 | | | | | 6 | 0.75 |
| Específico | | | | | | Debuff 5→10 s, ×1.25→×3 | Cono ×2 (×5 en 10) |
| **Runtime daño** | 25 | 25 | 25 | 25 | 25 | 46.4→89.3 | 42.9→62.5 |

**Ruta A — Kinetic explosion.** Radio ×2 en todos los modos (`:363-364`; el CSV implica ×1.33) y falloff ×0.65 (`:372-373`). Knockback ×3, o ×0.5 en la habilidad (`:380`). Cada cohete aplica un amplificador de daño recibido de ×1.2 durante 5 s, fijo (`:707-708`). El CSV pide que escale de 5 a 10 s y de ×1.25 a ×3.

**Ruta B — Fragmentation cap.**
- Radio ×0.5 en auto y manual (`:365-366`) y knockback ×0.75 (`:382`). En auto suma +1 cohete (`:353`).
- Cada explosión suelta fragmentos en un cono de 45°, con alcance = radio × 4 y daño ×1 (`:386-407`).
- La habilidad no hace lock-on: al soltar sale un cohete por la mira que libera 20 cohetes de racimo a ×0.5 de daño (`:251-271`, `:393-398`, `:716-751`).

**Heat.**

| Uso | Fórmula | Línea |
|---|---|---|
| Cadencia manual | intervalo / `(1 + h)` → hasta ×2 | `:330-335` |
| Cohetes extra en auto | +1 en h ≥ 0.25, 0.50 y 0.75 | `:343-347` |
| Máximo de locks | +`floor(h·10)` | `:499` |

**Problemas conocidos.**
- El manual usa el mismo `BaseRange` 20 que el automático; el rango manual de 40–50 del CSV no existe.
- `GetFirstActiveTarget` (`:627-636`) está muerto. Las ramas de ruta B en `:536` y `:542-546` son inalcanzables.
- `TickManual` usa `Spawn.position` (`:155`) sin chequear null, a diferencia de `:112` y `:172`.
- Todos los números de ruta (×2, ×0.65, 20 cohetes de racimo, 45°, ×1.2 / 5 s) están hardcodeados y no escalan por nivel.

## Flamethrower

**Rol.** Control cercano por ticks, con burn o freeze según la ruta.

**Automático.** No usa targeting: quema hacia el frente del cuerpo, proyectado al plano (`FlamethrowerWeapon.cs:241-250`). Cono de 45° con hasta 64 objetivos por tick (`:276-282`). Rango = `BaseRange` 7 × área (`:716-719`). Tick de 1 s, que baja a 0.5 s con heat ≥ 75 % (`:253-260`). En el básico no aplica burn; solo las rutas lo aplican, y solo si el golpe entró (`:287-292`).

**Manual.** Es una manguera: una polilínea de 12 segmentos con radio 0.75 (`:306-312`, `:721-724`), no un cono. Gasta 10 de munición por segundo en cada frame, también entre ticks (`:128-130`). Cada tick de 0.5 s pega daño ×1 y aplica burn siempre (`:152-164`). Rango = `7 × área × (1 + 0.75·h)`, de 7 a 12.25 m (`:263-267`).

**Habilidad.** Barrido de 360° con radio 6 × área (×1.2 en ruta A, ×0.9 en ruta B), daño ×2, y burn o freeze a todo lo que alcanza (`:180-208`, `:747-755`). Cuesta 40.

| Stat (CSV) | 1 | 2 | 3 | 4 | 5 | A6→A10 | B6→B10 |
|---|---|---|---|---|---|---|---|
| Daño por tick | 25 | 35 | 45 | 55 | 65 | 75→150 | 80→200 |
| Rango auto | 3 | 3 | 3.5 | 3.5 | 4 | 4→7 | 4→7 |
| Rango manual | 12 | 13 | 14 | 15 | 16 | 17→22 | 17→22 |
| Munición | 100 | 120 | 140 | 160 | 180 | 200→300 | 200→300 |
| Radio habilidad | 6 | 8 | 10 | 12 | 14 | 16→24 | 16→24 |
| CD habilidad | 14 | 13 | 12 | 11 | 10 | 9→5 | 9→5 |
| Específico | | | | | | Charco 1→3 m, 3→5 s | Freeze 2→6 s |
| **Runtime daño** | 5 | 5 | 5 | 5 | 5 | 15→30 | 16→40 |

**Ruta A — Jellified fuel.**
- Burn ×1.35 y duración ×2, o sea 6 s (`:353`, `:726-732`).
- Cada aplicación de burn deja un charco con radio `0.75 × levelScale` que dura lo mismo que el burn (`:507-518`). `levelScale = max(1, Level/6)` vale 1.0 en nivel 6 y 1.67 en nivel 10 (`:509`, `:744`).
- La habilidad deja un charco grande (`:211-222`).

**Ruta B — Liquid nitrogen.** Reemplaza el burn por un slow que baja de ×0.5 a ×0.1 en 6 golpes y dura 3 s (`:542`). La habilidad congela 2 s, fijo, y después aplica ×0.1 durante 4 s (`:531-535`).

**Heat.** Tick automático de 0.5 s con h ≥ 0.75 (`FlameOverheatTickThresholdPercent` 75, `:255-256`) y rango manual × `(1 + 0.75·h)` (`:265-266`). No recibe el bonus de cadencia base: usa sus propios timers.

**Problemas conocidos.**
- `BaseKnockback` 0 en el asset anula todo el knockback: las escalas 0.25 del manual y 3 de la habilidad multiplican por 0 (`WeaponMath.cs:118-121`).
- En manual, la ruta A crea un charco por objetivo por tick, hasta 64 cada 0.5 s y cada uno de 6 s (`:507-518`). Es un riesgo de rendimiento, y además cada charco suma su propio burn.
- El burn manual y el de la habilidad se aplican aunque el golpe directo no haya entrado (`:208`, `:320-323`).
- Con `FlameBurnDuration ≤ 0`, el slow de la ruta B también se apaga, porque ese chequeo corre antes de la rama B (`:472`).
- `FlameManualConeAngle` y `ApplyBurnToTarget` (`:455-463`) no se usan.

## Mortar

**Rol.** Artillería de área con arco, lenta y sin crítico (`MortarWeapon.cs:223`).

**Automático.** `RandomInRange`: toma un enemigo al azar dentro de `BaseRange` 22 (`:72-76`). Al punto de impacto le suma un desvío aleatorio de 3.8 m (`:83`, `:498-505`). Vuelo de 2 s con arco de altura 8, parábola `4·h·t(1−t)` (`MortarTrajectory.cs:12`). Explosión de radio 2 × área, con falloff 0.5 hacia el borde. Intervalo base ~1.33 s dividido por el multiplicador de heat.

**Manual.** El impacto cae siempre a `BaseRange` en la dirección de la mira, no donde apunta la retícula (`:126`). Desvío de 0.75 m, 1 de munición por proyectil. Vuelo de `1 / (1 + 0.5·h)` s: 1 s en frío, 0.667 s con heat al máximo (`:239-243`). Explosión de radio 2.5. El heat no acelera el manual (`:234-237`).

**Habilidad (bombardeo).** Tira `5 + floor(h·10)` proyectiles, de 5 a 15 (`:245-249`). Caen en vertical desde 14 m sobre un disperso de 6 × área (`:174`, `:197`). Daño ×1.2, radio 3.2, caídas escalonadas cada 0.08 s (`:297`). Cuesta 5 y acepta munición parcial (`:170`).

| Stat (CSV) | 1 | 2 | 3 | 4 | 5 | A6→A10 | B6→B10 |
|---|---|---|---|---|---|---|---|
| Daño | 100 | 125 | 150 | 175 | 200 | 100→200 | 225→400 |
| Rango auto / manual | 30 / 40 | | | | | 30 / 40 | 25 / 35 |
| Cadencia auto / manual | 0.25 / 0.5 | | | | | 0.33 / 0.5 | 0.33 / 0.5 |
| Munición | 15 | 16 | 17 | 18 | 20 | 21→30 | 21→30 |
| Radio habilidad | 4 | 4.25 | 4.5 | 4.75 | 5 | 7→9 | 7→8 |
| CD habilidad | 10 | | | | | 10→6 | 10→6 |
| Específico | | | | | | Explosiones 3→8 | Proyectiles extra 15→30, 10/s |
| **Runtime daño** | 40 | 40 | 40 | 40 | 40 | 40→80 | 90→160 |

**Ruta A — Grapeshot (código).** El proyectil ignora colisiones y revienta en el aire, a mitad de camino entre el apex y el impacto (`MortarShellImpact.cs:432`, `:787-795`). La explosión principal no hace daño; lo hacen 15 fragmentos a ×0.5 (`MortarWeapon.cs:261`, `MortarShellImpact.cs:554-560`). En la habilidad cae una lluvia de `(10 + floor(h·10)) × 5` proyectiles comunes durante 5 s, de 50 a 100 en total (`:276-295`).

**Ruta B — Multi-charged shells (código).** El proyectil atraviesa lo dañable y explota 3 veces en el mismo punto, cada 2 s (`:265`, `MortarShellImpact.cs:565-595`). En la habilidad, todos los proyectiles caen juntos a los 0.3 s.

**Heat.**

| Uso | Fórmula | Línea |
|---|---|---|
| Cadencia auto | 1 hasta h = 0.5; después `1 + InverseLerp(0.5,1,h)·0.75`, con tope ×1.75 | `:225-232` |
| Velocidad del manual | `1 + 0.5·h` | `:241` |
| Proyectiles de la habilidad | +`floor(h·10)` | `:247` |
| Lluvia de ruta A | +`floor(h·10)` por segundo | `:279` |

**Problemas conocidos.**
- Las rutas están cruzadas respecto del CSV. El CSV pone "Projectile explosions 3→8" bajo Grapeshot, y "Extra projectile number" y "10/s" bajo Multi-charged. El código hace lo contrario, con valores fijos (3 explosiones, 15 fragmentos, 10/s) que no escalan por nivel.
- Los fragmentos de Grapeshot tienen un radio de explosión de ~0.17 m (`MortarShellImpact.cs:666-700`) y casi no pegan. El `GrapeshotConeAngle` de 70° solo aparece en los diagnósticos.
- `MortarActiveShellCount` no se lee: el 5 está hardcodeado (`:248`). `WeaponBalanceSheetApplier.cs:133` también lo fuerza a 5.
- Un solo rango y una sola cadencia para los dos modos; el CSV distingue auto de manual.

## RotatingBlade

**Rol.** Melee orbital. En automático la hoja gira alrededor del jugador; en manual da tajos en cono.

**Automático.** La hoja orbita a un radio de 2.2 × área, con radio de golpe 0.6 (`RotatingBladeWeapon.cs:395-406`). Los golpes se resuelven sobre el arco recorrido, en tramos de 20° (`:268-283`). Gira a `240°/s × AttackSpeed × multiplicador de nivel` (`:223-229`), unas 0.67 vueltas/s. No lee `BaseAttackRate`. El daño se aplica cada `0.25 / cadencia` s (`:231-237`). Knockback `1.5 + 0.5·h` (`:389-393`).

**Manual.** Tajo en cono de 85° con rango 4.4 × área (`:408-411`). Cooldown de `0.85 / cadencia` y 1 de munición por tajo (`:239-245`, `:142`). Daño × `(1 + h)`, hasta ×2 (`:343-347`).

**Habilidad (estocada).** Línea de ancho 0.8 × área, con daño ×1.5 y knockback 1.25. Largo = rango manual × `min(3.5 + floor(h·100/20), 10)` (`:413-428`). Cuesta 8.

| Stat (CSV) | 1 | 2 | 3 | 4 | 5 | A6→A10 | B6→B10 |
|---|---|---|---|---|---|---|---|
| Daño | 60 | 70 | 80 | 90 | 115 | 55→65 | 215→295 |
| Largo de la hoja auto | 2 | 2.25 | 2.5 | 2.75 | 3 | 3.25→6 | 3.125→5 |
| Vueltas/s auto | 0.25 | 0.3 | 0.35 | 0.4 | 0.5 | 0.6→2 | 0.65→1.5 |
| Rango manual | 3 | 3.5 | 4 | 4.5 | 5 | 5.25→10 | 5.5→10 |
| Munición | 50 | 55 | 60 | 65 | 75 | 80→120 | 80→120 |
| Largo habilidad | 10 | 10.5 | 11 | 11.5 | 12 | 12.5→20 | 2→4 |
| CD habilidad | 5 | 5 | 4.5 | 4.5 | 4 | 4→2 | 2→1 |
| Específico | | | | | | Hojas extra 1→8 | — |
| **Runtime daño** | 25 | 25 | 25 | 25 | 25 | 22.9→27.1 | 89.6→122.9 (×2, ver problemas) |

**Ruta A — Multi-blade.**
- Número de hojas: `1 + max(1, Level − 5)`, o sea 2 a 6 entre los niveles 6 y 10 (`:297`). El CSV pide 1→8 hojas extra (9 en total en el nivel 10).
- Las hojas salen cada 0.1 s con 8° de abanico (`:382`, `:772`), todo hardcodeado.

**Ruta B — Atomic sharpness.**
- Daño ×2, knockback 0 y giro ×1.5 (`:349-353`).
- La estocada se reemplaza por un dash: rango base = rango manual × 3, o sea 13.2 m (el CSV dice 2 m). Crece × `(1 + golpes)` en hasta 4 pasadas (`:371-380`, `:742-766`).
- El dash da invulnerabilidad por su duración + 0.25 s (`:366-369`).

**Heat.** Daño manual `1 + h` (`:345`), knockback automático `+0.5·h` (`:391`) y largo de la habilidad con +1.0× cada 20 % (`:416`). No afecta el daño automático, los intervalos ni el dash.

**Problemas conocidos.**
- Probable doble conteo en la ruta B: el `DamageMultiplier` de `PathB.LevelData` (3.58→4.92) ya reproduce el CSV, y `:349` vuelve a multiplicar ×2.
- `BladeActiveMaxRangeMultiplier` 10 es inalcanzable: el máximo real es 3.5 + 5 = 8.5.
- En Multi-blade, las estocadas extra pegan sobre la misma línea (solo cambia el VFX, `:619`, `:659-663`). Una munición paga los N tajos y el cooldown se estira ~(N−1)·0.1 s (`:129-132`).
- El dash usa 1.5 de daño hardcodeado (`:355`) en vez de `BladeActiveDamageScale`, e ignora `AtomicDashRangePerHitMultiplier` al calcular la velocidad (`:722`).
- `GetRemainingHealth` (`:909-925`) está muerto.

## Assets Sandbox_*

Hay dos juegos de cinco `Sandbox_<Arma>.asset`, y no son iguales entre sí.

| Carpeta | Uso |
|---|---|
| `Assets/Scripts/Weapon/Testing/SO/` | Los genera `WeaponTestingSandboxSceneBuilder` (`WeaponAssetFolder`, `:15`, `:69-73`) y los referencia `Scenes/Testing/WeaponTestingSandbox.unity`. Sus valores vienen del builder (por ejemplo, el cañón con daño 10 y rango 12) y no siguen el balance de producción. |
| `Assets/ScriptableObjects/WeaponSO/Sandbox_*` | Sin referencias en escenas, prefabs ni código. Son copias huérfanas; el cañón tiene daño 10, rango 25 y `SkillCooldown` 1. |

Ninguno de los dos juegos está en `EconomyBootstrap` ni en los pools del prefab `player`. El sandbox también puede pisar stats en runtime con `WeaponStatOverride` y `WeaponHeatOverride` (ver `06-QA-y-debug.md`).

## Deuda / puntos abiertos

- Ningún número por nivel del CSV llega al juego en los niveles 1–5. Las cinco armas pegan igual en el nivel 1 que en el 5, y el daño base del asset está entre 25 % y 42 % del CSV.
- Los cooldowns de habilidad, los rangos, los radios, la cadencia por nivel y los stats específicos de ruta del CSV no tienen lectura en código. `UpgradeSpecificStats` está vacío en todos los assets.
- Los valores de las rutas están hardcodeados en cada `*Weapon.cs` y no escalan del nivel 6 al 10, salvo `levelScale` del lanzallamas y el conteo de hojas del blade.
- Las rutas del mortero están cruzadas respecto del CSV.
- El automático de Head Hunter dispara cada 10 s.
- El lanzallamas tiene knockback 0.
- En Atomic sharpness, el daño se multiplica dos veces.
- Borrar o documentar el juego huérfano de `ScriptableObjects/WeaponSO/Sandbox_*`.

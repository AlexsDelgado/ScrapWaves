# Daño — alpha 23-09-2026

Hay dos caminos separados. El daño **del jugador hacia enemigos** pasa por un contrato tipado (`DamageRequest` → `IDamageable` → `DamageApplicationResult`) y siempre entra por `WeaponDamageApplier`. El daño **de enemigos hacia el jugador** no usa ese contrato: cada behavior llama `PlayerHealth.TakeDamage(int)` directo. Los dos lados comparten solo la idea de i-frames/invencibilidad y los hooks de audio y challenges.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/Damage/DamageRequest.cs` | Struct inmutable del pedido: daño pedido, daño modificado, canal, status, lifesteal, arma origen |
| `Assets/Scripts/Damage/DamageChannel.cs` | `Direct` / `Status` |
| `Assets/Scripts/Damage/DamageApplicationResult.cs` | Resultado: aplicado, bloqueado, kill, vida antes/después |
| `Assets/Scripts/IDamageable.cs` | `IDamageable` (legacy, `bool ApplyDamage(int)`), `IAuthoritativeDamageable` y la extensión que despacha |
| `Assets/Scripts/Weapon/Projectiles/WeaponDamageApplier.cs` | Punto único de entrada del daño de armas |
| `Assets/Scripts/Weapon/Projectiles/WeaponDamageAmplifierStatus.cs` | Estado Vulnerable: multiplica el daño recibido |
| `Assets/Scripts/Weapon/Managers/WeaponDamageResolver.cs` | Calcula el número (nivel, path, stats, crit, elite, rango) |
| `Assets/Scripts/Weapon/Projectiles/WeaponRadialDamage.cs` | Daño en área con falloff |
| `Assets/Scripts/Enemy/EnemyHealth.cs` | Vida del enemigo, invencibilidad, muerte y despawn |
| `Assets/Scripts/Enemy/EnemyDamageHitZone.cs` | Zona de hit con multiplicador propio (cabeza) |
| `Assets/Scripts/Enemy/Behaviors/DestroyerMouthWeakPoint.cs` | Punto débil con vida propia del Destroyer |
| `Assets/Scripts/Player/PlayerHealth.cs` | Vida del jugador, i-frames, escudo, burn, regeneración |
| `Assets/Scripts/Enemy/EnemyContactDamage.cs` + `Player/PlayerContactHurtbox.cs` / `PlayerContactDamageReceiver.cs` | Daño por contacto |
| `Assets/Scripts/Enemy/EnemyOutgoingDamageScale.cs` | Escala del daño saliente del enemigo |

## Contrato

`DamageRequest` (`DamageRequest.cs:3-26`) clampa a ≥ 0 ambos valores. Defaults del constructor: `statusKind = Burn`, `canTriggerLifesteal = true`, `sourceWeaponId = null`.

| Campo | Significado |
|---|---|
| `RequestedDamage` | Daño que calculó el arma |
| `ModifiedDamage` | Tras Vulnerable. Es lo que el receptor usa |
| `Channel` | `Direct` (impacto) o `Status` (DoT) |
| `StatusKind` | Tipo de estado, para feedback |
| `CanTriggerLifesteal` | Si el daño aplicado cura al jugador |
| `SourceWeaponId` | Crédito de kill para challenges |

`DamageApplicationResult` tiene cuatro fábricas (`DamageApplicationResult.cs:37-99`):

| Fábrica | Cuándo | Notas |
|---|---|---|
| `Rejected` | Daño ≤ 0 o target ya muerto | `Applied = false`, `Blocked = false` |
| `BlockedResult` | Invencibilidad | `Blocked = true` |
| `FromHealthDelta` | Caso normal | `AppliedDamage = antes − después`. `Killed` si llega a 0 |
| `FromLegacy` | Target que solo implementa `IDamageable` | `IsAuthoritative = false`, vida 0/0, nunca `Killed` |

`ModifiedDamage` del resultado es el del pedido, no el escalado por el receptor. El daño real está en `AppliedDamage`.

La extensión `IDamageable.ApplyDamage(in DamageRequest)` (`IDamageable.cs:22-32`) usa la versión autoritativa si existe. Si no, cae al `bool ApplyDamage(int)` con `ModifiedDamage`.

Implementan `IAuthoritativeDamageable`: `EnemyHealth`, `EnemyDamageHitZone`, `DestroyerMouthWeakPoint`, `WeaponDummyEnemy` y `WeaponDummyWeakPoint` (sandbox). No hay implementaciones solo legacy en producción.

## Flujo: arma → enemigo

1. El arma resuelve el `IDamageable` con `GetComponentInParent<IDamageable>()` sobre el collider golpeado. Toma primero el objeto golpeado, así que una hit zone o un weak point le gana al `EnemyHealth` del padre.
2. Calcula el número con `WeaponDamageContext.CalculateDamageValue` (`WeaponDamageResolver.cs:306-346`): `TargetNeutralDamage × elite × rango × DamageScale × escalaAdicional`, redondeado con mínimo 1. Rango: más de 15 m usa `LongRangeDamageMultiplier` y menos de 10 m `CloseRangeDamageMultiplier` (`:300-305`).
3. Llama `WeaponDamageApplier.ApplyDamage` (`WeaponDamageApplier.cs:8-44`):
   - `ModifiedDamage = max(1, round(daño × Vulnerable))` (`:21-23`, `WeaponDamageAmplifierStatus.cs:32-42`).
   - Arma el `DamageRequest` y despacha.
   - Notifica el arma a `EnemyHealth.NotifyDamagedByWeapon` (`:33-34`).
   - Si `AppliedDamage > 0`: `ChallengeProgressTracker.NotifyDamageInstance` y, si corresponde, `PlayerCombatHooks.TryLifesteal` (`:36-41`).
4. `EnemyHealth.ApplyDamageInternal` (`EnemyHealth.cs:127-163`):
   - Rechaza si el daño es ≤ 0 o la vida ya es 0.
   - Bloquea si es invencible: `Direct` siempre, `Status` solo si se activó con `blockDot` (`:133-135`).
   - Escala: `round(ModifiedDamage × multiplicador)`. Usa `_damageTakenMultiplier` (1 por defecto) o el de la hit zone (`:139`).
   - Si mata: SFX de muerte, challenge, `OnDied`, `RunCombatStats` y despawn al pool o `Destroy` (`:165-185`). Si no mata y es `Direct`: SFX de hit (`:157-160`).

Llamadores de `WeaponDamageApplier.ApplyDamage`: `Projectile` (impacto, explosión, cono de fragmentos), `MortarShellImpact`, `WeaponRadialDamage`, `AutomaticCannonWeapon`, `FlamethrowerWeapon`, `FlamethrowerBurnStatus`, `FlamethrowerFuelPuddle` (vía radial, canal `Status`) y `RotatingBladeWeapon`.

### Área

`WeaponRadialDamage.Apply` (`WeaponRadialDamage.cs:26-60`) deduplica por `IDamageable`. Aplica el falloff por distancia al centro del collider:

```
escala = lerp(1, 1 − falloff, distancia / radio)
```

Por defecto corta en 128 targets. Los buffers de colliders van de 256 a 4096.

### Zonas especiales

| Target | Valores | Comportamiento |
|---|---|---|
| `EnemyDamageHitZone` | ×1,2 por defecto (`EnemyDamageHitZone.cs:9`) | Reenvía a `EnemyHealth.ApplyHitZoneDamage`. Reemplaza `_damageTakenMultiplier`, no lo multiplica |
| `DestroyerMouthWeakPoint` | 80 HP (`DestroyerMouthWeakPoint.cs:14`) | Vida propia. No mira la invencibilidad del cuerpo. A 0 dispara `OnWeakPointDestroyed` y se desactiva |
| Weak point por nombre | — | `Projectile` y `AutomaticCannonWeapon` detectan un collider cuyo nombre contiene `"WeakPoint"` (`Projectile.cs:1152-1158`, `AutomaticCannonWeapon.cs:1185-1188`). Afecta el feedback y el bonus Head Hunter |

### Invencibilidad del enemigo

`SetInvincible(bool, blockDot)` (`EnemyHealth.cs:28-32`). Por defecto el DoT sigue pasando. `blockDot: true` da inmunidad total, por ejemplo al Destroyer durante la succión. `OnEnable` y `PrepareForPoolSpawn` resetean vida, invencibilidad y crédito de arma (`:50-75`).

## Flujo: enemigo → jugador

1. El behavior calcula el daño. Casi todos pasan por `EnemyOutgoingDamageScale.ScaleFrom` (`EnemyOutgoingDamageScale.cs:30-47`). Usa el multiplicador cacheado al spawn y, si no hay componente, `DifficultyManager.GetEnemyDamageMultiplier()`.
2. Llama `PlayerHealth.TakeDamage(int)` (`PlayerHealth.cs:282-325`). Lo hacen `EnemyContactDamage`, `EnemyProjectile`, `EnemySeekingMissile`, `EnemyC4`, `FireArea`, `CorrosiveSlimeArea`, `ChargerEnemyBehavior`, `HellfireSlimeBehavior`, `DestroyerBehavior` (tragar) y `GigaWormBehavior`.
3. `TakeDamage` ignora el golpe en estos casos, en orden:
   - Daño ≤ 0 o jugador muerto.
   - Invulnerabilidad de powerup (`:287`).
   - i-frames activos (`:290`).
4. Si hay escudo, consume una carga y abre los i-frames. No resta vida (`:293-302`).
5. Si no hay escudo:
   - Resta `max(1, round(daño × (1 − resistencia)))`. La resistencia tiene tope 0,95 (`PlayerStatMath.cs:5-12`).
   - Abre los i-frames, reinicia el delay de regeneración y dispara SFX de hurt, `OnHitDamageTaken` y `OnHealthChanged`.
   - A 0 HP dispara `OnPlayerDied` una sola vez.
6. Durante los i-frames, `SyncEnemyCollisionIgnores` ignora la colisión física con los enemigos activos (`:359-368`).

El contacto entra por dos vías: el trigger del hurtbox (`PlayerContactHurtbox`) o colisiones del Rigidbody (`PlayerContactDamageReceiver`, con `OnCollisionStay`). Las dos terminan en `EnemyContactDamage.TryApplyContactDamage`: 5 de daño base y empuje 3 (`EnemyContactDamage.cs:5-8`).

### Valores del jugador

| Campo | Script | Prefab | `GameplayScene` |
|---|---|---|---|
| `_maxHealth` | 100 | 100 (`player.prefab:654`) | — |
| `_hitInvulnerabilitySeconds` | 1,5 | 1,5 (`:656`) | **0,5** (override, `GameplayScene.unity:34442`) |
| `_regenerationDelaySeconds` | 5 | 5 (`:658`) | — |

### Burn del jugador

`ApplyBurn(segundos, dps)` (`PlayerHealth.cs:220-229`) no apila: se queda con la mayor duración y el mayor dps. Tickea cada 0,5 s con `max(1, round(dps × 0,5))` y aplica resistencia (`:231-251`). No respeta i-frames, escudo ni invulnerabilidad de powerup. Tampoco dispara SFX ni `OnHitDamageTaken`.

## Deuda / puntos abiertos

- El jugador no es `IDamageable`. El daño entrante no tiene canal, resultado ni fuente, así que no hay crédito de muerte por tipo de enemigo ni API común para feedback.
- **El DoT de armas cura con lifesteal.** `EnemyHealth.ApplyDotDamage` fija `canTriggerLifesteal: false` (`EnemyHealth.cs:96-105`), pero no tiene llamadores. El burn real pasa por `WeaponDamageApplier` con canal `Status` y deja el default `true` (`FlamethrowerBurnStatus.cs:153-158`).
- `WeaponDamageApplier.PendingSourceWeaponId` y `SetPendingWeapon`/`ClearPendingWeapon` (`WeaponDamageApplier.cs:6,51-64`) no tienen llamadores. Hoy el crédito depende de que cada arma pase `sourceWeaponId`.
- El crédito de arma se anota aunque el golpe haya sido rechazado o bloqueado (`WeaponDamageApplier.cs:33-34`). Un hit contra un enemigo invencible cambia `LastDamagingWeaponId`.
- Vulnerable queda en el objeto golpeado (`GetComponent`, no `InParent`, en `WeaponDamageAmplifierStatus.cs:25,37`). Si se aplica sobre una hit zone o el weak point, no amplifica el daño al cuerpo, y al revés.
- El weak point se detecta por substring de nombre (`"WeakPoint"`) en dos armas. Renombrar un collider rompe el bonus sin error.
- El burn del jugador ignora el escudo y la invulnerabilidad de powerup (`PlayerHealth.cs:231-251`, contra `:287,293`).
- Los i-frames de la escena (0,5 s) difieren del prefab y del script (1,5 s). Falta documentar cuál es el valor de diseño.
- `FromLegacy` devuelve vida 0/0 y nunca `Killed`. Sin implementaciones legacy vivas es inocuo, pero cualquier `IDamageable` nuevo que no sea autoritativo va a romper el feedback de kill sin avisar.

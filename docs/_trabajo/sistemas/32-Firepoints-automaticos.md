# Firepoints automáticos — alpha 23-09-2026

Cada arma equipada se ve puesta en el cuerpo del jugador (un "wearable"). Mientras está en automático dispara desde el muzzle de ese montaje. El arma manual dispara desde la mano derecha (`Main Weapon Fire Point`). El sistema tiene que garantizar que el muzzle esté en la pose final del frame antes de que un arma lo lea.

## Scripts

| Script / asset | Rol |
|---|---|
| `Managers/PlayerWeaponMountController` | Crea un montaje por `WeaponType`, decide el muzzle de cada arma según su modo y destruye los montajes al limpiar |
| `Managers/AutomaticWeaponMount` | Componente del prefab del montaje: `Muzzle`, visibilidad, luces indicadoras de auto y sink de aim (sin efecto en wearables) |
| `Managers/WearableWeaponMountCatalog` + `Assets/Resources/WearableWeaponMounts.asset` | `WeaponType` → prefab, `AttachmentPath` y offsets locales |
| `Base/WeaponFireOrigin` (`WeaponFireOriginBinding`, `IWeaponFireOriginReceiver`) | Par muzzle + sink de aim que recibe cada behaviour |
| `Player/Animation/PlayerAnimationDriver` | Evalúa la pose antes de los ticks de arma y hace que los wearables sigan a sus huesos |

## Cómo se asigna un montaje

1. `WeaponManager.Awake` inicializa el controller con el projectile spawn de la mano (`WeaponManager.cs:45-49`). Si el componente falta, lo agrega con `AddComponent`. En `player.prefab` viene configurado (`player.prefab:797-799`), así que ese fallback no corre en `GameplayScene`.
2. Si `_catalog` está vacío, `Initialize` carga `Resources/WearableWeaponMounts` (`PlayerWeaponMountController.cs:52-53`).
3. `WeaponManager.AddWeapon` → `_mountController.AddWeapon(behaviour, manual: primera arma)` (`WeaponManager.cs:117`).
4. `AddWeapon` busca `_mounts[WeaponType]` o llama a `CreateMount(type)` (`PlayerWeaponMountController.cs:56-77`). La clave es el tipo, no el índice de slot: dos armas del mismo tipo compartirían montaje.
5. `CreateMount` pide la entrada al catálogo (`WearableWeaponMountCatalog.cs:25-38`, búsqueda lineal que salta prefabs null) y resuelve el anclaje (`PlayerWeaponMountController.cs:177-223`).

## Mapeo a huesos

`TryResolveAttachment` (`PlayerWeaponMountController.cs:205-223`):

- Si existe un socket animado en `_animatedSockets` para ese tipo, gana: el montaje queda en el socket con transform local identidad.
- Si no, usa el `AttachmentPath` y los offsets del catálogo. En `WearableWeaponMounts.asset` todos los `AttachmentPath` están vacíos, lo que significa la raíz del jugador. En la práctica mandan los sockets.

Sockets de `player.prefab` (`player.prefab:1228-1238`), creados por `Editor/PlaceholderPlayerAnimationBuilder.cs:170-171`:

| Arma | Hueso del socket |
|---|---|
| AutomaticCannon | `shoulder.R` |
| RocketLauncher | `spine.003` (pecho) |
| Mortar | `spine.003` (pecho) |
| Flamethrower | `forearm.L` |
| RotatingBlade | `spine` |
| Arma manual (cualquiera) | `Main Weapon Fire Point`, hijo de `hand.R` |

Cohete y mortero comparten hueso: si los dos están equipados, sus muzzles salen de la misma zona del pecho.

## Manual vs automático

`RefreshWeaponModes` → `ApplyMode` (`PlayerWeaponMountController.cs:89`, `:131-139`):

| Modo | Muzzle | Sink de aim |
|---|---|---|
| Automático | `mount.Muzzle` | el montaje |
| Manual | `_mainFirePoint` (mano) | ninguno |

- `SetFireOrigin` solo rebindea si cambia el muzzle o el sink (`:141-146`, `WeaponBehaviourBase.cs:70-77`).
- Al rebindear, el cañón cancela la ráfaga en vuelo y el cohete cancela la volea (`AutomaticCannonWeapon.cs:103-118`, `RocketLauncherWeapon.cs:90-95`). O sea, al cambiar de manual se pierde lo que estaba saliendo.
- El modelo del montaje sigue visible aunque el arma esté en manual; solo se apagan las luces `AutoIndicator*` (`AutomaticWeaponMount.cs:74-109`).
- `RefreshWeaponModes` corre dos veces por frame: una desde `WeaponManager.Update` y otra en el `LateUpdate` del controller (`PlayerWeaponMountController.cs:129`).

## Aim del montaje

`WeaponFireOriginBinding.AimAt / AimAlong / ClearAim` reenvía al sink (`WeaponFireOrigin.cs:23-36`). `BasicProjectileWeapon.TickAutomatic` llama a `AimAt` si tiene objetivo y a `ClearAim` si no (`WeaponBehaviourBase.cs:119-126`).

En los wearables no rota nada. `TickAim` sale enseguida (`AutomaticWeaponMount.cs:159-160`), los cinco prefabs tienen `_wearable: 1` y `_aimPivot` vacío, y el recoil del montaje también se saltea. La dirección del disparo la calcula cada arma, desde la posición del muzzle hacia el objetivo. El camino de torreta (no wearable: `_turnSpeedDegrees`, `ApplyWeaponColor`) queda sin uso.

## Pose antes del disparo

Orden dentro de `WeaponManager.Update` (`WeaponManager.cs:66-77`):

1. `ClearManualWeaponOverride()`: limpia el override manual que usa el sandbox.
2. `EvaluatePoseForWeapons(dt, aimPoint)` (`PlayerAnimationDriver.cs:328-355`). Evalúa locomoción, torso, grafo manual, dash y aim, después aplica el recoil pendiente y al final llama a `_wearableFollower.EvaluateAttachments()`. Un guard por `Time.frameCount` impide evaluar dos veces en el mismo frame (`:330-331`).
3. Resuelve el aim de nuevo con el muzzle ya en su pose (`WeaponManager.cs:73`).
4. `RefreshWeaponModes`, después los ticks automáticos y al final el manual.

Motivo: los muzzles son hijos de huesos. Si el arma lee el muzzle antes de evaluar la pose, dispara desde la pose del frame anterior, y con el cuerpo girando el tiro sale desfasado. El recoil de un disparo confirmado se encola para la pose siguiente, así el muzzle no se mueve en medio de una ráfaga (`PlayerAnimationDriver.cs:706-711`).

Si `WeaponManager` sale antes de tiempo (pausa, o `GameManager` fuera de `IsPlaying`), la pose la evalúa el `LateUpdate` del driver (`:357-366`). El driver corre con `DefaultExecutionOrder(100)` (`:12`).

## Limpieza y fallbacks

- `ClearEquippedWeapons` → `ClearWeapons` (`PlayerWeaponMountController.cs:112-121`): rebindea todas las armas a la mano sin sink, vacía los sets y destruye los montajes. `RemoveWeapon` (`:96-110`) existe, pero `WeaponManager` no lo llama.
- Sin entrada en el catálogo: `CreateMount` loguea un error y devuelve null (`:179-184`). El arma sí se equipa, pero conserva el binding del constructor (la mano) y dispara desde la mano aunque esté en automático.

## Deuda / puntos abiertos

- `ClearWeapons` destruye los montajes junto con cualquier `PooledWeaponVfx` que tenga parentado en el muzzle. Esa entrada queda null en `WeaponVfxPool._instances`, sigue contando contra `MaxSimultaneous`, y el cue pierde el slot para siempre.
- `ClearEquippedWeapons` no llama a `CancelHeldAbilities`: un lock-on de cohete en curso queda colgado.
- `RefreshWeaponModes` corre dos veces por frame.
- El sink de aim y `_turnSpeedDegrees` no tienen efecto en wearables: es código de torreta sin uso.
- La clave por `WeaponType` impide dos armas iguales con montajes distintos. Hoy no pasa (un arma por tipo), pero no hay validación.
- El `AttachmentPath` del catálogo está vacío en las cinco entradas. Si faltara un socket en el prefab, el montaje caería en la raíz del jugador sin avisar.
- Al cambiar de manual se cancelan la ráfaga del cañón y la volea del cohete en vuelo.

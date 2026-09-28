# Movimiento — alpha 23-09-2026

Movimiento del jugador sobre `Rigidbody` dinámico, relativo a cámara, con salto, saltos aéreos, dash por cargas, agacharse y slide. No hay máquina de estados explícita: el estado son flags (`_isGrounded`, `_isCrouching`, `_isSliding`, `_isDashing`) más timers (stun, knockback, post-dash, slow, aim-facing). El input se lee en `Update` y la física corre en `FixedUpdate` (paso fijo 0,02 s, `ProjectSettings/TimeManager.asset`). Las stats de movimiento salen de `PlayerStats`.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/Player/PlayerMovement.cs` | Input, aceleración, fricción, tope de velocidad, salto, dash, crouch/slide, efectos externos (knockback, pull, stun, slow, weapon dash) |
| `Assets/Scripts/Player/PlayerStats/PlayerStats.cs` | Fuente de `MovementSpeed`, `JumpHeight`, `AirJumps`, `DashCharges`, `DashSpeed` |
| `Assets/Scripts/ThirdPersonCamera.cs` | Orbit de cámara con mouse. `PlayerMovement` usa su `forward`/`right` aplanados |
| `Assets/Scripts/Powerups/TemporaryPowerupController.cs` | Buff ExtraSpeed: multiplica velocidad y salto, suma dashes y saltos aéreos |
| `Assets/Prefabs/player.prefab` | Rigidbody, collider y valores serializados de `PlayerMovement` |

`PlayerMovement` tiene `[DefaultExecutionOrder(-100)]` y exige `Rigidbody` y `PlayerStats` (`PlayerMovement.cs:5-7`). Expone `PlayerTransform` estático (singleton, `:10-12`) y eventos para animación y HUD: `OnJump`, `OnAirJump`, `OnLanded`, `OnCrouchStarted/Ended`, `OnSlideStarted/Ended`, `OnDashStarted/Ended`, `OnDashChargesChanged`, `OnStunned` (`:14-24`).

## Física base

| Parámetro | Valor | Fuente |
|---|---|---|
| Gravedad | -18 | `ProjectSettings/DynamicsManager.asset:7` |
| Fixed timestep | 0,02 s | `ProjectSettings/TimeManager.asset` |
| Masa Rigidbody | 1, sin damping | `player.prefab:764-765` |
| Interpolación | Prefab 0, forzada a Interpolate en `Awake` | `PlayerMovement.cs:274` |
| Rotación física | Congelada en `Awake` | `PlayerMovement.cs:275` |
| Collider de movimiento | Capsule r 0,5 · h 2 | `player.prefab:529-551` |
| Escala del jugador en escena | 1,2 (cápsula efectiva h 2,4) | override en `GameplayScene.unity` |
| Material de fricción | Runtime, fricción 0, combine Minimum | `PlayerMovement.cs:279-293` |

El prefab también tiene un `CharacterController` deshabilitado en la raíz (`player.prefab:552-576`) y un hurtbox trigger (`ContactHurtbox`, r 0,62) con su propio Rigidbody kinemático.

## Valores serializados

Prefab = `Assets/Prefabs/player.prefab:589-611`. La escena solo sobreescribe `_cameraTransform` y `_baseMoveAcceleration` (75, igual al prefab).

| Campo | Default código | Prefab / escena | Uso |
|---|---|---|---|
| `_baseMoveAcceleration` | 38 | **75** | Aceleración por input (m/s²) |
| `_baseFriction` | 9 | **10** | Desaceleración constante (m/s²) |
| `_airFrictionMultiplier` | 0,2 | **0,4** | Fricción en el aire |
| `_crouchAccelerationMultiplier` | 0,2 | 0,2 | Aceleración agachado |
| `_slideFrictionMultiplier` | 0,1 | **0,4** | Fricción en slide (solo en suelo) |
| `_dashDuration` | 0,25 | 0,25 | Duración del dash (s) |
| `_groundedDashRegenTime` | 2 | 2 | Recarga de 1 carga en suelo (s) |
| `_airborneDashRegenTime` | 4 | 4 | Recarga de 1 carga en aire (s) |
| `_slideStartSpeedMultiplier` | 1,5 | 1,5 | Umbral de slide = MoveSpeed × 1,5 |
| `_minSlideSpeed` | 2 | 2 | Debajo de esto el slide termina |
| `_postDashFrictionMultiplier` | 0,15 | **0,3** | Fricción tras el dash |
| `_postDashFrictionDuration` | 0,18 | 0,18 | Ventana post-dash (s) |
| `_knockbackWindow` | 0,3 | 0,3 | Ventana sin tope tras empuje (s) |
| `_knockbackFrictionMultiplier` | 0,1 | 0,1 | Fricción en la ventana de empuje |
| `_groundCheckExtraDistance` | 0,08 | 0,08 | Margen del raycast de suelo |
| `_groundMask` | Everything | Everything | Capas que cuentan como suelo |
| `_facingSmoothTime` | 0,12 | 0,12 | Suavizado del giro (s) |
| `_strafeTurnAngle` | 25 | 25 | Giro extra al strafear (°) |
| `_aimFacingRotationSpeed` | 720 | 720 | Giro forzado por arma (°/s) |

## Stats que consume

Assets en `Assets/ScriptableObjects/PlayerSO/Stats/`. Detalle en `41-Stats-del-jugador.md`.

| Stat | Base | Level-up | Uso en código |
|---|---|---|---|
| `MovementSpeed` | 6,8 | Sí, 0,04 | Tope de velocidad planar y umbral de slide (`PlayerMovement.cs:441,470,678`) |
| `JumpHeight` | 3 | Sí, 0,0025 | Altura de salto (`:578`) |
| `AirJumps` | 0 | No | Saltos aéreos, se recargan al aterrizar (`:763`) |
| `DashCharges` | 1 | No | Cargas máximas (`:686,727`) |
| `DashSpeed` | 10 | Sí, 0,025 | Velocidad sumada por el dash (`:697`) |

Derivados con los valores actuales: velocidad de salto √(2·18·3) ≈ 10,4 m/s, subida ≈ 0,58 s. Umbral de slide 6,8 × 1,5 = 10,2 m/s. Buff ExtraSpeed: `MovementSpeed` ×2–4 y `JumpHeight` ×2–3 durante 15 s; dashes y saltos aéreos +2× lo que ya tenga el jugador (`TemporaryPowerupController.cs:112-124`). Con `AirJumps` base 0 el buff no da saltos aéreos si no hay pasivo.

## Flujo por frame

1. `Update` (`PlayerMovement.cs:312-333`): si hay pausa de UI o `timeScale` 0, sale. Lee input. Si no está aturdido, dispara `TryJump`, `TryStartCrouchOrSlide`, `StopCrouchOrSlide`, `TryDash`. Los flags de "pressed" se consumen en el mismo frame: no hay buffer.
2. `FixedUpdate` (`:336-361`): `UpdateGroundedState` → retiene stun con momentum → si dashea, solo corre el timer; si no, `HandleMovement` → `ApplyPlanarSpeedCap` → `HandleFriction`. Después, ticks de post-dash, knockback, stun, slow, aim-facing y recarga de dash.

## Movimiento en suelo y aire

1. Dirección: `forward` y `right` de la cámara aplanados en XZ, combinados con WASD y normalizados (`:386-389`).
2. Giro: el cuerpo mira hacia la cámara con +25° de strafe con A/D. En slide conserva el rumbo del slide. Si un arma pidió `RequestAimFacing`, gira a 720°/s hacia ese punto (`:412-432`).
3. Aceleración: `75 × lerp(1, 0,35, v / vmax)`, ×0,2 agachado. No acelera durante el slide (`:434-445`).
4. Tope: si `v > MovementSpeed × slow`, recorta la velocidad planar. No aplica en slide, post-dash ni knockback (`:466-477`).
5. Fricción: aceleración constante opuesta a la velocidad planar. Base 10; ×0,4 en aire; ×0,4 en slide en suelo; ×0,3 post-dash; ×0,1 en knockback (`:449-462`). Los multiplicadores se encadenan.
6. Suelo: raycast hacia abajo desde el centro del collider, largo `extents.y + 0,08`, triggers ignorados (`:789-801`). Al aterrizar recarga `AirJumps` y emite `OnLanded`.

## Salto

1. En suelo, salto normal. En aire, consume un salto aéreo si queda (`:560-573`).
2. Pone en 0 la velocidad vertical si es negativa y aplica impulso `√(2·g·JumpHeight)`. El salto aéreo tiene la misma altura (`:576-591`).
3. Cancela el slide. No hay coyote time ni jump buffer: al salir de un borde el salto ya cuenta como aéreo.

## Dash

1. Requiere `DashCharges ≥ 1`, una carga disponible e input de dirección. Sin WASD no dashea (`:682-690`).
2. Corta slide y crouch. Suma `DashSpeed` (10 m/s) a la velocidad planar actual, en la dirección de input (`:697-703`). Durante 0,25 s no hay aceleración, fricción ni tope; la gravedad sigue.
3. Al terminar: ventana post-dash de 0,18 s con fricción ×0,3 y sin tope. Si Ctrl sigue apretado, intenta slide (`:712-722`).
4. Recarga: una carga cada 2 s en suelo o 4 s en aire. El timer se reinicia en cada dash (`:706,725-753`).
5. `ApplyWeaponDash` (armas) usa el mismo estado con velocidad absoluta y no consume cargas (`:242-264`).

## Crouch y slide

1. Ctrl presionado: si está en suelo y `v ≥ 10,2 m/s`, slide. Si no, crouch (`:594-611`). Crouch solo baja la aceleración a ×0,2; no cambia collider ni altura.
2. El slide fija su dirección a la velocidad actual, no acelera, no tiene tope y usa fricción ×0,4 (`:639-654`).
3. Termina al soltar Ctrl, al saltar o al bajar de 2 m/s en suelo (entonces pasa a crouch si Ctrl sigue apretado) (`:773-783`).
4. Aterrizar con Ctrl apretado arranca slide sin mirar la velocidad (`:766-770`).
5. Con correr normal no se llega al umbral (tope 6,8 < 10,2): en la práctica el slide sale de dash, knockback, caída o aterrizaje con Ctrl.

## Efectos externos

| API | Qué hace | Línea |
|---|---|---|
| `ApplyKnockback(from, force)` | Impulso horizontal `force × masa` y ventana de 0,3 s sin tope y fricción ×0,1 | `:151-164` |
| `ApplyPull(toward, accel)` | Aceleración continua hacia un punto. Reusa la ventana de knockback; se llama cada frame | `:171-184` |
| `ApplyStun(s)` | Bloquea input de movimiento, salto y dash. Refresca, no apila. La fricción sigue | `:187-196` |
| `ApplyMomentumPreservingStun` | Congela la velocidad planar, corta dash y slide y la restituye al final con ventana post-dash | `:198-226,500-527` |
| `ApplySlow(mult, s)` | Multiplica el tope (0,05–1). Conserva el más lento y la duración más larga | `:232-240` |
| `RefreshPassiveResources()` | Rellena saltos aéreos y cargas de dash al cambiar pasivos o buffs | `:135-145` |

## Input

`PlayerMovement` no usa el Input System por acciones. Lee `Keyboard.current` directo (`:374-398,827-841`):

| Acción en código | Tecla |
|---|---|
| Mover | W A S D |
| Saltar | Space |
| Dash | Left/Right Shift |
| Crouch / slide | Left/Right Ctrl (mantener) |
| Mirar | Delta de `Mouse.current` (`ThirdPersonCamera.cs:260-268`) |

`Assets/InputSystem_Actions.inputactions` es el asset de plantilla de Unity. Está registrado como acciones del proyecto (`ProjectSettings/EditorBuildSettings.asset:28`), pero ningún script de gameplay lo lee. El `InputSystemUIInputModule` de la escena usa otro asset (guid `ca9f5fa9…`, `GameplayScene.unity:23533`). Esquemas: Keyboard&Mouse, Gamepad, Touch, Joystick, XR.

Mapa `Player`:

| Acción | Teclado / mouse | Gamepad | Usada por código |
|---|---|---|---|
| Move (Vector2) | WASD, flechas | Left stick | No (WASD directo; flechas no) |
| Look (Vector2) | Pointer delta | Right stick | No (mouse directo) |
| Attack | Mouse izq., Enter | West | No |
| Interact (Hold) | E | North | No |
| Crouch | C | East | No (el código usa Ctrl) |
| Jump | Space | South | No (Space directo) |
| Previous / Next | 1 / 2 | D-pad izq. / der. | No |
| Sprint | Left Shift | Left stick press | No (Shift es dash) |

Mapa `UI`: Navigate, Submit, Cancel, Point, Click, RightClick, MiddleClick, ScrollWheel, TrackedDevicePosition/Orientation, con los bindings de plantilla. Solo `TitleScreenScreenStack.cs:50` referencia una acción por `InputActionReference` (cancel).

## Cámara

Valores de escena (`GameplayScene.unity:5410-5426`): pivote a 1,5 m, hombro +1, distancia 3,5, sensibilidad 0,12 / 0,12, pitch −70° a 70°, cursor bloqueado en Play. La cámara calcula el orbit en `Update` y lo escribe en `LateUpdate`, sumando shake y FOV kick (`ThirdPersonCamera.cs:289-296`). `PlayerMovement` lee el transform de la cámara, así que toma la orientación del frame anterior, con el shake incluido.

## Deuda / puntos abiertos

- Input hardcodeado a teclado y mouse (`PlayerMovement.cs:374-398`, `ThirdPersonCamera.cs:260`). Sin gamepad, sin rebinding, y el asset de acciones contradice el código: Crouch = C y Sprint = Shift en el asset, Ctrl y dash en el código.
- `Debug.Log` sin guardas en cada intento de slide, entrada y salida (`PlayerMovement.cs:598,602,651,661`). Llenan la consola y se compilan en release.
- Sin coyote time ni jump buffer (`:323,560-573`). Un salto apretado justo antes de aterrizar se pierde, y uno apretado al salir de un borde gasta un salto aéreo (con `AirJumps` 0, no salta).
- La fricción es una desaceleración constante sin corte en cero (`:449-462`). Con 10 m/s² × 0,02 s puede invertir el signo de velocidades menores a 0,2 m/s y dejar un temblor en reposo.
- El umbral de slide (MoveSpeed × 1,5 = 10,2) supera el tope de carrera (6,8). El slide no se alcanza corriendo, solo con dash, empuje o aterrizaje con Ctrl (`:676-679,766-770`). Hay que confirmar si es intencional.
- Crouch no cambia collider ni altura: solo baja la aceleración (`:437`). No tiene efecto defensivo ni de navegación.
- La dirección de movimiento usa el transform de la cámara con el shake aplicado (`ThirdPersonCamera.cs:294-295`), hasta ±5° de rotación. Debería usar la rotación de gameplay.
- `_groundMask` = Everything (`player.prefab:596-598`): enemigos y props no trigger cuentan como suelo y recargan saltos aéreos.
- Los incrementos por nivel de `JumpHeight` (0,0025) y `DashSpeed` (0,025) son despreciables frente a sus bases (3 y 10). Ocupan slots de la ruleta sin efecto visible.
- Restos en el prefab: `CharacterController` deshabilitado (`player.prefab:552-576`). El override de escena de `_baseMoveAcceleration` = 75 repite el valor del prefab.

# Animación del jugador — alpha 23-09-2026

La animación del jugador es solo presentación: no mueve al personaje ni decide el disparo. Un `AnimatorController` generado se evalúa a mano desde un `PlayableGraph`. Encima se aplican ajustes procedurales acotados: dash direccional, braceo con arma, aim de pecho y brazos, y retroceso. Las armas piden la pose antes de leer la boca del cañón, así el muzzle sale del esqueleto ya apuntado. Los enemigos usan otra cosa, un Animator clásico de un solo estado armado por un builder de editor.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/Player/Animation/PlayerAnimationDriver.cs` | Grafo manual, selección de estados, aim/recoil procedural |
| `Assets/Scripts/Player/Animation/PlayerWearableSurfaceFollower.cs` | Pega los sockets de armas rígidas a la piel deformada |
| `Assets/Scripts/Editor/PlaceholderPlayerAnimationBuilder.cs` | Importa el FBX, genera la máscara, el controller y los sockets del prefab |
| `Assets/Scripts/Editor/PlayerWearableSurfaceBuilder.cs` | Calibra las anclas de superficie a partir de los skin weights |
| `Assets/Scripts/Editor/EnemySkinnedAnimBuilder.cs` | Reemplaza el visual estático de los enemigos por el FBX skineado y crea sus controllers |
| `Assets/Scripts/Enemy/EnemyAnimPhaseOffset.cs` | Desfasa el clip del Slime al activarse |

Assets: `Assets/Art/Player/PlaceholderAnimation/PlaceholderPlayer.fbx`, `PlaceholderPlayer.controller`, `UpperBody.mask`. El driver vive en `Assets/Prefabs/player.prefab`, y la escena no le hace overrides.

## Grafo (Playables)

`EnsureGraph` (`PlayerAnimationDriver.cs:267`):

1. Guarda `applyRootMotion` y `cullingMode`, pone root motion en off y `AlwaysAnimate`.
2. Le saca el `runtimeAnimatorController` al Animator (`:277`) para que el pase normal de Unity no pise la pose.
3. `PlayableGraph` en `DirectorUpdateMode.Manual`, un único `AnimatorControllerPlayable` sobre el controller, y un `AnimationPlayableOutput` hacia el Animator (`:278-282`).
4. Arranca en `Locomotion` / `Aim` / `Empty`, con el peso de Upper Body en 0.

No hay `AnimationMixerPlayable` ni clips sueltos en el grafo. Las capas y la máscara salen del propio controller. `DestroyGraph` (`:294`) devuelve el controller y los flags originales al deshabilitar el componente o al resetear la presentación.

## Capas del controller

Lo genera `BuildController` (`PlaceholderPlayerAnimationBuilder.cs:249`). Todas las capas son Override: no hay capas aditivas.

| Capa (índice) | Máscara | Peso por defecto | Estados |
|---|---|---|---|
| Base Layer (0) | ninguna | 1 | Locomotion (2D Freeform Directional: Idle, MoveForward/Backward/Left/Right), Crouch (1D por `Speed`: CrouchIdle/CrouchMove), Jump, AirJump, Fall, Land, Slide, Dash, DashBackward, Stun |
| Upper Body (1) | `UpperBody.mask` | 1 en el asset, 0 al crear el grafo | Aim, Fire, Slash, Flame, Hit |
| Full Body (2) | ninguna | 0 | Empty (sin motion), Death |

- La máscara activa `spine.003` y todos sus hijos (`PlaceholderPlayerAnimationBuilder.cs:200-206`). Desde la espalda baja hacia abajo manda la locomoción.
- Parámetros: `MoveX`, `MoveY`, `Speed`, `LocomotionRate`. Locomotion y Crouch usan `LocomotionRate` como multiplicador de velocidad.
- El controller no tiene transiciones útiles. Todos los cambios los hace el driver con `CrossFadeInFixedTime` (`:429-434`).

## Clips (FBX, `PlaceholderPlayer.fbx.meta`)

| Clip | Frames | Loop | Clip | Frames | Loop |
|---|---|---|---|---|---|
| Idle | 0–120 | sí | Slide | 0–30 | sí |
| MoveForward/Backward/Left/Right | 0–22 | sí | Dash | 0–12 | no |
| CrouchIdle | 0–120 | sí | DashBackward | 0–9 | no |
| CrouchMove | 0–32 | sí | Stun | 0–40 | sí |
| Jump | 0–12 | no | Aim | 0–120 | sí |
| AirJump | 0–14 | no | Fire | 0–8 | no |
| Fall | 0–30 | sí | Slash | 0–16 | no |
| Land | 0–10 | no | Flame | 0–30 | sí |
| Hit | 0–10 | no | Death | 0–36 | no |

Import Generic, compresión Off, root bloqueado en XZ, Y y rotación (`PlaceholderPlayerAnimationBuilder.cs:37-45`, `:128-146`). El FBX sale de `Tools/Blender/generate_player_animations.py`.

Menús: `Tools/ScrapWaves/Build Placeholder Player Animation`, `Refresh Player Animation Controller`, `Refit Animated Wearable Sockets`, `Render Placeholder Player Animation`.

## Orden de evaluación

1. `WeaponManager.Update` limpia el override manual y llama `EvaluatePoseForWeapons(dt, CurrentAimSolution.TargetPoint)` (`WeaponManager.cs:69-73`). Después resuelve el aim de nuevo con el muzzle actualizado y recién ahí tickea las armas.
2. `EvaluatePoseForWeapons` (`PlayerAnimationDriver.cs:328`) corre una sola vez por frame (`_lastEvaluatedFrame`): locomoción, upper body, `_graph.Evaluate(dt)`, y después dash direccional, braceo con arma, aim, recuperación y cacheo de pose, recoil y wearables.
3. Si nadie evaluó en el frame (pausa, `GameManager` fuera de Playing), `LateUpdate` (`:357`) evalúa igual, contra `CurrentAimSolution` o el reticle.
4. Muerto: el grafo avanza con `unscaledDeltaTime` (`:332`) para que Death corra durante la pausa de game over. No se aplica aim ni recoil.
5. `PlayerWearableSurfaceFollower.EvaluateAttachments` se ejecuta al final (`:353`), antes de que cualquier arma lea sockets.

`[DefaultExecutionOrder(100)]` en el driver.

## Lectura del movimiento

`UpdateLocomotion` (`:368`) lee `PlayerMovement.CurrentVelocity` en espacio local:

- `MoveX/MoveY` = velocidad local / `_referenceMoveSpeed`, con clamp a 1 y suavizado exponencial (`_velocityBlendTime`).
- `LocomotionRate`: en reposo respeta el ritmo del Idle. En movimiento escala entre 0.35 y 2.5 según la velocidad real, contra 5 m/s de frente o 4.4 m/s de lado o atrás (`:381-386`).
- La prioridad de estado de la base sale de `:388-398`: Stun > Dash/DashBackward > Slide > Jump/AirJump (ventana `_jumpPoseTime`) > Fall (no grounded) > Land (ventana, solo con < 0.5 m/s) > Crouch > Locomotion.
- Eventos: `OnJump`, `OnAirJump` y `OnLanded` abren las ventanas. `OnHealthChanged`, si la vida baja, activa Hit. `OnPlayerDied` activa Death en Full Body (`:672-694`).
- Dash atrás: histéresis local z < −0.35 para entrar y < −0.2 para quedarse (`:447`). Solo el Dash adelante o lateral recibe redirección procedural de caderas y muslos (`:457-524`).

## Upper body

`UpdateUpperBody` (`:404`). La prioridad es Hit > Flame (sostenido) > acción (Fire/Slash) > Aim. El peso de la capa sube a `_upperBodyWeight` solo si hay arma manual activa, Hit, acción o carga. Con Stun se multiplica × 0.4.

Los eventos salen de `WeaponPresentationController.FeedbackEmitted` (`:696`), y los de armas en modo `Automatic` se ignoran:

| Evento | Efecto |
|---|---|
| ShotFired | Fire (Slash si es `RotatingBlade`), ventana de pose, reinicio del estado, recoil encolado |
| SustainedFireStarted/Stopped | Flame on/off |
| ChargeStarted | `_charging` = sube el peso de la capa, sin estado propio (queda Aim) |
| ChargeCancelled / AmmoEmpty | corta carga y sostenido |

## Aim procedural (hacia el reticle)

`ApplyAim` (`:587`). Solo actúa si el peso de Upper Body es > 0.001. Sin arma manual no hay aim.

- Origen en `hand.R`. Yaw y pitch del objetivo en espacio del jugador, con clamp y suavizado (`_aimSmoothTime`). `ResolveLimitedYaw` (`:640`) evita saltar de lado al cruzar ±180°.
- Pecho (`spine.003`): recibe el 50 % del yaw y el 40 % del pitch, con límites propios, rotados en ejes del mundo (`:609-613`).
- Brazos: `AimArm` hace 2 iteraciones de `FromToRotation` de antebrazo a objetivo sobre el upper arm, limitadas por `_armCorrectionLimit` (`:621-637`). Sin IK ni Humanoid.
- Brazo de apoyo (izquierdo): peso `_supportArmWeight` × (1 → 0.12 según el braceo de carrera).
- Pesos: Slash × 0.25, Hit × 0.4 (`:607-608`).
- Giro de cuerpo: si el yaw crudo supera `_bodyTurnThreshold` y no hay slide, dash ni stun, `PlayerMovement.RequestAimFacing(dir, _bodyTurnHoldTime)` (`:602-604`).
- Cabeza y cuello no apuntan al reticle. Solo reciben el balanceo de `ApplyArmedRunFollowThrough` (`:559-561`).

Huesos del prefab (vía `Configure`, `PlaceholderPlayerAnimationBuilder.cs:88`): chest `spine.003`, `upper_arm/forearm/hand .R/.L`. Además se resuelven por nombre `spine`, `thigh/shin .L/.R`, `spine.004`, `spine.006` y `shoulder.L/.R` (`PlayerAnimationDriver.cs:213-227`).

## Valores serializados (player.prefab:1265-1290 = defaults de código)

| Campo | Valor | Campo | Valor |
|---|---|---|---|
| `_referenceMoveSpeed` | 5 | `_aimWeight` | 1 |
| `_referenceSideAndBackwardSpeed` | 4.4 | `_supportArmWeight` | 0.85 |
| `_velocityBlendTime` | 0.08 | `_chestYawLimit` / `_chestPitchLimit` | 40° / 22° |
| `_stateBlendTime` | 0.09 | `_armYawLimit` | 95° |
| `_jumpPoseTime` / `_landingPoseTime` | 0.2 / 0.22 | `_aimUpLimit` / `_aimDownLimit` | 70° / 60° |
| `_upperBodyWeight` | 1 | `_armCorrectionLimit` | 110° |
| `_actionBlendTime` | 0.045 | `_bodyTurnThreshold` | 60° |
| `_firePoseTime` | 0.28 | `_aimSmoothTime` / `_bodyTurnHoldTime` | 0.055 / 0.1 |
| `_slashPoseTime` | 0.55 | `_recoilDegrees` / `_maximumRecoil` | 3° / 7° |
| `_hitPoseTime` | 0.34 | `_recoilRecovery` | 18 °/s |

Recoil: se acumula por disparo (`_recoilDegrees` × intensidad, hasta el máximo) y se aplica en el frame siguiente, rotando los upper arms sobre `transform.right`. Con `ReducedMotion` de `WeaponPresentationController` se escala × 0.2 (`:710-711`). El retroceso cosmético del cañón (`WeaponRecoilFeedback`) va aparte y es inmediato.

## Wearables

`PlayerWearableSurfaceFollower`: cada attachment arma un marco de 3 anclas (A, B, C), cada una un promedio ponderado de puntos en huesos. Opcionalmente suma un asiento exacto (`Seat`) o una corrección de profundidad por sondas de contacto, con un tope de `MaximumContactCorrection` = 0.08. Todo se reconstruye en absoluto en cada evaluación, sin acumular (`PlayerWearableSurfaceFollower.cs:84-121`). En el prefab hay 5 attachments, uno por `WeaponType` (0–4), con huesos base `spine`, `shoulder.R`, `forearm.L` y `spine.003` ×2 (`PlaceholderPlayerAnimationBuilder.cs:170-171`).

## Enemigos

`EnemySkinnedAnimBuilder` (menú `Tools/ScrapWaves/Fix Enemy Skinned Animations (Chaser+Drone)` más un menú por tipo):

| Familia | FBX | Controller (estado) | Prefabs | Escala |
|---|---|---|---|---|
| Chaser | `LarguiruchoRigWalk_anim.fbx` | `ChaserWalk.controller` (Walk) | Chaser, Chaser (variant), Chaser_Elite | visual ×1.57; root ×2 en variant/elite |
| Drone | `Avispa_Vuelo.fbx` | `DroneFly.controller` (Fly) | Drone, Drone (variant), Drone_Elite | visual y+1; root ×4 en elite |
| Slime | `Basurita_MiniSalto.fbx` | `SlimeHop.controller` (Hop) | EnemyPro, Slime (variant), Slime_Elite | conserva la pose previa, yaw 0 |

Pasos (`EnemySkinnedAnimBuilder.cs:183-385`):

1. Import Generic con todos los takes en loop y root bloqueado.
2. Controller de una capa y un estado, sin parámetros.
3. En el prefab destruye el visual viejo, instancia el FBX, copia los materiales y pone `updateWhenOffscreen = true`.
4. Animator con `AlwaysAnimate`, sin root motion, avatar del FBX.
5. Deshabilita los renderers placeholder (`Cylinder`, `drone`).
6. Solo en Slime agrega `EnemyAnimPhaseOffset`.

`EnemyAnimPhaseOffset.OnEnable` ejecuta `Animator.Play("Hop", 0, Random.value)` (`EnemyAnimPhaseOffset.cs:12-19`). Un Play por spawn o salida del pool, sin Update. Está en EnemyPro, Slime (variant) y Slime_Elite.

## Deuda / puntos abiertos

- Chaser y Drone no tienen `EnemyAnimPhaseOffset`: todas las instancias caminan y vuelan en fase. El componente además tiene hardcodeado el estado `"Hop"` (`EnemyAnimPhaseOffset.cs:10`), así que no se puede reusar en `ChaserWalk`/`DroneFly` sin generalizarlo.
- Todos los enemigos skineados quedan en `AlwaysAnimate` con `updateWhenOffscreen = true` (`EnemySkinnedAnimBuilder.cs:330`, `:355`). Con el swarm, el costo de skinning no se descarta fuera de cámara. Falta perfilarlo.
- El menú dice "(Chaser+Drone)" pero también reconstruye Slime (`EnemySkinnedAnimBuilder.cs:42-50`). `FixSlimeFacing` (`:390`) no tiene `MenuItem`: solo se llama por código.
- ChargeStarted sube el peso de Upper Body pero no hay estado de carga en el controller (`PlayerAnimationDriver.cs:416`, `:720-723`). La carga se ve como Aim.
- Las armas en modo Automatic no disparan pose de acción ni recoil óseo (`PlayerAnimationDriver.cs:698`). Sin arma manual, el peso de Upper Body queda en 0 y no hay aim de brazos (`:421`, `:589`).
- Los huesos auxiliares se buscan por nombre literal (`:213-227`). Si se renombra el rig, quedan en null en silencio: se apaga el dash direccional y el braceo, sin warning.
- `PlayerAutoAttack` sigue en el prefab deshabilitado (`player.prefab:619`) y llama `EvaluatePoseForWeapons` con la posición del enemigo (`PlayerAutoAttack.cs:53`). Si alguien lo habilita, compite con `WeaponManager` por la única evaluación del frame.
- El rig, el grafo y el controller siguen siendo placeholder (`"Player placeholder animation"`, `:278`), con compresión de animación Off (`PlaceholderPlayerAnimationBuilder.cs:45`). Hay que revisar el costo de memoria antes del arte final.

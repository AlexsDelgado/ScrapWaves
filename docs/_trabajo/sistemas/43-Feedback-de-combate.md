# Feedback de combate — alpha 23-09-2026

El game feel de combate lo resuelve una sola capa de presentación, que el gameplay alimenta con eventos inmutables (`WeaponFeedbackContext`). Ninguna pieza de esta capa lee ni escribe vida. Los canales son VFX, audio, impulso de cámara, hit-stop, recoil, números de daño y reacción del enemigo (flash, empuje, estados, muerte). Cada canal tiene su propio rate limit y respeta las opciones de accesibilidad.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/Weapon/Presentation/WeaponPresentationController.cs` | MonoBehaviour dueño, en el player. Implementa `IWeaponFeedbackSink` y crea un director por perfil |
| `Assets/Scripts/GameFeel/CombatFeedbackDirector.cs` | Ruteo de un perfil: cues, loops, cámara, hit-stop, recoil, texto, reacción |
| `Assets/Scripts/GameFeel/WeaponFxDirector.cs` / `WeaponAudioDirector.cs` | Pools de VFX por cue y voces de audio por director |
| `Assets/Scripts/GameFeel/CameraFeedbackController.cs` | Escala y limita el impulso hacia `ThirdPersonCamera` |
| `Assets/Scripts/GameFeel/HitStopController.cs` | Congela `Time.timeScale` con prioridad y replay mínimo |
| `Assets/Scripts/GameFeel/WeaponRecoilFeedback.cs` | Retroceso cosmético del cañón y tinte de calor |
| `Assets/Scripts/GameFeel/ProjectileVisualController.cs` | Mesh, material, escala y emisión del proyectil según arquetipo y calor |
| `Assets/Scripts/GameFeel/ImpactSurfaceResolver.cs` | Superficie de impacto: metadata, layer `Terrain` o nombre de material |
| `Assets/Scripts/GameFeel/CombatText/*` | Números de daño: evento, agregación, política, pool, vista, formato |
| `Assets/Scripts/GameFeel/EnemyHitFeedback.cs` | Flash (shell) y squash/empuje del visual del enemigo |
| `Assets/Scripts/GameFeel/EnemyDeathFeedback.cs` / `EnemyDeathReactionVfx.cs` | Snapshot y efecto de muerte desacoplado del despawn |
| `Assets/Scripts/GameFeel/EnemyStatusFeedback.cs` | Anillos de estado (burn, jellified, slow, freeze, vulnerable) |
| `Assets/Scripts/GameFeel/EnemyReactionProfile.cs` | Perfil de reacción más `EnemyReactionRuntime` (opciones globales estáticas) |
| `Assets/Scripts/GameFeel/GameFeelQualitySettings.cs` | Multiplicadores de partículas, luces y decals por calidad |
| `Assets/Scripts/Settings/Accessibility/*` | `PresentationAccessibilitySettings` / `State` / `Runtime` |

## Assets en uso

| Asset | Referenciado desde |
|---|---|
| `Assets/ScriptableObjects/GameFeel/CombatTextProfile.asset` | `player.prefab:1040` (`_combatTextProfile`) |
| `Assets/ScriptableObjects/WeaponPresentation/{AutomaticCannon,Flamethrower,Mortar,RocketLauncher,RotatingBlade}Presentation.asset` | `WeaponData.PresentationProfile` de cada arma (`_profile` del controller = null, `player.prefab:1036`) |
| `Assets/GameFeel/Profiles/GameFeelQuality_PC.asset` | los 5 perfiles de presentación |
| `Assets/GameFeel/Resources/EnemyReactionProfile.asset` | `Resources.Load` en `EnemyReactionProfile.Resolve` (`EnemyReactionProfile.cs:81-93`) |
| `Assets/GameFeel/Prefabs/CombatText/CombatTextView.prefab` | `CombatTextProfile.ViewPrefab` |

## Entradas y ruteo

`WeaponPresentationController` (`[DefaultExecutionOrder(-40)]`) arma un `CombatFeedbackDirector` por cada `WeaponPresentationProfile` distinto (`WeaponPresentationController.cs:512-541`). Todos comparten las mismas `GameFeelRuntimeOptions`, el `CameraFeedbackController`, el `HitStopController` y un único `CombatTextDirector`.

| Entrada | Qué hace |
|---|---|
| `Emit(WeaponPresentationContext)` (legacy, con cue explícito) | `EmitLegacy` → `PlayCue` con hit-stop y reacción de enemigo (`CombatFeedbackDirector.cs:80-90`). Lo usan los impactos de `Projectile`, `MortarShellImpact`, el impacto final de Blade y los cues propios de Cannon y Rocket |
| `EmitSemantic(evento, contexto)` | resuelve el cue por bindings (evento, modo, path, crit, weak, kill, superficie) (`:136-196`) |
| `Begin/Update/EndSemanticLoop`, `Begin/Update/EndLegacyLoop` | loops (flamethrower, cannon continuo, targeting de rocket) |
| `OnDamageConfirmed` | texto de daño + reacción del enemigo + hit-stop, si el perfil tiene binding |
| `OnStatusSegmentClosed` | cierra el tally de burn en el texto |
| `ConfigureProjectile` | aplica el arquetipo visual al proyectil (`:242-264`) |

Flujo de `EmitSemantic`:

1. En un DamageConfirmed con kill, la intensidad se multiplica por `EnemyDeathFeedback._effectIntensity` (`:142-144`).
2. DamageConfirmed → `CombatTextDirector.TryEmit`. Si no es de la familia burn, también `EnemyHitFeedback.TryPlay` (`:146-156`).
3. ShotFired → cierra el loop de carga y pide recoil (`:161-169`).
4. Resuelve el cue. Si es loop o está rate-limited, sale.
5. Presentación (VFX, audio, cámara) solo si el evento no es DamageConfirmed, o si es un kill (`:177-180`).
6. Hit-stop solo para DamageConfirmed que no sea burn, con duración × intensidad (`:182-192`).
7. Si algo se reprodujo, `_nextCueTimes[cue] = now + MinReplayInterval`.

`Update` del controller: no corre con `GameplayPause.IsUiPaused`. Tickea FX y audio, y el hit-stop solo una vez. El texto no avanza si el timeScale es 0 por una pausa que no sea hit-stop (`WeaponPresentationController.cs:446-461`).

## Hit-stop

`HitStopController` pone `Time.timeScale = 0` y lo restaura con tiempo no escalado (`HitStopController.cs:50-105`).

| Parámetro (player.prefab:1065-1068 = código) | Valor |
|---|---|
| `_minimumReplayInterval` | 0.035 s |
| `_reducedFeedbackScale` (ReducedShake o ReducedFlash) | × 0.5 |
| `_reducedMotionImportantDurationCap` | 0.015 s. Con ReducedMotion solo pasan los importantes (kill, crit, weak point, ability) |

- Un pedido de menor prioridad no pisa uno activo más largo.
- La duración efectiva es el máximo entre la activa y la nueva.

Duraciones y prioridades de hit-stop en los perfiles (cues con valor > 0):

| Perfil | Cue (duración s / prioridad) |
|---|---|
| Cannon | CriticalImpact 0.014/1, WeakPointImpact 0.024/2, KillImpact 0.04/3 |
| Rocket | Impact 0.02/2, KineticImpact 0.035/3, FragmentImpact 0.012/1, ClusterDetonation 0.025/3, KillImpact 0.045/4 |
| Mortar | Impact 0.018/1, GrapeshotAirburst 0.006/1, MultiChargedImpact 0.014/1, MultiChargedRepeat 0.018/2 |
| Blade | ManualSlash 0.02/0, ActiveThrust 0.06/1, MultiSlash 0.025/0, MultiThrust 0.045/1, AtomicSlash 0.018/0, AtomicDash 0.08/2, MultiFinalImpact 0.035/2, AtomicSliceImpact 0.012/1 |
| Flamethrower | ninguno |

## Cámara (shake / kick)

`CameraFeedbackController.Request` (`CameraFeedbackController.cs:53-89`). La escala es `_masterScale` × intensidad × (0.25 si ReducedShake) × `heat.CameraVibration(calor)` × atenuación por distancia (llega a 0 a 28 m). La cámara (`ThirdPersonCamera.cs:189-221`) aplica reduced motion y los topes.

| Valor | Fuente | Valor |
|---|---|---|
| `_masterScale` / `_reducedShakeScale` | player.prefab:1061-1062 | 1 / 0.25 |
| `_minimumImpulseInterval` / `_maximumImpactDistance` | player.prefab:1063-1064 | 0.025 s / 28 m |
| Tope de posición / rotación / FOV | GameplayScene.unity:5430-5433 | 0.35 / 5° / 5 |
| Decay | escena | 12 |
| ReducedMotion posición / rotación / FOV | escena | × 0.2 / × 0.35 / × 0 |

Rango de impulsos en los perfiles: de FOV 0.08 y rot −0.035 (cannon continuo) a FOV 2 y rot −1 (Rocket KillImpact). El impulso siempre es retroceso en z y pitch negativo.

## Números de daño (CombatText)

1. `CombatTextEvent.TryFromFeedback` valida el contexto. Clave de agregación: target, arma, tipo (explosión y fragmento cuentan como una sola familia), secuencia, instancia de estado, segmento (`CombatTextAggregation.cs:9-90`).
2. Merge: con `ActionSequenceId` > 0 se agrega hasta que `DamageFeedbackSequenceRuntime` marca la secuencia completa. Sin id, por ventana de fallback. Siempre con tope `*MaximumSegmentLifetime` (`CombatTextDirector.cs:321-346`, `:500-517`).
3. Prioridad y estilo (`CombatTextPresentationPolicy.cs:70-101`): Decorative 0, BurnTally 10, AutomaticDirect 20, ManualDirect 30, EliteBoss 40, MajorAbility 50, Critical 60, WeakPoint 70, CritWeak 80, Kill 90, CritWeakKill 100.
4. Visibilidad (`CombatTextDirector.cs:358-470`): modo (Off / ImportantOnly ≥ 40 / Full), en pantalla y delante de cámara, distancia, presupuesto de starts por frame (reserva uno para prioridad ≥ EliteBoss), densidad, y pool. Si no alcanza, reclama una vista de menor prioridad (nunca ≥ WeakPoint).
5. 4 carriles verticales (0, +20, −20, +40 unidades de motion × 0.015 m) y sesgo de 0.05 m hacia la cámara.
6. Formato sin allocations (`CombatTextFormatter.cs`): entero completo hasta 99 999. Por encima, K/M/B/T con un decimal solo si la parte entera es < 10.
7. `CombatTextWorldRenderDriver.LateUpdate` (orden 100) refresca los billboards de todo el pool después de la cámara.

`DamageFeedbackSequenceRuntime`: registro estático de capacidad fija (64, timeout huérfano 1.25 s). Lo abren Cannon, Mortar, Rocket y Blade (`BeginSequence`). En overflow devuelve id 0, que cae al fallback por ventana. `StatusDamageSource` lleva arma, sink, modo, path, daño de referencia (≥ 1) e id de instancia de burn para que cada tick de burn se agregue en su tally.

Valores de `CombatTextProfile.asset`:

| Grupo | Valores |
|---|---|
| Espacial | Sorting 800, WorldTextScale 0.10, 0.015 m por unidad de motion, sesgo a cámara 0.05 |
| Estilos (tamaño / escala base) | Normal 34/1, Burn 30/0.9 (naranja), Jellified 30/0.9 (verde), Crit 40/1.06, Weak 38/1.07, CritWeak 44/1.1, Kill 46/1.1, Ability 36/1.04 |
| Motion (vida s) | Normal 0.78, BurnTally 0.48, Crit 0.86, Weak 0.84, Kill 0.92, Reduced 0.66 (sin shake local) |
| Magnitud | curva ratio→escala 0.25:0.86 … 8:1.38; clamp 0.85–1.42; resuelta 0.82–1.48; crit/weak × 1.08, tope CritWeak 1.16, kill × 1.05, elite/boss × 1.03, burn × 0.9 |
| Fallback (s) | cannon auto 0.16 / manual 0.24 / scatter 0.18, HeadHunter 0.08, contacto 0.14 (blade 0.22), rocket 0.14, fragmento 0.30, llama 0.30, burn 0.65, mortar 0.18, multi-hit 0.18 |
| Vida máxima de segmento (s) | directo 1.10, rocket 1.10, burn 3.25; gracia 0.12 |
| Densidad Low/Med/High | prewarm 18/26/36, activas 16/24/32, starts por frame 3/5/7, burn visibles 6/10/16; pool máx. 40, agregados 128, secuencias 64 |
| Distancia | tamaño completo 26 m, rutina 38 m, importantes 50 m, `DistantScaleMultiplier` 1 |
| Ancla burn | `WorldAnchorHeight` 4.34 (código 1.25), clearance 0.25 |
| Accesibilidad | lateral en ReducedMotion × 0.35, `ReducedShakeMultiplier` 0 |

## Reacción del enemigo

`EnemyHitFeedback.TryPlay` busca el componente en el padre o en los hijos. Si no lo encuentra, lo agrega en caliente sobre `EnemyHealth` (`EnemyHitFeedback.cs:115-133`). Está serializado en los 14 prefabs de enemigo, con `_renderers` vacío.

- Tier (`EnemyReactionProfile.cs:95-107`): Kill > WeakPoint > Critical > Heavy (ability, ≥ 18 % de la vida máxima, o intensidad ≥ 1.15) > Light.
- Intensidad = evento × clase (elite 0.75, boss 0.42) × firma del arma. La firma es flamethrower 0.5, rocket 1.18, mortar 1.25, blade 0.82 y cannon 1 (`EnemyHitFeedback.cs:306`). Acumula con decay × 0.62 y tope 1.35.
- Flash: un "shell" duplicado de cada renderer (escala 1.012), tintado por `MaterialPropertyBlock`. Visibilidad × 0.35 con ReducedFlash y 0 si ScreenFlash está off (`:225-243`).

| EnemyReactionProfile.asset | Light | Heavy |
|---|---|---|
| Duración (× 1.25 si crit/weak) | 0.085 s | 0.14 s |
| Desplazamiento | 0.055 | 0.12 |
| Squash | 0.055 | 0.12 |

Estados: hasta 3 visuales por enemigo y 48 globales, fade de 0.24 s. Muerte: pool de 48 y duración de 1.25 s. Colores: light (1, 0.34, 0.08, 0.72), crit (1, 0.84, 0.28, 0.92), weak blanco 0.95. El asset no serializa los tres campos de reduced motion, así que usan el default de código (desplazamiento 0.2, squash 0.25, duración 0.75).

Muerte: `EnemyDeathFeedback` se suscribe a `EnemyHealth.OnDied`, calcula bounds y color, y programa `EnemyDeathReactionVfx.Schedule`. Ese paso captura un snapshot del mesh (`BakeMesh` en los skineados). El efecto se spawnea el frame siguiente: anillo, núcleo y 14 fragmentos con `LineRenderer`, en tiempo no escalado.

## Accesibilidad y settings

Hay dos fuentes que convergen en `GameFeelRuntimeOptions` (player.prefab:1043-1059, todo en on, CombatText Full, Quality High):

| Fuente | Campos | Llega por |
|---|---|---|
| `UserSettingsService` (menú principal, `SettingsScreenUI`) | ReducedMotion, ScreenShake, ScreenFlash | `UserSettingsApplier` → `ApplyUserFeedbackPreferences` (`WeaponPresentationController.cs:418-430`), `ThirdPersonCamera.ScreenShakeEnabled`, `EnemyReactionRuntime.ApplyUserPreferences` |
| `PresentationAccessibilityRuntime` (persistido por `SaveManager`, pausa `PauseMenuUI`) | ReducedMotion, ReducedShake, ReducedFlash, CombatText, CombatTextScale 0.75–1.25 | evento `Changed` → `ApplyAccessibilityState` (`:645-653`) → `EnemyReactionRuntime.Apply` |

Efecto de cada opción:

- ReducedMotion: bloquea el shake. Hit-stop solo para importantes, con tope de 15 ms. Recoil × 0.2, hueso y cañón. Cámara según la tabla. Texto con motion Reduced. Enemigo sin empuje ni squash.
- ReducedShake: cámara × 0.25, hit-stop × 0.5, sin shake local del texto.
- ReducedFlash / ScreenFlash off: flash del enemigo × 0.35 / 0, y reducción de flash en VFX.
- CombatText: Off libera todas las vistas.
- `GameFeelQualitySettings` (PC): partículas 0.35 / 0.7 / 1, decals 0.35 / 0.7 / 1, luces off en todos los niveles. La calidad cambia el pool del texto y los pools de VFX.

## Deuda / puntos abiertos

- El hit-stop de los cues de ShotFired de Blade (0.018–0.08 s) no se aplica nunca. El camino semántico pasa `playHitStop: false` (`CombatFeedbackDirector.cs:180`) y el perfil de Blade no tiene binding de DamageConfirmed. El de AtomicSliceImpact (ProjectileImpact semántico) tampoco. Solo MultiFinalImpact, que entra por legacy, frena.
- Posible doble reacción por golpe. El impacto legacy dispara `EnemyHitFeedback` (`playEnemyReaction: true`, `:89`, `:317-321`) y el DamageConfirmed del mismo daño lo vuelve a disparar (`:149-155`). También se duplica `EnemyDeathFeedback.RecordHit`.
- ReducedMotion anula por completo el empuje y el squash del enemigo (`EnemyHitFeedback.cs:62`, `:106`). Los multiplicadores `_reducedMotionDisplacementScale` / `SquashScale` del perfil (`EnemyReactionProfile.cs:46-51`) quedan muertos.
- `CreateFlashShells` duplica también los renderers deshabilitados, y `ApplyFlash` enciende el shell sin mirar `source.enabled` (`EnemyHitFeedback.cs:172-185`, `:237`). En Chaser, el `MeshRenderer` placeholder oculto (`Chaser.prefab:399`) aparecería en cada golpe. Hay que verificarlo en play.
- ReducedMotion tiene dos dueños: `UserSettingsService` y `PresentationAccessibilityRuntime`. Los dos escriben las mismas opciones y `EnemyReactionRuntime.ReducedMotion` (`EnemyReactionProfile.cs:174-190`), y gana el último. `PauseMenuUI` sincroniza en un solo sentido (`PauseMenuUI.cs:334-344`).
- UI incompleta. `ImportantOnly` no se puede elegir (dropdown Off/Full, `PauseMenuUI.cs:548`). `HitStopEnabled` y `Quality` solo se cambian desde el sandbox (`WeaponSandboxDebugUI.cs:638-642`), así que producción queda fija en High con hit-stop on.
- `EnemyDeathReactionVfx`: `Statuses`, `Critical` y `WeaponType` se guardan en el pending y no se leen (`EnemyDeathReactionVfx.cs:42-46`, `:104-119`). Además hay allocations por muerte y por frame: `new List<int>` en `FlushPending` (`:127`) y `new Mesh` + `BakeMesh` (`:285-292`).
- `CombatTextDirector.Tick` mide GC y Stopwatch en cada frame, también en builds de release (`:214-254`). `ResolveBurnAnchor` hace `GetComponent<Collider>` / `GetComponentInChildren<Renderer>` por tally de burn y por frame (`:754-759`). `WorldAnchorHeight` = 4.34 en el asset contra 1.25 en el código, y `DistantScaleMultiplier` = 1 deja sin efecto a `FullSizeDistance`.

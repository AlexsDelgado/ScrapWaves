# Cámara y aim — alpha 23-09-2026

Cámara over-the-shoulder con mouse. El aim sale del centro de pantalla. La regla del sistema es separar la pose de **gameplay** de la de **presentación**: el orbit, la colisión y el rayo de aim usan la pose de gameplay. Shake y FOV kick se suman después, solo al transform visible, y nunca entran al aim (`ThirdPersonCamera.cs:293-294`). La retícula de pantalla queda fija en el centro. El aim assist mueve el punto de impacto real, no la retícula.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/ThirdPersonCamera.cs` | Orbit yaw/pitch, hombro, colisión, impulsos de presentación, lock de cursor |
| `Assets/Scripts/Player/ReticleAimProvider.cs` | Rayo de gameplay, punto de impacto, aim assist, predicción de mortero |
| `Assets/Scripts/Player/AimSolution.cs` | `AimSolution` (origen → punto, frame) y `WeaponAimPolicy` |
| `Assets/Scripts/Weapon/Managers/WeaponManager.cs` | Resuelve el `AimSolution` por frame y lo pasa a las armas |
| `Assets/Scripts/Weapon/UI/ReticlePresentationLogic.cs` | Modo de retícula por tipo de arma, progreso del lock de cohete |
| `Assets/Scripts/Weapon/UI/ReticleHud.cs` | Canvas de retícula, marcador de mortero en mundo, flash de weak point |
| `Assets/Scripts/GameFeel/CameraFeedbackController.cs` | Traduce cues de combate a impulsos de cámara |

`ThirdPersonCamera` corre con `[DefaultExecutionOrder(-100)]` (`:5`). Así el aim de gameplay ya está actualizado cuando corre el `Update` de `WeaponManager`.

## Cámara: valores

Componente en la Main Camera de `GameplayScene` (`GameplayScene.unity:5407-5436`). FOV de la cámara: 90 (`:5321`).

| Campo | Script | `GameplayScene` |
|---|---|---|
| `_pivotHeight` | 1,6 | **1,5** |
| `_shoulderOffset` | 0,6 | **1** |
| `_cameraDistance` | 3,5 | 3,5 |
| `_horizontalSensitivity` / `_verticalSensitivity` | 0,12 / 0,12 | 0,12 / 0,12 (las pisa Settings) |
| `_minPitch` / `_maxPitch` | −55° / 65° | **−70° / 70°** |
| `_cameraCollisionMask` | Everything | Everything |
| `_cameraCollisionRadius` / `_cameraCollisionPadding` | 0,25 / 0,12 | igual |
| `_presentationImpulseDecay` | 12 | 12 |
| Tope de impulso: posición / rotación / FOV | 0,35 / 5° / 5 | igual |
| Reduced motion: posición / rotación / FOV | 0,2 / 0,35 / 0 | igual |

`ApplyMainGameOrbitDefaults()` (`:234-253`) reescribe los valores de script (1,6 / 0,6 / −55..65). La llaman los builders del sandbox y de balance (`WeaponTestingSandboxManager.cs:581`, `BalanceTestingSceneBuilder.cs:135`). Por eso el sandbox no encuadra igual que la escena principal.

## Cámara: frame

1. En `Update` (`:255-287`):
   - Suma el delta de mouse a yaw y pitch, salvo con `_lookBlockedByUi`, y clampa el pitch.
   - `pivot = target + up × pivotHeight`.
   - `anchor = pivot + orbit × (hombro, alturaCam, 0)`.
   - `deseada = anchor − forward × distancia`.
   - Resuelve la colisión y guarda `_gameplayPosition` / `_gameplayRotation`.
2. Colisión (`:311-351`): SphereCast desde el ancla hacia la posición deseada, ignorando el propio target y sus hijos. Deja la cámara en `hit − padding`, con mínimo 0,05 m.
3. En `LateUpdate` (`:289-303`):
   - Aplica al transform `gameplay + impulsos × _cameraFeedbackScale`.
   - Aplica `FOV = base + kick`.
   - Decae los impulsos con `exp(−12 × unscaledDt)`. Los impulsos siguen en hit-stop.
4. `GameplayCenterRay` (`:78`) = rayo desde la pose de gameplay más el near clip, en la dirección de gameplay. Es el rayo que usa el aim.

`AddPresentationImpulse` (`:189-223`) suma y clampa. Devuelve `false` si el shake está apagado, si la escala es 0 o si el impulso es nulo. Con `reducedMotion` escala primero por los factores de reduced motion.

`SetLookBlockedByUi` (`:111-128`) libera o bloquea el cursor. Lo usan `PauseMenuUI`, `LevelUpChoiceUI`, `CraftingUI`, `RunEndScreenUI` y las UIs de debug.

## Aim de gameplay

`WeaponManager.GetAimDirection` (`WeaponManager.cs:355-363`) llama `ReticleAimProvider.ResolveWeaponAim(spawn.position, armaManual)`. En `Update` (`:66-81`) lo resuelve una vez, evalúa la pose animada del esqueleto y lo vuelve a resolver con el muzzle actualizado. El aim se calcula antes del chequeo de `timeScale`, también en pausa.

Valores del provider (`player.prefab:831-837`, iguales al script): `_maxAimDistance` 150, `_aimMask` Everything, `_aimAssistRadius` 0,35 m. `_aimCamera` e `_ignoredRoot` vacíos: resuelven a `Camera.main` y al root del jugador.

Flujo de `TryGetAimSolution` (`ReticleAimProvider.cs:50-56`):

1. **Rayo.** `GameplayCenterRay` si la cámara tiene `ThirdPersonCamera` activa. Si no, el viewport en (0,5; 0,5) (`:65-74`).
2. **Raycast.** Hasta 150 m, sin triggers. Ignora el cuerpo del jugador y se queda con el hit más cercano (`:141-160`).
3. **Sin hit.** El punto es la intersección del rayo con una esfera de radio = alcance del arma (`BaseRange`), centrada en el muzzle (`:247-265`). Así un disparo al vacío converge con la retícula a la distancia del arma, no a 150 m.
4. **Aim assist.** Solo si la política del arma lo pide y el rayo no tocó un enemigo directo (`:163-164`). SphereCast de 0,35 m. Por candidato busca el punto del collider más cercano al rayo (ternaria de 24 pasos) y descarta los tapados por otra geometría (`:168-222`). Gana el menor gap. No hay target persistente entre frames.
5. `AimSolution` = origen del muzzle → punto, con `Direction` normalizada y `FrameNumber` (`AimSolution.cs:4-21`).

`WeaponAimPolicy.PreferDamageableAimPoint` (`AimSolution.cs:25-31`):

| Arma | Aim assist |
|---|---|
| RocketLauncher | Sí |
| AutomaticCannon | Sí, salvo con Path B avanzado |
| Resto | No |

Un collider cuenta como "enemigo" si tiene `EnemyRegistryMember` o `IDamageable` en los padres (`:234-245`).

### Predicción de mortero

`TryGetMortarTerrainImpact` (`:83-114`) recorre la parábola de `MortarTrajectory` en 64 cuerdas con SphereCast del radio del proyectil. Ignora jugador, `IDamageable` y miembros del registro de enemigos: solo cuenta terreno (`:116-131`).

## Retícula

`ReticlePresentationLogic.ResolveMode` (`ReticlePresentationLogic.cs:25-37`):

| Arma | Modo |
|---|---|
| Flamethrower, RotatingBlade | `WideBrackets` (250×70 px) |
| AutomaticCannon, RocketLauncher | `CircleDot` (Ø 34, punto 5) |
| RocketLauncher cargando lock | `RocketLock`: marco de 180×100 a 1344×756 que se agranda con el progreso |
| Mortar | `Mortar`: V en pantalla y anillos en el mundo |
| Sin arma manual | `Hidden` |

Valores en `player.prefab:852-877`, iguales al script.

`ReticleHud.LateUpdate` (`ReticleHud.cs:115-154`):

1. Lee el arma manual de `WeaponManager` o, si existe, del sandbox.
2. Resuelve el modo y actualiza el marco de cohete.
3. En modo mortero, recalcula la predicción cada 0,04 s (`_mortarPredictionInterval`) con el `AimSolution` del frame. Si el aim no es de este frame, oculta el marcador (`:474-495`).
4. Flash rojo de 0,18 s en toda la retícula ante `WeaponWeakPointFeedback.WeakPointHit`.

La retícula queda anclada al centro del canvas (`:137-138`).

## Deuda / puntos abiertos

- `_minimumDistanceFromLookPoint` (0,65) está serializado pero ningún código lo lee (`ThirdPersonCamera.cs:48`, solo aparece en `:248`). La cámara puede quedar a 0,05 m del ancla contra una pared.
- Los factores de reduced motion de la cámara casi nunca se aplican. `UserSettingsApplier` apaga todo el shake con reduced motion activo (`UserSettingsApplier.cs:239`) y `AddPresentationImpulse` sale antes de escalar (`:202-203`). Solo aplican cuando el reduced motion de accesibilidad (SaveManager) difiere del de Settings. Ver `13-Settings-y-accesibilidad.md`.
- `ReticlePresentationLogic.TryProjectAimPoint` no tiene llamadores. Con aim assist, la bala va a un punto distinto del centro y la retícula no lo muestra.
- El look lee `Mouse.current.delta` directo (`:260-268`). No hay gamepad ni suavizado.
- `ApplyMainGameOrbitDefaults` contradice los valores de `GameplayScene` (pivote, hombro, pitch). Las escenas de test encuadran distinto y el aim assist se siente distinto con hombro 0,6 que con hombro 1.
- El aim se resuelve dos veces por frame cuando hay animation driver (`WeaponManager.cs:66-79`): son dos raycasts más el SphereCast de assist.

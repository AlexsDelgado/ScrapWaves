# Rear threat — alpha 23-09-2026

Indicador de amenaza trasera: una media luna en el piso a la altura de la cadera del jugador que se estira en púas hacia los grupos de enemigos que vienen por detrás. No es UI de Canvas: es un mesh en mundo que se dibuja con `ZTest Always`, así que se ve a través del jugador y de las paredes. Separa lectura (sensor) de dibujo (presenter). El sensor no emite eventos de combate ni toca vida.

No hay otro widget "sensor" de enemigos: en `Assets/Scripts/Enemy` y `Assets/Scripts/Weapon/UI` el único `*Sensor*` es `RearThreatSensor`.

## Scripts

| Script | Rol |
|---|---|
| `Assets/Scripts/Weapon/UI/RearThreat/RearThreatSensor.cs` | Muestrea `EnemyRegistry`, filtra, agrupa y arma el snapshot. No crea UI |
| `Assets/Scripts/Weapon/UI/RearThreat/RearThreatSnapshot.cs` | `RearThreatSpike` y `RearThreatSnapshot`: copia inmutable, hasta 5 púas |
| `Assets/Scripts/Weapon/UI/RearThreat/RearThreatMesh.cs` | Geometría: tira radial de 128 segmentos con púas insertadas en el borde exterior |
| `Assets/Scripts/Weapon/UI/RearThreat/RearThreatPresenter.cs` | `[ExecuteAlways]`, orden 110. Maneja el sensor, suaviza con resortes, sigue al jugador, colorea |
| `Assets/Shaders/UI/RearThreatCrescent.shader` | Unlit URP, color plano `_BaseColor`, alpha blend, `ZWrite Off`, `ZTest Always`, `Cull Off` (:6-13) |
| `Assets/Scripts/Editor/RearThreatIndicatorAuthoring.cs` | Menús de autoría: prefab, materiales, meshes de preview e instancia bajo `UI` |
| `Assets/Scripts/Editor/RearThreatValidation.cs` | Compila player scripts y renderiza previews PC/Mobile a `Library/CodexValidation/RearThreat` |
| `Assets/Scripts/Editor/GameplayUiSceneMigration.cs` | La migración de UI llama `RearThreatIndicatorAuthoring.AuthorUi(ui, player)` (:132) |

Assets: `Assets/Prefabs/UI/RearThreatIndicator.prefab` (raíz con sensor + presenter, hijos `Fill` y `Outline`), `Assets/Art/UI/RearThreat/RearThreatFill.mat` (cola 2991), `RearThreatOutline.mat` (cola 2992), `RearThreatFillPreview.asset` y `RearThreatOutlinePreview.asset` (meshes estáticos solo para el editor).

## Qué cuenta como amenaza trasera

Fuente: `EnemyRegistry.CollectActive` (`Assets/Scripts/Enemy/EnemyRegistry.cs:46`), que ya descarta enemigos dormidos por altura (`EnemyVerticalEngagement.IsDisengaged`, :62). Sobre esa lista, el sensor (`RearThreatSensor.cs:266-303`) exige:

1. Que la raíz no sea el jugador ni un hijo suyo.
2. Vivo y activo: GameObject activo, `EnemyRegistryMember` habilitado si existe, y `EnemyHealth.CurrentHealth > 0`, o `WeaponDummyEnemy.CurrentHealth > 0` en el sandbox (:332-337).
3. Diferencia vertical `|Δy| ≤ 3 m`. Estricta, sin histéresis (:283).
4. Distancia planar (XZ, desde la raíz del enemigo, no desde sus bounds) `≤ 10 m`, o `≤ 10,5 m` si ya era elegible (:281).
5. Dentro del arco trasero: ángulo contra `-forward` `≤ 90°`, o `≤ 95°` si ya era elegible (:263-264, :290).

El `forward` sale del jugador (`PlayerFacing`, el valor serializado) o de la cámara (`CameraFacing`: usa `ThirdPersonCamera.GameplayForward` y, si no hay, `camera.forward`). Siempre se aplana a XZ (:227-255).

El jugador es válido si existe, está activo, tiene posición finita y `PlayerHealth.IsAlive` (:215-219). Si se destruye, el sensor intenta rebindear a `PlayerMovement.PlayerTransform` de la misma escena (:183-188).

## Valores (prefab = escena)

`GameplayScene` no sobrescribe ningún valor numérico del sensor ni del presenter: solo `_player`, `_cameraReference` y `_gameplayCamera` (`GameplayScene.unity:20487-20498`). Los valores del prefab coinciden con los defaults del código.

Sensor (`RearThreatSensor.cs:24-35`, `RearThreatIndicator.prefab`):

| Campo | Valor | Efecto |
|---|---|---|
| `_referenceFrame` | PlayerFacing | Base del arco |
| `_detectionRadius` | 10 m | Radio de entrada |
| `_fullStrengthDistance` | 2 m | Urgencia 1 a esta distancia o menos |
| `_rearArcDegrees` | 180° | Arco total de entrada (±90°) |
| `_maxVerticalDifference` | 3 m | Filtro de altura |
| `_sampleInterval` | 0,1 s | Muestreo completo cada 100 ms de `Time.time` |
| `_distanceExitMargin` | 0,5 m | Histéresis de salida por distancia |
| `_arcExitMargin` | 5° | Histéresis de salida por borde de arco |
| `_clusterAngle` | 15° | Radio angular de un grupo |
| `_maxSpikes` | 5 | Tope de púas (tope duro `RearThreatSnapshot.MaximumSpikes` = 5) |
| `_replacementDistanceAdvantage` | 0,5 m | Ventaja que necesita un grupo nuevo para desplazar al más lejano |

Presenter (`RearThreatPresenter.cs:13-40`):

| Campo | Valor | Efecto |
|---|---|---|
| `_hipOffset` | (0; 0,25; 0) | Offset desde la raíz del jugador, rotado con el forward |
| `_innerRadius` | 0,8 m | Hueco alrededor del jugador |
| `_idleOuterRadius` | 0,86 m | Borde exterior en reposo (banda de 6 cm) |
| `_maximumRadiusMultiplier` | 1,1 | Borde exterior con urgencia 1: 0,946 m |
| `_minimumSpikeLength` / `_maximumSpikeLength` | 0,1 / 0,65 m | Largo de púa según urgencia del grupo |
| `_spikeWidthDegrees` | 10° | Ancho de la punta |
| `_shoulderWidthDegrees` | 44° | Ancho del "hombro" redondeado |
| `_localFlex` | 0,75 | Parte del crecimiento que se concentra cerca de las púas |
| `_outlineWidth` | 0,03 m | Trazo del contorno |
| `_growthTime` / `_relaxTime` | 0,12 / 0,25 s | SmoothDamp al crecer / al relajar |
| `_directionSmoothingTime` | 0,08 s | Seguimiento del rumbo de cada púa |
| `_idleColor` | (1; 0,35; 0,65; 0,08) | Rosa casi transparente en reposo |
| `_alertColor` | (1; 0,02; 0,06; 0,95) | Rojo casi opaco con urgencia 1 |
| `_outlineColor` | (0; 0; 0; 0) | Contorno apagado |

Con un contorno de alpha 0, `SetVisible` deja apagado el renderer `Outline` (:337). En el prefab ese `MeshRenderer` ya viene con `m_Enabled: 0`.

## Geometría

`RearThreatMesh.Update` (`RearThreatMesh.cs:26-116`):

- Ángulos en grados relativos a "atrás". Positivo = izquierda. `Direction(a) = (-sin a, 0, -cos a)` (:128-132).
- Medio arco dibujado = `rearArc/2 + arcExitMargin` = 95° (`RearThreatPresenter.cs:240`). El arco visible total es de 190°, 10° más ancho que el de entrada.
- 129 muestras uniformes, más 3 por cada púa (flanco, punta, flanco), ordenadas y sin duplicados.
- Radio exterior por muestra = `outer − crecimiento_localizado × hombro_no_cubierto + largo_púa`. Una púa corta sale como bulto redondeado y se afila al madurar (:55-88).
- Tapas en los extremos que no invaden el hueco interior. Si el arco llega a 360° no hay tapas (:98-112).
- Bounds fijos: `2 × (idleOuter × 1,1 + 0,65)` de lado.

Color: `Color.Lerp(idle, alert, urgenciaSuavizada)` vía `MaterialPropertyBlock` sobre `_BaseColor`. El material compartido no cambia (`RearThreatPresenter.cs:254-264`). Los renderers no proyectan ni reciben sombras, no usan probes ni motion vectors (:274-283).

## Flujo por frame

1. `RearThreatPresenter.LateUpdate` (:115) en play: si falta el sensor o los meshes, resetea y oculta.
2. `RearThreatSensor.RefreshForPresentation(Time.time)` (:174). Si pasó `_sampleInterval`, hace un muestreo completo (`Sample`). Si no, solo saca candidatos que murieron o se desactivaron y reagrupa (:204-208). El forward se recalcula cada frame.
3. `Sample` ordena candidatos por distancia (desempata por InstanceID) y llama `BuildGroups`.
4. `BuildGroups` (:358-464):
   1. Agrupa con semilla = el más cercano sin grupo. Suma a todos los que quedan a ≤ 15° de la semilla. No encadena.
   2. Reasigna `TrackId` previos por solapamiento de miembros. Si cambia el enemigo más cercano, el track visual no cambia.
   3. Primero retiene los grupos con track. Después completa hasta 5. Un grupo nuevo desplaza al seleccionado más lejano solo si está 0,5 m más cerca.
   4. Urgencia de cada púa = `InverseLerp(10, 2, distancia del grupo)`. `OverallUrgency` = urgencia del candidato más cercano, esté o no en una púa seleccionada.
5. El presenter empareja púas por `TrackId`. Un track nuevo arranca en largo 0 y en su propio rumbo, sin barrer desde el anterior (:226-238). Una púa que quedó fuera del arco por un giro va a largo 0 (:216).
6. `Follow`: pone el objeto en `player.position + rot × hipOffset` y compensa la escala del padre `UI` (:242-250).
7. `Draw` regenera los meshes `fill` y `outline`.

`EnemyRegistry.Unregistered` invalida en el acto al enemigo despawneado (:495-512). Con el pool, la reutilización arranca con track nuevo.

Pausa: con `Time.deltaTime == 0` (pausa de UI o hit-stop) los resortes se congelan. Solo se reexpresan los rumbos si cambia la base (:155-162). Como `Time.time` no avanza, tampoco se remuestrea.

## Autoría y editor

Menús en `ScrapWaves/UI/`:

| Menú | Hace |
|---|---|
| Create Missing Rear Threat Indicator Assets | Crea el prefab, los materiales (colas 2991/2992) y los meshes de preview si falta el prefab (`RearThreatIndicatorAuthoring.cs:35-76`) |
| Add Rear Threat Indicator To Current Scene | Instancia bajo la raíz `UI` y bindea jugador y cámara `MainCamera` (:78, :137-201) |
| Refresh Rear Threat Prefab Preview Meshes | Regenera los assets de preview desde el prefab (:81-100) |
| Add Rear Threat Indicator To All Player Scenes | Backup a `Library/RearThreatIndicatorAuthoring/<fecha>` y autoría en 6 escenas fijas (:25-33, :203-233) |

`AuthorUi` no pisa lo que ya existe. Falla si hay dos indicadores, si el indicador existente apunta a otro jugador o si la jerarquía está incompleta.

En modo edición, el presenter dibuja un preview con `_previewUrgency`, `_previewSpikeAngles` y `_previewSpikeUrgencies` (:372-394). `RearThreatPreviewSaveProcessor` (:298-319) restaura los meshes autorados antes de cada guardado y vuelve a generar el preview en el `delayCall`, para que el mesh dinámico (`HideFlags.DontSave`) no termine serializado.

Validación (`ScrapWaves/Validation/Rear Threat/`): *Compile and Render* y *Render PC and Mobile Previews* (`RearThreatValidation.cs:63-165`). Renderiza 6 casos (idle, single, cluster, a través de pared, cámara baja, jugador real) en los perfiles de calidad `PC` y `Mobile`. `BeginPlayModeValidation` (:30-43) no tiene menú: es para batch y corre `RearThreatPlayModeValidationTests`.

## Escena

`GameplayScene.unity:20443-20515`: instancia de `RearThreatIndicator.prefab` bajo la raíz `UI` (`GameplayScene.unity:30161`, posición mundial (238,55; 0; 34,09)). `_player` apunta al Transform de `player.prefab`; `_cameraReference` y `_gameplayCamera`, a la cámara principal. El indicador también está en SampleScene, WeaponTestingSandbox, WeaponTestingSandbox_GameFeel, test_balance y enemiesTesting.

## Tests

`Assets/Tests/Editor/`: `RearThreatSensorTests` (filtros, histéresis, clustering, tracks, 0 allocs con 100 enemigos), `RearThreatPresentationTests` (continuidad del mesh, resortes, pausa, paleta sin tocar materiales), `RearThreatAuthoringTests` (preserva ediciones manuales, un indicador por escena de jugador) y `RearThreatPlayModeValidationTests` (Play Mode, transiciones y allocs en régimen).

## Deuda / puntos abiertos

- `GameplayScene` sobrescribe `m_Mesh` de `Fill` y `Outline` a None (`GameplayScene.unity:20484`, `:20504`). Lo mismo pasa en WeaponTestingSandbox y WeaponTestingSandbox_GameFeel. En runtime no afecta, porque `EnsureMeshes` crea meshes dinámicos. Pero contradice `RearThreatAuthoringTests.PlayerScene_HasOneAuthoredRearThreatIndicatorUnderUi` (`RearThreatAuthoringTests.cs:150`, `AssertMesh` exige `sharedMesh` persistente no nulo): en esas tres escenas ese test debería fallar. Hay que revertir el override.
- El presenter regenera los dos meshes en cada `LateUpdate` aunque no haya amenazas (`RearThreatPresenter.cs:148`). También genera el `outline` con el contorno apagado (alpha 0 y renderer deshabilitado). Es trabajo por frame que se puede evitar con dirty-check.
- `MaximumSpikes = 5` está duplicado en `RearThreatSnapshot.cs:26` y `RearThreatMesh.cs:6`, sin vínculo entre las dos constantes. El sensor usa una y el presenter la otra.
- `OverallUrgency` (el color de toda la media luna) toma al enemigo más cercano aunque su grupo no esté entre las púas mostradas (`RearThreatSensor.cs:462`). Puede haber rojo sin púa visible en esa dirección.
- Hay dos listas de escenas distintas. El menú de rear threat usa una lista fija de 6 (`RearThreatIndicatorAuthoring.cs:25-33`). `GameplayUiSceneMigration` busca escenas por el GUID del jugador (`GameplayUiSceneMigration.cs:32-34`). Una escena de jugador nueva solo entra por la segunda vía.
- El arco dibujado (±95°) es más ancho que el de entrada (±90°) porque suma la histéresis (`RearThreatPresenter.cs:240`). Si se quiere que el borde visual marque el límite real de detección, hay que separar los dos valores.
- La salida de validación va a `Library/CodexValidation/...` (`RearThreatValidation.cs:19`), un nombre heredado de otra herramienta.

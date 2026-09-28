# Etapa 2 — Deudas y mejoras: resumen consolidado (23-09-2026)

Base: `main` @ `53ef237`. Fuentes:
- `etapa2/deuda-gameplay.md` — 19 hallazgos (G-01…G-19): armas, enemigos, spawning, overheat, daño.
- `etapa2/deuda-sistemas.md` — 26 hallazgos (S-01…S-26): economía, meta, XP, jugador, UI, settings, audio, game feel.
- `etapa2/deuda-proyecto.md` — 21 hallazgos (P-01…P-21): repo, build, release, paquetes, assets, tests, proceso.
- Secciones "Deuda" de los 22 docs de `sistemas/` — lo que no estaba en las auditorías se lista abajo como D-01…D-30.

Total: ~96 hallazgos. Severidad: Alta / Media / Baja. Esfuerzo: S (horas), M (días), L (semanas).

## 1. Prioridad 1 — rompe el balance, pierde datos o expone el release

| # | Tema | Hallazgo | Ref. | Esf. |
|---|---|---|---|---|
| 1 | Balance de armas | El CSV de armas nunca se importa: `WeaponStatsParser` busca "Basic"/"Total" en columnas invertidas. Antes de salir vacía `BalanceStats` y aplana `LevelData` a 1.0, y `EconomyBootstrap` lo corre **en cada Play** sobre los assets reales. Resultado: los niveles 1–5 de las armas no suben daño, y `BaseDamage` no coincide con el CSV ni con el snapshot. Hay dos herramientas que escriben `WeaponData` y gana la última. | D-01, S-10 | M |
| 2 | Economía | Scavenging base 50 con fórmula `chance*(1+S)` → drop de material al 100 % siempre; `_dropChance` y el stat no hacen nada. DoubleDrop es binario. Hay 4 fórmulas distintas. | S-04, E1-C | S |
| 3 | Combate | El powerup ExtraDamage suma 50–100 a `CriticalChance` (escala 0–1) → 100 % de crítico durante 15 s. ExtraScavenging tiene el mismo error de escala. | E1-9, D-12 | S |
| 4 | Guardado | Guardado síncrono de todo el save en cada kill y cada pickup; escritura no atómica, sin backup; un save corrupto se pisa con uno vacío en el primer kill. | S-01, S-02 | S–M |
| 5 | Release | Debug activo sin guardas en `GameplayScene`: vida infinita, 999 materiales, cambio de velocidad y Overheat manual. Hay 3 escenas de test en Build Settings. Player Settings siguen con valores de plantilla (`Dumpster FIre`, `urp-blank`), y ese nombre define la ruta del save. | P-01, P-02, P-03 | S |
| 6 | Softlock | El boss no tiene red de seguridad: si queda fuera de juego sin morir, el Overheat no termina nunca. | G-03 | M |
| 7 | Loop | El boost de la fase intermedia y de salida solo lo lee `EnemyFollow`, que usan los bosses. El swarm usa `SimpleFollow` y no acelera. El flag estático no se resetea al reintentar. | G-01, G-02 | S |
| 8 | Pausa | `Time.timeScale` no tiene dueño. El hit-stop, `DebugUI` (P, y Q/E serializados como teclas de velocidad) y los menús lo pisan, así que el juego corre detrás de la pausa o del level-up. | S-06, D-27 | M |
| 9 | Meta | "Survival adept" se regala al arrancar (`ResetRunScratch` sin llamadas). Salir desde la pausa pierde el scrap. | S-03, S-08 | S |
| 10 | Settings | El volumen de SFX al 100 % (o de música al 45 %) vuelve a 20 % en cada arranque, porque la migración legacy no tiene marca de "ya migrado". | D-02 | S |
| 11 | Repo | No hay LFS ni `.gitattributes`. Hay un `.rar` de 58 MB en el historial, que es la mitad del repo. `docs/` no está versionado y no hay README. | P-04, P-05, P-08 | S–M |

## 2. Prioridad 2 — diseño y contenido que no se juega como está pensado

- Las variantes (Hellfire, Bomber, Shocker) nunca salen del orbital. Shocker no aparece en ningún lado. `ExtraEliteChance` no tiene efecto (E1-6).
- El multi-boss está configurado para un ciclo impar y nunca ocurre (G-04). La vida del boss es fija en 1500 y la oleada de elites es siempre 3+3+3, sin escalar con dificultad ni ciclos (G-05).
- Las meta-mejoras se aplican dos veces (base+sumas y cada tirada), y los topes están repetidos en 4 archivos (S-11). La meta de crítico multiplica una base 0, así que casi no hace nada (D-13).
- Los logros BossHunter y RustyMarathoner prometen ítems que no existen. Path B se puede comprar sin hacer su challenge (D-14). Los logros `WeaponLevelReached` no se pueden conseguir (S-18).
- `AbilityCooldownReduction` llega a ~1.09 en nivel 36, con clamp en 0.95, y la habilidad queda al 5 % de su cooldown (D-15).
- El cooldown del ciclo manual nunca corre, así que con un arma el manual es infinito. El cooldown de Q solo avanza en manual (D-03).
- Bugs por arma (D-04):
  - Rutas del mortero cruzadas respecto del CSV.
  - Atomic sharpness multiplica el daño dos veces.
  - Head Hunter automático dispara cada 10 s.
  - El lanzallamas tiene knockback 0.
- El burn del lanzallamas cura por lifesteal (D-05). El burn al jugador ignora el escudo y la invulnerabilidad (S-09).
- Tinker cobra los materiales antes de verificar que queden armas (D-06).
- La puerta tiene radio 20 e ignora Y, así que se activa desde otro piso (D-07).
- Movimiento (D-08):
  - Sin coyote time ni jump buffer.
  - El slide solo se alcanza con dash.
  - El suelo es "Everything" (los enemigos recargan saltos).
  - El shake de cámara tuerce la dirección de movimiento.
- Audio (D-09):
  - El SFX de disparo no suena nunca.
  - Los clips de hit, muerte y capa de Overheat están vacíos.
  - No hay mixer y la música no se pausa.
- UI: los títulos de la carta de level-up se ignoran, y hay idiomas mezclados (S-12, D-10). El crafting no se cierra con Escape (D-11).

## 3. Prioridad 3 — rendimiento

- `DebugMonitor` hace `Debug.Log` cada frame de disparo continuo (G-06). Hay 121 `Debug.Log` en runtime, 4 por cada slide (P-12).
- El lanzallamas asigna mesh arrays por frame (G-07). `WeaponRadialDamage` hace `SyncTransforms`, no usa layer mask y tiene una dedup O(n²) (G-08).
- `PlayerHealth` durante los i-frames: O((P·E)²) por frame (S-05).
- `LevelExitHud` y `WorldPickup` asignan memoria y hacen `GetComponent` por frame (S-14, S-15). `MaterialDrop` ignora `PickupRange`.
- Pools:
  - Los topes de `EnemyTimedAreaPool` y `ExplosionRadiusVfxPool` no funcionan (D-16).
  - Pasado el pool de 256, se instancia sin dormancia (D-17).
  - `OrbitalSpawner._spawned` crece sin límite (G-09).
  - Hay 64 enemigos legacy precargados que no se usan (G-11).
- El Stalker pierde un material por cada escupitajo (G-10). Los enemigos skineados usan `AlwaysAnimate` fuera de cámara (D-18).
- Feedback de combate (D-19):
  - Posible doble reacción por golpe.
  - Allocs en cada muerte.
  - Medición de GC en cada frame en release.

## 4. Prioridad 4 — arquitectura, tests y proceso

- No hay asmdefs: el código de testing entra al build y está acoplado a producción (G-12, P-09).
- Hay 17 clases de más de 800 líneas. `AutomaticCannonWeapon` tiene 1850 y `ObjectivesMenuUI` 1317 (G-13, S-16).
- El input está fijo en código: sin gamepad y sin remapeo. El asset de acciones contradice el código (S-13, D-08).
- No hay localización: ~290 textos en código, con español e inglés mezclados (S-12, G-19).
- La accesibilidad está repartida en dos almacenes: el título y la pausa escriben flags distintos (S-07, D-20).
- 840 tests, todos EditMode. Sin PlayMode ni CI. Sin tests en overheat, bosses, spawners, ruleta, scavenging ni crafteo (P-09, P-10, P-11).
- Código muerto:
  - `UIManager`, `SurvivorHud`, `UpgradeManager` y `WeaponLevelUpHandler`.
  - `GameplayHud.prefab` V1, con un builder que apunta a V1 (D-21).
  - `SwarmSpawner`, campos de timer de Overheat, `Sandbox_*` huérfanos y `XPPool` (G-15, S-17).
- La pausa no está en el prefab del HUD, sino como override por escena (D-22). Las salidas desde gameplay no usan la transición de escena (D-23).
- Tres relojes de run distintos; la fórmula de scrap con números mágicos (G-17).
- Repo:
  - Restos de la plantilla URP (~75 MB).
  - Carpetas `Art` y `Arte` duplicadas.
  - BGM duplicada en `.ogg` y `.m4a`.
  - 20 ramas, mergeadas o abandonadas.
  - Commits "placeholder".
  - Churn de TMP (P-06, P-07, P-13, P-14, P-15, P-19).
- Paquetes experimentales o sin uso. Standalone en Mono, sin vSync ni límite de FPS (P-16, P-17).
- Escena (D-24):
  - Iluminación sin luz direccional, pero con sombras de main light reservadas.
  - `EnemyPro` suelto en la raíz.
  - Nombres de plantilla (`SampleSceneProfile`).
- El override de la escena deja los meshes del rear threat en None. El test de authoring debería fallar (D-25).

## 5. Hallazgos que vienen de los docs de sistemas (D-xx)

| ID | Sev. | Hallazgo | Doc |
|---|---|---|---|
| D-01 | Alta | Parser del CSV de armas invertido: vacía `BalanceStats` y aplana `LevelData` en cada Play. Hay bugs latentes para cuando se arregle (`:203` costo ← "Ability damage", `:289`). | 20, 30, 31 |
| D-02 | Alta | Migración de volumen sin marca: SFX 100 % o música 45 % → 20 % en cada arranque. | 13 |
| D-03 | Media | Cooldown manual muerto; cooldown de habilidad solo en manual. | 30 |
| D-04 | Media | Mortero con paths cruzados; Atomic con daño ×2; Head Hunter cada 10 s; lanzallamas knockback 0 y ruta A con hasta 64 charcos. | 31 |
| D-05 | Media | El burn (DoT) cura con lifesteal. El crédito de arma se anota aunque el golpe sea rechazado. | 10 |
| D-06 | Media | Tinker cobra antes de verificar candidatos. El mensaje "+50 %" no coincide con los costos reales (+60/+47/+50). | 20 |
| D-07 | Media | Puerta con radio 20 y 20 s en escena (3/5 en prefab) y chequeo sin Y. | 52 |
| D-08 | Media | Movimiento: sin coyote time ni buffer, slide inalcanzable corriendo, crouch sin efecto, `_groundMask` = Everything, shake en la dirección, fricción que invierte el signo. | 40 |
| D-09 | Media | Audio: SFX de disparo nunca suena, clips vacíos, sin mixer ni pausa de audio, `AudioManager.Instance` sin guarda. | 14 |
| D-10 | Baja | `LevelUpChoiceUI` ignora `title`; textos en inglés fijos. | 45 |
| D-11 | Baja | El crafting no se cierra con Escape. | 45 |
| D-12 | Media | ExtraScavenging en escala 0–100 → doble drop garantizado. Nuke sin crítico ni multiplicadores y con colliders múltiples. Powerups como esferas en build. | 21 |
| D-13 | Baja | Meta de crítico sobre base 0. `MaxHealth` y la vida se desincronizan. Los modificadores de level-up con fuente null. | 41 |
| D-14 | Media | Logros que prometen ítems inexistentes; Path B comprable sin challenge. | 22 |
| D-15 | Media | `AbilityCooldownReduction` ~1.09 en nivel 36 (clamp 0.95). `JumpHeight` y `DashSpeed` con montos por nivel despreciables. | 23, 41 |
| D-16 | Media | Topes de `EnemyTimedAreaPool` y `ExplosionRadiusVfxPool` cuentan solo los inactivos → crecen sin límite. | 51, 30 |
| D-17 | Media | Pool agotado → `Instantiate` sin dormancia ni drops meta. Los dormidos cuentan para el tope de 300. | 51 |
| D-18 | Baja | Enemigos skineados con `AlwaysAnimate` y `updateWhenOffscreen`. Chaser y Drone en fase. | 42 |
| D-19 | Media | Hit-stop de Blade nunca aplicado; doble reacción por golpe; flash de renderers ocultos; `ReducedMotion` anula empuje y squash; GC en `CombatTextDirector.Tick`. | 43 |
| D-20 | Media | Reduced motion con dos dueños. `ImportantOnly` no se puede elegir. Hit-stop y calidad solo en el sandbox. | 13, 43 |
| D-21 | Baja | `GameplayHudPrefabBuilder` reescribe V1, que ninguna escena usa. | 45 |
| D-22 | Media | `PauseRoot` como override de escena: un HUD V2 nuevo queda sin pausa. | 45 |
| D-23 | Baja | Retry y Quit sin transición. `GameplayScene` arrancada en el editor no crea `UserSettingsService`. | 12 |
| D-24 | Baja | Escena: sin luz direccional, `EnemyPro` suelto, nombres de plantilla, trigger de niebla redundante. | 52 |
| D-25 | Baja | Override de los meshes del rear threat a None; el test de authoring debería fallar. Meshes regenerados por frame. | 44 |
| D-26 | Baja | Cámara: `_minimumDistanceFromLookPoint` no se lee; la retícula no muestra el punto real con aim assist; aim resuelto 2 veces por frame. | 11 |
| D-27 | Media | `DebugUI` en la escena: las teclas de velocidad serializadas son Q y E (habilidad e interactuar). | 53 |
| D-28 | Baja | Firepoints: `ClearWeapons` destruye VFX pooleados y pierde slots; lock-on de cohete colgado; aim de wearables muerto. | 32 |
| D-29 | Baja | Elites con drops de la familia base. Comentario de `BomberDroneBehavior` desactualizado. | 50 |
| D-30 | Baja | `BalanceTestingSceneBuilder` rehabilita `test_balance` en Build Settings cada vez que se corre. | 53 |

## 6. Quick wins sugeridos (≤ 1 día en total, sin rediseño)

1. Guardas `#if UNITY_EDITOR || DEVELOPMENT_BUILD` en las 6 herramientas de debug. Sacar las escenas de test del build (P-01, P-02).
2. `ResetRunScratch()` en `GameManager.Awake` (S-03).
3. Save atómico con `.tmp` + `.bak`, y guardar con debounce en vez de por kill (S-02, S-01).
4. Escala del powerup de crítico, y una sola fórmula de Scavenging (E1-9, S-04). Requiere re-testear el balance.
5. Marca de "migrado" en settings de audio (D-02).
6. Resetear el boost estático y aplicarlo en `SimpleFollow` (G-01, G-02).
7. `_multiBossOverheatCycle` a un ciclo par. Escalar la vida del boss (G-04, G-05).
8. Apagar `DebugMonitor` y borrar los logs de slide (G-06, P-12).
9. Quitar `SwarmEnemyPool` de la escena (G-11).
10. `.gitignore` para `*.rar`, `*.zip` y `*.7z`, `.gitattributes` para EOL, commitear `docs/` y crear el README (P-04, P-05, P-08).

El arreglo del parser de armas (D-01) no es un quick win, porque cambia el balance de golpe: primero hay que decidir si la fuente de verdad son el CSV, el snapshot o los assets.

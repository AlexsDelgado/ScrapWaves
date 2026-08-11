# Sistema de guardado y logros (meta-progresión)

Documentación del sistema de persistencia entre runs: desbloqueo de contenido, logros y
Scrap (meta-moneda), más la ventana de "Objetivos" en el menú principal. Diseñado y
construido el 2026-08-10. No existía nada de esto antes — es un sistema nuevo, greenfield.

No confundir con objetivos **dentro** de una run (`LevelExitObjective`, Overheat): esos son
transitorios y mueren con la escena. Este sistema es meta: sobrevive a cerrar el juego.

## Resumen del modelo elegido

- **Precio/desbloqueo: híbrido (logro + Scrap).** Cada ítem desbloqueable puede tener un
  logro requerido, un precio en Scrap, ambos, o ninguno (si no tiene `Requirement`, no se
  puede comprar todavía — hay que asignarle uno). No hace falta usar las dos partes: un
  ítem puede costar solo Scrap, o requerir solo un logro (precio 0).
- **Contenido existente (armas y passive items ya en el juego): todo arranca desbloqueado
  desde el minuto 1.** El sistema solo bloquea contenido nuevo que un diseñador marque
  explícitamente como bloqueado.
- **Scrap** es la meta-moneda. Se gana al terminar cualquier run (ganada o perdida) según
  tiempo sobrevivido + bosses derrotados + materiales de crafting sobrantes.

## Arquitectura (`Assets/Scripts/Meta/`)

| Archivo | Rol |
|---|---|
| `IUnlockable.cs` | Interfaz (`UnlockId`, `UnlockedFromStart`, `Requirement`) que implementan `WeaponData` y `PassiveItemData`. |
| `UnlockRequirement.cs` | Clase serializable embebida: `RequiredAchievement` (opcional) + `ScrapPrice` (opcional, default 0). |
| `AchievementConditionType.cs` | Enum con el catálogo estandarizado de condiciones de logro (ver abajo). |
| `AchievementDefinition.cs` | ScriptableObject: un logro (id, nombre, descripción, ícono, condición, valor objetivo, recompensa en Scrap). |
| `SaveData.cs` | POCO serializable a JSON: Scrap, IDs desbloqueados, IDs de logros completados, contadores de vida. |
| `SaveManager.cs` | MonoBehaviour singleton, se auto-crea antes de la primera escena (`RuntimeInitializeOnLoadMethod`, mismo patrón que `EconomyBootstrap`), persiste con `DontDestroyOnLoad`. Guarda/carga JSON en `Application.persistentDataPath`. Expone la API pública (ver abajo). |
| `UnlockCatalog.cs` | ScriptableObject curado a mano por diseño: qué armas/ítems pasivos aparecen listados en la ventana de Objetivos. |
| `UI/ObjectivesMenuUI.cs` | Ventana modal (placeholder visual) con lista de logros + grid de compra, siguiendo el mismo patrón runtime-UI que `CraftingUI`. |

### API pública de `SaveManager`

- `bool IsUnlocked(IUnlockable item)` — true si `UnlockedFromStart` o si ya se compró.
- `bool TryPurchase(IUnlockable item)` — intenta comprar; valida logro requerido y Scrap disponible.
- `bool IsAchievementUnlocked(AchievementDefinition achievement)`
- `float GetProgress(AchievementDefinition achievement)` — valor actual del contador relevante.
- `void AddScrap(int amount)`
- `void ReportRunEnded(bool victory, int bossKills, int enemiesKilled, float survivalSeconds, int scrapEarned)` — se llama desde `GameManager.EnterEndState`.
- `void ReportWeaponLevelReached(string weaponId, int level)` — **sin call site todavía**, ver "Trabajo pendiente".
- `void ReportCustomProgress(string key, float value)` — escape hatch para logros que no entran en un contador genérico.

## Catálogo de condiciones de logro (`AchievementConditionType`)

Diseño no necesita pedir código nuevo para la mayoría de los logros: alcanza con elegir un
`ConditionType` y un `TargetValue` en el Inspector del `AchievementDefinition`.

| ConditionType | Qué mide | Alimentado por |
|---|---|---|
| `BossKillsTotal` | Bosses derrotados acumulados entre todas las runs | `GameManager.ReportRunToSaveSystem` |
| `RunsCompletedTotal` | Runs ganadas (llegar a la salida) acumuladas | ídem |
| `EnemiesKilledTotal` | Enemigos eliminados acumulados | ídem |
| `SurviveTimeSingleRun` | Mejor tiempo de supervivencia en una sola run | ídem |
| `PlayerLevelReached` | Nivel de jugador más alto alcanzado en cualquier run | `SaveManager` engancha `PlayerXP.OnLevelUp` solo al cargar una escena |
| `WeaponLevelReached` (+ `WeaponIdFilter`) | Nivel más alto alcanzado por un arma específica | **Pendiente**: falta 1 línea de código, ver abajo |
| `Custom` (+ `CustomKey`) | Cualquier condición a medida (ej. "ganar sin recibir daño") | Cualquier sistema llama `SaveManager.Instance.ReportCustomProgress(key, valor)` |

## Cómo agregar contenido nuevo desbloqueable (workflow de diseño)

1. Crear el `WeaponData`/`PassiveItemData` como siempre (`ScrapWaves/Weapon Data` o
   `ScrapWaves/Passives/Passive Item`).
2. En la sección **"Meta / Desbloqueo"** del Inspector, destildar `Unlocked From Start`.
3. Asignar un `Requirement`:
   - `Required Achievement`: arrastrar un `AchievementDefinition` (o dejarlo vacío si el
     ítem se compra libremente con Scrap).
   - `Scrap Price`: costo en Scrap (0 si solo depende del logro).
4. Agregar el asset a un `UnlockCatalog` (`ScrapWaves/Meta/Unlock Catalog`) para que aparezca
   listado en la ventana de Objetivos. En el Editor, `ObjectivesMenuUI` carga automáticamente
   `Assets/ScriptableObjects/Meta/UnlockCatalog.asset` si el campo `_catalog` quedó sin asignar
   (mismo patrón que `EconomyBootstrap`) — en un build fuera del Editor hay que asignarlo a
   mano en el Inspector, ese fallback solo corre en Editor.
5. Si el logro que gatea el ítem todavía no existe, crearlo con
   `ScrapWaves/Meta/Achievement Definition` dentro de
   `Assets/ScriptableObjects/Meta/Achievements/`. `SaveManager` también autocompleta su lista
   de logros desde esa carpeta en el Editor si quedó vacía; en un build hay que asignarla a
   mano en el prefab/bootstrap correspondiente.

No hace falta tocar `RunStartWeaponChoice`, `WeaponCraftingService` ni
`PassiveItemLevelUpHandler`: los tres ya filtran automáticamente por `SaveManager.IsUnlocked`.

> ⚠️ **Paso obligatorio y fácil de olvidar** (nos pasó en la primera pasada de este
> sistema): que un `PassiveItemData` esté en el `UnlockCatalog` (para que se vea en la
> tienda) **no alcanza** para que pueda salir como opción al subir de nivel. También hay
> que agregarlo al pool real de gameplay: el campo `_itemPool` del componente
> `PassiveItemLevelUpHandler` en `Assets/Prefabs/player.prefab`. Son dos listas
> distintas con dos propósitos distintos — catálogo = qué se muestra en la tienda,
> pool = qué puede ofrecerse en una run. Un ítem nuevo necesita estar en **ambas**.

## Fórmula de Scrap ganado por run (placeholder, a balancear)

```
Scrap = round(segundos_sobrevividos / 10) + bosses_derrotados * 25 + Σ(material_sobrante * peso)
```

Materiales comunes (Sheet Metal, Metal Pipe, Gears) pesan 1; materiales raros (Jellified
Fuel, Plastic Explosive, Wiring) pesan 5 — mismo criterio 1×/5× que ya usa el diseño para
XP común/rara. Los números exactos son un punto de partida para playtesting, no un balance
cerrado (ver `GameManager.CalculateScrapEarned`).

## Contenido placeholder creado en esta pasada

12 ítems pasivos ficticios en `Assets/ScriptableObjects/PlayerSO/Passives/Shop_*.asset`
(nombres inventados, efecto moderado en una sola stat cada uno; al menos 3 por slot —
Head/Torso/Arm/Leg) y 2 logros placeholder en `Assets/ScriptableObjects/Meta/Achievements/`.
Los 12 ítems están dados de alta tanto en `Assets/ScriptableObjects/Meta/UnlockCatalog.asset`
(para la tienda) como en el `_itemPool` de `Assets/Prefabs/player.prefab` (para que puedan
salir como opción al subir de nivel una vez desbloqueados). Son contenido de prueba para
poblar la tienda — diseño los va a reemplazar/ajustar cuando defina el arte y el balance real.

| Slot | Ítems |
|---|---|
| Head | Visor de Chatarra, Antena Improvisada, Casco Reforzado |
| Torso | Núcleo Recalentado (logro), Placa Anti-Impacto, Núcleo de Combate |
| Arm | Garra Reciclada, Guantelete Magnético, Brazo Hidráulico |
| Leg | Botas a Reacción (logro), Resortes Reciclados, Piernas de Repuesto |

## Botón "Reiniciar progreso" (herramienta de demo/QA)

La ventana de Objetivos tiene un botón rojo "Reiniciar progreso" al lado de "Cerrar".
Requiere tocarlo dos veces (arma la confirmación, el texto cambia a "¿Seguro? Tocá de
nuevo", y recién el segundo click ejecuta) para evitar un reset accidental. Llama a
`SaveManager.ResetProgress()`, que vuelve `SaveData` a un estado nuevo (Scrap en 0, todos
los desbloqueos e IDs de logros borrados, contadores en 0) y reescribe el archivo de save.
No hay riesgo de dejar contenido inaccesible: todo lo que tiene `UnlockedFromStart = true`
sigue disponible igual, solo se pierde lo comprado/desbloqueado con Scrap o logros. Pensado
para poder mostrar el flujo de desbloqueo desde cero en una demo sin reinstalar el juego.

## Nota: botones de testing ocultos para build

`TitleScreenController` tiene un nuevo campo `_includeTestingButtons` (default `false`) que
oculta "Weapon Sandbox" y "Enemies Testing" del menú principal sin borrar la funcionalidad —
solo `SetActive(false)` sobre los botones ya creados/cacheados. Para volver a mostrarlos en
una build de QA, tildar ese campo en el Inspector del `TitleScreenController` de la escena.

## Trabajo pendiente / limitaciones conocidas

- **Escena `TitleScreen.unity` sin regenerar.** El botón "Objetivos" ya está soportado en
  `TitleScreenController` y en `TitleScreenSceneBuilder`, pero la escena guardada en disco
  todavía tiene solo los 3 botones originales (Play/Weapon Sandbox/Enemies Testing). Hay que
  correr **`Tools > Scenes > Rebuild Title Screen`** en el Editor de Unity para regenerarla
  con el botón nuevo. Hasta entonces, el botón "Objetivos" solo aparece en escenas donde
  `TitleScreenController` cae en su fallback de construcción runtime (cuando no encuentra los
  3 botones originales ya armados a mano).
  - Al rebuildear, el test `TitleScreenScene_HasEditableCanvasWithControllerAndButtons`
    (`Assets/Tests/Editor/TitleScreenControllerTests.cs`) va a necesitar que se agregue
    `"Objetivos"` a su lista de labels esperados — no se tocó todavía a propósito, para no
    dejar un test roto contra la escena actual sin regenerar.
- **`WeaponLevelReached` sin call site.** El método existe en `SaveManager` pero nadie lo
  llama todavía. Agregar `SaveManager.Instance?.ReportWeaponLevelReached(weapon.WeaponId, instance.Level);`
  donde `WeaponManager.UpgradeWeapon` sube de nivel un arma.
- **`UnlockCatalog` y el `_achievementCatalog` del `SaveManager` deben asignarse a mano en la
  escena** (arrastrar los assets en el Inspector) — no se auto-descubren.
- **La UI de `ObjectivesMenuUI` es un placeholder visual**, igual que el resto de la UI del
  juego (mismo patrón runtime que `CraftingUI`/`PauseMenuUI`). El diseño final de esta
  pantalla queda pendiente de un pase de UI/UX aparte.

# Progresión, crafting y meta — alpha 22-09-2026

Actualiza `DumpsterFire-DEV-Player_Progression.docx` (21-7). El reparto de decisiones de aquel documento sigue siendo el que corre. Desde entonces se sumaron meta, logros, iconos y herramientas de dev. La matemática de stats está en `referencia-codigo/DumpsterFire-StatSystem.docx`, no en el StatSystem de julio.

## Reparto

| Decisión | Dónde |
|---|---|
| Crecimiento de fondo | Cada level-up, ruleta automática de stats. Sin elección |
| Build | Cada level-up, 2–3 pasivos (equipar o subir uno que ya está) |
| Loadout de armas | Inicio de run (una de dos) y estación de crafting. El level-up no da armas |

`WeaponLevelUpHandler` sigue en el proyecto y no lo llama `LevelUpOrchestrator`. `UpgradeManager` está obsoleto.

## XP

`PlayerXP`. Tope de nivel 36. La XP entra al recoger un material (`MaterialCatalog.GetPickupXpValue`: el DEV del 21-7 fija común = 4 y raro = 12). No hay orbes.

Un pickup puede subir varios niveles. El orquestador los encola y resuelve uno por uno: primero la carta de pasivos, después las stats automáticas y su feedback. La UI no es dueña del estado.

El drop de material depende de la tirada de Scavenging. Al inicio de la run, buena parte de los kills no dan XP. Eso es intencional y hay que validarlo contra el tiempo-a-nivel deseado.

`NormalizedProgressToNextLevel` alimenta la barra del HUD (`PlayerBarsHud`).

## Stats automáticas

`PlayerStatsLevelUpHandler`. Solo entran definiciones con `UpgradeableByLevel`.

Pesos: cada stat elegible arranca en 5. La elegida baja (mínimo 1). Las que no salieron en ese nivel suben 1. El monto escala con el nivel y un factor aleatorio corto, como describe el DEV del 21-7.

`MaxHealth` también empuja la vida actual vía `PlayerHealth`.

La fórmula de valor final no es “base + todos los bonus sumados”. Es:

```
(base + suma de aditivos) × producto de multiplicadores
```

Un multiplicador de 1 es neutro. Los porcentajes de probabilidad (crítico) van de 0 a 1. Meter un 20 pensando “20 %” satura el stat. El documento de armas marca un defecto vivo: la definición actual de `CriticalChance` suma del orden de 1.0 por level-up, y el powerup de daño extra suma 50–100, mientras el resolver clampéa a 0–1. Cualquiera de las dos fuentes deja el crítico en 100 %. Hay que tratarlo como bug de datos, no como tuning.

Desde el 22-09 la pausa, en builds de editor y development, tiene `StatAttributionPanel`: de qué fuente sale cada stat, y un registro del último golpe (`LastHitRecorder`).

## Pasivos

`PassiveItemManager`, `PassiveItemInventory`, `PassiveItemData`, `PassiveItemRoulette`.

Slots del DEV del 21-7, que el código mantiene:

| Slot | Capacidad |
|---|---|
| Head | 1 |
| Torso / Core | 1 |
| Arm | 2 |
| Leg | 2 |

Hasta 6 pasivos en la run, cada uno subible varias veces. Los modificadores usan la instancia como `SourceReference`, así que se pueden sacar al reemplazar. Al cambiar un pasivo se refrescan vida y recursos de movimiento que dependan de esas stats.

El HUD de loadout es `PassiveLoadoutHud`. Los iconos grandes de pasivos se cablearon el 21-09.

Hay una herramienta de sandbox: `PassiveItemTestingController`.

## Economía de materiales y crafting

`EnemyMaterialDrop` tira según `MaterialDropConfig` y el rol del material en el catálogo. El jugador los recoge a `MaterialInventory`.

`WeaponCraftingService` en `CraftingStation`, UI `CraftingUI` (pausa el gameplay):

- Mejorar el nivel del arma con materiales.
- Tinker: ofrece dos armas distintas, desbloqueadas y no equipadas. El jugador selecciona una y confirma la compra; la otra queda excluida del Tinkering durante esa run. La oferta se conserva al cerrar/reabrir. Con cinco armas y una inicial, la segunda compra ofrece las dos que no aparecieron en la primera. La elección inicial no descarta armas. Se mantienen costos y tope de 3 slots; sin dos opciones elegibles no se cobra ni se permite comprar.
- En nivel 6, Advanced Tinkering: el jugador elige uno de dos paths. A partir de ahí las filas de path mandan daño, cadencia y, si viene, munición manual.

Los costes salen de `MaterialUsageBalance` / CSV. Menú de editor: `ScrapWaves/Balance/Import All CSV`. Si el CSV y un markdown de diseño no coinciden, manda el CSV (`BALANCE_NOTES.md`).

`RunStartWeaponChoice` ofrece un arma al empezar y, si está activo, `WeaponManager` no equipa las starting weapons del inspector. El prefab de jugador de producción serializa la lista de armas inicial vacía: la escena o el bootstrap tienen que inyectar pool y elección.

## Meta, fuera de la run

`SaveManager` persiste JSON: scrap, desbloqueos, upgrades de meta (0–10 en un conjunto de stats) y progreso de logros (`Resources/Meta/Achievements`).

`MetaProgressionApplier` aplica los multiplicadores de meta al arrancar la run.

`ChallengeProgressTracker` acumula métricas entre runs (daño, golpe máximo, etc.) para los logros.

`ObjectivesMenuUI` en el menú principal agrupa Objectives, Unlocks y Upgrades. Los iconos de logros y de armas (locked / selected) se cablearon el 21-09. Los textos de logros pasaron a inglés el 22-09.

En builds de desarrollo el mismo menú puede resetear el progreso y poner los upgrades de meta al máximo.

`AchievementUnlockToast` muestra el desbloqueo.

## Feedback de level-up

`LevelUpChoiceUI` para la carta. `LevelUpStatFeedback` para las stats que cayeron solas. Ninguno de los dos escribe el stat: leen el resultado que ya aplicó el handler.

# Playtest "wazel" — avance, cómo probarlo y pendientes

Rama: `features/wazel` (sale de `main`). Fecha: 2026-11-18.
Origen: lista de 8 puntos de feedback del playtest. Todo lo de abajo está commiteado y pusheado, **pero nada se verificó visualmente en el Editor ni en un build**: solo compila y pasa tests de EditMode. Lo primero que hay que hacer es probarlo a mano con la guía de esta página.

## 1. Qué se hizo

| # | Punto del playtest | Estado | Qué cambió |
|---|---|---|---|
| 1 | Efectos visuales de items en el piso | Hecho, sin ver en pantalla | Power-ups y materiales **raros** (Jellified Fuel, Plastic Explosive, Wiring) tienen contorno fresnel + anillo en el suelo del color de su tipo, con pulso. Shader nuevo `ScrapWaves/GameFeel/Pickup Highlight` (`Assets/GameFeel/Shaders/PickupHighlight.shader`), materiales `PickupRim`/`PickupRing` en `Resources/GameFeel`, script `PickupHighlight.cs`. El pulso se congela con "efectos estáticos" (`HudVfxAccessibility.StaticEffects`). |
| 1b | (bug detectado) power-ups del piso en builds | Corregido en código, sin confirmar en build | `TemporaryPowerupPool` cargaba los prefabs solo vía `AssetDatabase` (Editor). Ahora los toma de `TemporaryPowerupVisualCatalog` (en `Resources`), que tiene un campo `Prefab` por entrada. |
| 2 | Hover en la mesa de crafteo: quién dropea cada material | Hecho, sin ver en pantalla | Tooltip "Dropped by: …" al pasar el mouse por un material (balance y costos). Solo mouse. Textos generados desde los `MaterialDropConfig`. |
| 3a | Descripciones de armas e items | Hecho, **textos a revisar** | Campo `Description` en `WeaponData`, `PassiveItemData` y `WeaponPathUnlockData`. Inglés; armas = comportamiento + especialidad + habilidad activa. |
| 3b | Sin confirmación de compra | Hecho | Un solo click compra en Unlocks y en Upgrades. |
| 3c | Layout de Unlocks | **Parcial** | Hecho: items base (los que arrancan desbloqueados) ocultos; panel de detalle reordenado con la descripción. Falta: sub-tabs WEAPONS / ITEMS / PATHS. |
| 4 | Cartel de overheat más llamativo | Hecho, sin ver en pantalla | Estilo peligro amarillo/negro, texto negro, franjas diagonales arriba y abajo que pulsan. Aplicado a `GameplayScene` con un script de editor. |
| 5 | Partículas de overheat en el mundo | **No empezado** | Ver pendientes. |
| 6 | Explosión del cohete más dinámica | Hecho, sin ver en pantalla | Nace pequeña (12 % del radio) y llega al radio completo en el primer 20 % de su vida, con salida rápida. Nunca supera el radio de daño. El mortero no se tocó (ya animaba con su curva de escala). |
| 7 | Comunicar mejor los objetivos | **Parcial** | Hecho (7a): textos unificados en inglés que nombran el loop completo. Falta (7b/7c): panel de objetivos e indicador de baterías. |
| 8 | Indicador de upgrade que tapaba el arma | Hecho, sin ver en pantalla | En el slot **seleccionado** ya no hay borde pulsante, solo la marca de esquina; los demás slots conservan el borde. |

Decisiones del usuario que sostienen lo anterior: pickups con anillo + fresnel (también raros); tooltip solo "de dónde sale" y sin soporte de mando todavía; descripciones en inglés; compra sin doble click (y rework completo del menú de unlocks más adelante); items base ocultos por defecto; cartel a criterio (amarillo y negro).

## 2. Cómo probarlo

Abrí la rama, dejá que Unity reimporte y probá cada punto.

### Pickups con aura (1, 1b)
1. Jugá una run y matá enemigos hasta que caiga un power-up y un material raro.
2. Esperado: contorno fresnel + anillo en el piso del color del tipo, con pulso suave. Los materiales **comunes** no deben tener aura.
3. Con "efectos estáticos" activado el pulso no debe animarse.
4. Mirá el costo en GPU con varios materiales en el piso (Profiler): no se midió.
5. En un build: confirmar que los power-ups del piso aparecen (era el bug 1b).

### Tooltip del crafteo (2)
1. Abrí la mesa de crafteo y pasá el mouse por cada material de la barra de balance y de los costos.
2. Esperado: panel debajo del material, p. ej. `Sheet Metal / Dropped by: Junk Slime, Hellfire Slime`; los raros llevan "(rare)". Al salir del material el panel desaparece.
3. Si cambian las tablas de drops, regenerar con el menú `ScrapWaves/UI/Author Crafting Material Tooltip`.

### Menú de Unlocks (3a, 3b, 3c)
1. Title screen → Objectives/Unlocks.
2. Esperado: no aparecen armas/pasivos que arrancan desbloqueados; al seleccionar una tarjeta el detalle muestra nombre, tipo, estado, **descripción**, requisito y botón.
3. Con scrap suficiente, un click en PURCHASE compra (mensaje "PURCHASE COMPLETE"). Lo mismo en Upgrades.
4. **Revisar el layout**: las anclas del panel se calcularon a ojo; una descripción larga podría cortarse.
5. Revisar los textos en `Assets/Scripts/Editor/UnlockDescriptionAuthoring.cs`. Para reaplicar tras editarlos: menú `ScrapWaves/UI/Author Unlock Descriptions` (idempotente; no toca el layout si ya está autorado).

### Cartel de overheat (4)
1. Provocá el overheat en la gameplay scene.
2. Esperado: banner amarillo/negro con franjas que pulsan. Si no gusta, ajustar con `ScrapWaves/UI/Restyle Overheat Warning (Hazard)` (ver `OverheatPreparationAuthoring.cs`).

### Explosión del cohete (6)
1. Disparar el Rocket Launcher contra enemigos y contra el piso.
2. Esperado: la explosión nace chica y se expande rápido hasta el radio completo, sin pasarse del radio de daño. Constantes en `RocketLauncherCueVfx.cs`: `BlastStartFraction` y `BlastExpandFraction`.

### Indicador de mejora en crafteo (8)
1. En la mesa de crafteo con un arma mejorable seleccionada.
2. Esperado: el marco de selección se ve completo; el slot seleccionado muestra solo la marca de esquina y los no seleccionados el borde pulsante.

### Objetivos (7a)
1. Jugá una run desde el inicio y leé los carteles de nivel, boss y baterías.
2. Esperado: el texto de descubrimiento dice "Escape: trigger Overheat, defeat the boss, collect batteries" y los carteles de boss y baterías explican el siguiente paso.

### Tests automáticos
Con el Editor cerrado (los tests ensucian `ProjectSettings`; revertirlo después):

```powershell
& "D:/unity installs/6000.3.13f1/Editor/Unity.exe" -batchmode -nographics -projectPath D:/Projects/ScrapWaves -runTests -testPlatform EditMode -testFilter "MaterialSourceTooltipTests;ObjectivesMenuUIPresenterTests;TitleScreenScreenStackTests;CraftingAvailabilityFeedbackTests;RocketLauncherPresentationTests;LevelExitHudTests" -testResults results.xml -logFile test.log
```

Resultado conocido al cerrar esta etapa:
- Los tests de arriba pasan, **salvo** `AuthoredRocketExplosion_EmitsImpactDamageAndKineticStatusAtDetonation` (NullReference dentro de `Projectile.ApplyExplosionDamage`; no se tocó `Projectile` y no se comprobó si ya fallaba en `main`).
- 4 tests de `RunMenuUiTests` (los `Tinker_*`) fallan: esperan que el costo del slot sea 15 de Sheet Metal y se cobra 10. No se tocaron costos ni lógica de tinkering; falta confirmar si ya fallaban en `main`.

## 3. Pendientes

### Para el próximo bloque de trabajo
1. **Verificación visual** de todo lo marcado "sin ver en pantalla" (aura, banner, explosión, panel de Unlocks, tooltip) y un build de prueba.
2. **Sub-tabs WEAPONS / ITEMS / PATHS** en Unlocks (3c). Requiere re-autorar botones y agrupar tarjetas en `TitleScreen`. Probablemente conviene hacerlo junto con el rework completo del menú que se quiere a futuro.
3. **Objetivos, parte 2 (7b/7c)**: el juego todavía no guía el loop *matar enemigos → Overheat → matar bosses → baterías → puerta*. Falta un panel de objetivos y un indicador de baterías que lo expliquen paso a paso. Hay mucho más que no se explica o demuestra (el propio playtester lo notó).
4. **Partículas de overheat en el mundo (5)**: no empezado; medir el costo en GPU antes de dejarlo fijo.
5. **Triage de los tests rotos** (el de `Projectile` y los 4 de tinkering): comparar contra `main`.

### Opcionales
- Brillo/glint para materiales **comunes** (hoy solo los raros tienen aura).
- Extender el indicador de upgrade a un badge/chip también en los slots del HUD de armas.
- Soporte de mando para el tooltip del crafteo.
- Código sin uso que quedó de la compra con confirmación: campos `_armedPurchase` / `IsPurchaseArmed` en `ObjectivesMenuUI` y el helper `ArmObjectivePurchase` en `TitleScreenScreenStackTests`. Se pueden limpiar.
- Tooltip de crafteo: conviene revisar si un material nuevo necesita también su fila de hover (se regenera con el menú, pero la fila se agrega en `CraftingReadoutAuthoring`).

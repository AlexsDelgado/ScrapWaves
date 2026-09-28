# Backlog hacia el playtest — 28-09-2026

Backlog de las 7 mejoras pedidas, repartido por rol y estimado en **días ideales**. La rama de referencia es `main`, commit `53ef237`, más el merge a `Map_V2` (`8852862`).

El hito que ordena las prioridades es un **playtest con jugadores**. Por eso manda lo que arruina la primera impresión: que el combate se rompa, que el jugador no entienda lo que ve, y que no sepa qué hacer.

**Equipo:** 2 game designers · 2 artistas 3D · 1 QA · 1 programador.

---

## 1. Tres hallazgos que reescriben el pedido

Al revisar el código, tres de los siete ítems resultaron ser algo distinto de lo que se pidió. Conviene leer esto antes de asignar.

### 1.1 La niebla no es niebla

La niebla de Unity está **apagada** (`Assets/Scenes/GameplayScene.unity:17`, `m_Fog: 0`). Lo que ven los jugadores es un **quad plano horizontal**:

| Archivo | Qué es |
|---|---|
| `Assets/Prefabs/Level/BelowLevelFogPlane.prefab` | El plano, una única instancia en la escena |
| `Assets/GameFeel/Shaders/BelowLevelFog.shader` | Unlit, 100 líneas, `Blend SrcAlpha OneMinusSrcAlpha` |
| `Assets/GameFeel/Materials/BelowLevelFog.mat` | `_BaseColor: (0.45, 0.52, 0.6, 0.92)` |

Cada propiedad apunta a "superficie líquida", no a bruma:

| Propiedad | Valor | Cómo se lee |
|---|---|---|
| Geometría | Quad plano con normal hacia arriba | Superficie, no volumen |
| Alpha | **0.92**, casi opaco | Una lámina sólida |
| Color | Azul-gris `(0.45, 0.52, 0.6)` | Color de agua |
| Animación | Ruido sobre `positionWS.xz`, scroll `_Time.y * 0.05` | Oleaje |
| Borde | `smoothstep` 0.15 | Se desvanece como un charco |

**Los jugadores no se confundieron: describieron bien lo que ven.** El shader muestrea ruido sólo en XZ — no tiene profundidad, ni gradiente vertical, ni reacción a la distancia de cámara.

Además el plano quedó **desactivado por accidente** en `Map_V2` (`m_IsActive: 0`). Hay que reactivarlo.

### 1.2 El spawn en el aire tiene causa raíz identificada

`Assets/Scripts/Spawning/OrbitalSpawnPlacement.cs:153-159` sale **sin hacer el raycast al suelo** cuando el prefab no tiene `CharacterController` habilitado:

```csharp
CharacterController cc = instance.GetComponent<CharacterController>();
if (cc == null || !cc.enabled)
{
    root.SetPositionAndRotation(desiredPosition, Quaternion.identity);
    return true;          // <-- sale sin tocar el suelo
}
```

Y **los 8 prefabs de enemigo que la ruleta spawnea tienen `m_Enabled: 0`** en su `CharacterController`. Es decir: el raycast **nunca se ejecuta**. Todos aterrizan a `player.position.y`. En un mapa multi-piso, cada vez que el jugador está arriba, el anillo de spawn cae sobre vacío.

Corolario: toda la parametrización de suelo de la escena está muerta — `_groundRaycastMask`, `_raycastStartHeight: 48`, `_maxAbsSpawnSurfaceDeltaY: 3.5`.

Agravante de escena: hay **395 objetos en layer `Default` y sólo 7 en `Terrain`**, y el spawn filtra por `Terrain`. Aunque se arregle el código, sin sanear layers el raycast va a elegir mal el piso.

Los drones **no** son el problema: `FlyingRangedBehavior` y `BomberDroneBehavior` se auto-corrigen con `MaintainHover()` en ~0.2 s. El bug visible es de los terrestres.

### 1.3 El proyectil del drone no es un cambio de referencia

Son dos jerarquías sin nada en común:

| | Jugador | Enemigo |
|---|---|---|
| Clase | `Weapon/Projectiles/Projectile.cs` (**1258** líneas) | `Enemy/Behaviors/EnemyProjectile.cs` (**118**) |
| Pool | `ProjectilePool.cs` (628) | `EnemyProjectilePool.cs` (217) |
| A quién daña | `IDamageable` | `PlayerHealth` |
| Explosión / AoE | Sí | No |

Y `Projectile.IsIgnoredCollision` (`:544-556`) **ignora al jugador por diseño**: un `Projectile` disparado por un drone lo atravesaría sin hacer daño. Asignar el prefab directo falla en seco, porque `EnemyProjectilePool` exige el componente `EnemyProjectile`.

El camino realista es extender `EnemyProjectile` con explosión y reusar el **visual** del cohete, no su clase.

---

## 2. Decisión bloqueante: dirección de arte

Hay **dos spikes sin mergear que apuntan a lugares distintos**. Hasta elegir uno, cualquier estimación de arte es inventada.

| Rama | Qué trae | Efecto |
|---|---|---|
| `spike/psx-vntg-style` | Look PSX quantizado. 302 archivos, Shader Graphs, 8 materiales preset, texturas CC0. **Ya trae fog por profundidad con ruido, tuneada en 3 perfiles** | Niebla y estilo pasan a ser **una sola tarea**. Su shader no expone normal ni roughness todavía |
| `texture-testing` | Paleta de chatarra PBR: 10 materiales `GB_*`, texturas generadas, `GrayboxWorldTiling.cs` | Más cerca del look actual. La niebla queda aparte |

Estado hoy: de **91 texturas, 62 son iconos de UI**. Hay **cero texturas de entorno**. El mapa nuevo está en graybox literal — 29 referencias al `Default-Material` de Unity.

---

## 3. Backlog por rol

**Dep.** = de qué depende para poder arrancar. **PT** = prioridad para el playtest.

### Programación — 1 persona · ~14 días

| # | Tarea | Días | Dep. | PT |
|---|---|---:|---|---|
| **P1** | Fix spawn en el aire: quitar el early-out por `CharacterController` y resolver suelo con el `Collider`/`Bounds` del prefab | 1.0 | — | **P0** |
| P1b | Flag `IsFlying` y bypass del grounding para drones | 0.5 | P1 | P0 |
| P1c | Endurecer `SpawnGroundUtility.cs:110-124`, que hoy acepta el piso equivocado antes que fallar | 0.25 | P1 | P0 |
| P1d | Borrar `SwarmSpawner.cs` (`[Obsolete]`, deshabilitado, duplica el mismo bug) | 0.1 | — | P2 |
| **P6** | Niebla: alpha 0.92 → ~0.4, desaturar a gris-marrón, `_NoiseStrength` 0.2 → ~0.6, gradiente vertical, **encender `m_Fog`**, reactivar el plano en `Map_V2` | 0.5 | A0 | **P0** |
| **P2** | Prompt `[E]` genérico: `IInteractable` + `InteractionPrompt`, unificar el E duplicado de `CraftingStation.cs:66-73` y `ExitDoor.cs:174-181` contra la acción `Interact` que **ya existe sin usar** | 1.5 | A2 | **P0** |
| **P4** | Mostrar % / valores finales: conectar la UI de jugador a `StatDisplayFormat` y agregarle `FormatBonus()` | 2.0 | G1 | P1 |
| **P5** | Proyectil de drone estilo cohete: explosión + falloff en `EnemyProjectile`, `EnemyRocket.prefab` reusando el visual del cohete | 1.5 | — | P1 |
| **P7** | AudioMixer con buses Master/BGM/SFX/UI, rutear AudioSources, unificar los 3 caminos de volumen, pausar audio con `timeScale = 0` | 1.5 | — | P1 |
| P8 | Arreglar clips rotos: `_enemyHit` y `_enemyDeath` vacíos, `TryPlayShoot` inalcanzable | 0.5 | P7 | P1 |
| **P3** | Sistema de diálogo del boss: cola, retratos, tipeo, skip, autorable en ScriptableObject | 4.0 | G2 | P1 |
| P9 | Strings de UI al inglés: `LevelExitHud.cs`, `WeaponLevelUpHandler.cs` | 0.5 | — | P2 |

**Reusar:** `HudUiFactory` para UI consistente · `AchievementUnlockToast` o los `_messageSlots` de `LevelUpStatFeedback` como base de mensajes · `Arrow_Guide.prefab` + `GuideArrow.cs` como patrón del prompt 3D diegético (mesh, no canvas).

### Arte 3D — 2 personas

| # | Tarea | Días | Dep. | PT |
|---|---|---:|---|---|
| **A0** | **Decidir dirección de arte** con los GD y el programador. Bloquea A3, A4 y la opción volumétrica | 2.0 | — | **P0** |
| **A1** | Sanear layers del mapa: geometría caminable a `Terrain` | 0.5 | — | **P0** |
| **A2** | Asset 3D del prompt de interacción (tecla E flotante, billboard) | 1.0 | — | **P0** |
| A3 | Materiales y texturas del mapa nuevo | 5–8 | A0 | P1 |
| A4 | Props y dressing de entorno | 5+ | A0 | P2 |

### Game design — 2 personas

| # | Tarea | Días | Dep. | PT |
|---|---|---:|---|---|
| **G4** | Qué se le comunica al jugador y cuándo: objetivos, mecánicas, guion de los prompts | 1.0 | — | **P0** |
| **G1** | Tabla de política de display por `StatType` (ver §4) | 1.0 | — | P1 |
| **G2** | Guion de diálogos del boss + mockup de la pantalla | 2.0 | — | P1 |
| **G3** | Priorizar la lista de sonidos faltantes (§5) y escribir briefs | 1.5 | — | P1 |
| G5 | Balance de niebla: densidad y alcance para la lectura del mapa | 0.5 | P6 | P1 |

### QA — 1 persona

| # | Tarea | Días | Dep. |
|---|---|---:|---|
| **Q1** | Plan de test del playtest + guion de observación | 1.0 | G4 |
| **Q2** | Verificar spawn en **todos los pisos**, con los 6 tipos de enemigo | 1.0 | P1, A1 |
| Q3 | Regresión de las 5 armas tras P5 y P7 | 1.0 | P5, P7 |
| Q4 | Legibilidad de stats tras P4 | 0.5 | P4 |

---

## 4. Por qué el % de los stats no se puede automatizar

`StatDisplayFormat` **ya existe** (`Assets/Scripts/Player/PlayerStats/StatDisplayFormat.cs`) pero **ninguna UI de jugador lo usa** — sólo el panel dev y el inspector. El `x1.5` que ve el jugador sale de `PassiveItemUiText.cs:48` y de literales hardcodeados en `MetaUpgradeShopUI.cs:461,495`.

El problema no es técnico, es de diseño: con sólo el `ModifierType` **no alcanza** para derivar el porcentaje. Hay 4 casos distintos entre los 17 pasivos:

| Caso | Ejemplo | ¿% derivable? |
|---|---|---|
| Multiplicativo | CQB module, 1.2 → `(v-1)*100` | **Sí**, +20% |
| Aditivo sobre base 1 | Belt-driven cartridges, +0.15 | **Sí**, +15% |
| Aditivo sobre base 0, fracción | Advanced targeting, CriticalChance +0.05 | **Sí**, pero son **puntos** porcentuales |
| Aditivo sobre base ≠ 0 y ≠ 1 | Bounty-hunter, +0.2 sobre base **1.25** | **Ambiguo**: ¿+16% o +20 puntos? |
| Enteros / segundos | Heel charges (AirJumps), Electromagnetic shield | **No aplica %**: "+1 salto aéreo", "+8 s" |

Por eso G1 es una decisión de diseño, no de código: hay que fijar **qué se muestra para cada `StatType`**.

Ojo con dos datos raros al hacerlo: `DoubleDrop.asset` tiene `BaseValue: -30` y `Scavenging.asset` tiene `50`, ambos en puntos porcentuales crudos.

---

## 5. Sonidos que faltan

Estado actual: de **~41 eventos sonoros del juego, sólo 7 tienen audio**, y **3 de esos están rotos**. **No existe ningún AudioMixer** en el proyecto. **4 de las 5 armas están mudas**, y el AutomaticCannon usa **el mismo `shoot.wav` para sus 8 cues distintos**.

Lo que ayuda: la arquitectura ya está. `WeaponPresentationCueData` soporta listas de clips, layering y variación de pitch por calor, con pool de 16 voces 3D. **Falta el contenido, no el código.**

### Combate — 13

Impacto a enemigo \*, muerte de enemigo \*, punto débil golpeado, punto débil destruido, crítico, **boss derrotado**, aparición de boss, **muerte del jugador**, escudo roto/ganado, y las 4 armas mudas (Flamethrower, Mortar, RocketLauncher, RotatingBlade).

### Movimiento — 11, ninguno tiene audio

Salto, doble salto, aterrizaje, **pasos (no existen)**, crouch in/out, slide in/out, dash in/out, recarga de dash, aturdimiento.

### Economía y progresión — 10

**Pickup de material**, **crafteo**, scrap ganado/gastado, **logro desbloqueado**, pasiva adquirida, **powerup recogido** (6 tipos), stat subida, recogida de XP.

### Nivel y objetivo — 9

**Puerta de salida desbloqueada**, carga de puerta (start/progress/ready), **llave recogida**, **todas las llaves**, subida de tensión, oleada élite, capa dinámica de overheat (`_bgmOverheatLayer` vacío con el AudioSource ya cableado), compactor door, launch pad.

### UI en gameplay — 4

Hover de botón, abrir/cerrar pausa, cerrar objetivos.

### Ambiente — 0

No existe nada: ni loop de nivel, ni viento, ni maquinaria, ni props posicionales.

\* `_enemyHit` y `_enemyDeath` tienen el caller escrito pero el clip **vacío** en la escena. El tercero roto es `TryPlayShoot`, inalcanzable porque `PlayerAutoAttack` está deshabilitado en `player.prefab:619`.

### Limpieza barata

Borrar los **6 `.m4a` sin referenciar** de `Assets/Audio/BGM/` · organizar los 9 SFX sueltos en subcarpetas · renombrar `010.wav` · revisar los 10–30 s de silencio entre pistas de BGM (`_bgmGapMinSeconds` / `_bgmGapMaxSeconds`), que en una run corta es mucho tiempo sin música.

---

## 6. Sobre fog volumétrica

**URP 17.3 no trae fog volumétrica nativa** — eso es HDRP. Las opciones son adoptar el pass del spike PSX (ya escrito y tuneado) o escribir un `ScriptableRendererFeature` propio con raymarch. Hoy el proyecto **no tiene ninguna render feature custom**; la única es el SSAO stock de URP.

El presupuesto de GPU está: target **PC-only** (único build profile, StandaloneWindows64), Forward+, MSAA off, render scale 1, y **`_RequireDepthTexture` y `_RequireOpaqueTexture` ya están en ON** en `PC_RPAsset.asset:21-22` — que es el prerequisito caro de cualquier fog volumétrica.

Lo que **no** está medido: el único dato del spike es *"0.42 ms average sampled editor update"* en batch mode, y su propio documento lo descalifica explícitamente como *"not a player-build GPU benchmark"*.

**Recomendación: no presupuestar volumétrica todavía.** Hacer P6 (medio día) y ponerlo delante de jugadores. El problema no es que falte volumetría — es que un plano casi opaco con scroll horizontal se lee como agua. Si después del playtest siguen pidiendo profundidad, ahí se justifica medir un raymarch a media resolución.

---

## 7. Hueco de rol: no hay artista 2D

Estas tareas **no tienen dueño** con el equipo actual:

- Retratos del boss para el sistema de diálogo (P3)
- Iconos y marco de la ventana de diálogo
- Cualquier icono de UI nuevo

Opciones: tercerizar · que un artista 3D los resuelva con renders del modelo del boss · ir a placeholder para el playtest y reemplazar después.

**Conviene decidirlo antes de arrancar P3**: el sistema se puede construir con placeholders, pero el playtest los va a mostrar.

---

## 8. Riesgo operativo con el equipo creciendo

`BalanceAutoImporter` y `EconomyBootstrap` **reescriben assets de balance solos** en cada Play. Con 6 personas tocando el repo, hay que **revisar `git status` antes de cada commit** o se van a commitear reversiones del balance sin querer.

Está anotado como deuda Alta en `docs/_trabajo/etapa2/`. Vale la pena arreglarlo antes de que el equipo crezca: es media jornada y evita una clase entera de conflictos.

---

## 9. Verificación

| Tarea | Cómo se comprueba |
|---|---|
| P1 | Entrar a `GameplayScene` en **cada piso** del mapa nuevo: ningún enemigo flotando. Verificar que `EnemyVerticalEngagement` deje de dormir enemigos por spawns mal ubicados. Los 6 tipos de la ruleta, forzando a mano los de peso 0 |
| P6 | Capturas antes/después desde 3 puntos del mapa, y preguntar a 3 personas que no vieron el juego qué creen que es |
| P2 / P3 | Un jugador nuevo llega a la crafting station y craftea **sin que se le explique por fuera del juego** |
| P4 | Ningún `x1.5` visible en UI de jugador |
| P5 / P7 | Regresión de las 5 armas: el pipeline de daño no se toca, pero el pool y el audio sí |
| P4 | Los 26 tests EditMode de atribución de stats siguen verdes (`StatAttributionTests`, `StatDisplayFormatTests`, `WeaponDamageAttributionTests`) |

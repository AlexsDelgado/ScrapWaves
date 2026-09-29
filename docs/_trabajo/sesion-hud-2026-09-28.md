# Sesión HUD — 28 sep 2026

Traspaso para seguir en otra PC. Rama `Map_V2`. Repo: `https://github.com/AlexsDelgado/ScrapWaves.git`.

Unity `6000.3.13f1`. Blender `D:\3D\Blender 5\blender.exe` (5.2.1 LTS), addon MCP conectado. Responder en español. Estilo de los iconos: chatarra, hierro, remaches, mostaza, sin sombras duras en los iconos de items.

## Qué ya está en origin/Map_V2

- `4631f05` — iconos de todos los pasivos (piernas, brazos, cabeza, pecho), variantes `_selected` y `_locked`. Scripts en `ArtSource/<Slot>/scripts/`, blends en `ArtSource/<Slot>/`, PNG en `Assets/Art/UI/Icons/`.
- `1bf5a5d` — HUD de gameplay en tres placas modeladas en Blender, aplicadas al prefab `Assets/Prefabs/UI/GameplayHud V2.prefab`.

## Qué NO está commiteado (hay que copiarlo o commitear antes de cambiar de PC)

Working tree sucio respecto de `origin/Map_V2`:

- `ArtSource/Tools/blender_hud_rig.py` — luz rasante + oclusión ambiental (EEVEE `use_fast_gi`, método `AMBIENT_OCCLUSION_ONLY`). Metales del HUD más especulares.
- `ArtSource/Tools/finish_hud.py` — contorno de 6px a 3px; grade `(1.0, 1.22)`.
- Renders y sprites: `ArtSource/UI/renders/Hud*.png`, `ArtSource/UI/HUD.blend`, `Assets/Art/UI/HUD/Hud{Left,Center,Right,Badge}.png`.
- `Assets/Scripts/Editor/HudArtApplier.cs` — los dashes ya no van bajo la retícula.
- `Assets/Scripts/Weapon/UI/WeaponClusterHud.cs` — tooltip del layout de dashes.
- `Assets/Prefabs/UI/GameplayHud V2.prefab` — arte reaplicado con esos cambios.

No commitear: `.cursor/mcp.json`, `Assets/Temp/`, `Library/`, `Temp/`, `HUD.blend1`.

## Cómo está armado el HUD

Tres piezas, plano XZ, cámara ortográfica mirando desde -Y. 1 unidad = 1000 px. Ventanas (fills y slots) en `ArtSource/UI/HudLayout.json`, coordenadas en px desde arriba-izquierda. Escala en Unity: `0.45` (`HudArtApplier.Scale`).

| Pieza | Contenido | Scripts que la leen |
| --- | --- | --- |
| Izquierda | Dial de heat/overheat, barra HP arriba, XP abajo | `PlayerBarsHud` (`HpFill`, `XpFill`, `OverheatFill`) |
| Centro | 6 sockets: Head, Core, Arm, Arm, Leg, Leg | `PassiveLoadoutHud` (`Passives/PassiveSlot_N/Icon` + badge `Level`) |
| Derecha | Dial Q, nombre/nivel, munición, arma actual, dos siguientes | `WeaponClusterHud` |

Dashes: otra vez hijo de `ColumnRight`, no del canvas. Rect del prefab anterior al arte: anclas abajo estiradas, pivot `(1, 0)`, posición `(-75, 182)`, alto `28`. Pips `Charge_0..2` en `DashCharges/Layout`, alineados a la derecha. Con una sola carga activa se ve un único pip cyan pegado al borde derecho, justo arriba del panel de arma. La retícula (chevron / círculo) vuelve a quedar sola en el centro.

Menú para reaplicar: **ScrapWaves → UI → Apply HUD Art To GameplayHud V2**. Después, en la instancia de `GameplayScene` (`/UI/GameplayHud V2 (1)`), hacer Revert si la escena no toma el prefab. El applier borra un `DashCharges` que cuelgue directo de `GameplayHudCanvas`.

Pipeline para regenerar arte:

```text
# En Blender MCP, o blender -b --python:
exec del archivo ArtSource/Tools/blender_build_hud.py  ->  build_hud()
python ArtSource/Tools/finish_hud.py
```

`build_hud()` vacía la escena, modela, renderiza a `ArtSource/UI/renders/`, escribe `HudLayout.json` y guarda `ArtSource/UI/HUD.blend`. `finish_hud.py` compone el PNG final en `Assets/Art/UI/HUD/` (contorno, máscara circular, fill de barra, pip de dash, slots vacíos con silueta).

Iconos de items: `ArtSource/Tools/blender_icon_rig.py` (`use_shadow = False`), `blender_model_kit.py`, `compose_icon.py`. No regenerar iconos salvo pedido. Cabezas = casco de moto, no robot.

## Errores vistos al entrar en Play (GameplayScene)

1. **Pause roto.** `PauseMenuUI.Awake` loguea: `PauseMenuUI requires its authored PauseRoot hierarchy. Run the gameplay UI authoring tool in the Editor.` El prefab tiene `PauseMenuUI` y `RunEndRoot`, pero no hay un GameObject hijo llamado `PauseRoot` (`PauseMenuUI.TryWireFromHierarchy` hace `transform.Find("PauseRoot")`). Si `PauseRoot` solo existía como override de la instancia de escena, el Revert lo borró. El pause no cablea botones ni se oculta bien. Siguiente paso: reautorizar la jerarquía de pause (herramienta de editor que ya construye `PauseRoot` en `PauseMenuUI`, cerca de la línea 571) sin volver a pasar `HudArtApplier` por encima de ese subárbol. `HudArtApplier` no debería tocar `PauseMenuUI`; confirmar que un Apply nuevo no lo pise.

2. **Settings del pause.** `PauseMenuUI: no authored UserSettingsService is available; settings controls were disabled.` Venía de antes. Hace falta un `UserSettingsService` autorado en la escena.

3. **Spam de elipsis en TMP.** `The character used for Ellipsis is not available in font asset [LiberationSans SDF]`. `HudArtApplier.AddText` pone `TextOverflowModes.Ellipsis`. LiberationSans no tiene el glifo. Cambiar a `Truncate`, o usar la fuente del proyecto que sí lo tenga. `PassiveLoadoutHud` / `WeaponClusterHud` no son la fuente del warning; es el texto creado por el applier (nombre de arma, munición, nivel).

4. **Dashes visualmente mal ubicados.** La posición `(-75, 182)` era del `ColumnRight` viejo (layout, alto de la tira). El column nuevo mide ~148 px de alto, así que la fila queda flotando arriba del panel derecho y, con `HorizontalLayoutGroup` estirado a todo el ancho, el pip activo se pega al borde de la pantalla. Hay que reposicionar la fila a ojo sobre el panel derecho (compacta, no estirada a 500 px) y dejar la retícula como está.

5. **Profundidad del HUD.** El viewport de Blender se ve más 3D porque está en ángulo. El sprite es ortográfico de frente para que HP/XP/calor sigan alineados al pixel (`HudLayout.json` no cambió de tamaño: Left 1038×408, Center 1454×324, Right 1126×328). El re-render local (sin commit) ya mete oclusión y luz desde arriba; en Unity se nota más el bisel, pero no iguala una vista inclinada. No inclinar la cámara sin rehacer el layout: los fills dejarían de coincidir.

Warnings viejos, no de esta sesión: `SwarmSpawner` obsoleto, `enableWordWrapping` obsoleto, campos sin usar, partículas en `TemporaryPowerupController`.

## Decisiones que no hay que reabrir salvo que se pida

- Personaje humano. Cabeza = casco de moto con smooth shading (`ArtSource/Head/scripts/head_base.py`).
- Iconos de items sin sombra (`use_shadow = False`, `shadow_px = 0`).
- Pasivos de pecho: bobina (escudo), placas + reactor (vida máxima), remiendos + soplete (robo de vida), cruz + viales (regeneración). Base en `ArtSource/Chest/scripts/chest_base.py`.
- Slots vacíos del HUD: silueta por tipo (`HudSlotEmpty_Head/Core/Arm/Leg`). Al equipar, el icono `_selected` del ScriptableObject.
- GUIDs de `_selected.png`: se sobrescribió el PNG para no romper referencias. `_locked.png` nuevos tienen `.meta` propio.

## Seguimiento (misma fecha, segunda sesión)

Resuelto:

1. **Pause roto.** La causa no era el prefab: `PauseRoot` vive en `GameplayScene.unity` como objeto agregado a la instancia de `GameplayHud V2 (1)`. El Revert completo de la instancia (sin guardar) lo había borrado de la escena cargada. Se recargó la escena desde disco y se revirtieron **solo** los overrides de RectTransform de `ColumnLeft/Center/Right` (eran los que rompían el arte nuevo). No volver a hacer Revert de toda la instancia. Respaldo de la escena sucia en `Assets/Temp/GameplayScene_dirty_backup.unity` (no commitear).
2. **Elipsis.** `HudArtApplier.AddText` usa `Truncate`. Los menús de LevelUp, Crafting y WeaponSelection siguen con Ellipsis desde antes; avisan solo si el texto desborda.
3. **Dashes.** Fila compacta de 72×20 anclada arriba a la derecha de `ColumnRight`, en `(-14, 24)`.
4. **Retícula de lock del rocket.** Verificada en Play: con 8 locks el marco pasa de 180×100 a ~811×455. Crece solo por encima de `RocketActiveInitialTargetCount` (5): con 5 o menos enemigos fijados no cambia de tamaño. Así está diseñado y no cambió en ningún commit. Ojo: con el editor en segundo plano Unity no avanza frames (`runInBackground` apagado), así que la retícula parece congelada.

Variantes de arte nuevas, sin tocar la base:

- `ArtSource/UI/V2A/` basada en `Assets/Arte/Player_Bars.png`: hierro oscuro, dial de bloques, tubos de vidrio, caños, LEDs naranjas y contorno de 6 px.
- `ArtSource/UI/V2B/` con el estilo de los íconos de armas: marcos de acero claro con bulones abombados, bandas de cobre, franjas de peligro, manómetros y barras-caño. Contorno de 4 px.
- Regenerar: `blender -b --factory-startup --python ArtSource/Tools/blender_build_hud.py -- --variant V2A [--only HudLeft]` y después `python ArtSource/Tools/finish_hud.py --variant V2A --outline 6 --grade 1.0 1.3` (V2B: `--outline 4 --grade 1.0 1.2`).
- Aplicar en Unity: **ScrapWaves → UI → Apply HUD Art V2A (Player Bars)** o **V2B (Estilo armas)**. La base sigue en **Apply HUD Art To GameplayHud V2**.

## Cómo retomar

1. Pull de `Map_V2`. Si este MD y los PNG nuevos no están en el commit, copiar el working tree o commitear antes de irse.
2. Abrir Unity en `GameplayScene` y Blender con el addon MCP.
3. Arreglar en este orden: `PauseRoot`, overflow Ellipsis, posición de los dashes.
4. Recién después seguir puliendo profundidad o el panel derecho.
5. Verificar en Play con captura que incluya el Canvas overlay (`ScreenCapture.CaptureScreenshotAsTexture`). La captura de Game View del MCP no incluye UI Screen Space - Overlay.

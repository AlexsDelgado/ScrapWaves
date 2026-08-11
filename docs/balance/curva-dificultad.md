# Curva de dificultad del juego

Documento para **diseño y QA**. Resume las capas que hacen que la partida se ponga más dura con el tiempo y con eventos.

---

## Idea en una frase

La dificultad no es un solo número: combina **tiempo de run**, ciclos de **Heat → Overheat**, y al final **presión de salida** cuando tenés todas las llaves.

---

## Las 3 capas

```
[ Tiempo de run ]  → más spawns + enemigos más duros al aparecer
[ Heat / Overheat ] → presión cíclica (boost, elites o bosses)
[ Presión de salida ] → al tener todas las llaves, overheat permanente + escalones
```

---

## 1) Tiempo de run (`DifficultyManager`)

- Empieza tras un **delay** (típicamente 30 s).
- Sube una **intensidad 0→1** con una curva por minutos.
- Efectos:
  - **Más enemigos** por oleada.
  - **Spawns más frecuentes** (intervalo más corto).
  - **Más vida / velocidad** en enemigos nuevos (ver `enemigos-stats.md`).

Curva default del código (si no se toca en escena):

| Minutos tras el delay | Intensidad aprox. |
|-----------------------|-------------------|
| 0 | 0 |
| 5 | 0.35 |
| 15 | 0.7 |
| 30 | 1.0 |

El campo **Difficulty Ramp Speed Multiplier** hace que esa curva se recorra más rápido o más lento (ej. 1.5 = llega antes al tope).

---

## 2) Heat y Overheat

### Heat (barra)

- Sube con **kills** (`heat por kill` configurable).
- Barra en dos tramos visuales (~0–80 % y ~80–100 %).
- Al llegar al 100 % → entra **Overheat**.
- Cada ciclo completado puede **subir el requisito** de heat del siguiente (escalación).

### Fase intermedia (Heat alto, aún no Overheat)

Cuando la barra está en el tramo alto (aprox. 80–100 %):

- Enemigos comunes más **rápidos** (boost de swarm).
- Oleadas de spawn más **grandes** (×2 típico).
- **No** aplica al boss.

### Overheat (objetivo)

- El jugador gana **buff de fire rate** (típicamente ×1.5).
- **No termina por timer**: termina cuando se completa el objetivo.
- Ciclos:
  - **Impares (1, 3, 5…):** oleada de **elites** → matarlos a todos.
  - **Pares (2, 4…):** **boss(es)** → derrotarlos.
- Al terminar: heat residual + decay; los spawns pueden **pausarse** mientras el heat sigue alto (post-overheat).

### Tras Overheat

- Suele quedar heat residual alto y **decay**.
- Con “pausar spawn mientras heat elevado”: menos presión hasta que baje el heat.

---

## 3) Presión de salida (todas las llaves)

Cuando el jugador reúne **todas las llaves**:

1. Entra **Overheat permanente** (no se cierra con elites/boss de ciclo).
2. Se apagan elites/bosses de ciclo Overheat.
3. Cada **minuto** (configurable) sube un escalón de presión, tipicamente **×2 → ×3 → ×4**.
4. Efecto práctico en el loop actual:
   - **Más enemigos** por tick de spawn.
   - Enemigos **más rápidos**.
   - El intervalo del `OrbitalSpawner` **no** se acorta por esta presión (sí afecta cantidad y velocidad).

---

## Qué mirar para balancear

| Sistema | Dónde | Qué tocar |
|---------|--------|-----------|
| Tiempo | `DifficultyManager` | Delay, curva, ramp, topes de spawn/stats |
| Heat | `HeatManager` | Puntos por tramo, heat por kill, escalación por ciclo |
| Overheat | `OverheatManager` (player) | Fire rate buff, residual post-overheat |
| Swarm boost | `OverheatSwarmBoost` | Multiplicadores de velocidad / oleada |
| Salida | `LevelExitPressure` / `ExitSpawnPressure` | Minutos por tier, multiplicadores |

**Importante:** los valores de **GameplayScene** / **SampleScene** pueden diferir mucho de los defaults del script. Siempre validá en la escena real de playtest.

---

## Checklist de pruebas

1. **0–30 s:** intensidad 0; spawns “base”.
2. **Más minutos:** más cantidad, spawns más seguidos, enemigos más duros.
3. **Matar hasta ~80–100 % heat:** boost de swarm (más rápidos / más densos).
4. **Overheat impar:** aparecen elites; al limpiarlos termina Overheat.
5. **Overheat par:** aparece boss; al matarlo termina Overheat.
6. **Post-Overheat:** spawns pausados o reducidos mientras decay.
7. **Todas las llaves:** presión permanente y escalones cada minuto; sin elites/boss de ciclo.

### Herramientas útiles

| Atajo | Uso |
|-------|-----|
| **F3** | Ver `count x` / `interval x` de dificultad y boosts |
| **F2** | Tweaks runtime |
| **Numpad 0** | Vida infinita para sobrevivir y observar curva |

---

## Notas / puntos a confirmar con diseño

- En escenas de play, los topes de vida/velocidad a veces están **muy altos** (¿tuning de prueba o intención final?).
- Posible desalineación: multi-boss configurado en un ciclo que el sistema trata como “impar” (elites). Conviene verificar en play qué ciclo da boss vs elites.

---

## Archivos de código (referencia)

- `Assets/Scripts/DifficultyManager.cs`
- `Assets/Scripts/Overheat/HeatManager.cs`
- `Assets/Scripts/Overheat/OverheatManager.cs`
- `Assets/Scripts/Overheat/OverheatSwarmBoost.cs`
- `Assets/Scripts/Level/LevelExitPressure.cs`
- `Assets/Scripts/Level/ExitSpawnPressure.cs`
- `Assets/Scripts/Enemy/BossManager.cs`
- `Assets/Scripts/Spawning/OverheatEliteWaveSpawner.cs`

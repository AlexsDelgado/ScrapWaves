# Incremento de stats de enemigos

Documento para **diseño y QA**. Explica qué stats de los enemigos suben durante una partida y cuáles no.

---

## Idea en una frase

Cuando aparece un enemigo nuevo, el juego puede **multiplicar su vida y su velocidad** según cuánto tiempo lleva la run. **El daño de contacto no sube** con la dificultad.

---

## Qué se escala

| Stat | ¿Escala con dificultad? | Notas |
|------|-------------------------|--------|
| Vida máxima | Sí | Se aplica al spawnear |
| Velocidad de movimiento | Sí | Se aplica al spawnear |
| Daño de contacto | **No** | Queda el valor del prefab |
| Daño de proyectiles / habilidades | **No** (hoy) | Sin hook de dificultad |
| Vida de bosses | **No** por esta curva | Usa valor fijo de `BossManager` |

---

## Cuándo se aplica

- Solo en el **momento del spawn** (enemigo que acaba de aparecer).
- Los enemigos **ya vivos no se reescalan** si la dificultad sigue subiendo.
- Afecta spawns de: Orbital, Zones, oleadas de elites, etc.

---

## Cómo se calcula (simple)

1. Los primeros **X segundos** (por defecto **30 s**) la intensidad es **0** → multiplicadores en **1×** (stats base del prefab).
2. Después empieza a subir una **intensidad de 0 a 1** según una curva de minutos.
3. A intensidad 1:
   - Vida ≈ entre **1×** y el tope configurado (default de código **2×**; en escena puede ser otro).
   - Velocidad ≈ entre **1×** y el tope (default de código **1.35×**).
4. Fórmula mental:  
   `multiplicador = lerp(1, tope, intensidad)`  
   `vida_final = redondear(vida_prefab × multiplicador)`

Hay un extra aparte (Overheat / presión de salida) que puede **duplicar o más la velocidad** en ciertos momentos; eso no es la curva de tiempo, es otro sistema. Ver `curva-dificultad.md`.

---

## Dónde se configura

Objeto con componente **`DifficultyManager`** (en la escena de gameplay).

Campos útiles:

| Campo (Inspector) | Qué hace |
|-------------------|----------|
| Scaling Start Delay Seconds | Segundos antes de empezar a subir |
| Intensity Over Minutes After Start | Curva: minutos → intensidad 0–1 |
| Difficulty Ramp Speed Multiplier | Acelera o frena el recorrido de la curva |
| Scale Enemy Health | On/Off del escalado de vida |
| Max Enemy Health Multiplier | Vida a intensidad 1 |
| Scale Enemy Move Speed | On/Off del escalado de velocidad |
| Max Enemy Speed Multiplier | Velocidad a intensidad 1 |

Los stats **base** (vida/daño/velocidad del prefab) se editan en cada prefab de enemigo (`EnemyHealth`, `EnemyFollow`, `EnemyContactDamage`, etc.).

---

## Valores a tener en cuenta (escenas)

Los defaults del **script** no siempre coinciden con la **escena**:

- **GameplayScene** puede tener topes de vida/velocidad muy altos (tuning agresivo o de prueba).
- **SampleScene** también suele estar más agresiva que los defaults del código.

**QA:** al balancear, mirá siempre el `DifficultyManager` de la escena que estás jugando, no solo el default del script.

---

## Checklist de pruebas

1. Primeros ~30 s: enemigos con vida/velocidad **base** del prefab.
2. Más adelante en la run: mismos prefabs, pero **más tanque / más rápidos**.
3. Matar un enemigo viejo y spawnear uno nuevo: el nuevo debe reflejar la dificultad **actual**; el viejo no cambia.
4. Verificar que el **daño al jugador** no sube solo por el tiempo (mismo prefab).
5. Boss: vida fija de `BossManager`, no la curva de spawn comunes.

### Herramientas útiles

| Atajo | Uso |
|-------|-----|
| **F3** | Panel QA del core loop (ver multiplicadores de dificultad) |
| **F2** | Tweaks runtime de stats por tipo |
| **Numpad 0** | Vida infinita (si hay `DebugInfiniteHealth`) |

---

## Archivos de código (referencia)

- `Assets/Scripts/DifficultyManager.cs`
- `Assets/Scripts/Enemy/EnemyHealth.cs`
- `Assets/Scripts/Enemy/EnemyFollow.cs`
- `Assets/Scripts/Enemy/EnemyContactDamage.cs`
- `Assets/Scripts/Enemy/BossManager.cs`

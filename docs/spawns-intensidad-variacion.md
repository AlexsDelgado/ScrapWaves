# Intensidad y variación de spawns

Documento para **diseño y QA**. Explica qué enemigos aparecen, en qué cantidad y qué hace que el spawn varíe.

---

## Idea en una frase

El spawner principal (`OrbitalSpawner`) elige un tipo con una **ruleta de pesos**, spawnea un **batch**, y después multiplica esa cantidad por **dificultad** y por **boosts** (Heat / salida).

---

## Piezas del sistema

| Pieza | Rol |
|-------|-----|
| **OrbitalSpawner** | Loop principal de la run: spawnea alrededor del jugador |
| **EnemySpawnRoulette** + asset SO | Qué tipo sale y cuántos (batch base) |
| **DifficultyManager** | Multiplica cantidad y acorta intervalo con el tiempo |
| **OverheatSwarmBoost** | Multiplica oleadas (y velocidad) en Heat alto / salida |
| **ZoneSpawner** | Emboscada one-shot al entrar a una zona |
| **OverheatEliteWaveSpawner** | Oleada de elites en Overheat impar |
| **SwarmSpawner** | **Obsoleto** (solo escenas QA legacy) |

---

## OrbitalSpawner (principal)

Cada tick:

1. Espera el **intervalo** (base × escala de dificultad).
2. Tira la **ruleta** → elige tipo + batch base.
3. Cantidad final ≈  
   `redondear(batch × multiplicador_dificultad × multiplicador_boost)`.
4. Spawnea en un **anillo** alrededor del jugador (radio min/max).
5. Respeta un **cap** de enemigos activos.

Campos útiles:

| Campo | Qué hace |
|-------|----------|
| Config | Asset de ruleta |
| Spawn Interval | Segundos base entre rolls |
| Min / Max Spawn Radius | Distancia del anillo |
| Max Active Enemies | Tope global |
| Pause Spawn While Heat Elevated | Pausa en decay post-Overheat si heat sigue alto |

---

## Ruleta de enemigos (variación)

Asset típico:  
`Assets/ScriptableObjects/Spawning/DefaultEnemySpawnRoulette.asset`

Cada entrada tiene:

- **Kind** (tipo de enemigo)
- **BaseWeight** (probabilidad relativa)
- **BatchSize** (cuántos salen si gana ese roll)
- **IsVariant** (variantes “especiales”; ganan peso con el tiempo)

### Lectura rápida del asset actual

| Tipo | Peso base | Batch | ¿Variante? |
|------|-----------|-------|------------|
| Junk Slime | 75 | 10 | No |
| Vigilance Drone | 30 | 3 | No |
| Chaser Bot | 20 | 5 | No |
| Hellfire Slime | 2 | 2 | Sí |
| Bomber Drone | 2 | 1 | Sí |
| Shocker Bot | 2 | 1 | Sí |

### Cómo suben las variantes

- Cada cierto tiempo (ej. **120 s**) las entradas `IsVariant` ganan **+peso** (ej. **+3**).
- Stats del jugador (`ExtraEliteChance`) pueden empujar más peso hacia variantes (con un tope).

**Mental model:** al inicio salen casi siempre comunes; más tarde las variantes aparecen más seguido, sin reemplazar del todo a los comunes.

---

## Intensidad (cantidad / cadencia)

Además de la ruleta, la **intensidad** cambia el “cuánto” y el “cada cuánto”:

| Fuente | Efecto en spawn |
|--------|-----------------|
| Tiempo (`DifficultyManager`) | Más enemigos por oleada + intervalo más corto |
| Heat alto (pre-Overheat) | Oleadas × boost (típicamente ×2) |
| Presión de salida | Oleadas ×2 / ×3 / ×4 por escalones |
| Cap de activos | Si está lleno, no spawnea más hasta que mueran |

Detalle: la presión de salida en el loop actual **aumenta cantidad** (y velocidad de enemigos), pero **no** acorta el intervalo del Orbital como sí hacía el Swarm legacy.

---

## Otros spawns

### Zones (emboscada)

- Al entrar el jugador → spawnea N enemigos de un prefab en el área.
- Se desarma; puede **rearmarse** al inicio del siguiente Overheat.
- Prefabs típicos: Slime ×12, Drone ×6, Chaser ×8.
- También reciben modificadores de dificultad al spawn.

### Elites de Overheat

- Solo en ciclos **impares** de Overheat.
- Oleada fija (ej. varios elites de 3 tipos).
- Hay que limpiarlos para cerrar el Overheat.
- También reciben dificultad al spawn.

### Bosses

- Ciclos **pares** de Overheat (vía `BossManager`).
- Vida fija; no usan la ruleta orbital.

---

## Checklist de pruebas

1. Al inicio: predominan Slime / Drone / Chaser según pesos; pocas variantes.
2. Dejar pasar ~2+ minutos: variantes con más peso (más frecuentes).
3. Con el tiempo: **más** enemigos por tick y spawns **más seguidos** (F3 ayuda).
4. Heat 80–100 %: batches más grandes + enemigos más rápidos.
5. Overheat impar: elites; Overheat par: boss.
6. Post-Overheat: spawns pueden pausarse hasta que baje el heat.
7. Todas las llaves: más densidad por presión de salida; sin elites/boss de ciclo.
8. Cap de activos: spawnear deja de sumar si el mapa está lleno.

### Herramientas útiles

| Atajo | Uso |
|-------|-----|
| **F3** | Intervalo orbital, radios, pesos/batch, multiplicadores |
| **F1** | Harness de enemigos (si está en escena) |
| **Numpad 1** (en harness) | Roll + spawn de prueba |
| **Ctrl+Numpad 9** | Materiales (craft; no afecta spawn) |

---

## Dónde editar

| Qué querés cambiar | Dónde |
|--------------------|--------|
| Mix de enemigos / batches | `DefaultEnemySpawnRoulette` (SO) |
| Cadencia base / anillo / cap | `OrbitalSpawner` en escena |
| Curva de cantidad/intervalo | `DifficultyManager` |
| Zones | Prefabs `ZoneSpawner_*` |
| Oleada elites | `OverheatEliteWaveSpawner` en escena |

---

## Archivos de código (referencia)

- `Assets/Scripts/Spawning/OrbitalSpawner.cs`
- `Assets/Scripts/Spawning/EnemySpawnRoulette.cs`
- `Assets/Scripts/Spawning/EnemySpawnRouletteConfig.cs`
- `Assets/Scripts/Spawning/ZoneSpawner.cs`
- `Assets/Scripts/Spawning/OverheatEliteWaveSpawner.cs`
- `Assets/Scripts/DifficultyManager.cs`
- `Assets/Scripts/Overheat/OverheatSwarmBoost.cs`
- `Assets/ScriptableObjects/Spawning/DefaultEnemySpawnRoulette.asset`
- `Assets/Scripts/Spawning/QaCoreLoopMenu.cs`

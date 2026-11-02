# SfxGen

Generador procedural de SFX (Python + numpy/scipy). Escribe en `Assets/Audio/Synth/`.

```
python Tools/SfxGen/generate.py                 # todo, 3 tomas por familia
python Tools/SfxGen/generate.py dash slide -n 2 # familias puntuales
```

Misma semilla + misma receta = mismos bytes; los GUID de los `.meta` son estables.
Regenerar con menos tomas **no borra** las sobrantes (`_04`, `_05`...): borrarlas a mano.

## Familias → evento del juego

| Familia | Evento / cue |
|---|---|
| `shot` | Cañón automático (disparo) |
| `rocket_launch`, `rocket_explosion` | Rocket: salida / detonación |
| `mortar_launch`, `mortar_explosion` | Mortero: salida / impacto |
| `blade_slash` | Hoja giratoria: tajo |
| `flame_loop` (loop), `flame_burst` | Lanzallamas: continuo / habilidad |
| `cannon_ability`, `rocket_ability`, `mortar_ability` | Q del cañón / racimo / barrage |
| `blade_thrust`, `blade_dash` | Hoja: estocada / dash atómico |
| `dash`, `slide` | Movimiento del jugador |
| `jump`, `land`, `hurt`, `xp`, `levelup` | Jugador |
| `overheat_warning`, `overheat_start`, `overheat_end` | Aviso previo / inicio / fin del Overheat |
| `enemy_hit` | Golpe genérico a enemigo |
| `bomber_*`, `charger_*`, `destroyer_*`, `flyer_*`, `worm_*`, `slime_*` (`_spawn` / `_death`) | BomberDrone, Charger, Destroyer, FlyingRanged, GigaWorm, HellfireSlime |
| `enemy_death` | Muerte genérica (fallback) |

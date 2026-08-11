# Progresión de enemigos — índice (Diseño / QA)

Documentación corta para entender y probar la progresión de enemigos sin leer el código.

| Documento | Tema |
|-----------|------|
| [enemigos-stats.md](./enemigos-stats.md) | Incremento de stats (vida, velocidad; qué no escala) |
| [curva-dificultad.md](./curva-dificultad.md) | Curva de dificultad (tiempo + Heat/Overheat + salida) |
| [spawns-intensidad-variacion.md](./spawns-intensidad-variacion.md) | Intensidad y variación de spawns (ruleta, orbital, zones, elites) |

## Atajos rápidos de playtest

| Tecla | Para qué |
|-------|----------|
| **F3** | Panel QA del core loop (dificultad / spawn) |
| **F2** | Tweaks runtime de balance |
| **F1** | Harness de enemigos (si está en escena) |
| **Numpad 0** | Vida infinita (`DebugInfiniteHealth`) |
| **Ctrl + Numpad 9** | 999 de todos los materiales (`DebugCrafting`) |

## Regla de oro al balancear

Los valores del **Inspector en la escena** (sobre todo `GameplayScene`) pueden diferir de los defaults del script. Siempre validá en la escena que se juega.

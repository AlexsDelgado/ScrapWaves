# Dumpster Fire / Scrap Waves — GDD alpha 22-09-2026

Vertical slice en `main`. El código y las escenas usan el nombre Scrap Waves. Los documentos de diseño siguen diciendo Dumpster Fire.

## Concepto

Survivors-like en tercera persona, en un junkyard. El jugador se mueve, esquiva y dispara. Hay una arma en manual (munición que se gasta a propósito) y el resto del loadout en automático, con orígenes de fuego en wearables del cuerpo.

La firma del ritmo es el Heat: matar llena una barra. La barra empuja el spawn, potencia el kit de las armas y, al llenarse, abre un Overheat con un objetivo obligatorio. El objetivo alterna una oleada de elites y un boss. El boss deja una llave. Con dos llaves se abre la puerta de salida y la run entra en un Overheat permanente hasta que el jugador carga la puerta y escapa.

Público y referencias de diseño (Vampire Survivors, Megabonk, Risk of Rain 2, runs de unos 15–20 minutos) se mantienen como intención. El mapa de dos capas (islas flotantes arriba, scrapyard abajo, bajar forzado al Overheat) sigue escrito en el GDD de julio y en el Combat Design viejo. En el código de la run actual el espacio jugable es una arena con spawn orbital alrededor del jugador, estaciones de crafting y una puerta. No hay un viaje obligatorio entre dos pisos atado al Heat.

## Loop de una run

1. Elección de un arma al empezar (`RunStartWeaponChoice`). El jugador no arranca con las armas del inspector si esa elección está activa.
2. Spawn orbital continuo. Matar enemigos suelta materiales. Recoger un material da el material y la XP. No hay orbes de XP separados.
3. Cada level-up ofrece pasivos (2–3 opciones, slots de cuerpo) y después aplica mejoras de stats automáticas. Las armas no salen del level-up.
4. En las estaciones de crafting el jugador mejora armas, hace Tinker (arma nueva aleatoria que no tenga) y, en nivel 6, Advanced Tinkering (un path de evolución).
5. La barra de heat sube por kill. Al 80 % visual entra la fase intermedia (los enemigos del swarm van más rápido). Al 100 % empieza un Overheat sin temporizador.
6. Overheat impar: oleada de elites. Overheat par: boss. Al cerrar el objetivo el swarm común sigue vivo, queda heat residual y decae. Mientras decae por encima del primer tramo, el spawn orbital se pausa. Cada Overheat cerrado deja la run más densa para los ciclos siguientes.
7. Cada boss derrotado suelta una llave. A las 2 llaves: Overheat permanente, presión de salida en escalones, flecha a la puerta. Interactuar carga la puerta. Al terminar la carga, otra interacción dispara la victoria.
8. Morir (vida a 0) o escapar cierra la run y abre la pantalla de fin. El meta (scrap, desbloqueos, upgrades, logros) persiste fuera de la run.

## Sistemas y dónde leerlos

| Sistema | Estado en código | Documento alpha |
|---|---|---|
| Game loop, llaves, puerta | Funcional | `04-Game-Loop.md` |
| Heat / Overheat | Funcional. Números movidos desde el 21-7 | `02-Overheat.md` |
| Spawning y enemigos | Funcional. Pool y ruleta; `SwarmSpawner` obsoleto | `03-Spawning-y-enemigos.md` |
| Progresión, crafting, meta | Funcional | `05-Progresion-crafting-y-meta.md` |
| Stats | Funcional. Fórmula aditiva × multiplicativa | `referencia-codigo/DumpsterFire-StatSystem.docx` |
| Armas | Funcional. Cinco armas de producción | `referencia-codigo/DumpsterFire-WeaponSystem.docx` |
| Firepoints automáticos | Funcional | `referencia-codigo/DumpsterFire-AutomaticFirepointSystem.docx` |
| Movimiento | Funcional. El commit del 22-09 tocó valores, no la máquina de estados | `referencia-codigo/DumpsterFire-Movement System.docx` |
| Animación del jugador | Funcional. Playables, no Animator de runtime | `referencia-codigo/DumpsterFire-PlayerAnimationSystem.docx` |
| Combat feedback y números | Funcional | `referencia-codigo/DumpsterFire-CombatFeedbackSystem.docx` |
| Rear threat | Funcional | `referencia-codigo/DumpsterFire-RearThreatSystem.docx` |
| UI de presentación | Funcional. Pausa, fin de run, menú, HUD authored | `referencia-codigo/DumpsterFire-UIPresentationSystem.docx` y `07-Deltas-y-huecos.md` |
| QA y debug | Funcional | `06-QA-y-debug.md` |

## Qué quedó atrás del GDD de diseño

- XP en orbes, y armas que se eligen en el level-up. El level-up ya no entrega armas.
- Vaciar la arena y poner el heat a 0 al cerrar un Overheat. El swarm común permanece y el heat residual decae.
- Overheat con temporizador. Dura hasta limpiar elites o matar al boss.
- Victoria por cantidad de bosses. La victoria es la puerta.
- Mapa de dos capas como estructura de la run. Sigue como dirección de arte y de nivel; el loop implementado no depende de bajar al underlevel para el Overheat.

## Identidad visual y UI de diseño

- Figma citado en el GDD: `https://www.figma.com/design/AtnDBPq6gGpvg5xmt44KAg/Scrap`
- Miro citado en el GDD: `https://miro.com/app/board/uXjVGwZChAQ=`

Esos enlaces no se versionan en el repo. La UI que corre está descrita en el documento de presentación y en las escenas (`Title`, `GameplayScene`).

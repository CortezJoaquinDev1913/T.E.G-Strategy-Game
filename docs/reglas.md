# Reglas del juego (fuente de verdad del motor C#)

Base: reglamento de **TEG clásico** (Yetem), adaptado a **2–4 participantes en un mismo navegador**.
Mapa, nombres de países y arte son **originales** (no se copia el tablero comercial). El grafo de
adyacencias y los continentes se definen en `docs/mapa.md` (pendiente). Todo lo marcado como
**[ADAPTACIÓN]** se desvía del reglamento o lo completa donde era ambiguo.

> Contrato para el motor: cada regla de este documento debe tener al menos un test xUnit.

---

## 1. Alcance de esta versión

| Tema | Estado |
|---|---|
| 50 países, 6 continentes, combate con dados, refuerzos, tarjetas y canjes | Incluido |
| Objetivos secretos + objetivo común | Incluido |
| Partidas de 2 y 3: objetivo único «conquistar los 50 países» | Incluido |
| Participantes Humano o Bot (mínimo 1 humano), máx. 4 | Incluido |
| Pactos (entre países, mundiales, zona internacional) | **Fuera de alcance** [ADAPTACIÓN]: son acuerdos verbales entre personas y no se validan en el motor |
| Canje defensivo (variante opcional) | **Fuera de alcance** |
| Comodines («tres símbolos juntos») | **Fuera de alcance**: ningún país tiene tarjeta comodín en el mazo de 50 |
| Salas online, SignalR, sesiones por jugador | **Fuera de alcance** |
| «Enclaves» | **No existen en TEG clásico.** Su rol de victoria lo cubren los objetivos (sección 8) [ADAPTACIÓN] |

## 2. Participantes

- Entre **2 y 4**, cada uno con nombre, tipo (`Humano` | `Bot`) y **color único** de: azul, rojo, negro, amarillo, verde, magenta.
- Al menos 1 humano. Se admiten todos humanos o mezclados.
- El **orden de mesa** es el orden en que se agregan en «Nueva partida» y sigue el sentido horario. Es el orden de turnos.
- Un participante **eliminado** (0 países) se **saltea** en la rotación para siempre.
- El **«jugador de la derecha»** de X es el participante que juega inmediatamente **antes** que X en el orden de mesa (circular).
- Los bots usan **exactamente** las mismas acciones y validaciones que los humanos.
- Información oculta (objetivo secreto, tarjetas): en modo local es solo **cosmética**. La UI la muestra únicamente al participante activo tras «Empezar turno».

## 3. Reparto inicial [ADAPTACIÓN en lo marcado]

Se usa una única **tirada de dados por participante** (RNG con semilla guardada en la partida; los empates se
re-tiran solo entre los empatados). Resultado: un **ranking** completo. El primero del ranking es el
**primer jugador** (quien abre cada ronda). Los siguientes jugadores siguen el orden de mesa desde él.

Cada país recibe 1 ejército del dueño. Los 50 países siempre están ocupados y nunca hay dos colores en un país.

| Participantes | Países por participante | Quién recibe el país extra | Ejércitos a colocar (vueltas) | Objetivo |
|---|---|---|---|---|
| 2 | 25 / 25 | — | 12 = 6 + 6 [ADAPTACIÓN] | Conquistar 50 |
| 3 | 17 / 17 / 16 | Los 2 primeros del ranking | 8 = 5 + 3 | Conquistar 50 |
| 4 | 13 / 13 / 12 / 12 | Los 2 primeros del ranking | 8 = 5 + 3 | 30 países **o** secreto |

- **4 participantes**: se mezcla el mazo de 50 tarjetas de países y se reparten según la tabla. Luego las tarjetas
  vuelven al mazo y se mezclan de nuevo.
- **2 y 3 participantes** [ADAPTACIÓN]: el reglamento pide colocar de a un ejército alternando. En un navegador compartido
  eso es tedioso, así que el motor **asigna los países al azar y alternando**, de forma reproducible por semilla.
- Totales iniciales en tablero (países + vueltas): 2 → 37 y 37; 3 → 25, 25, 24; 4 → 21, 21, 20, 20.

### Vueltas de colocación inicial
1. **Vuelta 1** (fase `ColocacionInicial`): en orden de mesa desde el primer jugador, cada participante coloca sus ejércitos de la vuelta 1 (5, o 6 si son 2) en países propios, como quiera.
2. **Vuelta 2**: igual, con los ejércitos de la vuelta 2 (3, o 6 si son 2).
3. Un bot coloca automáticamente. La acción es válida solo si el total de la vuelta se reparte entre países propios.

## 4. Estructura de turnos y rondas

- **Ronda de hostilidades** (primera ronda tras la colocación): turno = `Ataque` → `Reagrupe` → `Tarjeta`. **No hay refuerzos.**
- **Rondas siguientes**: `Refuerzo` → `Ataque` → `Reagrupe` → `Tarjeta`.
- Las fases son **ordenadas e irreversibles** en un turno. Cada una (salvo `Refuerzo` cuando hay ejércitos que colocar) es opcional y se puede saltear.
- Una **ronda** termina cuando todos los participantes vivos jugaron una vez; el contador de ronda sube cuando la rotación vuelve al primer jugador.
- Si se elimina a un participante, no se le cuenta el turno ni la ronda.
- El turno termina con la acción explícita `TerminarTurno` (o al terminar `Tarjeta`). El siguiente participante **vivo** en orden de mesa pasa a ser el activo.

## 5. Refuerzos (fase `Refuerzo`)

Total a colocar = **países** + **continentes** + **canjes**, todo al comenzar el turno.

1. **Países**: `max(3, floor(países / 2))`.
2. **Continentes** completos al comenzar el turno: Asia 7, Europa 5, América del Norte 5, América del Sur 3, África 3, Oceanía 2.
   Esos ejércitos **solo** pueden ir a países de ese continente.
3. **Canje de tarjetas**: ver sección 7.

Los de países y canjes se colocan libremente en países propios. La fase no termina hasta colocar todo. Pasada
la fase, no se pueden colocar refuerzos.

## 6. Ataque (fase `Ataque`) y reagrupe

### Ataque
- Origen: país propio con **≥ 2** ejércitos. Destino: país **enemigo y limítrofe** (frontera común o puente, según `docs/mapa.md`).
- Dados del atacante = `min(3, ejércitos_origen − 1)`. Dados del defensor = `min(3, ejércitos_destino)`.
- Se ordenan los dados de cada lado de mayor a menor y se comparan por pares. Sobran los menores del lado con más dados. **Ante empate gana el defensor.** Cada par perdido cuesta 1 ejército a quien lo perdió.
- Los dados salen del RNG del servidor y se guardan en el historial con el resultado.
- **Conquista** (destino en 0): el país cambia de dueño. El atacante mueve entre **1 y 3** ejércitos desde el origen, dejando ≥ 1 en origen (`1 ≤ n ≤ min(3, ejércitos_origen − 1)`). El país conquistado puede atacar en el mismo turno si queda con ≥ 2.
- Se pueden hacer tantos ataques como se quiera, a cualquier color, desde cualquier país.
- Se cuenta cuántos países conquistó el participante en el turno (`conquistasEnTurno`).
- **Eliminación**: si el dueño derrotado queda con 0 países, se lo elimina; el atacante **hereda sus tarjetas**.

### Reagrupe (fase `Reagrupe`)
- Al pasar a `Reagrupe` ya no se puede atacar en este turno.
- Se mueven ejércitos de un país propio a otro **propio limítrofe**, siempre dejando ≥ 1 en el origen.
- **No se encadena**: los ejércitos que llegaron a un país en esta fase no se pueden mover de nuevo. Modelo: cada país tiene un cupo
  `movibles = ejércitos_al_empezar_reagrupe − 1`; mover resta del cupo del origen y **no** suma al del destino.
- Es opcional.

## 7. Tarjetas (fase `Tarjeta`) y canjes

- El mazo tiene 50 tarjetas, una por país. Símbolo por país asignado en `docs/mapa.md` (galeón/globo/cañón, repartido 17/17/16) [ADAPTACIÓN].
- **Obtener tarjeta** al final del turno: si conquistó **≥ 1** país mientras haya hecho **< 3 canjes**, o **≥ 2** países si ya hizo **≥ 3 canjes**. Solo **una** por turno. Si el mazo se acaba, se mezcla el descarte.
- **Premio de país**: por cada tarjeta que tenga cuyo país sea propio, y **por única vez** por tarjeta, se suman **2 ejércitos en ese país** [ADAPTACIÓN]: en el motor es automático al cierre del turno propio, lo que elimina el olvido del reglamento físico. La tarjeta se conserva.
- **Canje** (en `Refuerzo`): 3 tarjetas del mismo símbolo o 3 de símbolos distintos. Valores: 4, 7, 10, 15, 20, 25… (después del 3.º, +5 cada vez). El contador de canjes es **por participante**. Las tarjetas canjeadas van al descarte.
- Con **≥ 5 tarjetas** al empezar `Refuerzo` el canje es **obligatorio** (hasta quedar con < 5) [ADAPTACIÓN]. Siempre existe un canje válido con 5 tarjetas. Con 3 o 4 tarjetas es opcional. Se permiten varios canjes en el mismo `Refuerzo`.
- Si por herencia un participante supera 5 tarjetas a mitad del turno, lo resuelve en el siguiente `Refuerzo`.

## 8. Victoria

El motor evalúa tras **cada acción que cambia países** (conquista, eliminación). Gana el primero en cumplir:

1. **Eliminación**: queda como **único participante con países** (en partidas de 3 o 4). En 2 y 3 es equivalente al objetivo «50 países».
2. **Objetivo común**: 4 participantes → **30 países** (según el reglamento). 2 y 3 participantes → los **50 países**.
3. **Objetivo secreto** (solo 4 participantes), uno por participante, de los 15 del reglamento, sorteado sin repetir (con 4 participantes salen 4 de los 15):

| N.º | Objetivo |
|---|---|
| 1 | África + 5 países de América del Norte + 4 de Europa |
| 2 | América del Sur + 7 de Europa + 3 países limítrofes entre sí en cualquier lugar* |
| 3 | Asia + 2 de América del Sur |
| 4 | Europa + 4 de Asia + 2 de América del Sur |
| 5 | América del Norte + 2 de Oceanía + 4 de Asia |
| 6 | 2 de Oceanía + 2 de África + 2 de América del Sur + 3 de Europa + 4 de América del Norte + 3 de Asia |
| 7 | Oceanía + América del Norte + 2 de Europa |
| 8 | América del Sur + África + 4 de Asia |
| 9 | Oceanía + África + 5 de América del Norte |
| 10–15 | Destruir al ejército azul / rojo / negro / amarillo / verde / magenta |

\* [ADAPTACIÓN] «3 limítrofes entre sí» = 3 países propios **conectados** (cada uno adyacente a al menos otro de los tres), distintos de los ya contados por el resto del objetivo. El motor los evalúa con búsqueda sobre el grafo.

**Reglas de los objetivos de destrucción** (10–15):
- Se cumplen cuando **el dueño del objetivo** elimina el último ejército de ese color (no cuenta si lo elimina otro).
- Si al repartir el color objetivo es **el propio** o **nadie juega con él**, el objetivo pasa a ser destruir al **jugador de la derecha** (se fija al sortear).
- Si otro participante elimina a ese color primero, el objetivo **pasa automáticamente al común** (30 países), sin anunciarlo.
- Si al repartir queda un objetivo secreto **ya cumplido**, se sortea otro (regla de TEG Jr, extendida a todo el juego [ADAPTACIÓN]).

Prioridad si coinciden varias condiciones en la misma acción: **eliminación → común → secreto**. Solo puede ganar el participante activo, así que no hay empates. La partida queda en estado `Terminada` con el ganador y el motivo, y ya no acepta jugadas.

## 9. Datos que debe guardar la partida (resumen para persistencia)

Semilla de RNG, orden de mesa, ranking inicial, fase/turno/ronda activos, versión, y por participante: tipo, color, objetivo, canjes hechos, tarjetas (con flag de premio cobrado), estado de eliminación. Por país: dueño y ejércitos. Más historial: tirada de dados, orígenes/destinos, refuerzos y movimientos.

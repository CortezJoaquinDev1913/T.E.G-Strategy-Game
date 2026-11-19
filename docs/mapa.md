# Mapa (original)

Mapa **original** de 50 países y 6 continentes para el motor, el tablero SVG y el bot.
Las formas, nombres y fronteras no reproducen el tablero comercial.

> **Archivo generado.** La fuente de verdad legible por máquina es [`mapa.json`](mapa.json).
> Este documento es una vista de ese JSON. Los ids son **estables** (kebab-case ASCII) y son las claves que usan
> la API y la base de datos.

## Convenciones
- Dos países son **limítrofes** si comparten una frontera terrestre (`land`) **o** están unidos por un puente sobre el agua (`bridge`). Para atacar y reagrupar ambos tipos valen igual.
- Las fronteras son **simétricas** (no dirigidas) y no hay duplicadas.
- El **símbolo de tarjeta** sigue el orden de la tabla de países (galeón, globo, cañón, repetido): 17 galeones, 17 globos y 16 cañones.

## Continentes

| Continente | Países | Bonus de refuerzo |
|---|---|---|
| América del Norte | 10 | 5 |
| América del Sur | 6 | 3 |
| Europa | 9 | 5 |
| África | 7 | 3 |
| Asia | 14 | 7 |
| Oceanía | 4 | 2 |
| **Total** | **50** | |

## Países y limítrofes

### América del Norte (10)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `borealia` | Borealia | Galeón | `cabo-helado`, `costa-ambar`, `lagos-norte` |
| `cabo-helado` | Cabo Helado | Globo | `borealia`, `isla-niebla` (puente), `lagos-norte`, `valle-brumoso` |
| `lagos-norte` | Lagos del Norte | Cañón | `borealia`, `cabo-helado`, `costa-ambar`, `llanura-alta`, `valle-brumoso` |
| `costa-ambar` | Costa Ámbar | Galeón | `borealia`, `lagos-norte`, `llanura-alta`, `sierra-roja` |
| `sierra-roja` | Sierra Roja | Globo | `costa-ambar`, `llanura-alta`, `peninsula-sol` |
| `llanura-alta` | Llanura Alta | Cañón | `costa-ambar`, `golfo-dorado`, `lagos-norte`, `sierra-roja`, `valle-brumoso` |
| `valle-brumoso` | Valle Brumoso | Galeón | `cabo-helado`, `golfo-dorado`, `lagos-norte`, `llanura-alta` |
| `golfo-dorado` | Golfo Dorado | Globo | `istmo-verde`, `llanura-alta`, `peninsula-sol`, `valle-brumoso` |
| `peninsula-sol` | Península del Sol | Cañón | `golfo-dorado`, `istmo-verde`, `sierra-roja` |
| `istmo-verde` | Istmo Verde | Galeón | `cuenca-esmeralda`, `golfo-dorado`, `peninsula-sol` |

### América del Sur (6)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `cuenca-esmeralda` | Cuenca Esmeralda | Globo | `cordillera-gris`, `costa-cobriza`, `istmo-verde`, `selva-madre` |
| `cordillera-gris` | Cordillera Gris | Cañón | `archipielago-aurora` (puente), `cuenca-esmeralda`, `pampa-larga`, `selva-madre`, `tierra-austral` |
| `selva-madre` | Selva Madre | Galeón | `cordillera-gris`, `costa-cobriza`, `cuenca-esmeralda`, `pampa-larga` |
| `costa-cobriza` | Costa Cobriza | Globo | `costa-salina` (puente), `cuenca-esmeralda`, `pampa-larga`, `selva-madre` |
| `pampa-larga` | Pampa Larga | Cañón | `cordillera-gris`, `costa-cobriza`, `selva-madre`, `tierra-austral` |
| `tierra-austral` | Tierra Austral | Galeón | `cordillera-gris`, `pampa-larga` |

### Europa (9)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `brumaria` | Brumaria | Globo | `albaran`, `estepa-norte`, `isla-niebla` (puente), `valdoria` |
| `isla-niebla` | Isla Niebla | Cañón | `albaran` (puente), `brumaria` (puente), `cabo-helado` (puente) |
| `albaran` | Albarán | Galeón | `brumaria`, `cierzo`, `isla-niebla` (puente), `valdoria` |
| `cierzo` | Cierzo | Globo | `albaran`, `dunas-rojas` (puente), `montealto`, `valdoria` |
| `valdoria` | Valdoria | Cañón | `albaran`, `brumaria`, `cierzo`, `estepa-norte`, `marca-del-alba`, `montealto` |
| `montealto` | Montealto | Galeón | `cierzo`, `marca-del-alba`, `puerto-sirio`, `valdoria` |
| `estepa-norte` | Estepa Norte | Globo | `brumaria`, `marca-del-alba`, `taiga-eterna`, `valdoria` |
| `marca-del-alba` | Marca del Alba | Cañón | `estepa-alta`, `estepa-norte`, `montealto`, `puerto-sirio`, `valdoria` |
| `puerto-sirio` | Puerto Sirio | Galeón | `desierto-ocre`, `marca-del-alba`, `montealto`, `oasis-largo` |

### África (7)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `dunas-rojas` | Dunas Rojas | Globo | `cierzo` (puente), `costa-salina`, `oasis-largo`, `savana-alta` |
| `oasis-largo` | Oasis Largo | Cañón | `desierto-ocre`, `dunas-rojas`, `lagos-altos`, `puerto-sirio`, `savana-alta` |
| `costa-salina` | Costa Salina | Galeón | `costa-cobriza` (puente), `dunas-rojas`, `savana-alta`, `selva-umbria` |
| `savana-alta` | Savana Alta | Globo | `costa-salina`, `dunas-rojas`, `lagos-altos`, `oasis-largo`, `selva-umbria` |
| `selva-umbria` | Selva Umbría | Cañón | `cabo-tormenta`, `costa-salina`, `lagos-altos`, `savana-alta` |
| `lagos-altos` | Lagos Altos | Galeón | `cabo-tormenta`, `oasis-largo`, `savana-alta`, `selva-umbria` |
| `cabo-tormenta` | Cabo Tormenta | Globo | `lagos-altos`, `selva-umbria` |

### Asia (14)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `taiga-eterna` | Taiga Eterna | Cañón | `cumbres-azur`, `estepa-alta`, `estepa-norte`, `montes-jade` |
| `estepa-alta` | Estepa Alta | Galeón | `desierto-ocre`, `marca-del-alba`, `meseta-zafiro`, `montes-jade`, `taiga-eterna` |
| `montes-jade` | Montes de Jade | Globo | `cumbres-azur`, `estepa-alta`, `meseta-zafiro`, `sierra-ambar`, `taiga-eterna`, `valle-bambu` |
| `cumbres-azur` | Cumbres Azur | Cañón | `costa-loto`, `montes-jade`, `sierra-ambar`, `taiga-eterna` |
| `desierto-ocre` | Desierto Ocre | Galeón | `estepa-alta`, `llanos-marfil`, `meseta-zafiro`, `oasis-largo`, `puerto-sirio` |
| `meseta-zafiro` | Meseta Zafiro | Globo | `desierto-ocre`, `estepa-alta`, `llanos-marfil`, `montes-jade`, `valle-bambu` |
| `sierra-ambar` | Sierra Ámbar | Cañón | `costa-loto`, `cumbres-azur`, `montes-jade`, `valle-bambu` |
| `costa-loto` | Costa Loto | Galeón | `cumbres-azur`, `delta-perla`, `estrecho-lunar` (puente), `sierra-ambar` |
| `valle-bambu` | Valle Bambú | Globo | `cuenca-azafran`, `delta-perla`, `llanos-marfil`, `meseta-zafiro`, `montes-jade`, `sierra-ambar` |
| `llanos-marfil` | Llanos Marfil | Cañón | `cuenca-azafran`, `desierto-ocre`, `meseta-zafiro`, `puerto-coral`, `valle-bambu` |
| `cuenca-azafran` | Cuenca Azafrán | Galeón | `delta-perla`, `llanos-marfil`, `puerto-coral`, `valle-bambu` |
| `delta-perla` | Delta Perla | Globo | `costa-loto`, `cuenca-azafran`, `puerto-coral`, `valle-bambu` |
| `puerto-coral` | Puerto Coral | Cañón | `cuenca-azafran`, `delta-perla`, `llanos-marfil` |
| `estrecho-lunar` | Estrecho Lunar | Galeón | `costa-loto` (puente), `isla-tortuga` (puente) |

### Oceanía (4)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `isla-tortuga` | Isla Tortuga | Globo | `costa-arrecife`, `estrecho-lunar` (puente), `tierra-roja` |
| `costa-arrecife` | Costa Arrecife | Cañón | `archipielago-aurora`, `isla-tortuga`, `tierra-roja` |
| `tierra-roja` | Tierra Roja | Galeón | `archipielago-aurora`, `costa-arrecife`, `isla-tortuga` |
| `archipielago-aurora` | Archipiélago Aurora | Globo | `cordillera-gris` (puente), `costa-arrecife`, `tierra-roja` |

## Puentes sobre el agua

| Desde | Hasta |
|---|---|
| Albarán | Isla Niebla |
| Archipiélago Aurora | Cordillera Gris |
| Brumaria | Isla Niebla |
| Cabo Helado | Isla Niebla |
| Cierzo | Dunas Rojas |
| Costa Cobriza | Costa Salina |
| Costa Loto | Estrecho Lunar |
| Estrecho Lunar | Isla Tortuga |

## Conexiones entre continentes

| Continentes | Fronteras |
|---|---|
| América del Sur ↔ Oceanía | Archipiélago Aurora–Cordillera Gris (puente) |
| América del Norte ↔ Europa | Cabo Helado–Isla Niebla (puente) |
| África ↔ Europa | Cierzo–Dunas Rojas (puente); Oasis Largo–Puerto Sirio |
| África ↔ América del Sur | Costa Cobriza–Costa Salina (puente) |
| América del Norte ↔ América del Sur | Cuenca Esmeralda–Istmo Verde |
| África ↔ Asia | Desierto Ocre–Oasis Largo |
| Asia ↔ Europa | Desierto Ocre–Puerto Sirio; Estepa Alta–Marca del Alba; Estepa Norte–Taiga Eterna |
| Asia ↔ Oceanía | Estrecho Lunar–Isla Tortuga (puente) |

## Invariantes (cada uno debe tener un test xUnit)
1. Hay exactamente 50 países con ids únicos, repartidos así: América del Norte 10, América del Sur 6, Europa 9, África 7, Asia 14, Oceanía 4.
2. El grafo es simétrico, sin auto-fronteras ni duplicadas, y **conexo** (se llega de cualquier país a cualquier otro).
3. Cada continente es **conexo internamente**, lo que habilita refuerzos por continente y objetivos de «3 limítrofes entre sí».
4. Todo país tiene al menos 2 limítrofes (ninguno queda aislado ni con una sola salida).
5. Símbolos de tarjeta: 17 galeones, 17 globos, 16 cañones.
6. Los continentes alcanzan los cupos de los 15 objetivos secretos (por ejemplo, Europa ≥ 7 y Asia ≥ 4).
7. Los puentes son los 8 de la tabla anterior.

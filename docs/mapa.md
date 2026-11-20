# Mapa

Mapa de 50 países y 6 continentes para el motor, el tablero SVG y el bot.
Los **nombres** de los países son los de la geografía real. Las **fronteras, el dibujo y el arte** son propios:
el grafo de adyacencias se armó a partir de la geografía y de los ejemplos del reglamento (por ejemplo
Francia–España, Chile–Australia y Turquía–Egipto por puente), y no pretende ser idéntico a ningún tablero comercial.

> **Archivo generado.** La fuente de verdad legible por máquina es [`mapa.json`](mapa.json).
> Este documento es una vista de ese JSON. Los ids son **estables** (kebab-case ASCII, sin tildes) y son las claves
> que usan la API y la base de datos.

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
| África | 6 | 3 |
| Asia | 15 | 7 |
| Oceanía | 4 | 2 |
| **Total** | **50** | |

## Países y limítrofes

### América del Norte (10)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `alaska` | Alaska | Galeón | `kamchatka` (puente), `yukon` |
| `california` | California | Globo | `mexico`, `nueva-york`, `oregon` |
| `canada` | Canadá | Cañón | `labrador`, `nueva-york`, `oregon`, `terranova`, `yukon` |
| `groenlandia` | Groenlandia | Galeón | `islandia` (puente), `labrador` (puente) |
| `labrador` | Labrador | Globo | `canada`, `groenlandia` (puente), `terranova` |
| `mexico` | México | Cañón | `california`, `colombia`, `nueva-york` |
| `nueva-york` | Nueva York | Galeón | `california`, `canada`, `mexico`, `oregon`, `terranova` |
| `oregon` | Oregón | Globo | `california`, `canada`, `nueva-york`, `yukon` |
| `terranova` | Terranova | Cañón | `canada`, `labrador`, `nueva-york` |
| `yukon` | Yukon | Galeón | `alaska`, `canada`, `oregon` |

### América del Sur (6)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `argentina` | Argentina | Globo | `brasil`, `chile`, `uruguay` |
| `brasil` | Brasil | Cañón | `argentina`, `colombia`, `peru`, `sahara` (puente), `uruguay` |
| `chile` | Chile | Galeón | `argentina`, `australia` (puente), `peru` |
| `colombia` | Colombia | Globo | `brasil`, `mexico`, `peru` |
| `peru` | Perú | Cañón | `brasil`, `chile`, `colombia` |
| `uruguay` | Uruguay | Galeón | `argentina`, `brasil` |

### Europa (9)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `alemania` | Alemania | Globo | `francia`, `italia`, `polonia`, `suecia` (puente) |
| `espana` | España | Cañón | `francia`, `gran-bretana` (puente), `sahara` (puente) |
| `francia` | Francia | Galeón | `alemania`, `espana`, `gran-bretana` (puente), `italia` |
| `gran-bretana` | Gran Bretaña | Globo | `espana` (puente), `francia` (puente), `islandia` (puente) |
| `islandia` | Islandia | Cañón | `gran-bretana` (puente), `groenlandia` (puente) |
| `italia` | Italia | Galeón | `alemania`, `francia` |
| `polonia` | Polonia | Globo | `alemania`, `rusia` |
| `rusia` | Rusia | Cañón | `aral`, `iran`, `polonia`, `suecia`, `turquia` |
| `suecia` | Suecia | Galeón | `alemania` (puente), `rusia` |

### África (6)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `egipto` | Egipto | Globo | `etiopia`, `israel`, `sahara`, `turquia` (puente) |
| `etiopia` | Etiopía | Cañón | `arabia` (puente), `egipto`, `madagascar` (puente), `sahara`, `zaire` |
| `madagascar` | Madagascar | Galeón | `etiopia` (puente), `sudafrica` (puente) |
| `sahara` | Sahara | Globo | `brasil` (puente), `egipto`, `espana` (puente), `etiopia`, `zaire` |
| `sudafrica` | Sudáfrica | Cañón | `madagascar` (puente), `zaire` |
| `zaire` | Zaire | Galeón | `etiopia`, `sahara`, `sudafrica` |

### Asia (15)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `arabia` | Arabia | Globo | `etiopia` (puente), `iran`, `israel` |
| `aral` | Aral | Cañón | `china`, `gobi`, `iran`, `mongolia`, `rusia`, `tartaria` |
| `china` | China | Galeón | `aral`, `gobi`, `india`, `japon` (puente), `malasia`, `mongolia` |
| `gobi` | Gobi | Globo | `aral`, `china`, `mongolia` |
| `india` | India | Cañón | `china`, `iran`, `malasia` |
| `iran` | Irán | Galeón | `arabia`, `aral`, `india`, `rusia`, `turquia` |
| `israel` | Israel | Globo | `arabia`, `egipto`, `turquia` |
| `japon` | Japón | Cañón | `china` (puente), `kamchatka` (puente) |
| `kamchatka` | Kamchatka | Galeón | `alaska` (puente), `japon` (puente), `mongolia`, `siberia` |
| `malasia` | Malasia | Globo | `borneo` (puente), `china`, `india`, `sumatra` (puente) |
| `mongolia` | Mongolia | Cañón | `aral`, `china`, `gobi`, `kamchatka`, `siberia`, `tartaria` |
| `siberia` | Siberia | Galeón | `kamchatka`, `mongolia`, `taimir`, `tartaria` |
| `taimir` | Taimir | Globo | `siberia`, `tartaria` |
| `tartaria` | Tartaria | Cañón | `aral`, `mongolia`, `siberia`, `taimir` |
| `turquia` | Turquía | Galeón | `egipto` (puente), `iran`, `israel`, `rusia` |

### Oceanía (4)

| Id | País | Tarjeta | Limítrofes |
|---|---|---|---|
| `australia` | Australia | Globo | `borneo` (puente), `chile` (puente), `java` (puente) |
| `borneo` | Borneo | Cañón | `australia` (puente), `java` (puente), `malasia` (puente), `sumatra` (puente) |
| `java` | Java | Galeón | `australia` (puente), `borneo` (puente), `sumatra` (puente) |
| `sumatra` | Sumatra | Globo | `borneo` (puente), `java` (puente), `malasia` (puente) |

## Puentes sobre el agua

| Desde | Hasta |
|---|---|
| Alaska | Kamchatka |
| Alemania | Suecia |
| Arabia | Etiopía |
| Australia | Borneo |
| Australia | Chile |
| Australia | Java |
| Borneo | Java |
| Borneo | Malasia |
| Borneo | Sumatra |
| Brasil | Sahara |
| China | Japón |
| Egipto | Turquía |
| España | Gran Bretaña |
| España | Sahara |
| Etiopía | Madagascar |
| Francia | Gran Bretaña |
| Gran Bretaña | Islandia |
| Groenlandia | Islandia |
| Groenlandia | Labrador |
| Japón | Kamchatka |
| Java | Sumatra |
| Madagascar | Sudáfrica |
| Malasia | Sumatra |

## Conexiones entre continentes

| Continentes | Fronteras |
|---|---|
| América del Norte ↔ Asia | Alaska–Kamchatka (puente) |
| África ↔ Asia | Arabia–Etiopía (puente); Egipto–Israel; Egipto–Turquía (puente) |
| Asia ↔ Europa | Aral–Rusia; Irán–Rusia; Rusia–Turquía |
| América del Sur ↔ Oceanía | Australia–Chile (puente) |
| Asia ↔ Oceanía | Borneo–Malasia (puente); Malasia–Sumatra (puente) |
| África ↔ América del Sur | Brasil–Sahara (puente) |
| América del Norte ↔ América del Sur | Colombia–México |
| África ↔ Europa | España–Sahara (puente) |
| América del Norte ↔ Europa | Groenlandia–Islandia (puente) |

## Invariantes (cada uno debe tener un test xUnit)
1. Hay exactamente 50 países con ids únicos, repartidos así: América del Norte 10, América del Sur 6, Europa 9, África 6, Asia 15, Oceanía 4.
2. El grafo es simétrico, sin auto-fronteras ni duplicadas, y **conexo** (se llega de cualquier país a cualquier otro).
3. Cada continente es **conexo internamente**, lo que habilita refuerzos por continente y objetivos de «3 limítrofes entre sí».
4. Todo país tiene al menos 2 limítrofes (ninguno queda aislado ni con una sola salida).
5. Símbolos de tarjeta: 17 galeones, 17 globos, 16 cañones.
6. Los continentes alcanzan los cupos de los 15 objetivos secretos (por ejemplo, Europa ≥ 7 y Asia ≥ 4).
7. Los puentes son los 23 de la tabla anterior.

# Instant Transitions para Graveyard Keeper 2

[English](README.md) | **Español**

Cada puerta de Graveyard Keeper 2 congela el juego con la pantalla en negro entre 1 y 2 segundos, y más mientras más mods tengas. **Instant Transitions** deja las puertas y los viajes por el mapa en unos **0.1 segundos**, y la primera visita a un lugar casi igual de rápida.

> ⚠️ **Beta (0.9.1).** Las puertas y la carga están medidas y probadas. Las sesiones largas todavía se están probando: si algo se siente raro, [repórtalo](#reportar-errores) con tu registro. Ayuda muchísimo.

![Una puerta y un viaje por el mapa lado a lado: 1.5 s sin mods, 0.35 s con Instant Transitions](docs/images/doors-vs-vanilla.gif)

*Grabado con la 0.9.0, cuando las puertas todavía tenían un fundido de 0.15 s. Desde la 0.9.1 no hay fundido, así que son aún más rápidas.*

---

## Qué hace

| | Sin mods | Con Instant Transitions |
|---|---|---|
| Una puerta (casa ↔ patio) | 1.4–1.9 s (hasta 2.6 s con muchos mods) | **~0.1 s** |
| Viajar por el mapa | ~1.8 s | **~0.1 s** |
| Primera visita a un lugar | +0.3–1.3 s mientras carga | **casi igual que cualquier otra visita** |
| Cargar una partida | lo normal | unos 5 s más, con una línea de avance |

**El congelón en cada puerta.** Con la pantalla en negro, el juego hace una limpieza completa de memoria en cada puerta: libera recursos sin usar, recoge basura y busca 24 veces, por todo lo cargado, componentes que solo sirven en el editor. Eso es casi toda la espera. Este mod nunca la hace toda de golpe: cada tercera puerta quita uno de los 24 tipos de componentes de editor (unos 30 ms), así que nada se acumula, y una puerta libera los recursos sin usar cada 10 minutos o cuando la memoria crece 300 MB (unos 0.3 s). La recolección de basura del juego ya trabaja en pedacitos mientras juegas. Al cargar partida, la limpieza completa se hace como siempre.

**Sin fundidos.** En las puertas que usas y en los viajes por el mapa desaparecen los dos fundidos de 0.3 s y la pausa extra de 0.3 s en negro: el lugar nuevo simplemente aparece. Las peleas y las escenas de historia conservan los tiempos del juego. Si quieres un fundido suave, ajusta `FadeSeconds`.

**Primeras visitas rápidas.** Mientras carga una partida, el juego precarga una lista fija de las piezas con las que se dibujan los lugares. Lo que no está en esa lista (por ejemplo, lo que construiste en tu patio) se carga del disco la primera vez que lo ves, en un cuadro congelado. Instant Transitions precarga el resto del mapa al final de la pantalla de carga, varias piezas a la vez, para que la primera visita sea casi tan rápida como las siguientes. Una línea bajo la barra de carga muestra el avance. Usa unos 500 MB más de memoria y se omite en PCs con menos de 7 GB de RAM.

![La pantalla de carga: el juego carga y luego Instant Transitions prepara todos los lugares del mapa](docs/images/loading.gif)

**Compáralo tú mismo.** Presiona **Ctrl + Shift + O** mientras juegas para prender o apagar el mod. El cambio aplica desde la siguiente puerta y un aviso muestra cómo quedó.

---

## Instalación (unos 2 minutos)

Solo se hace una vez. Cierra el juego antes de empezar.

### Paso 1: Descarga dos archivos

1. **BepInEx 5**, el cargador de mods (sáltatelo si ya juegas con otros mods de BepInEx): en su [página oficial](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5), descarga **`BepInEx_win_x64_5.4.23.5.zip`**.
2. **Instant Transitions**: en [Releases](../../releases) o en la [página de Nexus Mods](https://www.nexusmods.com/graveyardkeeper2/mods/206), descarga **`InstantTransitions-x.y.z.zip`**.

### Paso 2: Copia la dirección de la carpeta del juego

1. Abre **Steam** y ve a tu **Biblioteca**.
2. Clic derecho en **Graveyard Keeper 2** → **Administrar** → **Ver archivos locales**. Se abre una carpeta: es la *carpeta del juego*.
3. Haz clic en la **barra de dirección** de arriba y presiona **Ctrl + C** para copiarla.

### Paso 3: Extrae los dos archivos en esa carpeta

Hazlo primero con BepInEx y luego con Instant Transitions:

1. Busca el archivo que descargaste (normalmente en **Descargas**).
2. Clic derecho → **Extraer todo…**
3. Borra la ruta del cuadro y presiona **Ctrl + V** para pegar la dirección de la carpeta del juego.
4. Clic en **Extraer**. Si Windows pregunta por reemplazar archivos, elige **Reemplazar**.

### Paso 4: Comprueba que funcionó

Abre el juego y carga tu partida. Cerca del final de la pantalla de carga verás *"Preparando los lugares para que las puertas sean rápidas…"* bajo la barra. Luego cruza cualquier puerta. 🎉

<details>
<summary><b>Actualizar o desinstalar</b></summary>

- **Actualizar:** extrae la versión nueva igual, eligiendo **Reemplazar**.
- **Desinstalar:** borra la carpeta `BepInEx\plugins\InstantTransitions` dentro de la carpeta del juego. Tus partidas nunca se tocan.

</details>

---

## Ajustes

Los ajustes están en `BepInEx\config\verto13.gk2.instanttransitions.cfg` (se crea la primera vez que juegas; ábrelo con el Bloc de notas con el juego cerrado). Cada ajuste viene explicado adentro:

| Ajuste | De fábrica | Qué hace |
|---|---|---|
| `Enabled` | `true` | `false` = todo como el juego sin mods; el mod solo mide las puertas. |
| `ToggleKey` | `Ctrl + Shift + O` | Prende y apaga el mod mientras juegas. |
| `FullCleanupEveryMinutes` | `10` | Cada cuánto una puerta libera los recursos sin usar (unos 0.3 s). Las puertas nunca hacen la limpieza completa del juego. |
| `FullCleanupWhenMemoryGrowsMB` | `300` | O antes, si la memoria creció esto. |
| `FadeSeconds` | `0` | Duración de cada fundido en puertas y viajes. `0` = sin fundido (el juego: 0.3). |
| `BlackPauseSeconds` | `0` | Pausa en negro antes de moverte (el juego: 0.3). |
| `PreloadPlaces` | `true` | Precargar todo el mapa al cargar partida. |
| `PreloadMaxSeconds` | `20` | La precarga nunca alarga la carga más que esto. |

## Qué cambia / compatibilidad

- No toca tus partidas ni cambia el balance. Puedes apagarlo (o quitarlo) cuando quieras.
- Usa **un solo** mod que cambie las puertas o la limpieza de memoria del juego; dos se pelearían por lo mismo.
- Probado con Graveyard Keeper 2 1.007.1 y BepInEx 5.4.23.5, junto con una docena de mods más. También carga en versiones anteriores del juego (desde la 1.004.2): lo que el juego todavía no tiene se apaga solo, con una línea en el registro.

## Reportar errores

Abre un [issue](../../issues/new) e incluye:

1. Qué hiciste y qué pasó.
2. El archivo `BepInEx\LogOutput.log` de la carpeta del juego. Ahí queda cada puerta, pantalla de carga y limpieza (líneas `[Door]`, `[Load]`, `[Preload]` y `[Clean-up]`), y con eso los problemas se encuentran rápido.

## Licencia

[MIT](LICENSE). Hecho por VERTO13, con ayuda de IA.

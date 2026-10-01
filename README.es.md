# Instant Transitions para Graveyard Keeper 2

[English](README.md) | **Español**

Cada puerta de Graveyard Keeper 2 congela el juego con la pantalla en negro entre 1 y 2 segundos, y más mientras más mods tengas. Con **Instant Transitions** todo el mapa se siente como un solo lugar: **caminas hacia** una puerta y, en el mismo instante, estás del otro lado, todavía caminando. Sin pantalla negra, sin congelón, sin "[E] Entrar".

> ⚠️ **Beta (0.10.0).** Las puertas, los viajes por el mapa y la carga están medidos y probados, y se revisaron todas las puertas del mapa. Si algo se siente raro, [repórtalo](#reportar-errores) con tu registro. Ayuda muchísimo.

![Cruzando puertas: sin mods y luego con Instant Transitions](docs/images/walk-in.gif)

---

## Qué hace

| | Sin mods | Con Instant Transitions |
|---|---|---|
| Una puerta (casa ↔ patio) | 1.4–1.9 s en negro (hasta 2.6 s con muchos mods) | **~0.05 s, sin negro** |
| Viajar por el mapa | ~1.8 s en negro | **~0.05 s, sin negro** |
| Primera visita a un lugar | +0.3–1.3 s mientras carga | **igual que cualquier otra visita** |
| Cargar una partida | lo normal | unos 5 s más, con una línea de avance |

**Un corte en lugar de un fundido.** Las puertas y los viajes dentro de la misma escena (y todo el mapa es una sola escena: casa, patio, taberna, iglesia, morgue…) ya no pasan por negro. Te mueve en el mismo cuadro, como un corte en una película: la vista de antes se queda unos tres cuadros mientras se dibuja el lugar nuevo, y luego desaparece. Las puertas que llevan a otra escena siguen con un negro rápido.

**Entra caminando.** No hace falta apretar E: camina hacia una puerta y la cruzas justo al llegar, sin chocar con ella. Donde el piso se acaba antes de la puerta (el porche de la taberna, arriba de las escaleras del cuartel), tu personaje sigue caminando hasta la puerta, o baja unos escalones, antes del corte. Solo se mueve el dibujo de tu personaje; tu posición y tu partida no cambian.

**Sin aviso y sin rebotes.** El "[E] Entrar" de esas puertas se oculta para que se sientan parte del mapa (**Ctrl + Shift + H** lo regresa). Las trampillas del piso siguen con la tecla, y también las escaleras de mano para escalar. La puerta por la que acabas de llegar espera a que sueltes la tecla de caminar, te des la vuelta o te alejes, así nunca rebotas sin querer. Durante las peleas, las puertas van solo con la tecla.

**Listo antes de llegar.** La primera vez en un rato que cruzas una puerta, el juego carga las piezas del otro lado antes de dejarte mover, y el corte tendría que esperarlo. Al acercarte a una puerta, Instant Transitions carga su otro lado por adelantado, fuera de cámara y un poco en cada cuadro, para que el corte nunca espere.

**Sin congelón en cada puerta.** Con la pantalla en negro, el juego hace una limpieza completa de memoria en cada puerta: libera recursos sin usar, recoge basura y busca 24 veces, por todo lo cargado, componentes que solo sirven en el editor. Eso es casi toda la espera. Este mod nunca la hace toda de golpe: cada tercera puerta quita uno de los 24 tipos de componentes de editor (unos 30 ms), así que nada se acumula. Los recursos sin usar se liberan cada 10 minutos en una puerta que pasa por negro, o cuando la memoria crece mucho; al cargar partida siempre se hace la limpieza completa.

**Primeras visitas rápidas.** Mientras carga una partida, el juego precarga una lista fija de las piezas con las que se dibujan los lugares. Lo que no está en esa lista (por ejemplo, lo que construiste en tu patio) se carga del disco la primera vez que lo ves, en un cuadro congelado. Instant Transitions precarga el resto del mapa al final de la pantalla de carga, varias piezas a la vez. Una línea bajo la barra de carga muestra el avance. Usa unos 500 MB más de memoria y se omite en PCs con menos de 7 GB de RAM.

![La pantalla de carga: el juego carga y luego Instant Transitions prepara todos los lugares del mapa](docs/images/loading.gif)

**Teclas.** Ninguna choca con las del juego, y puedes cambiarlas en los ajustes:

| Tecla | Qué hace |
|---|---|
| **Ctrl + Shift + O** | Prende y apaga todo el mod, para comparar con el juego sin mods. |
| **Ctrl + Shift + C** | Prende y apaga entrar caminando. Apagado, todas las puertas van con E y muestran su aviso. |
| **Ctrl + Shift + H** | Muestra u oculta los avisos "[E] Entrar" de las puertas. |

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

Abre el juego y carga tu partida. Cerca del final de la pantalla de carga verás *"Preparando los lugares para que las puertas sean rápidas…"* bajo la barra. Luego camina hacia cualquier puerta. 🎉

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
| `HardCut` | `true` | Las puertas y los viajes en la misma escena cortan sin negro. `false` = un fundido rápido por negro. |
| `WalkIntoDoors` | `true` | Caminar hacia las puertas para cruzarlas. `false` = puertas solo con la tecla, como el juego. |
| `WalkInKey` | `Ctrl + Shift + C` | Prende y apaga entrar caminando mientras juegas. |
| `HideDoorPrompts` | `true` | Oculta el aviso "[E] Entrar" de las puertas a las que se entra caminando. |
| `PromptsKey` | `Ctrl + Shift + H` | Muestra u oculta esos avisos mientras juegas. |
| `FullCleanupEveryMinutes` | `10` | Cada cuánto una puerta por negro libera los recursos sin usar (unos 0.3 s). Las puertas nunca hacen la limpieza completa del juego. |
| `FullCleanupWhenMemoryGrowsMB` | `300` | O antes, si la memoria creció esto. Las puertas con corte solo lo hacen al doble. |
| `FadeSeconds` | `0` | Duración de cada fundido en las puertas que todavía pasan por negro. `0` = sin fundido (el juego: 0.3). |
| `BlackPauseSeconds` | `0` | Pausa en negro antes de moverte, en esas puertas (el juego: 0.3). |
| `PreloadPlaces` | `true` | Precargar todo el mapa al cargar partida. |
| `PreloadMaxSeconds` | `20` | La precarga nunca alarga la carga más que esto. |

## Qué cambia / compatibilidad

- No toca tus partidas ni cambia el balance. Puedes apagarlo (o quitarlo) cuando quieras.
- Usa **un solo** mod que cambie las puertas o la limpieza de memoria del juego; dos se pelearían por lo mismo.
- Probado con Graveyard Keeper 2 1.007.1 y BepInEx 5.4.23.5, junto con muchos otros mods. También carga en versiones anteriores del juego (desde la 1.004.2): lo que el juego todavía no tiene se apaga solo, con una línea en el registro.

## Reportar errores

Abre un [issue](../../issues/new) e incluye:

1. Qué hiciste y qué pasó (qué puerta, si es de una puerta).
2. El archivo `BepInEx\LogOutput.log` de la carpeta del juego. Ahí queda cada puerta, pantalla de carga y limpieza (líneas `[Door]`, `[Walk-in]`, `[Load]`, `[Preload]`, `[Prewarm]` y `[Clean-up]`), y con eso los problemas se encuentran rápido.

## Licencia

[MIT](LICENSE). Hecho por VERTO13, con ayuda de IA.

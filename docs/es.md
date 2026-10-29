# Mod Manager — mods, juegos y su documentación

Mod Manager es la pantalla **Mods y colecciones** del menú principal. Sirve para ver qué mods hay instalados, leer qué hace cada uno, instalar o actualizar mods desde el catálogo en línea, desactivar mods y mirar dentro de los juegos ya hechos para ver de qué mods están hechos.

Forma parte de la carcasa que siempre se carga junto con el menú, así que funciona incluso antes de que empieces una partida.

## Cómo abrirlo

En el menú principal elige **Mods y colecciones**. La pantalla tiene dos pestañas:

- **Mods** — todos los mods que el juego conoce: los instalados y los que ofrece el catálogo.
- **Colecciones** — los juegos ya hechos y el esquema de mods con el que está montado cada uno.

**← Menú** vuelve al menú principal. El botón **?** de la esquina superior derecha, o **F1**, activa las sugerencias contextuales: pasa el ratón sobre un control para ver qué hace, haz clic para fijar la sugerencia y pulsa **Esc** para salir.

## Explorar los mods

![La pestaña Mods: filtros a la izquierda, una cuadrícula de tarjetas de mods con imágenes de portada, el cuadro de búsqueda con una sugerencia abierta, el orden y el botón Refresh catalog.](assets/mods-list.jpg)

Cada tarjeta muestra la portada del mod, su título, el autor, el catálogo del que viene, cuántos juegos lo usan (**en 4 colecciones**), su estado y versión, y un interruptor de encendido/apagado.

- **Buscar mods...** filtra por título, autor o identificador.
- **Por nombre / Por autor / Por estado** ordenan la lista.
- **Filtros** a la izquierda acotan la lista por **Estado** (No instalado, Instalado, Hay actualización, Hay una versión anterior, Modificado localmente, Desactivado) y por **Categorías** como Historia, Jugabilidad, Visual, Audio, Biblioteca, Motor y Herramientas. **Restablecer** limpia filtros y búsqueda.
- **Actualizar catálogo** descarga la lista actual de mods publicados.

*Modificado localmente* significa que los archivos instalados difieren de la copia de esa misma versión en el catálogo; por ejemplo, un mod que estás editando tú.

## Leer sobre un mod

Haz clic en una tarjeta para abrir su página: la portada, el selector de versión, las acciones, la descripción corta del manifiesto del mod y, cuando el mod la ofrece, su **Documentación**: una guía completa como esta.

![Una página de mod: la portada y la versión de Audio Engine, los botones Reinstall y Disable, "in 4 collections", la descripción y la sección Documentation con su selector de idioma.](assets/mod-page.jpg)

La documentación es la guía de la versión **instalada**. Si eliges otra versión en el selector, un aviso te recuerda que la guía de abajo sigue siendo la del mod instalado.

**Idioma de la documentación.** El selector a la derecha del encabezado Documentación arranca en **Auto · idioma de la interfaz**: la guía sigue el idioma de la interfaz del juego. Si el mod no tiene guía en ese idioma, el gestor muestra primero la guía predeterminada del mod, luego la inglesa y luego la primera disponible; la línea sobre la guía indica el idioma realmente mostrado (por ejemplo `EN`). También puedes elegir un idioma en el selector. Un mod sin ninguna guía sigue mostrando la descripción de su manifiesto.

Para autores de mods: pon la guía en `docs/en.md` (las traducciones en `docs/<idioma>.md`, o `docs/default.md`) y las imágenes al lado, por ejemplo `docs/assets/screen.png`, referenciada como `assets/screen.png`. El visor entiende encabezados, párrafos, listas, citas, bloques de código, enlaces e imágenes PNG/JPEG/WebP; no ejecuta HTML, tablas ni animaciones.

## Instalar, actualizar, desactivar, eliminar

Los botones de la página del mod y el interruptor de su tarjeta:

- **Instalar / Actualizar / Volver a versión anterior / Reinstalar** descargan la versión elegida del catálogo a tu carpeta de usuario (`user://mods/`).
- **Desactivar / Activar** (o el interruptor de la tarjeta) deciden si el mod se carga. Un mod desactivado conserva sus archivos y los datos de tus partidas.
- **Eliminar** borra un mod descargado tras confirmar.

Después de cualquiera de estas acciones aparece un aviso: **Los cambios surtirán efecto al reiniciar el juego**. El propio Mod Manager no puede eliminarse ni desactivarse desde su propia pantalla.

**Instalado no es lo mismo que usado.** Un juego carga los mods que nombra su colección, más la carcasa del menú. Instalar un mod lo deja disponible; solo aparece en un juego cuando la colección de ese juego lo incluye.

## Juegos y colecciones

La pestaña **Colecciones** muestra primero los juegos ya hechos. Un juego es una *colección*: un conjunto de mods con nombre y el lugar donde empezar.

![La galería de juegos: tarjetas de Cthangover, My Novel y RPG Plus, cada una "Ready to play" con Mods 32/32, 11/11 y 36/36, y botones Edit scheme.](assets/games-gallery.jpg)

Una tarjeta te dice si está instalado todo lo que el juego necesita (**Listo para jugar**, o **Faltan N** / **Instalar (N)** / **Activar (N)**). Un solo botón instala o activa lo que falta. **Editar esquema** abre la estructura del juego; **＋ Crea tu propio juego** empieza una colección nueva.

### El esquema de un juego

![El esquema de My Novel: los bloques Story, Assets y 2D engine con sus mods, flechas de cada dependencia al mod que la necesita, y el Inspector mostrando el requisito de versión de My Novel.](assets/collection-scheme.jpg)

El esquema muestra las *mecánicas* del juego (bloques como Story, Assets, 2D engine) y los mods dentro de ellas. Las flechas van de una dependencia al mod que la necesita. Las posiciones de las tarjetas y los bloques son solo para leer: no deciden el orden de carga.

Selecciona un bloque o un mod para verlo en el **Inspector**. Ahí puedes cambiar la versión que exige el juego: **Cualquiera**, **Como mínimo**, **Exacta**, **Rama** (`^1` acepta cualquier 1.x) y otras. Es el requisito del juego, no la versión que tienes instalada. **Comprobar** enumera problemas; **Plan** muestra qué instalaría o activaría aplicar la colección.

![El Inspector de un mod cuyo requisito "Exactly 999.0.0" no coincide con la versión instalada 1.0.0: el requisito y la versión instalada aparecen en rojo.](assets/version-mismatch.jpg)

Otros botones del esquema: **+ Mecánica** y **Añadir mod** cambian el juego, **Duplicar** hace tu propia copia (y ofrece copiar las partidas), **JSON** muestra la definición en crudo, **Guardar** y **Restablecer** conservan o descartan los cambios, **Aplicar colección** instala y activa lo que enumera el plan. Un juego integrado que hayas cambiado se restaura con **Restaurar la integrada**; tus partidas se quedan.

Los cambios de un juego se aplican la próxima vez que arranque. Las partidas siguen sirviendo cuando cambian sus mods: los datos de los mods que salieron del juego se guardan aparte y vuelven con el mod.

## Si algo falla

1. **Un mod no aparece en un juego.** Comprueba que está activado y que la colección del juego lo incluye (abre **Editar esquema**).
2. **«No se pudo instalar».** Revisa la conexión a internet y prueba otra vez **Actualizar catálogo**.
3. **Una tarjeta dice Faltan o Problemas.** Abre su esquema y la pestaña **Comprobar**: enumera los mods que faltan, las diferencias de versión y los conflictos.
4. **Conflicto de catálogos.** El mismo mod y versión está publicado con contenido distinto por dos catálogos; fíate del catálogo en el que confíes.
5. **No hay sección de documentación.** El mod todavía no tiene guía en `docs/`; en su lugar se muestra la descripción del manifiesto.

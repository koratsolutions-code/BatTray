<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/header-dark.png" />
  <img src="docs/images/header-light.png" alt="BatTray — Your devices. One glance." width="296" />
</picture>

🌐 [English](README.md) · **Español**

BatTray muestra el nivel de batería de tus periféricos inalámbricos (ratón, teclado, auriculares, auriculares de
botón, mandos…) directamente en la barra de tareas de Windows: el icono del dispositivo junto a su porcentaje.

**Bluetooth y 2,4 GHz, en un solo lugar.** Windows solo muestra la batería de algunos dispositivos Bluetooth, y los
receptores USB de 2,4 GHz (dongles) no tienen un estándar de batería común: cada marca usa su propio protocolo, que
normalmente solo lee su propia app. BatTray lee los dos: dispositivos Bluetooth y receptores de 2,4 GHz de más de 70
protocolos y marcas, sin instalar el software del fabricante.

```
[🖱 50%]  [🎧 80%⚡]  [⌨ 12%]   ⌃  🔊  🔋97%   13:45
```

## Funciones
- **Widget en la barra de tareas** con el icono y el porcentaje de cada dispositivo (verde / ámbar / rojo),
  en la barra de cada monitor. Si no hay sitio, se ocultan primero los que tienen más batería; nunca tapa
  los botones de las apps.
- **Panel de detalle** (clic izquierdo en el widget o en el icono de la bandeja, o **Ctrl+Alt+B**): todos los
  dispositivos con barra de nivel, estado de carga, última lectura, origen y un **gráfico de los últimos 7 días**.
- **Estimación por tiempo de uso** para dispositivos que nunca envían su batería (muchos auriculares y ratones
  económicos): BatTray cuenta cuánto los usas y muestra una estimación como `~62%`.
- **Alertas de batería baja**: una notificación de Windows por descarga cuando un dispositivo llega al nivel crítico,
  o a su propio nivel (cada dispositivo puede tener uno distinto, o ninguno).
- **Aviso de carga completa**: te avisa cuando un dispositivo llega al 100 % para que lo desconectes.
- **A tu gusto**: elige el orden de los dispositivos, muestra el porcentaje siempre, solo cuando es bajo o nunca
  (solo iconos), y elige elementos pequeños, normales o grandes.
- **Icono y menú en la bandeja** (clic derecho), agrupados en **Bluetooth**, **2,4 GHz (receptor USB)** y **Mandos**.
- **Español e inglés**: sigue el idioma de Windows, o elige uno en Ajustes.
- **Tema claro y oscuro** en menús y ventanas (sigue a Windows o el que elijas); cualquier escalado DPI, barra que
  se oculta sola, apps a pantalla completa y reinicios del Explorador.
- **Actualización con un clic**: cuando sale una versión nueva (comprobación opcional), BatTray descarga el
  instalador desde GitHub, lo verifica y lo instala. **Informar de un problema** con un clic.

## Instalación
**[Descarga el último instalador](https://github.com/koratsolutions-code/BatTray/releases/latest)** y ejecuta
`BatTray-Setup-<versión>.msi`. Se instala **por usuario** (sin permisos de administrador)
en `%LocalAppData%\Programs\BatTray`, crea un acceso en el menú Inicio y arranca BatTray. Se desinstala desde
*Configuración › Aplicaciones*; el desinstalador también quita el inicio con Windows.

El instalador incluye una **guía de usuario** (menú Inicio → *BatTray User Guide*, o *User guide* en el menú de
BatTray). Si Windows muestra *"Windows protegió su PC"*, pulsa **Más información → Ejecutar de todas formas**:
el instalador aún no está firmado digitalmente.

## Dispositivos soportados
Más de 70 protocolos que cubren cientos de modelos. La mayoría de los protocolos de 2,4 GHz se han implementado a partir de
documentación pública y ningún usuario de BatTray los ha confirmado todavía: si el tuyo funciona (o no), cuéntanoslo.

| Origen | Dispositivos |
|---|---|
| Bluetooth LE | Ratones, teclados, auriculares y mandos modernos (servicio de batería GATT) |
| Bluetooth (Windows) | Auriculares clásicos y cualquier dispositivo cuya batería muestre Windows en Configuración |
| Auriculares de botón | **Apple AirPods / Beats** y auriculares **Google Fast Pair** (Pixel Buds, Jabra Elite, muchos Sony / JBL / Bose), leídos de sus emisiones Bluetooth; **Samsung Galaxy Buds** y **Nothing / CMF** por su servicio Bluetooth: izquierdo, derecho y estuche |
| Mandos | Xbox (USB, adaptador, Bluetooth), DualShock 4, DualSense / Edge, Switch Pro y compatibles, 8BitDo (Ultimate, Pro 2/3, SN30/SF30 Pro), GameSir (Nova, Cyclone 2, Cyclone Pro por Bluetooth), Flydigi (Vader 5 Pro, Apex 5), Steam Controller, BEITONG |
| HeadsetControl | Todos los auriculares que soporta [HeadsetControl](https://github.com/Sapd/HeadsetControl), si lo instalas tú (junto a BatTray.exe o en `%LocalAppData%\Programs\HeadsetControl`) |
| Receptores USB (2,4 GHz) | Ver abajo |

### Receptores de 2,4 GHz y dispositivos USB
A diferencia de Bluetooth, los receptores de 2,4 GHz no tienen un estándar de batería común: cada fabricante usa su
propio protocolo.

| Categoría | Marcas y modelos |
|---|---|
| Grandes marcas | **Logitech** (Unifying, Bolt, Lightspeed, auriculares G, dispositivos HID++ 1.0 antiguos), **Razer** (ratones y teclados inalámbricos; BlackShark V2 HyperSpeed / V2 Pro / V3 X), **SteelSeries** (Arctis 1/7/9/Pro/Nova 3P/5/7/Pro/Elite, Aerox, Prime, Rival), **Corsair** (Void/HS, dispositivos Bragi, Sabre V2 Pro), **HyperX** (Cloud Flight / Flight S / Alpha / II / II Core / III / III S / Stinger Core, Pulsefire Haste 2 / Mini), **ASTRO** A50 / A20, **Audeze** Maxwell, **Sony INZONE** H9 II / H5 / Buds, **Keychron** (Ultra-Link, receptores 1K y 4K, Nape Pro) |
| Ratones gaming | Glorious, Hitscan, G-Wolves, WLMouse, LAMZU, Arbiter, Pulsar (X2 V3, XS-1), ATK / VXE (también los dongles Nano), VGN, Scyrox, Endgame Gear, Ninjutso, Sprime, ZOWIE, VAXEE, Finalmouse, ROG, MSI, Cooler Master, Fantech, Machenike, Incott, EWEADN, Niceboy, Redragon, HECATE, Zenith, Chimera, Darmoshark, Rapoo VT, Ajazz AJ179 / AJ139, Delux, Motospeed, Attack Shark, Inphic, ARDOR Fury V2, Taidu, Zaopin, Dareu, Sharkoon |
| Teclados | MonsGeek, Akko, teclados con chip RongYuan (Attack Shark, AJAZZ, MSI…), AULA, MCHOSE, Royal Kludge, CHERRY, NuPhy, EPOMAKER, URX / EVO80 (VIA), teclados con chip Semico, Redragon, ATK / VGN / VXE |
| Auriculares | Razer, ASTRO, SteelSeries, HyperX, Corsair, Audeze, Sony INZONE, JBL Quantum, Jabra, AKG N9 Hybrid, ENDORFY Viro, NZXT Lift, Fractal Scape, MOONDROP GM01, MAONO PD200W, DARKRIM, MCHOSE, ATK, VGN, Alienware AW920H |
| Cualquier otro | Receptores que siguen la especificación HID de batería |

No soportados (necesitan comandos que no son de solo lectura, así que BatTray no los envía): Logitech G435, Razer
BlackShark V2 Pro 2023 / V3 / Barracuda, Turtle Beach Stealth 700 Gen 3, Poly BT600, RAWM, teclados PMO / WOBKEY.

**Seguridad:** solo se envían consultas de lectura, y solo a receptores que un protocolo reconoce con certeza.
Nunca se usan comandos que cambien ajustes o el modo del dongle. A los receptores no reconocidos nunca se les
escribe nada; usa **Informar de un problema…** (o *Ajustes → Guardar informe de diagnóstico…*) para registrar
su estructura HID y poder añadirlos.

**Privacidad:** BatTray solo se conecta a GitHub: para la comprobación diaria opcional de actualizaciones (una
consulta anónima del número de la última versión; se desactiva en Ajustes) y, cuando eliges instalar una
actualización, para descargarla. No se envía nada sobre ti ni tus dispositivos. El historial de batería se queda en
tu PC.

> Los protocolos de Razer, SteelSeries, Corsair, HyperX, PlayStation, Aurora, MonsGeek/Akko, Keychron, Rapoo,
> Ajazz, Darmoshark y MCHOSE están implementados a partir de
> documentación pública y drivers de código abierto y cubiertos con tests, pero aún no se han verificado con hardware real.

## Menú
Haz clic derecho en el widget o en el icono de BatTray del área de notificación. Los dispositivos se agrupan por
conexión: **Bluetooth**, **2,4 GHz (receptor USB)** y **Mandos**.
- **Cada dispositivo** aparece con su nivel: desmárcalo para ocultarlo de la barra.
- **Los receptores USB que no envían batería** aparecen en *2,4 GHz* con un submenú (ver abajo), o puedes elegir
  **Ocultar este receptor**; por ejemplo, el dongle de un teclado que usas por Bluetooth. Los ocultos se pueden
  volver a mostrar desde *Receptores ocultos*. Los dispositivos internos con cable (como el teclado de un portátil)
  nunca se listan.
- **Personalizar dispositivos…**: cambia el nombre de cualquier dispositivo y elige su icono del set incluido
  (ratón, teclado, auricular, auriculares de botón, altavoz, micrófono, mando, trackball, lápiz, teléfono, tablet,
  reloj, batería, Bluetooth) o sube tu propia imagen (PNG, JPG, BMP, GIF o ICO; se reduce a 64×64 y se guarda en
  la carpeta de ajustes de BatTray). Arrastra los dispositivos (o usa las flechas) para elegir su **orden en la
  barra**, y elige el **aviso de batería baja** de cada uno: el nivel general, uno propio o ninguno.
- **Actualización disponible** (cuando la hay): abre una ventana que descarga el nuevo instalador desde los
  releases de GitHub de este proyecto, comprueba que su tamaño y su SHA-256 coinciden con los publicados por
  GitHub y lo instala.
- **Ajustes…**: ver abajo.
- **Informar de un problema…**: guarda un informe de diagnóstico, lo muestra en el Explorador y abre una incidencia
  de GitHub ya rellenada.
- **Guía de usuario** y **Acerca de BatTray**.

## Ajustes
Tres pestañas; los cambios se aplican al momento.

| Pestaña | Ajuste | Por defecto |
|---|---|---|
| Batería | Nivel bajo (ámbar) / nivel crítico (rojo) | por debajo del 30 % / del 15 % |
| Batería | Avisarme cuando un dispositivo llegue al nivel crítico | activado |
| Batería | Avisarme cuando un dispositivo termine de cargar | activado |
| Batería | Guardar 7 días de historial y mostrarlo como gráfico (al desactivarlo se borra) | activado |
| Barra de tareas | Porcentaje junto a cada icono: siempre / solo por debajo de un nivel / nunca (solo iconos) | siempre |
| Barra de tareas | Tamaño: pequeño / normal / grande | normal |
| Barra de tareas | Ordenar, renombrar y cambiar iconos… (abre Personalizar dispositivos) | — |
| Barra de tareas | Mostrar en la barra de cada monitor | activado |
| General | Idioma | igual que Windows (español o inglés) |
| General | Menús y ventanas (tema) | igual que Windows (claro u oscuro) |
| General | Iniciar con Windows | activado al instalar |
| General | Abrir el panel con un atajo de teclado (haz clic en el cuadro y pulsa las teclas nuevas para cambiarlo) | activado, Ctrl+Alt+B |
| General | Buscar nuevas versiones una vez al día | activado |

Los ajustes se guardan en `%AppData%\KoratSolutions\BatTray\settings.json` y el historial en `history.json` en la
misma carpeta; los registros en `%LocalAppData%\KoratSolutions\BatTray\logs`.

### Dispositivos sin datos de batería
Algunos dispositivos no envían su nivel de batería al PC, así que nada puede leerlo. Sus receptores aparecen en
el menú dentro de **2,4 GHz**, marcados como *sin datos de batería*, con dos opciones:

- **Fijar nivel manualmente**: elige 100 / 75 / 50 / 25 % u *Otro…*. El nivel se queda fijo hasta que lo cambies.
- **Estimar por tiempo de uso**: indica la autonomía (horas de uso con una carga completa, según el manual) y la carga
  actual. A partir de ahí:
  - Un auricular cuenta como en uso mientras suena audio por él y durante los 10 minutos siguientes.
  - Un ratón o teclado cuenta como en uso mientras lo usas y durante los 10 minutos siguientes.

Ambos se muestran con `~` (por ejemplo `~62%`). El submenú de cada dispositivo tiene **Ajustar nivel actual**,
**Lo acabo de cargar**, el cambio entre los dos modos (**Estimar por tiempo de uso** / **Nivel fijo**) y **Quitar**.

La estimación es una aproximación: la batería real depende del volumen, las luces y la edad de la batería.
Las estimaciones se guardan en `%AppData%\KoratSolutions\BatTray\estimates.json`.


## Licencia
[MIT](LICENSE) © 2026 Korat Solutions.

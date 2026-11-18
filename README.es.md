<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/header-dark.png" />
  <img src="docs/images/header-light.png" alt="BatTray — Your devices. One glance." width="296" />
</picture>

🌐 [English](README.md) · **Español**

BatTray muestra el nivel de batería de tus periféricos (ratón, teclado, auriculares, mandos…) directamente
en la barra de tareas de Windows: el icono del dispositivo junto a su porcentaje.

```
[🖱 50%]  [🎧 80%⚡]  [⌨ 12%]   ⌃  🔊  🔋97%   13:45
```

## Funciones
- **Widget en la barra de tareas** con el icono y el porcentaje de cada dispositivo (verde / ámbar / rojo),
  en la barra de cada monitor. Si no hay sitio, se ocultan primero los que tienen más batería; nunca tapa
  los botones de las apps.
- **Panel de detalle** (clic izquierdo en el widget o en el icono de la bandeja): todos los dispositivos con
  barra de nivel, estado de carga, última lectura y origen.
- **Estimación por tiempo de uso** para dispositivos que nunca envían su batería (muchos auriculares y ratones
  económicos): BatTray cuenta cuánto los usas y muestra una estimación como `~62%`.
- **Alertas de batería baja**: una notificación de Windows por descarga cuando un dispositivo llega al nivel crítico.
- **Icono y menú en la bandeja** (clic derecho), agrupados en **Bluetooth**, **2,4 GHz (receptor USB)** y **Mandos**.
- **Español e inglés**: sigue el idioma de Windows, o elige uno en Ajustes.
- **Tema claro y oscuro** en menús y ventanas (sigue a Windows o el que elijas); cualquier escalado DPI, barra que
  se oculta sola, apps a pantalla completa y reinicios del Explorador.
- **Aviso de nueva versión** cuando se publica una (opcional) e **Informar de un problema** con un clic.

## Instalación
**[Descarga el último instalador](https://github.com/koratsolutions-code/BatTray/releases/latest)** y ejecuta
`BatTray-Setup-<versión>.msi`. Se instala **por usuario** (sin permisos de administrador)
en `%LocalAppData%\Programs\BatTray`, crea un acceso en el menú Inicio y arranca BatTray. Se desinstala desde
*Configuración › Aplicaciones*; el desinstalador también quita el inicio con Windows.

El instalador incluye una **guía de usuario** (menú Inicio → *BatTray User Guide*, o *User guide* en el menú de
BatTray). Si Windows muestra *"Windows protegió su PC"*, pulsa **Más información → Ejecutar de todas formas**:
el instalador aún no está firmado digitalmente.

## Dispositivos soportados
| Fuente | Dispositivos | Cómo |
|---|---|---|
| Bluetooth LE | Ratones, teclados, auriculares y mandos modernos | Servicio GATT de batería (0x180F) |
| Bluetooth (Windows) | Auriculares clásicos y cualquier dispositivo que Windows reporte | Propiedad PnP `DEVPKEY_Bluetooth_Battery`, el valor de Configuración |
| Mandos de juego | Mandos Xbox (USB, adaptador inalámbrico Xbox, Bluetooth) y otros que Windows reporte | Informe de batería de `Windows.Gaming.Input` |
| Mandos PlayStation | DualShock 4, DualSense, DualSense Edge (USB y Bluetooth) | Campo de batería del report de entrada normal |
| Receptores USB (2,4 GHz) | Ver abajo | Protocolos HID enchufables |

### Receptores de 2,4 GHz
A diferencia de Bluetooth, los dongles de 2,4 GHz no tienen un estándar común de batería: cada fabricante usa
su propio protocolo. BatTray usa un catálogo de **protocolos enchufables** (`IHidBatteryProtocol`):

| Protocolo | Dispositivos |
|---|---|
| Logitech HID++ 2.0 (0x1004, 0x1000, 0x1001) | Receptores Unifying, Bolt, Lightspeed (hasta 6 dispositivos cada uno) y Logitech por USB |
| Razer (feature report de 90 bytes) | Ratones y teclados inalámbricos (receptor y cable) |
| SteelSeries | Arctis 1/7/7+/Pro/7X/7P, Arctis Nova 3P/5/7, Aerox 3/5/9, Prime, Rival 3 Wireless, Rival 650 |
| Corsair | Auriculares Void/HS antiguos y dispositivos Bragi (Dark Core, Ironclaw, Harpoon, M65/M75, K57/K70, Slipstream, Virtuoso, HS80…) |
| HyperX | Cloud Flight, Cloud Alpha Wireless, Cloud II Wireless, Cloud Alpha 2, Pulsefire Haste 2 |
| Sino Wealth "Aurora" | Glorious Model O Wireless, Lamzu, Attack Shark R5 Ultra |
| MonsGeek / Akko (RongYuan) | Teclados MonsGeek y Akko con su dongle de 2,4 GHz (3151:5037/5038) |
| Keychron Ultra-Link | Ratones Keychron M2 / M5 / M6 |
| Rapoo | Receptor Rapoo VT3 PRO |
| Ajazz | Ajazz AJ179 |
| Darmoshark | Ratones Darmoshark (receptor 1915:0723) |
| MCHOSE (Realtek) | MCHOSE L7 / M7 / A7 V2 / K7 |
| GetPower (Compx/Areson/Nordic) | Redragon (25A7:FA07/FA08), ATK, VXE, VGN, Dareu, Zaopin, Scyrox, G-Wolves |
| Batería HID estándar (0x06/0x20, 0x85) | Cualquier receptor que siga la especificación HID |

**Seguridad:** solo se envían consultas de lectura, y solo a receptores que un protocolo reconoce con certeza.
Nunca se usan comandos que cambien ajustes o el modo del dongle. A los receptores no reconocidos nunca se les
escribe nada; usa **Informar de un problema…** (o *Ajustes → Guardar informe de diagnóstico…*) para registrar
su estructura HID y poder añadirlos.

**Privacidad:** la única conexión que hace BatTray es la comprobación diaria opcional de actualizaciones: una
consulta anónima a GitHub del número de la última versión (se desactiva en Ajustes). No se envía nada sobre ti ni
tus dispositivos.

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
  la carpeta de ajustes de BatTray).
- **Ajustes…**: ver abajo.
- **Informar de un problema…**: guarda un informe de diagnóstico, lo muestra en el Explorador y abre una incidencia
  de GitHub ya rellenada.
- **Guía de usuario** y **Acerca de BatTray**.

## Ajustes
| Ajuste | Por defecto |
|---|---|
| Nivel bajo (ámbar) / nivel crítico (rojo) | por debajo del 30 % / del 15 % |
| Avisarme cuando un dispositivo llegue al nivel crítico | activado |
| Idioma | igual que Windows (español o inglés) |
| Menús y ventanas (tema) | igual que Windows (claro u oscuro) |
| Iniciar con Windows | activado al instalar |
| Mostrar en la barra de cada monitor | activado |
| Buscar nuevas versiones una vez al día | activado |

Los ajustes se guardan en `%AppData%\KoratSolutions\BatTray\settings.json`; los registros en
`%LocalAppData%\KoratSolutions\BatTray\logs`.

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

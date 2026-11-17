# BatTray
**Your devices. One glance.** — por Korat Solutions

🌐 [English](README.md) · **Español**

BatTray muestra el nivel de batería de tus periféricos (ratón, teclado, auriculares, mandos…) directamente
en la barra de tareas de Windows: el icono del dispositivo junto a su porcentaje.

```
[🖱 50%]  [🎧 80%]  [⌨ 12%]   ⌃  🔊  🔋97%   13:45
```

## Requisitos
- Windows 11 (Windows 10 2004+ como mínimo)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Estructura
| Ruta | Contenido |
|---|---|
| `src/BatTray.Core` | Modelos, proveedores de batería (Bluetooth LE / clásico), monitor de dispositivos, ajustes |
| `src/BatTray.App` | App WPF: widget de barra de tareas, icono de bandeja |
| `tests/BatTray.Tests` | Tests unitarios (xUnit) de `BatTray.Core` |

## Compilar y ejecutar
```bash
dotnet build
dotnet test
dotnet run --project src/BatTray.App
```

### Opciones de línea de comandos
| Opción | Descripción |
|---|---|
| `--mode child` | *(por defecto)* El widget es una ventana hija de la barra de tareas. |
| `--mode overlay` | Ventana emergente encima de la barra (estrategia alternativa). |
| `--demo` | Muestra dispositivos simulados en lugar de leer baterías reales. |

Para cerrar: clic derecho sobre el widget → **Exit BatTray**. Los logs se guardan en `%LocalAppData%\KoratSolutions\BatTray\logs`.

## Fuentes de batería soportadas
| Fuente | Dispositivos | Cómo |
|---|---|---|
| Bluetooth LE | Ratones, teclados y auriculares modernos | Servicio GATT de batería (0x180F) |
| Bluetooth (Windows) | Auriculares clásicos y cualquier dispositivo que Windows reporte | Propiedad PnP `DEVPKEY_Bluetooth_Battery`, el mismo valor que muestra Configuración |

## Estado
🚧 En desarrollo — fase 2: lectura real de baterías Bluetooth.

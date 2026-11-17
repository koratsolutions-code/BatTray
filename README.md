# BatTray
**Your devices. One glance.** — by Korat Solutions

BatTray muestra el nivel de batería de tus periféricos (ratón, teclado, auriculares, mandos…)
directamente en la barra de tareas de Windows: el icono del dispositivo junto a su porcentaje.

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

## Estado
🚧 En desarrollo — fase 1: prototipo del widget de barra de tareas con datos simulados.

Opciones de línea de comandos (prototipo):
- `--mode child` (por defecto): el widget es una ventana hija de la barra de tareas.
- `--mode overlay`: ventana emergente encima de la barra (estrategia alternativa).

Para cerrar: clic derecho sobre el widget → **Salir de BatTray**. Logs en `%LocalAppData%\KoratSolutions\BatTray\logs`.

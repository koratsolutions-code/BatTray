namespace BatTray.Core.Models;

/// <summary>Un periférico con su último nivel de batería conocido.</summary>
/// <param name="Id">Identificador estable (p. ej. dirección Bluetooth).</param>
/// <param name="Name">Nombre visible del dispositivo.</param>
/// <param name="Kind">Tipo de dispositivo.</param>
/// <param name="BatteryPercent">0–100, o <c>null</c> si no se conoce.</param>
/// <param name="LastUpdated">Momento de la última lectura.</param>
public sealed record Device(
    string Id,
    string Name,
    DeviceKind Kind,
    int? BatteryPercent,
    DateTimeOffset LastUpdated);

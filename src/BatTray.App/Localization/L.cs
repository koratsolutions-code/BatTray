using System.Globalization;

namespace BatTray.App.Localization;

/// <summary>
/// User interface texts in English and Spanish. The language follows Windows (any Spanish locale → Spanish)
/// unless the user picks one in Settings. Menus are rebuilt when they open, so a change applies at once;
/// windows pick it up the next time they open.
/// </summary>
public static class L
{
    public static bool Spanish { get; private set; } = IsWindowsSpanish();

    /// <summary>Applies the language setting: <c>auto</c>, <c>en</c> or <c>es</c>.</summary>
    public static void Apply(string? setting) =>
        Spanish = setting switch
        {
            "es" => true,
            "en" => false,
            _ => IsWindowsSpanish(),
        };

    public static bool IsWindowsSpanish() =>
        CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "es" ||
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";

    private static string T(string en, string es) => Spanish ? es : en;

    // ---------- Common ----------
    public static string Tagline => "Your devices. One glance.";
    public static string Save => T("Save", "Guardar");
    public static string Cancel => T("Cancel", "Cancelar");
    public static string Close => T("Close", "Cerrar");

    // ---------- Menu ----------
    public static string NoDevices => T("No devices with battery data", "No hay dispositivos con datos de batería");
    public static string SectionBluetooth => "Bluetooth";
    public static string SectionReceiver => T("2.4 GHz (USB receiver)", "2,4 GHz (receptor USB)");
    public static string SectionControllers => T("Controllers", "Mandos");
    public static string SectionOther => T("Other", "Otros");
    public static string ShowOnTaskbar => T("Show on the taskbar", "Mostrar en la barra de tareas");
    public static string Charging => T("charging", "cargando");
    public static string NoBatteryData => T("no battery data", "sin datos de batería");
    public static string NoBatteryDataTip => T(
        "This receiver does not send its battery level. Set it yourself or estimate it from usage.",
        "Este receptor no envía su nivel de batería. Fíjalo tú o estímalo por tiempo de uso.");
    public static string SetLevelManually => T("Set level manually", "Fijar nivel manualmente");
    public static string SetCurrentLevel => T("Set current level", "Ajustar nivel actual");
    public static string Other => T("Other…", "Otro…");
    public static string EstimateFromUsage => T("Estimate from usage…", "Estimar por tiempo de uso…");
    public static string EstimateFromUsageWithLife(double hours) =>
        T($"Estimate from usage ({hours:0.#} h battery life)…", $"Estimar por tiempo de uso ({hours:0.#} h de autonomía)…");
    public static string EstimateFromUsageTip => T("The level goes down while you use the device", "El nivel baja mientras usas el dispositivo");
    public static string FixedLevel => T("Fixed level", "Nivel fijo");
    public static string FixedLevelTip => T("The level stays where you set it", "El nivel se queda donde lo pongas");
    public static string JustCharged => T("I just charged it", "Lo acabo de cargar");
    public static string Remove => T("Remove", "Quitar");
    public static string HideReceiver => T("Hide this receiver", "Ocultar este receptor");
    public static string HiddenReceivers(int count) => T($"Hidden receivers ({count})", $"Receptores ocultos ({count})");
    public static string ShowAgain => T("Show again", "Volver a mostrar");
    public static string CustomizeDevices => T("Customize devices…", "Personalizar dispositivos…");
    public static string CustomizeDevicesTip => T("Rename devices and change their icons", "Cambia nombres e iconos");
    public static string Settings => T("Settings…", "Ajustes…");
    public static string StartWithWindows => T("Start with Windows", "Iniciar con Windows");
    public static string SaveDiagnostics => T("Save diagnostics report…", "Guardar informe de diagnóstico…");
    public static string SaveDiagnosticsTip => T("Lists every device found, including unsupported receivers", "Lista todos los dispositivos encontrados, incluidos los receptores no soportados");
    public static string OpenLogFolder => T("Open log folder", "Abrir carpeta de registros");
    public static string ReportProblem => T("Report a problem…", "Informar de un problema…");
    public static string UserGuide => T("User guide", "Guía de usuario");
    public static string About => T("About BatTray", "Acerca de BatTray");
    public static string Exit => T("Exit BatTray", "Salir de BatTray");
    public static string UpdateAvailable(string version) => T($"Update available: BatTray {version}", $"Actualización disponible: BatTray {version}");

    // ---------- Widget, tray, panel ----------
    public static string ChargingSuffix => T(" (charging)", " (cargando)");
    public static string EstimatedSuffix => T(" (estimated from usage)", " (estimado por uso)");
    public static string LastReading(DateTimeOffset time) => T($"Last reading: {time.LocalDateTime:HH:mm:ss}", $"Última lectura: {time.LocalDateTime:HH:mm:ss}");
    public static string PanelEmpty => T(
        "No devices with battery data. Connect a Bluetooth device or a supported receiver.",
        "No hay dispositivos con datos de batería. Conecta un dispositivo Bluetooth o un receptor compatible.");
    public static string HiddenFromTaskbar => T("hidden from taskbar", "oculto en la barra");
    public static string Updated(DateTimeOffset time) => T($"Updated {time.LocalDateTime:HH:mm}", $"Actualizado {time.LocalDateTime:HH:mm}");
    public static string SetManuallyAgo(string ago) => T($"Set manually · {ago} ago", $"Fijado a mano · hace {ago}");
    public static string EstimatedDetails(string used, double rated, string ago) =>
        T($"Estimated from usage · {used} of {rated:0.#} h used · set {ago} ago",
          $"Estimado por uso · {used} de {rated:0.#} h usadas · ajustado hace {ago}");

    // ---------- Notifications ----------
    public static string LowBatteryTitle(string name, int? percent) => T($"{name}: {percent}% battery", $"{name}: {percent} % de batería");
    public static string LowBatteryEstimatedTitle(string name, int? percent) => T($"{name}: about {percent}% battery (estimated)", $"{name}: alrededor del {percent} % de batería (estimado)");
    public static string LowBatteryMessage => T("Charge it or replace the batteries soon.", "Cárgalo o cambia las pilas pronto.");
    public static string UpdateTitle(string version) => T($"BatTray {version} is available", $"BatTray {version} está disponible");
    public static string UpdateMessage => T("Right-click BatTray and choose the update to download it.", "Haz clic derecho en BatTray y elige la actualización para descargarla.");

    // ---------- About ----------
    public static string Version(string v) => T($"Version {v}", $"Versión {v}");
    public static string AboutDescription => T(
        "Battery levels of your wireless mice, keyboards, headsets and controllers, right on the Windows taskbar.",
        "El nivel de batería de tus ratones, teclados, auriculares y mandos inalámbricos, en la barra de tareas de Windows.");
    public static string WebsiteAndSource => T("Website and source code", "Web y código fuente");
    public static string License => T("MIT License", "Licencia MIT");
    public static string MadeBy => T("Made by", "Hecho por");

    // ---------- Estimate / level dialogs ----------
    public static string EstimateTitle => T("Estimate battery from usage", "Estimar batería por tiempo de uso");
    public static string EstimateIntro => T(
        "This device does not report its battery. BatTray will count how long you use it and show an estimate (~%).",
        "Este dispositivo no informa su batería. BatTray contará cuánto lo usas y mostrará una estimación (~%).");
    public static string Name => T("Name", "Nombre");
    public static string BatteryLife => T("Battery life on a full charge (hours of use)", "Autonomía con una carga completa (horas de uso)");
    public static string BatteryLifeHint => T("Check the manual or the product page. You can change it later.", "Consulta el manual o la página del producto. Puedes cambiarlo después.");
    public static string CurrentCharge => T("Current charge (%)", "Carga actual (%)");
    public static string ErrorName => T("Enter a name.", "Escribe un nombre.");
    public static string ErrorHours => T("Battery life must be a number of hours between 0.5 and 10000.", "La autonomía debe ser un número de horas entre 0,5 y 10000.");
    public static string ErrorPercent => T("Enter a whole number from 0 to 100.", "Escribe un número entero del 0 al 100.");
    public static string LevelTitle => T("Set battery level", "Fijar nivel de batería");
    public static string BatteryLevel => T("Battery level (%)", "Nivel de batería (%)");

    // ---------- Customize ----------
    public static string CustomizeTitle => T("Customize devices", "Personalizar dispositivos");
    public static string Icon => T("Icon", "Icono");
    public static string OriginalName(string name) => T($"Original name: {name}", $"Nombre original: {name}");
    public static string UploadImage => T("Upload image…", "Subir imagen…");
    public static string UploadHint => T("PNG, JPG, BMP, GIF or ICO. It is resized to 64×64; square images look best.", "PNG, JPG, BMP, GIF o ICO. Se reduce a 64×64; las imágenes cuadradas se ven mejor.");
    public static string ChooseImage => T("Choose an icon image", "Elige una imagen para el icono");
    public static string Images => T("Images", "Imágenes");
    public static string ResetToDefault => T("Reset to default", "Restablecer");
    public static string DefaultIcon(string key) => T($"Default ({key})", $"Por defecto ({key})");
    public static string YourImage => T("Your image", "Tu imagen");
    public static string ErrorImageMissing => T("The file does not exist.", "El archivo no existe.");
    public static string ErrorImageTooLarge => T("The image is larger than 10 MB.", "La imagen pesa más de 10 MB.");
    public static string ErrorImageUnreadable => T("This file is not an image BatTray can read.", "BatTray no puede leer este archivo como imagen.");
    public static string ErrorImageSave(string message) => T($"Could not save the image: {message}", $"No se pudo guardar la imagen: {message}");

    public static string IconLabel(string key) => key switch
    {
        "mouse" => T("Mouse", "Ratón"),
        "keyboard" => T("Keyboard", "Teclado"),
        "headset" => T("Headset", "Auricular"),
        "earbuds" => T("Earbuds", "Auriculares de botón"),
        "speaker" => T("Speaker", "Altavoz"),
        "microphone" => T("Microphone", "Micrófono"),
        "gamepad" => T("Gamepad", "Mando"),
        "trackball" => "Trackball",
        "pen" => T("Pen", "Lápiz"),
        "phone" => T("Phone", "Teléfono"),
        "tablet" => "Tablet",
        "watch" => T("Watch", "Reloj"),
        "battery" => T("Battery", "Batería"),
        "bluetooth" => "Bluetooth",
        _ => key,
    };

    // ---------- Settings ----------
    public static string SettingsTitle => T("BatTray settings", "Ajustes de BatTray");
    public static string Battery => T("Battery", "Batería");
    public static string LowLevel => T("Low level (amber) below", "Nivel bajo (ámbar) por debajo de");
    public static string CriticalLevel => T("Critical level (red) below", "Nivel crítico (rojo) por debajo de");
    public static string LowBatteryAlerts => T("Notify me when a device reaches the critical level", "Avisarme cuando un dispositivo llegue al nivel crítico");
    public static string Appearance => T("Appearance", "Apariencia");
    public static string Language => T("Language", "Idioma");
    public static string Theme => T("Menus and windows", "Menús y ventanas");
    public static string ThemeShort => T("Theme", "Tema");
    public static string FollowWindows => T("Same as Windows", "Igual que Windows");
    public static string Light => T("Light", "Claro");
    public static string Dark => T("Dark", "Oscuro");
    public static string General => "General";
    public static string ShowOnAllTaskbars => T("Show on the taskbar of every monitor", "Mostrar en la barra de cada monitor");
    public static string RestartToApply => T("Applies after restarting BatTray.", "Se aplica al reiniciar BatTray.");
    public static string CheckForUpdates => T("Check for new versions once a day", "Buscar nuevas versiones una vez al día");
    public static string CheckForUpdatesHint => T(
        "Only asks GitHub for the latest version number. Nothing about you or your devices is sent.",
        "Solo pregunta a GitHub el número de la última versión. No se envía nada sobre ti ni tus dispositivos.");
    public static string CheckNow => T("Check now", "Comprobar ahora");
    public static string Checking => T("Checking…", "Comprobando…");
    public static string UpToDate(string v) => T($"You have the latest version ({v}).", $"Tienes la última versión ({v}).");
    public static string NewVersion(string v) => T($"BatTray {v} is available.", $"BatTray {v} está disponible.");
    public static string Download => T("Download", "Descargar");
    public static string CheckFailed => T("Could not check right now. Try again later.", "No se pudo comprobar ahora. Inténtalo más tarde.");

    // ---------- Report a problem ----------
    public static string ReportTitle => T("Report a problem", "Informar de un problema");
    public static string ReportIntro => T(
        "BatTray saved a diagnostics report and will open GitHub to create an issue. Describe the problem and attach the report shown in File Explorer.",
        "BatTray guardó un informe de diagnóstico y abrirá GitHub para crear una incidencia. Describe el problema y adjunta el informe que se muestra en el Explorador de archivos.");
    public static string ReportPrivacy => T(
        "The report lists device names, models and USB ids. Check it before attaching it.",
        "El informe incluye nombres, modelos e identificadores USB de tus dispositivos. Revísalo antes de adjuntarlo.");
    public static string Continue => T("Continue", "Continuar");
}

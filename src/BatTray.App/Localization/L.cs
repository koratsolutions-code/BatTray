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
    public static string UpdateMessage => T("Right-click BatTray and choose the update to install it.", "Haz clic derecho en BatTray y elige la actualización para instalarla.");

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
    public static string Download => T("Install…", "Instalar…");
    public static string CheckFailed => T("Could not check right now. Try again later.", "No se pudo comprobar ahora. Inténtalo más tarde.");

    public static string TabBattery => T("Battery", "Batería");
    public static string TabTaskbar => T("Taskbar", "Barra de tareas");
    public static string TabGeneral => "General";
    public static string Devices => T("Devices", "Dispositivos");
    public static string Levels => T("Levels", "Niveles");
    public static string Alerts => T("Notifications", "Avisos");
    public static string FullChargeAlerts => T("Notify me when a device finishes charging (to unplug it)", "Avisarme cuando un dispositivo termine de cargar (para desconectarlo)");
    public static string PerDeviceAlertsHint => T(
        "You can set a different level, or turn notifications off, for each device in Customize devices.",
        "Puedes poner otro nivel, o desactivar los avisos, para cada dispositivo en Personalizar dispositivos.");
    public static string History => T("History", "Historial");
    public static string KeepHistory => T("Keep a 7-day battery history and show it as a chart in the panel", "Guardar 7 días de historial de batería y mostrarlo como gráfico en el panel");
    public static string KeepHistoryHint => T("Stored only on this PC. Turning it off deletes it.", "Se guarda solo en este PC. Al desactivarlo se borra.");
    public static string ClearHistory => T("Clear history", "Borrar historial");
    public static string HistoryCleared => T("History cleared.", "Historial borrado.");
    public static string PercentOnTaskbar => T("Percentage next to each icon", "Porcentaje junto a cada icono");
    public static string PercentAlways => T("Always", "Siempre");
    public static string PercentLow => T("Only below", "Solo por debajo de");
    public static string PercentNever => T("Never (icons only)", "Nunca (solo iconos)");
    public static string PercentNeverHint => T(
        "Icons turn amber or red when the level is low. Hover an icon to see its percentage.",
        "Los iconos se ponen ámbar o rojos cuando el nivel es bajo. Pasa el ratón por un icono para ver su porcentaje.");
    public static string ItemSize => T("Size", "Tamaño");
    public static string SizeSmall => T("Small", "Pequeño");
    public static string SizeNormal => T("Normal", "Normal");
    public static string SizeLarge => T("Large", "Grande");
    public static string SizeHint => T("BatTray makes items smaller by itself when they do not fit.", "BatTray reduce los elementos por sí sola cuando no caben.");
    public static string OrderAndCustomize => T("Order, rename and change icons…", "Ordenar, renombrar y cambiar iconos…");
    public static string Hotkey => T("Open the panel with", "Abrir el panel con");
    public static string HotkeyHint => T("Click the box and press the new keys (Ctrl or Alt plus a key).", "Haz clic en el cuadro y pulsa las teclas nuevas (Ctrl o Alt más una tecla).");
    public static string HotkeyPress => T("Press the keys…", "Pulsa las teclas…");
    public static string HotkeyInUse => T("Another app already uses this shortcut. Choose a different one.", "Otra app ya usa este atajo. Elige otro.");
    public static string HotkeyNeedsModifier => T("Use Ctrl or Alt together with another key.", "Usa Ctrl o Alt junto con otra tecla.");

    // ---------- Customize: order and alerts ----------
    public static string MoveUp => T("Move up", "Subir");
    public static string MoveDown => T("Move down", "Bajar");
    public static string OrderHint => T("Drag devices or use the arrows to change their order on the taskbar.", "Arrastra los dispositivos o usa las flechas para cambiar su orden en la barra.");
    public static string LowBatteryAlert => T("Low battery notification", "Aviso de batería baja");
    public static string AlertGeneral(int percent) => T($"Use the general setting ({percent}%)", $"Usar el ajuste general ({percent} %)");
    public static string AlertGeneralOff => T("Use the general setting (notifications are off)", "Usar el ajuste general (avisos desactivados)");
    public static string AlertAt => T("Notify me at", "Avisarme al");
    public static string AlertNever => T("Never notify me about this device", "No avisarme de este dispositivo");

    // ---------- Full charge ----------
    public static string FullChargeTitle(string name) => T($"{name} is fully charged", $"{name} está completamente cargado");
    public static string FullChargeMessage => T("You can unplug it: keeping it on the charger at 100% wears the battery.", "Ya puedes desconectarlo: dejarlo cargando al 100 % desgasta la batería.");

    // ---------- Panel history ----------
    public static string Last7Days => T("Last 7 days", "Últimos 7 días");

    // ---------- One-click update ----------
    public static string UpdateDialogTitle => T("Update BatTray", "Actualizar BatTray");
    public static string UpdateDialogIntro(string current) => T(
        $"You have version {current}. BatTray will download the installer from GitHub, check it and install it. BatTray closes while it installs and you can open it again from the last step of the installer.",
        $"Tienes la versión {current}. BatTray descargará el instalador desde GitHub, lo comprobará y lo instalará. BatTray se cierra mientras se instala y puedes volver a abrirla en el último paso del instalador.");
    public static string WhatsNew => T("What's new", "Novedades");
    public static string DownloadAndInstall => T("Download and install", "Descargar e instalar");
    public static string OpenDownloadPage => T("Open download page", "Abrir página de descarga");
    public static string NoVerifiedInstaller => T(
        "This release has no installer BatTray can verify. Download it from the release page instead.",
        "Esta versión no tiene un instalador que BatTray pueda verificar. Descárgalo desde la página de la versión.");
    public static string Downloading(int percent) => T($"Downloading… {percent}%", $"Descargando… {percent} %");
    public static string Verifying => T("Checking the installer…", "Comprobando el instalador…");
    public static string DownloadFailed(string reason) => T($"The update could not be downloaded: {reason}", $"No se pudo descargar la actualización: {reason}");
    public static string VerifyFailed => T(
        "The downloaded file is not the one published on GitHub, so it was deleted and not installed.",
        "El archivo descargado no es el publicado en GitHub, así que se borró y no se instaló.");
    public static string InstallerFailed => T("The installer could not be started.", "No se pudo iniciar el instalador.");

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

using System;

namespace UIMarkerEditor;

internal static class StartupLocalCharacterScanPolicy
{
    public static bool ShouldRun(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.StartupLocalCharacterScanMode == StartupLocalCharacterScanMode.EveryStartup ||
            !settings.StartupLocalCharacterScanCompleted;
    }

    public static bool ShouldMarkCompleted(AppSettings settings, bool scanCompleted)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return scanCompleted &&
            settings.StartupLocalCharacterScanMode == StartupLocalCharacterScanMode.FirstInitializationOnly &&
            !settings.StartupLocalCharacterScanCompleted;
    }
}

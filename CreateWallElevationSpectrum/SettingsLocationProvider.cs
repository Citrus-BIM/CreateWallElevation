using System;

namespace CreateWallElevation
{
    internal static class SettingsLocationProvider
    {
        public static SettingsLocation GetLocation() => SettingsLocation.InUserProfile(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
}

using System;
using System.Reflection;

namespace CreateWallElevation
{
    internal static class SettingsLocationProvider
    {
        public static SettingsLocation GetLocation() => SettingsLocation.BesideAssembly(
            Assembly.GetExecutingAssembly().Location,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }
}

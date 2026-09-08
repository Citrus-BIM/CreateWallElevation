using System;
using System.IO;

namespace CreateWallElevation
{
    internal sealed class SettingsLocation
    {
        private const string FileName = "CreateWallElevationSettings.xml";

        public string FilePath { get; }
        public string MigrationFilePath { get; }

        private SettingsLocation(string filePath, string migrationFilePath = null)
        {
            FilePath = filePath;
            MigrationFilePath = migrationFilePath;
        }

        public static SettingsLocation BesideAssembly(string assemblyLocation, string localApplicationData)
        {
            RequireAbsolutePath(assemblyLocation, "Не удалось определить расположение DLL для сохранения настроек.");
            string directory = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("Не удалось определить папку DLL для сохранения настроек.");

            string migrationPath = IsAbsolutePath(localApplicationData) ? UserFilePath(localApplicationData) : null;
            return new SettingsLocation(Path.Combine(directory, FileName), migrationPath);
        }

        public static SettingsLocation InUserProfile(string localApplicationData) =>
            new SettingsLocation(UserFilePath(localApplicationData));

        public T Load<T>(Func<T> defaults) where T : class =>
            SettingsStore.Load(FilePath, MigrationFilePath, defaults);

        public void Save<T>(T settings) => SettingsStore.Save(FilePath, settings);

        private static string UserFilePath(string localApplicationData)
        {
            RequireAbsolutePath(localApplicationData, "Не удалось определить локальную папку пользователя для настроек.");
            return Path.Combine(localApplicationData, "Citrus BIM", "CreateWallElevation", FileName);
        }

        private static void RequireAbsolutePath(string path, string message)
        {
            if (!IsAbsolutePath(path)) throw new InvalidOperationException(message);
        }

        private static bool IsAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;

            // Path.IsPathRooted also accepts drive-relative and current-drive paths on Windows.
            string root = Path.GetPathRoot(path);
            return root != "\\" && root != "/" && !root.EndsWith(":", StringComparison.Ordinal);
        }
    }
}

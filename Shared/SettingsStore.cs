using System;
using System.IO;
using System.Xml.Serialization;

namespace CreateWallElevation
{
    internal static class SettingsStore
    {
        public static T Load<T>(string path, string legacyPath, Func<T> defaults) where T : class
        {
            string source = File.Exists(path) ? path : legacyPath;
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return defaults();
            try
            {
                using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return new XmlSerializer(typeof(T)).Deserialize(stream) as T ?? defaults();
            }
            catch (IOException) { return defaults(); }
            catch (UnauthorizedAccessException) { return defaults(); }
            catch (InvalidOperationException) { return defaults(); }
        }

        public static void Save<T>(string path, T settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new XmlSerializer(typeof(T)).Serialize(stream, settings);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

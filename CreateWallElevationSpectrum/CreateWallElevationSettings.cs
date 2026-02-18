using System;
using System.IO;
using System.Xml.Serialization;

namespace CreateWallElevation
{
    public class CreateWallElevationSettings
    {
        public string SelectedViewFamilyTypeName { get; set; }

        public bool UseTemplate { get; set; } = false;
        public string ViewSectionTemplateName { get; set; }

        public string SelectedBuildByName { get; set; } = "rbt_ByRoom";
        public string SelectedUseToBuildName { get; set; } = "rbt_Section";

        public string Indent { get; set; } = "0";
        public string IndentUp { get; set; } = "0";
        public string IndentDown { get; set; } = "0";
        public string ProjectionDepth { get; set; } = "0";
        public string CurveNumberOfSegments { get; set; } = "5"; 
        public string SelectedViewSheetName { get; set; }

        public string MinSegmentLength { get; set; } = "1000";

        private const string FileName = "CreateWallElevationSettings.xml";

        private static string SettingsDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Citrus BIM",
                "CreateWallElevation");

        private static string SettingsFilePath => Path.Combine(SettingsDirectory, FileName);

        public static CreateWallElevationSettings GetSettings()
        {
            if (!File.Exists(SettingsFilePath))
                return new CreateWallElevationSettings();

            try
            {
                using (var fs = new FileStream(SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var xSer = new XmlSerializer(typeof(CreateWallElevationSettings));
                    return xSer.Deserialize(fs) as CreateWallElevationSettings
                           ?? new CreateWallElevationSettings();
                }
            }
            catch
            {
                return new CreateWallElevationSettings();
            }
        }

        public void SaveSettings()
        {
            Directory.CreateDirectory(SettingsDirectory);

            var tmpPath = SettingsFilePath + ".tmp";

            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { }
            }

            var xSer = new XmlSerializer(typeof(CreateWallElevationSettings));
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                xSer.Serialize(fs, this);
            }

            TryReplaceOrMove(tmpPath, SettingsFilePath);
        }

        private static void TryReplaceOrMove(string tmpPath, string targetPath)
        {
            try
            {
                if (File.Exists(targetPath))
                    File.Replace(tmpPath, targetPath, destinationBackupFileName: null);
                else
                    File.Move(tmpPath, targetPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(targetPath))
                        File.Delete(targetPath);

                    File.Move(tmpPath, targetPath);
                }
                catch
                {
                }
            }
        }
    }
}

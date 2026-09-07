using System;
using System.IO;
using System.Reflection;

namespace CreateWallElevation
{
    public class CreateWallElevationSettings
    {
        public string SelectedViewFamilyTypeName { get; set; }
        public bool UseTemplate { get; set; }
        public string ViewSectionTemplateName { get; set; }
        public string SelectedBuildByName { get; set; } = "rbt_ByRoom";
        public string SelectedUseToBuildName { get; set; } = "rbt_Section";
        public string Indent { get; set; } = "300";
        public string IndentUp { get; set; } = "0";
        public string IndentDown { get; set; } = "0";
        public string ProjectionDepth { get; set; } = "500";
        public string CurveNumberOfSegments { get; set; } = "10";
        public string SelectedViewSheetName { get; set; }
        public string SelectedViewSheetNumber { get; set; }
        public string MinSegmentLength { get; set; } = "1000";

        private const string FileName = "CreateWallElevationSettings.xml";
        private static string UserPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Citrus BIM", "CreateWallElevation", FileName);

        public static CreateWallElevationSettings GetSettings()
        {
            var result = SettingsStore.Load(UserPath,
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), FileName),
                () => new CreateWallElevationSettings());
            // Old releases stored 0 to request the default 500 mm depth.
            if (string.IsNullOrWhiteSpace(result.ProjectionDepth) || result.ProjectionDepth.Trim() == "0")
                result.ProjectionDepth = "500";
            return result;
        }

        public void SaveSettings() => SettingsStore.Save(UserPath, this);
    }
}

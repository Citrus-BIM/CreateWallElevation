using System;

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
        // Missing in old XML: positive IndentDown used to expand the crop downwards.
        public bool UsesSignedBottomOffset { get; set; }
        public string ViewNamePrefix { get; set; } = "";
        public string ProjectionDepth { get; set; } = "500";
        public string CurveNumberOfSegments { get; set; } = "10";
        public string SelectedViewSheetName { get; set; }
        public string SelectedViewSheetNumber { get; set; }
        public string MinSegmentLength { get; set; } = "1000";

        public static CreateWallElevationSettings GetSettings()
        {
            var result = SettingsLocationProvider.GetLocation().Load(() => new CreateWallElevationSettings());
            result.Upgrade();
            return result;
        }

        internal void Upgrade()
        {
            // Old releases stored 0 to request the default 500 mm depth.
            if (string.IsNullOrWhiteSpace(ProjectionDepth) || ProjectionDepth.Trim() == "0")
                ProjectionDepth = "500";
            if (!UsesSignedBottomOffset)
            {
                try
                {
                    double oldOffset = InputValues.SignedMillimeters(IndentDown, "Смещение снизу");
                    if (oldOffset > 0)
                        IndentDown = (-oldOffset).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (ArgumentException) { /* Keep invalid text visible for correction in the dialog. */ }
                UsesSignedBottomOffset = true;
            }
        }

        public void SaveSettings() => SettingsLocationProvider.GetLocation().Save(this);
    }
}

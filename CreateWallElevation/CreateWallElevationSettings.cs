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

        public string Indent { get; set; } = "500";
        public string IndentUp { get; set; } = "0";
        public string IndentDown { get; set; } = "0";
        public string ProjectionDepth { get; set; } = "0";
        public string CurveNumberOfSegments { get; set; } = "5";
        public string SelectedViewSheetName { get; set; }

        public string MinSegmentLength { get; set; } = "1000";

        public CreateWallElevationSettings GetSettings()
        {
            CreateWallElevationSettings createWallElevationSettings = null;
            string assemblyPathAll = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string fileName = "CreateWallElevationSettings.xml";
            string assemblyPath = assemblyPathAll.Replace("CreateWallElevation.dll", fileName);

            if (File.Exists(assemblyPath))
            {
                using (FileStream fs = new FileStream(assemblyPath, FileMode.Open))
                {
                    XmlSerializer xSer = new XmlSerializer(typeof(CreateWallElevationSettings));
                    createWallElevationSettings = xSer.Deserialize(fs) as CreateWallElevationSettings;
                    fs.Close();
                }
            }
            else
            {
                createWallElevationSettings = null;
            }

            return createWallElevationSettings;
        }

        public void SaveSettings()
        {
            string assemblyPathAll = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string fileName = "CreateWallElevationSettings.xml";
            string assemblyPath = assemblyPathAll.Replace("CreateWallElevation.dll", fileName);

            if (File.Exists(assemblyPath))
            {
                File.Delete(assemblyPath);
            }

            using (FileStream fs = new FileStream(assemblyPath, FileMode.Create))
            {
                XmlSerializer xSer = new XmlSerializer(typeof(CreateWallElevationSettings));
                xSer.Serialize(fs, this);
                fs.Close();
            }
        }
    }
}

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace CreateWallElevation
{
    class WallSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Autodesk.Revit.DB.Element elem)
        {
            if (elem is Wall)
            {
                return true;
            }
            return false;
        }

        public bool AllowReference(Autodesk.Revit.DB.Reference reference, Autodesk.Revit.DB.XYZ position)
        {
            return false;
        }
    }
}

using Autodesk.Revit.DB;

namespace CreateWallElevation
{
    internal static class RevitUnits
    {
        public static double FromMillimeters(double value)
        {
#if R2019 || R2020
            return UnitUtils.ConvertToInternalUnits(value, DisplayUnitType.DUT_MILLIMETERS);
#else
            return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters);
#endif
        }
    }
}

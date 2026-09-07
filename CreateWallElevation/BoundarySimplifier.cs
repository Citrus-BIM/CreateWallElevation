using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.Collections.Generic;
using System.Linq;

namespace CreateWallElevation
{
    public static class BoundarySimplifier
    {
        private const double DefaultDistTolFt = 1.0 / 304.8;

        internal static List<List<Curve>> GetRoomLoops(Room room)
        {
            using (var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish })
            {
                var loops = room.GetBoundarySegments(options);
                if (loops == null) return new List<List<Curve>>();
                return loops.Select(loop => loop.Select(s => s.GetCurve()).ToList()).ToList();
            }
        }

        public static List<List<Curve>> SimplifyRoomLoops(Room room, double minLength) => GetRoomLoops(room)
            .Select(loop => SimplifyCurves(loop, minLength, DefaultDistTolFt, 1e-6, true)).ToList();

        public static List<Curve> SimplifyRoomOuterLoop(Room room, double minSegLenFt,
            double distTolFt = DefaultDistTolFt, double angleTolRad = 1e-6)
        {
            using (var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish })
            {
                var loops = room.GetBoundarySegments(options);
                return loops == null || loops.Count == 0 ? new List<Curve>() :
                    SimplifySegments(loops[0], minSegLenFt, distTolFt, angleTolRad, true);
            }
        }

        public static List<Curve> SimplifySegments(IList<BoundarySegment> segments, double minSegLenFt,
            double distTolFt, double angleTolRad, bool closeLoopMerge) =>
            SimplifyCurves(segments.Select(s => s.GetCurve()).ToList(), minSegLenFt, distTolFt, angleTolRad, closeLoopMerge);

        public static List<Curve> SimplifyCurves(IList<Curve> curves, double minSegLenFt,
            double distTolFt, double angleTolRad, bool closeLoopMerge)
        {
            if (curves == null) return new List<Curve>();
            return Geometry2D.SimplifyEdges(curves.Where(c => c != null).ToList(),
                (Curve first, Curve next, out Curve combined) => TryMerge(first, next, distTolFt, out combined),
                curve => curve.Length, minSegLenFt, distTolFt, closeLoopMerge);
        }
        private static bool TryMerge(Curve first, Curve next, double tolerance, out Curve result)
        {
            result = null;
            if (!(first is Line) || !(next is Line)) return false;
            XYZ a = first.GetEndPoint(0), b = first.GetEndPoint(1), c = next.GetEndPoint(0), d = next.GetEndPoint(1);
            if (System.Math.Abs(a.Z - b.Z) > tolerance || System.Math.Abs(a.Z - c.Z) > tolerance ||
                System.Math.Abs(a.Z - d.Z) > tolerance) return false;
            if (!Geometry2D.TryMerge(new Segment2(new Point2(a.X, a.Y), new Point2(b.X, b.Y)),
                new Segment2(new Point2(c.X, c.Y), new Point2(d.X, d.Y)), tolerance, out _)) return false;
            result = Line.CreateBound(a, d);
            return true;
        }
    }
}

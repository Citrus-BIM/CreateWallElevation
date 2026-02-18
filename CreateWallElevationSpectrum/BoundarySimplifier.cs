using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CreateWallElevation
{
    public static class BoundarySimplifier
    {
        // 1 мм в ft
        private const double DefaultDistTolFt = 1.0 / 304.8; // ~1мм
        private const double DefaultAngleTolRad = 1e-6;      // можно ослабить до 1e-4

        public static List<Curve> SimplifyRoomOuterLoop(
            Room room,
            double minSegLenFt,
            double distTolFt = DefaultDistTolFt,
            double angleTolRad = DefaultAngleTolRad)
        {
            var loops = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
            if (loops == null || loops.Count == 0) return new List<Curve>();

            // как в исходнике — первый контур
            var segs = loops.First();
            return SimplifySegments(segs, minSegLenFt, distTolFt, angleTolRad, closeLoopMerge: true);
        }

        public static List<Curve> SimplifySegments(
            IList<BoundarySegment> segs,
            double minSegLenFt,
            double distTolFt,
            double angleTolRad,
            bool closeLoopMerge)
        {
            if (segs == null || segs.Count == 0) return new List<Curve>();

            var curves = segs.Select(s => s.GetCurve()).Where(c => c != null).ToList();
            return SimplifyCurves(curves, minSegLenFt, distTolFt, angleTolRad, closeLoopMerge);
        }

        public static List<Curve> SimplifyCurves(
            IList<Curve> curves,
            double minSegLenFt,
            double distTolFt,
            double angleTolRad,
            bool closeLoopMerge)
        {
            var result = new List<Curve>();
            if (curves == null || curves.Count == 0) return result;

            bool hasGroup = false;
            XYZ gStart = null;
            XYZ gEnd = null;
            XYZ gDir = null; // normalized

            void FlushGroup()
            {
                if (!hasGroup) return;

                double len = gStart.DistanceTo(gEnd);
                if (len >= minSegLenFt - distTolFt)
                    result.Add(Line.CreateBound(gStart, gEnd));

                hasGroup = false;
                gStart = gEnd = gDir = null;
            }

            foreach (var c in curves)
            {
                // 1) фильтр по длине: всё, что короче, игнорим (и это "сшивает" через коротыши)
                if (c.Length < minSegLenFt - distTolFt)
                    continue;

                // 2) линии пытаемся склеивать, всё остальное — режет группу
                var ln = c as Line;
                if (ln != null)
                {
                    XYZ a = ln.GetEndPoint(0);
                    XYZ b = ln.GetEndPoint(1);
                    XYZ dir = (b - a).Normalize();

                    if (!hasGroup)
                    {
                        hasGroup = true;
                        gStart = a;
                        gEnd = b;
                        gDir = dir;
                        continue;
                    }

                    // Подгоняем направление под gDir (если противоположно — разворачиваем отрезок)
                    if (gDir.DotProduct(dir) < 0)
                    {
                        XYZ tmp = a; a = b; b = tmp;
                        dir = (b - a).Normalize();
                    }

                    XYZ newEnd;
                    if (CanMergeLineSequential(gStart, gEnd, gDir, a, b, dir, distTolFt, angleTolRad, out newEnd))
                    {
                        gEnd = newEnd;
                        continue;
                    }

                    FlushGroup();

                    hasGroup = true;
                    gStart = a;
                    gEnd = b;
                    gDir = dir;
                }
                else
                {
                    // дуга/сплайн и т.п. — закрываем группу и добавляем как есть
                    FlushGroup();
                    result.Add(c);
                }
            }

            FlushGroup();

            // 3) Для замкнутого контура: попробовать склеить последний и первый (если это линии)
            if (closeLoopMerge && result.Count >= 2)
            {
                var first = result[0] as Line;
                var last = result[result.Count - 1] as Line;

                if (first != null && last != null)
                {
                    XYZ f0 = first.GetEndPoint(0);
                    XYZ f1 = first.GetEndPoint(1);
                    XYZ l0 = last.GetEndPoint(0);
                    XYZ l1 = last.GetEndPoint(1);

                    XYZ fDir = (f1 - f0).Normalize();
                    XYZ lDir = (l1 - l0).Normalize();

                    // Подгоним last под first
                    if (fDir.DotProduct(lDir) < 0)
                    {
                        XYZ tmp = l0; l0 = l1; l1 = tmp;
                        lDir = (l1 - l0).Normalize();
                    }

                    XYZ mergedEnd;
                    if (CanMergeLineSequential(l0, l1, lDir, f0, f1, fDir, distTolFt, angleTolRad, out mergedEnd))
                    {
                        // mergedEnd обычно будет f1 (или дальше, если first длиннее по t)
                        // делаем одну линию от l0 до mergedEnd
                        if (l0.DistanceTo(mergedEnd) > distTolFt)
                        {
                            var merged = Line.CreateBound(l0, mergedEnd);
                            result.RemoveAt(result.Count - 1);
                            result[0] = merged;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Склейка последовательных соосных линий:
        /// - направление почти совпадает
        /// - оба конца новой линии лежат на той же бесконечной прямой группы
        /// - "зазор" НЕ ограничиваем (его роль играет фильтр коротышей и последовательность в обходе)
        /// </summary>
        private static bool CanMergeLineSequential(
            XYZ gStart, XYZ gEnd, XYZ gDir,
            XYZ a, XYZ b, XYZ dir,
            double distTolFt,
            double angleTolRad,
            out XYZ newEnd)
        {
            newEnd = gEnd;

            // 1) направление почти одинаковое
            double angle = gDir.AngleTo(dir);
            if (angle > angleTolRad)
                return false;

            // 2) Проверим коллинеарность (оба конца лежат на прямой группы)
            if (!IsPointOnLine(gStart, gDir, a, distTolFt)) return false;
            if (!IsPointOnLine(gStart, gDir, b, distTolFt)) return false;

            // 3) Удлиняем группу до наиболее "дальнего" конца по направлению gDir
            double tEnd = (gEnd - gStart).DotProduct(gDir);
            double tA = (a - gStart).DotProduct(gDir);
            double tB = (b - gStart).DotProduct(gDir);

            double tMax = tEnd;
            XYZ pMax = gEnd;

            if (tA > tMax) { tMax = tA; pMax = a; }
            if (tB > tMax) { tMax = tB; pMax = b; }

            newEnd = pMax;
            return true;
        }

        private static bool IsPointOnLine(XYZ origin, XYZ dir, XYZ p, double distTolFt)
        {
            var v = p - origin;
            var t = v.DotProduct(dir);
            var proj = origin + t * dir;
            return proj.DistanceTo(p) <= distTolFt;
        }
    }
}

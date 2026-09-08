using System;
using System.Collections.Generic;
using System.Linq;

namespace CreateWallElevation
{
    internal delegate bool TryMergeEdge<T>(T first, T next, out T combined);
    internal struct Point2
    {
        public readonly double X, Y;
        public Point2(double x, double y) { X = x; Y = y; }
        public double Length => Math.Sqrt(X * X + Y * Y);
        public Point2 Normalized()
        {
            if (!Geometry2D.IsFinite(Length) || Length < 1e-10)
                throw new ArgumentException("Невозможно определить направление нулевого отрезка.");
            return this * (1 / Length);
        }
        public double Dot(Point2 other) => X * other.X + Y * other.Y;
        public static Point2 operator +(Point2 a, Point2 b) => new Point2(a.X + b.X, a.Y + b.Y);
        public static Point2 operator -(Point2 a, Point2 b) => new Point2(a.X - b.X, a.Y - b.Y);
        public static Point2 operator *(Point2 a, double b) => new Point2(a.X * b, a.Y * b);
    }
    internal struct Segment2
    {
        public readonly Point2 Start, End;
        public Segment2(Point2 start, Point2 end) { Start = start; End = end; }
        public double Length => (End - Start).Length;
    }
    internal struct ObserverFrame
    {
        public Point2 Observer, ObserverDirection;
        public Point2 SectionBoxDirection => ObserverDirection * -1;
    }
    internal sealed class PreparedSegment2
    {
        public ObserverFrame Frame;
        public List<Point2> SurfacePoints;
    }
    internal struct Rect2
    {
        public double MinX, MinY, MaxX, MaxY;
        public Rect2(double minX, double minY, double maxX, double maxY)
        { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        public bool Contains(Rect2 other) => other.MinX >= MinX - 1e-9 && other.MaxX <= MaxX + 1e-9 && other.MinY >= MinY - 1e-9 && other.MaxY <= MaxY + 1e-9;
        public bool Overlaps(Rect2 other, double gap) => MinX < other.MaxX + gap && MaxX + gap > other.MinX && MinY < other.MaxY + gap && MaxY + gap > other.MinY;
    }
    internal static class Geometry2D
    {
        public static List<PreparedSegment2> PrepareSegments(Func<double, Point2> evaluate, Func<double, Point2> tangent,
            Func<Point2, Point2, Point2> normalAt, int count, double surfaceOffset, double indent, double depth,
            Func<Point2, bool> observerIsValid)
        {
            if (!IsFinite(depth) || depth <= 0 || !IsFinite(surfaceOffset) || surfaceOffset < 0)
                throw new ArgumentException("Некорректная глубина проекции или толщина стены.");
            double[] parameters = SegmentParameters(count);
            var result = new List<PreparedSegment2>();
            for (int i = 0; i < count; i++)
            {
                double t0 = parameters[i], t1 = parameters[i + 1], tm = (t0 + t1) / 2;
                Point2 mid = evaluate(tm);
                Point2 normal = normalAt(mid, tangent(tm)).Normalized();
                ObserverFrame frame = CreateFrame(mid + normal * surfaceOffset, normal, indent);
                if (!observerIsValid(frame.Observer))
                    throw new ArgumentException("Маркер выходит за пределы помещения. Уменьшите отступ от грани.");
                var points = new List<Point2>();
                for (int sample = 0; sample <= 4; sample++)
                {
                    double t = t0 + (t1 - t0) * sample / 4;
                    Point2 point = evaluate(t);
                    Point2 offsetDirection = surfaceOffset == 0 ? normal : normalAt(point, tangent(t)).Normalized();
                    Point2 facePoint = point + offsetDirection * surfaceOffset;
                    points.Add(facePoint);
                    double ahead = (frame.Observer - facePoint).Dot(normal);
                    if (ahead < -1e-6 || ahead > depth - 1e-6)
                        throw new ArgumentException("Стена выходит за глубину проекции или пересекает плоскость вида. " +
                            "Увеличьте число сегментов кривой или скорректируйте отступ и глубину.");
                }
                result.Add(new PreparedSegment2 { Frame = frame, SurfacePoints = points });
            }
            return result;
        }

        public static bool IsInsideLoops(Point2 point, IList<IList<Point2>> loops)
        {
            // Even/odd winding handles holes and disconnected outer loops without relying on loop order.
            bool inside = false;
            foreach (var loop in loops)
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    Point2 a = loop[i], b = loop[j];
                    if ((a.Y > point.Y) != (b.Y > point.Y) &&
                        point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
            return inside;
        }
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static ObserverFrame CreateFrame(Point2 surface, Point2 observerDirection, double indent)
        {
            if (!IsFinite(indent) || indent < 0 || !IsFinite(surface.X) || !IsFinite(surface.Y))
                throw new ArgumentException("Отступ должен быть конечным неотрицательным числом.");
            var normal = observerDirection.Normalized();
            return new ObserverFrame { Observer = surface + normal * indent, ObserverDirection = normal };
        }

        public static double[] SegmentParameters(int count)
        {
            if (count < 1 || count > 1000)
                throw new ArgumentException("Число сегментов должно быть от 1 до 1000.");
            return Enumerable.Range(0, count + 1).Select(i => (double)i / count).ToArray();
        }

        public static Point2 InteriorNormal(Point2 origin, Point2 tangent, Func<Point2, bool> contains, double probe)
        {
            var normal = new Point2(tangent.Y, -tangent.X).Normalized();
            for (int i = 0; i < 9; i++, probe *= 0.25)
            {
                bool positive = contains(origin + normal * probe);
                bool negative = contains(origin - normal * probe);
                if (positive != negative) return positive ? normal : normal * -1;
            }
            throw new ArgumentException("Не удалось однозначно определить внутреннюю сторону границы помещения.");
        }

        public static bool TryMerge(Segment2 first, Segment2 next, double tolerance, out Segment2 merged)
        {
            merged = first;
            if (first.Length < 1e-10 || next.Length < 1e-10 || (first.End - next.Start).Length > tolerance)
                return false;
            var direction = (first.End - first.Start).Normalized();
            var other = (next.End - next.Start).Normalized();
            if (direction.Dot(other) < Math.Cos(1e-6)) return false;
            var delta = next.End - first.Start;
            if ((delta - direction * delta.Dot(direction)).Length > tolerance) return false;
            merged = new Segment2(first.Start, next.End);
            return true;
        }

        public static List<Segment2> SimplifyLines(IList<Segment2> lines, double minLength, double tolerance, bool closed)
            => SimplifyEdges(lines, (Segment2 first, Segment2 next, out Segment2 combined) =>
                TryMerge(first, next, tolerance, out combined), edge => edge.Length, minLength, tolerance, closed);

        public static List<T> SimplifyEdges<T>(IList<T> edges, TryMergeEdge<T> tryMerge, Func<T, double> length,
            double minLength, double tolerance, bool closed)
        {
            var merged = new List<T>();
            foreach (var line in edges)
            {
                if (merged.Count > 0 && tryMerge(merged[merged.Count - 1], line, out var combined))
                    merged[merged.Count - 1] = combined;
                else merged.Add(line);
            }
            if (closed && merged.Count > 1 && tryMerge(merged[merged.Count - 1], merged[0], out var closing))
            {
                merged.RemoveAt(merged.Count - 1);
                merged[0] = closing;
            }
            return merged.Where(s => length(s) >= minLength - tolerance).ToList();
        }

        public static (double Min, double Max) VerticalRange(double bottom, double top, double down, double up)
        {
            if (!IsFinite(bottom) || !IsFinite(top) || !IsFinite(down) || !IsFinite(up) ||
                up < 0 || top <= bottom)
                throw new ArgumentException("Некорректные вертикальные границы или отступы.");
            double min = bottom + down, max = top + up;
            if (!IsFinite(min) || !IsFinite(max) || min >= max)
                throw new ArgumentException("Смещение снизу должно оставлять нижнюю границу вида ниже верхней. Уменьшите смещение.");
            return (min, max);
        }

        public static bool TryPlace(double width, double height, Rect2 bounds, IEnumerable<Rect2> occupied, double gap, out Rect2 result)
        {
            result = default(Rect2);
            if (!IsFinite(width) || !IsFinite(height) || width <= 0 || height <= 0) return false;
            var busy = occupied.ToList();
            var xs = new[] { bounds.MinX }.Concat(busy.Select(r => r.MaxX + gap)).Distinct().OrderBy(x => x);
            var ys = new[] { bounds.MaxY }.Concat(busy.Select(r => r.MinY - gap)).Distinct().OrderByDescending(y => y);
            foreach (double y in ys)
                foreach (double x in xs)
                {
                    var candidate = new Rect2(x, y - height, x + width, y);
                    if (bounds.Contains(candidate) && !busy.Any(r => candidate.Overlaps(r, gap - 1e-9)))
                    { result = candidate; return true; }
                }
            return false;
        }
    }
}

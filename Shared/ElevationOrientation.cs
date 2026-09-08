using System;

namespace CreateWallElevation
{
    internal static class ElevationOrientation
    {
        private const double MaxStep = Math.PI / 6;
        private const double DirectionTolerance = 1e-6;
        private const int MaxSteps = 12;

        public static void Align(Func<Point2> readDirection, Point2 target, Action<double> rotateAndRegenerate)
        {
            target = target.Normalized();
            for (int step = 0; step < MaxSteps; step++)
            {
                double remaining = SignedAngle(readDirection(), target);
                if (Math.Abs(remaining) <= DirectionTolerance) return;
                // REVIT-134773: a large single marker turn can gain another 180 degrees.
                // Use the shortest signed route, in steps no larger than 30 degrees.
                rotateAndRegenerate(Math.Max(-MaxStep, Math.Min(MaxStep, remaining)));
            }
            double error = Math.Abs(SignedAngle(readDirection(), target));
            if (error > DirectionTolerance)
                throw new InvalidOperationException("Не удалось направить фасад к стене. Отклонение " +
                    (error * 180 / Math.PI).ToString("0.###", System.Globalization.CultureInfo.CurrentCulture) +
                    "° после " + MaxSteps + " шагов поворота.");
        }

        private static double SignedAngle(Point2 current, Point2 target)
        {
            current = current.Normalized();
            return Math.Atan2(current.X * target.Y - current.Y * target.X, current.Dot(target));
        }
    }
}

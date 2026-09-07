using System;
using System.Globalization;

namespace CreateWallElevation
{
    internal static class InputValues
    {
        public static double Millimeters(string text, string label, bool positive)
        {
            if (!double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) || !Geometry2D.IsFinite(value) ||
                (positive ? value <= 0 : value < 0))
                throw new ArgumentException(label + ": введите " + (positive ? "положительное" : "неотрицательное") + " число в миллиметрах.");
            return value;
        }
        public static int Segments(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 1 || count > 1000)
                throw new ArgumentException("Число сегментов для кривой должно быть целым числом от 1 до 1000.");
            return count;
        }
    }
}

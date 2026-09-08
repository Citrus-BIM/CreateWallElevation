using System;
using System.Linq;

namespace CreateWallElevation
{
    internal static class ViewNaming
    {
        private const string InvalidCharacters = "\\:{}[]|;<>?`~";

        public static string NormalizePrefix(string value)
        {
            string prefix = (value ?? "").Trim();
            if (prefix.Any(c => InvalidCharacters.Contains(c) || char.IsControl(c)))
                throw new ArgumentException("Префикс имени вида содержит недопустимые символы: " + InvalidCharacters + ". Удалите их и переносы строк.");
            return prefix;
        }

        public static string RoomPrefix(string customPrefix, bool sections, string roomNumber)
        {
            string prefix = NormalizePrefix(customPrefix);
            string number = new string((roomNumber ?? "").Select(c =>
                InvalidCharacters.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
            return prefix.Length == 0 ? (sections ? "Р_П" : "Ф_П") + number : prefix + "_" + number;
        }

        public static string WallPrefix(string customPrefix, bool sections)
        {
            string prefix = NormalizePrefix(customPrefix);
            return prefix.Length == 0 ? (sections ? "Р_Ст" : "Ф_Ст") : prefix;
        }
    }
}

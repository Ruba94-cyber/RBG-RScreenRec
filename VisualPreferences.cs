using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RScreenRec
{
    internal static class VisualPreferences
    {
        private static string PreferencesPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RBG", "RScreenRec", "preferences.ini");
            }
        }

        public static int BadgeSizePercent
        {
            get { return ReadNumber("badge-size", 100, 70, 130); }
            set { WriteNumber("badge-size", value, 70, 130); }
        }

        public static int BorderFadeDurationMs
        {
            get { return ReadNumber("border-fade-ms", 180, 120, 500); }
            set { WriteNumber("border-fade-ms", value, 120, 500); }
        }

        public static bool SelectionMagnifierEnabled
        {
            get { return ReadNumber("selection-magnifier", 0, 0, 1) == 1; }
            set { WriteNumber("selection-magnifier", value ? 1 : 0, 0, 1); }
        }

        private static int ReadNumber(string key, int fallback, int minimum, int maximum)
        {
            try
            {
                int value;
                string text;
                if (ReadAll().TryGetValue(key, out text) &&
                    int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return Math.Max(minimum, Math.Min(maximum, value));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return fallback;
        }

        private static Dictionary<string, string> ReadAll()
        {
            var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(PreferencesPath))
                return pairs;
            foreach (string line in File.ReadAllLines(PreferencesPath))
            {
                int equals = line.IndexOf('=');
                if (equals > 0)
                    pairs[line.Substring(0, equals)] = line.Substring(equals + 1);
            }
            return pairs;
        }

        private static void WriteNumber(string key, int value, int minimum, int maximum)
        {
            if (value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException("value");

            var pairs = ReadAll();
            pairs[key] = value.ToString(CultureInfo.InvariantCulture);
            string path = PreferencesPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var lines = new List<string>();
                foreach (KeyValuePair<string, string> pair in pairs)
                    lines.Add(pair.Key + "=" + pair.Value);
                File.WriteAllLines(temp, lines.ToArray());
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
    }
}

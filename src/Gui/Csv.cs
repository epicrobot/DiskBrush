using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DiskBrush
{
    static class Csv
    {
        public static void Write(string path, string[] header, IEnumerable<string[]> rows)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(Line(header));
                foreach (string[] row in rows) writer.WriteLine(Line(row));
            }
        }

        static string Line(string[] fields)
        {
            var parts = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                parts[i] = "\"" + (fields[i] ?? "").Replace("\"", "\"\"") + "\"";
            return string.Join(",", parts);
        }
    }
}

using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using TruckLib.HashFs;

namespace ETS2AndroidEngine;

public static class HashFsArchiveInspector
{
    private static readonly string[] TobjPaths =
    {
        "/vehicle/truck/daf_xf/color.tobj",
        "/material/environment/vehicle_reflection.tobj",
        "/vehicle/truck/daf_xf/lightmask.tobj",
        "/vehicle/truck/share/glass_ex.tobj"
    };

    public static string Inspect(Stream archiveStream)
    {
        using var reader = HashFsReader.Open(archiveStream);

        var result = new StringBuilder();

        result.AppendLine("ETS2 TOBJ INSPECTION");
        result.AppendLine("-------------------");
        result.AppendLine($"HashFS Version: {reader.Version}");
        result.AppendLine($"Total entries: {reader.Entries.Count}");

        foreach (string path in TobjPaths)
        {
            result.AppendLine();
            result.AppendLine("================================");
            result.AppendLine("TOBJ: " + path);
            result.AppendLine("================================");

            try
            {
                var files = reader.Extract(path);

                if (files.Length == 0)
                {
                    result.AppendLine("FILE NOT FOUND");
                    continue;
                }

                byte[] data = files[0];

                result.AppendLine($"File size: {data.Length} bytes");

                result.AppendLine();
                result.AppendLine("HEX DATA (first 128 bytes):");

                int count = Math.Min(data.Length, 128);

                for (int i = 0; i < count; i += 16)
                {
                    int length = Math.Min(16, count - i);

                    result.Append($"{i:X4}: ");

                    for (int j = 0; j < length; j++)
                    {
                        result.Append(data[i + j].ToString("X2"));
                        result.Append(' ');
                    }

                    result.AppendLine();
                }

                result.AppendLine();
                result.AppendLine("PRINTABLE STRINGS:");

                string decoded = Encoding.Latin1.GetString(data);

                var strings = new List<string>();
                var current = new StringBuilder();

                foreach (char c in decoded)
                {
                    if (c >= 32 && c <= 126)
                    {
                        current.Append(c);
                    }
                    else
                    {
                        if (current.Length >= 4)
                            strings.Add(current.ToString());

                        current.Clear();
                    }
                }

                if (current.Length >= 4)
                    strings.Add(current.ToString());

                if (strings.Count == 0)
                    result.AppendLine("(No printable strings found)");
                else
                    foreach (string value in strings.Distinct())
                        result.AppendLine(value);
            }
            catch (Exception ex)
            {
                result.AppendLine("ERROR: " + ex.Message);
            }
        }

        result.AppendLine();
        result.AppendLine("TOBJ INSPECTION COMPLETED.");

        return result.ToString();
    }
}

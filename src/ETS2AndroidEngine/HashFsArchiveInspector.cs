using System;
using System.IO;
using System.Linq;
using System.Text;
using TruckLib.HashFs;

namespace ETS2AndroidEngine;

public static class HashFsArchiveInspector
{
    public static string Inspect(Stream archiveStream)
    {
        using var reader = HashFsReader.Open(archiveStream);

        var result = new StringBuilder();

        result.AppendLine("ETS2 ARCHIVE REPORT");
        result.AppendLine("-------------------");
        result.AppendLine($"HashFS Version: {reader.Version}");
        result.AppendLine($"Total indexed entries: {reader.Entries.Count}");
        result.AppendLine();

        try
        {
            var versionData = reader.Extract("/version.txt");

            if (versionData.Length > 0)
            {
                result.AppendLine("GAME VERSION FILE:");
                result.AppendLine(
                    Encoding.UTF8.GetString(versionData[0]).Trim());
            }
        }
        catch (Exception ex)
        {
            result.AppendLine("Version read failed: " + ex.Message);
        }

        result.AppendLine();
        result.AppendLine("DAF XF MODEL FILE HEADERS:");

        InspectHeader(reader, result,
            "/vehicle/truck/daf_xf/truck.pmd");

        InspectHeader(reader, result,
            "/vehicle/truck/daf_xf/truck.pmg");

        InspectHeader(reader, result,
            "/vehicle/truck/daf_xf/truck.pmc");

        return result.ToString();
    }

    private static void InspectHeader(
        HashFsReader reader,
        StringBuilder result,
        string path)
    {
        result.AppendLine();
        result.AppendLine(path);

        try
        {
            var files = reader.Extract(path);

            if (files.Length == 0)
            {
                result.AppendLine("No data returned.");
                return;
            }

            var data = files[0];

            result.AppendLine($"File size: {data.Length} bytes");

            result.AppendLine(
                "First 32 bytes (HEX): " +
                Convert.ToHexString(data.Take(32).ToArray()));

            result.AppendLine(
                "First 32 bytes (TEXT): " +
                new string(data.Take(32)
                    .Select(b => b >= 32 && b <= 126
                        ? (char)b
                        : '.')
                    .ToArray()));
        }
        catch (Exception ex)
        {
            result.AppendLine("Extraction failed: " + ex.Message);
        }
    }
}

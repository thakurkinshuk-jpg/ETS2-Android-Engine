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

        result.AppendLine("ETS2 MATERIAL INSPECTION");
        result.AppendLine("------------------------");
        result.AppendLine($"HashFS Version: {reader.Version}");
        result.AppendLine($"Total entries: {reader.Entries.Count}");
        result.AppendLine();

        try
        {
            var version = reader.Extract("/version.txt");

            if (version.Length > 0)
            {
                result.AppendLine("GAME VERSION:");
                result.AppendLine(
                    Encoding.UTF8.GetString(version[0]).Trim());
            }
        }
        catch (Exception ex)
        {
            result.AppendLine("Version error: " + ex.Message);
        }

        var pmdFiles =
            reader.Extract("/vehicle/truck/daf_xf/truck.pmd");

        if (pmdFiles.Length == 0)
        {
            result.AppendLine("ERROR: truck.pmd not found.");
            return result.ToString();
        }

        byte[] pmd = pmdFiles[0];

        result.AppendLine();
        result.AppendLine("PMD FILE:");
        result.AppendLine($"Size: {pmd.Length} bytes");
        result.AppendLine(
            $"First 32-bit value: {BitConverter.ToUInt32(pmd, 0)}");

        string pmdText = Encoding.Latin1.GetString(pmd);

        var materialPaths = pmdText
            .Split('\0')
            .Where(s => s.StartsWith(
                "/automat/",
                StringComparison.Ordinal))
            .Where(s => s.EndsWith(
                ".mat",
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        result.AppendLine();
        result.AppendLine(
            $"MATERIAL REFERENCES FOUND: {materialPaths.Length}");

        foreach (string path in materialPaths)
        {
            result.AppendLine();
            result.AppendLine("================================");
            result.AppendLine("MATERIAL: " + path);
            result.AppendLine("================================");

            try
            {
                var files = reader.Extract(path);

                if (files.Length == 0)
                {
                    result.AppendLine("File not found in archive.");
                    continue;
                }

                string content =
                    Encoding.UTF8.GetString(files[0]);

                result.AppendLine(
                    $"File size: {files[0].Length} bytes");

                result.AppendLine(content);
            }
            catch (Exception ex)
            {
                result.AppendLine(
                    "Extraction error: " + ex.Message);
            }
        }

        result.AppendLine();
        result.AppendLine("MATERIAL INSPECTION COMPLETED.");

        return result.ToString();
    }
}

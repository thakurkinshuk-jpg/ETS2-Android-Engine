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

        result.AppendLine("ETS2 PMD MODEL ANALYSIS");
        result.AppendLine("-----------------------");
        result.AppendLine($"HashFS Version: {reader.Version}");
        result.AppendLine($"Total entries: {reader.Entries.Count}");
        result.AppendLine();

        try
        {
            var versionData = reader.Extract("/version.txt");

            if (versionData.Length > 0)
            {
                result.AppendLine("GAME VERSION:");
                result.AppendLine(
                    Encoding.UTF8.GetString(versionData[0]).Trim());
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

        byte[] data = pmdFiles[0];

        result.AppendLine();
        result.AppendLine("DAF XF TRUCK.PMD");
        result.AppendLine("----------------");
        result.AppendLine($"File size: {data.Length} bytes");
        result.AppendLine();

        result.AppendLine("COMPLETE PMD HEX DUMP:");

        for (int row = 0; row < data.Length; row += 16)
        {
            int count = Math.Min(16, data.Length - row);

            var hex = new StringBuilder();
            var ascii = new StringBuilder();

            for (int i = row; i < row + count; i++)
            {
                hex.Append(data[i].ToString("X2"));
                hex.Append(' ');

                byte value = data[i];

                ascii.Append(
                    value >= 32 && value <= 126
                        ? (char)value
                        : '.');
            }

            result.AppendLine(
                $"0x{row:X4}: {hex} | {ascii}");
        }

        result.AppendLine();
        result.AppendLine("PRINTABLE STRINGS:");

        var current = new StringBuilder();

        foreach (byte value in data)
        {
            if (value >= 32 && value <= 126)
            {
                current.Append((char)value);
            }
            else
            {
                if (current.Length >= 4)
                    result.AppendLine(current.ToString());

                current.Clear();
            }
        }

        if (current.Length >= 4)
            result.AppendLine(current.ToString());

        result.AppendLine();
        result.AppendLine("PMD ANALYSIS COMPLETED.");

        return result.ToString();
    }
}

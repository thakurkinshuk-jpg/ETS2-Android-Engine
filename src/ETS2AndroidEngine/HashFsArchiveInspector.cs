using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Buffers.Binary;
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

        var pmdFiles =
            reader.Extract("/vehicle/truck/daf_xf/truck.pmd");

        var pmgFiles =
            reader.Extract("/vehicle/truck/daf_xf/truck.pmg");

        var pmcFiles =
            reader.Extract("/vehicle/truck/daf_xf/truck.pmc");

        InspectHeader(pmdFiles, result, "truck.pmd");
        InspectHeader(pmgFiles, result, "truck.pmg");
        InspectHeader(pmcFiles, result, "truck.pmc");

        InspectPmgRawHeader(pmgFiles, result);

        return result.ToString();
    }

    private static void InspectPmgRawHeader(
        byte[][] files,
        StringBuilder result)
    {
        result.AppendLine();
        result.AppendLine("PMG VERSION 20 RAW HEADER:");
        result.AppendLine("--------------------------");

        if (files.Length == 0 || files[0].Length < 4)
        {
            result.AppendLine("PMG file is missing or incomplete.");
            return;
        }

        byte[] data = files[0];

        result.AppendLine($"PMG version byte: {data[0]}");
        result.AppendLine($"Total PMG size: {data.Length} bytes");
        result.AppendLine("Showing first 256 bytes.");
        result.AppendLine(
            "Each row contains raw HEX, ASCII and little-endian 32-bit words.");
        result.AppendLine();

        int length = Math.Min(256, data.Length);

        for (int row = 0; row < length; row += 16)
        {
            int count = Math.Min(16, length - row);

            var hex = new StringBuilder();
            var ascii = new StringBuilder();
            var words = new StringBuilder();

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

            for (int i = row; i + 4 <= row + count; i += 4)
            {
                uint value =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        data.AsSpan(i, 4));

                words.Append($"0x{value:X8} ");
            }

            result.AppendLine(
                $"Offset 0x{row:X4}: {hex}");

            result.AppendLine(
                $"  ASCII: {ascii}");

            result.AppendLine(
                $"  WORDS: {words}");

            result.AppendLine();
        }

        result.AppendLine("RAW HEADER DUMP COMPLETED.");
    }

    private static void InspectHeader(
        byte[][] files,
        StringBuilder result,
        string fileName)
    {
        result.AppendLine();
        result.AppendLine(fileName);

        try
        {
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
            result.AppendLine(
                "Inspection failed: " + ex.Message);
        }
    }
}

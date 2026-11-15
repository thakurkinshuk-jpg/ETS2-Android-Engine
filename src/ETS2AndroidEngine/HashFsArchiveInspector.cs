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

        var pmdFiles =
            reader.Extract("/vehicle/truck/daf_xf/truck.pmd");

        var pmgFiles =
            reader.Extract("/vehicle/truck/daf_xf/truck.pmg");

        var pmcFiles =
            reader.Extract("/vehicle/truck/daf_xf/truck.pmc");

        InspectHeader(pmdFiles, result, "truck.pmd");
        InspectHeader(pmgFiles, result, "truck.pmg");
        InspectHeader(pmcFiles, result, "truck.pmc");

        InspectPmg20Header(pmgFiles, result);

        return result.ToString();
    }

    private static void InspectPmg20Header(
        byte[][] files,
        StringBuilder result)
    {
        result.AppendLine();
        result.AppendLine("PMG VERSION 20 STRUCTURE:");

        if (files.Length == 0 || files[0].Length < 116)
        {
            result.AppendLine(
                "PMG file is missing or header is incomplete.");
            return;
        }

        byte[] data = files[0];

        uint U(int offset) =>
            BitConverter.ToUInt32(data, offset);

        float F(int offset) =>
            BitConverter.ToSingle(data, offset);

        result.AppendLine($"Magic: 0x{U(0):X8}");
        result.AppendLine($"Pieces: {U(4)}");
        result.AppendLine($"Parts: {U(8)}");
        result.AppendLine($"Bones: {U(12)}");
        result.AppendLine($"Locators: {U(16)}");

        result.AppendLine();
        result.AppendLine("DATA OFFSETS AND SIZES:");

        string[] names =
        {
            "Skeleton",
            "Parts",
            "Locators",
            "Pieces",
            "String pool",
            "Skin pool",
            "Dynamic pool",
            "Static pool",
            "Index pool"
        };

        int[] offsets =
        {
            60, 64, 68, 72, 76, 84, 92, 100, 108
        };

        for (int i = 0; i < names.Length; i++)
        {
            uint offset = U(offsets[i]);
            uint size = i < 4 ? 0 : U(offsets[i] + 4);

            result.AppendLine(
                $"{names[i]}: Offset={offset}, Size={size}");

            if (i >= 4)
            {
                ulong end = (ulong)offset + size;

                result.AppendLine(
                    end <= (ulong)data.Length
                        ? "  Range: Within file"
                        : "  Range: EXCEEDS FILE SIZE");
            }
        }

        result.AppendLine();
        result.AppendLine($"PMG file size: {data.Length}");
        result.AppendLine($"Bounding radius: {F(32)}");
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

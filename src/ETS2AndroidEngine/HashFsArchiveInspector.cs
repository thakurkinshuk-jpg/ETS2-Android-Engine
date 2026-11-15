using System;
using System.IO;
using System.Linq;
using System.Text;
using TruckLib.HashFs;
using TruckLib.Models;

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

        TryDecodeDafModel(pmdFiles, pmgFiles, result);

        return result.ToString();
    }

    private static void TryDecodeDafModel(
        byte[][] pmdFiles,
        byte[][] pmgFiles,
        StringBuilder result)
    {
        result.AppendLine();
        result.AppendLine("DAF XF MODEL DECODE TEST:");

        try
        {
            if (pmdFiles.Length == 0 || pmgFiles.Length == 0)
            {
                result.AppendLine("PMD or PMG file is missing.");
                return;
            }

            // Keep the original archive data unchanged.
            // This is only an experimental parser compatibility test.
            var compatiblePmg = (byte[])pmgFiles[0].Clone();

            var detectedVersion = compatiblePmg[0];

            result.AppendLine(
                $"Original PMG version byte: {detectedVersion}");

            if (detectedVersion == 20)
            {
                compatiblePmg[0] = 21;
                result.AppendLine(
                    "Experimental PMG version adjustment applied in memory.");
            }

            var model = Model.Load(
                pmdFiles[0],
                compatiblePmg);

            int partCount = model.Parts.Count;

            int pieceCount = model.Parts.Sum(
                part => part.Pieces.Count);

            int vertexCount = model.Parts.Sum(
                part => part.Pieces.Sum(
                    piece => piece.Vertices.Count));

            int triangleCount = model.Parts.Sum(
                part => part.Pieces.Sum(
                    piece => piece.Triangles.Count));

            result.AppendLine("Model parser completed.");
            result.AppendLine($"Parts: {partCount}");
            result.AppendLine($"Pieces: {pieceCount}");
            result.AppendLine($"Vertices: {vertexCount}");
            result.AppendLine($"Triangles: {triangleCount}");

            result.AppendLine(
                "NOTE: Version-20 compatibility is experimental. Geometry requires validation.");
        }
        catch (Exception ex)
        {
            result.AppendLine("Model decoding failed.");
            result.AppendLine(
                ex.GetType().Name + ": " + ex.Message);
        }
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

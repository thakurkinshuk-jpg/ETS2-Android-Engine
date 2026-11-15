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
            result.AppendLine("Could not read version.txt:");
            result.AppendLine(ex.Message);
        }

        result.AppendLine();

        try
        {
            var listing = reader.GetDirectoryListing("/");

            result.AppendLine("ROOT DIRECTORIES:");

            foreach (var folder in listing.Subdirectories.Take(30))
                result.AppendLine("[DIR] " + folder);

            result.AppendLine();
            result.AppendLine("ROOT FILES:");

            foreach (var file in listing.Files.Take(30))
                result.AppendLine("[FILE] " + file);

            result.AppendLine();
            result.AppendLine($"Directories: {listing.Subdirectories.Count}");
            result.AppendLine($"Files: {listing.Files.Count}");
        }
        catch (Exception ex)
        {
            result.AppendLine("Root directory listing unavailable.");
            result.AppendLine(ex.Message);
        }

        result.AppendLine();
        result.AppendLine("VEHICLE ASSETS:");

        try
        {
            var vehicles = reader.GetDirectoryListing("/vehicle");

            result.AppendLine("VEHICLE SUBDIRECTORIES:");

            foreach (var folder in vehicles.Subdirectories.Take(40))
                result.AppendLine("[DIR] " + folder);

            result.AppendLine();
            result.AppendLine("VEHICLE FILES:");

            foreach (var file in vehicles.Files.Take(40))
                result.AppendLine("[FILE] " + file);

            result.AppendLine();
            result.AppendLine($"Vehicle subdirectories: {vehicles.Subdirectories.Count}");
            result.AppendLine($"Vehicle files: {vehicles.Files.Count}");
        }
        catch (Exception ex)
        {
            result.AppendLine("Vehicle listing failed:");
            result.AppendLine(ex.Message);
        }

        return result.ToString();
    }
}



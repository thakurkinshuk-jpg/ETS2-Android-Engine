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

        result.AppendLine("ETS2 HASHFS ARCHIVE REPORT");
        result.AppendLine("==========================");
        result.AppendLine($"HashFS Version: {reader.Version}");
        result.AppendLine($"Indexed entries: {reader.Entries.Count}");
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
            result.AppendLine(
                $"Directories: {listing.Subdirectories.Count}");

            result.AppendLine(
                $"Files: {listing.Files.Count}");
        }
        catch (Exception ex)
        {
            result.AppendLine("Root directory listing unavailable.");
            result.AppendLine(ex.Message);
        }

        return result.ToString();
    }
}


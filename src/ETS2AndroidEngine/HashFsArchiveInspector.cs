using System;
using System.IO;
using System.Linq;
using TruckLib.HashFs;

namespace ETS2AndroidEngine;

public static class HashFsArchiveInspector
{
    public static string Inspect(Stream archiveStream)
    {
        using var reader = HashFsReader.Open(archiveStream);

        var listing = reader.GetDirectoryListing("/");

        var result = new System.Text.StringBuilder();

        result.AppendLine("ETS2 ARCHIVE INSPECTION");
        result.AppendLine("----------------------");
        result.AppendLine($"HashFS Version: {reader.Version}");
        result.AppendLine($"Total indexed entries: {reader.Entries.Count}");
        result.AppendLine();
        result.AppendLine("ROOT DIRECTORIES:");

        foreach (var folder in listing.Subdirectories)
            result.AppendLine("[DIR] " + folder);

        result.AppendLine();
        result.AppendLine("ROOT FILES:");

        foreach (var file in listing.Files)
            result.AppendLine("[FILE] " + file);

        return result.ToString();
    }
}

using System;
using System.Text;
using TruckLib.HashFs;

namespace ETS2AndroidEngine;

public static class HashFsArchiveInspector
{
    private static readonly string[] TexturePaths =
    {
        "/vehicle/truck/daf_xf/color.dds",
        "/material/environment/generic_s.dds",
        "/vehicle/truck/daf_xf/lightmask.dds",
        "/vehicle/truck/share/glass_ex.dds"
    };

    public static string Inspect(Stream archiveStream)
    {
        using var reader = HashFsReader.Open(archiveStream);

        var result = new StringBuilder();

        result.AppendLine("ETS2 DDS TEXTURE INSPECTION");
        result.AppendLine("--------------------------");
        result.AppendLine($"HashFS Version: {reader.Version}");
        result.AppendLine($"Total entries: {reader.Entries.Count}");

        foreach (string path in TexturePaths)
        {
            result.AppendLine();
            result.AppendLine("================================");
            result.AppendLine(path);
            result.AppendLine("================================");

            try
            {
                var files = reader.Extract(path);

                if (files.Length == 0)
                {
                    result.AppendLine("TEXTURE NOT FOUND");
                    continue;
                }

                byte[] data = files[0];

                result.AppendLine("STATUS: FOUND");
                result.AppendLine($"File size: {data.Length} bytes");

                if (data.Length < 128)
                {
                    result.AppendLine("ERROR: DDS header is incomplete.");
                    continue;
                }

                string magic = Encoding.ASCII.GetString(data, 0, 4);

                result.AppendLine($"Magic: {magic}");

                if (magic != "DDS ")
                {
                    result.AppendLine("ERROR: Invalid DDS signature.");
                    continue;
                }

                uint headerSize = BitConverter.ToUInt32(data, 4);
                uint height = BitConverter.ToUInt32(data, 12);
                uint width = BitConverter.ToUInt32(data, 16);
                uint mipmaps = BitConverter.ToUInt32(data, 28);
                uint pixelFormatSize = BitConverter.ToUInt32(data, 76);
                uint pixelFlags = BitConverter.ToUInt32(data, 80);

                string fourCC = Encoding.ASCII.GetString(data, 84, 4);

                uint rgbBits = BitConverter.ToUInt32(data, 88);

                result.AppendLine($"Header size: {headerSize}");
                result.AppendLine($"Dimensions: {width} x {height}");
                result.AppendLine($"Mipmaps: {(mipmaps == 0 ? 1 : mipmaps)}");
                result.AppendLine($"Pixel format size: {pixelFormatSize}");
                result.AppendLine($"Pixel flags: 0x{pixelFlags:X8}");
                result.AppendLine($"FourCC: {fourCC}");
                result.AppendLine($"RGB bit count: {rgbBits}");

                if (fourCC == "DX10")
                {
                    result.AppendLine("Extended DX10 header detected.");
                }
                else if (!string.IsNullOrWhiteSpace(fourCC))
                {
                    result.AppendLine("Compression: " + fourCC);
                }
                else
                {
                    result.AppendLine("Compression: Uncompressed or RGB format");
                }
            }
            catch (Exception ex)
            {
                result.AppendLine("ERROR: " + ex.Message);
            }
        }

        result.AppendLine();
        result.AppendLine("DDS INSPECTION COMPLETED.");

        return result.ToString();
    }
}

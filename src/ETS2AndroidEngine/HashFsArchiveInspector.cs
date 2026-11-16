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

    private const string ModelPath =
        "/vehicle/truck/daf_xf/truck.pmg";

    public static string Inspect(Stream archiveStream)
    {
        using var reader = HashFsReader.Open(archiveStream);

        var result = new StringBuilder();

        result.AppendLine("ETS2 ASSET INSPECTION");
        result.AppendLine("---------------------");
        result.AppendLine($"HashFS Version: {reader.Version}");
        result.AppendLine($"Total entries: {reader.Entries.Count}");

        // Inspect DDS textures.
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
                    result.AppendLine("Extended DX10 header detected.");
                else if (!string.IsNullOrWhiteSpace(fourCC))
                    result.AppendLine("Compression: " + fourCC);
                else
                    result.AppendLine("Compression: Uncompressed or RGB format");
            }
            catch (Exception ex)
            {
                result.AppendLine("ERROR: " + ex.Message);
            }
        }

        // Inspect the DAF XF PMG v20 model.
        result.AppendLine();
        result.AppendLine("================================");
        result.AppendLine("DAF XF PMG MODEL INSPECTION");
        result.AppendLine("================================");

        try
        {
            var files = reader.Extract(ModelPath);

            if (files.Length == 0)
            {
                result.AppendLine("MODEL NOT FOUND");
            }
            else
            {
                byte[] modelData = files[0];

                result.AppendLine("STATUS: FOUND");
                result.AppendLine($"File size: {modelData.Length} bytes");

                PmgV20Model model = PmgV20Reader.Read(modelData);

                result.AppendLine("PMG VERSION: 20");
                result.AppendLine($"Pieces: {model.PieceCount}");
                result.AppendLine($"Parts: {model.PartCount}");
                result.AppendLine($"Bones: {model.BoneCount}");
                result.AppendLine($"Weight width: {model.WeightWidth}");

                int totalVertices = 0;
                int totalIndices = 0;

                foreach (PmgV20Piece piece in model.Pieces)
                {
                    totalVertices += piece.VertexCount;
                    totalIndices += piece.Indices.Length;

                    result.AppendLine();
                    result.AppendLine($"Piece {piece.Index}:");
                    result.AppendLine($"  Vertices: {piece.VertexCount}");
                    result.AppendLine($"  Edges: {piece.EdgeCount}");
                    result.AppendLine($"  Triangles: {piece.Indices.Length / 3}");
                    result.AppendLine($"  Material index: {piece.MaterialIndex}");
                    result.AppendLine($"  Texture coordinate width: {piece.TexCoordWidth}");
                }

                result.AppendLine();
                result.AppendLine("MODEL GEOMETRY TOTALS");
                result.AppendLine($"Total vertices: {totalVertices}");
                result.AppendLine($"Total indices: {totalIndices}");
                result.AppendLine($"Total triangles: {totalIndices / 3}");
                result.AppendLine("PMG GEOMETRY READING COMPLETED.");
            }
        }
        catch (Exception ex)
        {
            result.AppendLine("PMG MODEL READING FAILED");
            result.AppendLine(ex.GetType().Name + ": " + ex.Message);
        }

        result.AppendLine();
        result.AppendLine("ASSET INSPECTION COMPLETED.");

        return result.ToString();
    }
}

using System;
using System.Linq;
using System.Text;

namespace ETS2AndroidEngine;

public static class UvDiagnostics
{
    public static string Build(PmgV20Model model)
    {
        var result = new StringBuilder();

        result.AppendLine("UV CHANNEL COORDINATE ANALYSIS");
        result.AppendLine("==============================");
        result.AppendLine("Ranges are calculated from all vertices.");
        result.AppendLine("First three UV samples are shown.");

        foreach (var piece in model.Pieces)
        {
            result.AppendLine();
            result.AppendLine("--------------------------------");
            result.AppendLine($"PIECE {piece.Index}");
            result.AppendLine($"Material index: {piece.MaterialIndex}");
            result.AppendLine($"Vertices: {piece.VertexCount}");
            result.AppendLine($"UV channels: {piece.TexCoordWidth}");

            int channelCount = piece.Vertices
                .Where(v => v.TextureCoordinates != null)
                .Select(v => v.TextureCoordinates.Length)
                .DefaultIfEmpty(0)
                .Max();

            for (int channel = 0; channel < channelCount; channel++)
            {
                float minU = float.MaxValue;
                float minV = float.MaxValue;
                float maxU = float.MinValue;
                float maxV = float.MinValue;
                int valid = 0;

                foreach (var vertex in piece.Vertices)
                {
                    if (vertex.TextureCoordinates.Length <= channel)
                        continue;

                    var uv = vertex.TextureCoordinates[channel];

                    if (float.IsNaN(uv.X) || float.IsNaN(uv.Y) ||
                        float.IsInfinity(uv.X) || float.IsInfinity(uv.Y))
                        continue;

                    minU = Math.Min(minU, uv.X);
                    minV = Math.Min(minV, uv.Y);
                    maxU = Math.Max(maxU, uv.X);
                    maxV = Math.Max(maxV, uv.Y);
                    valid++;
                }

                result.AppendLine();
                result.AppendLine($"CHANNEL {channel}");
                result.AppendLine($"Valid UVs: {valid}");

                if (valid == 0)
                {
                    result.AppendLine("No valid coordinates.");
                    continue;
                }

                result.AppendLine($"U range: {minU:F5} to {maxU:F5}");
                result.AppendLine($"V range: {minV:F5} to {maxV:F5}");
                result.AppendLine($"U span: {maxU - minU:F5}");
                result.AppendLine($"V span: {maxV - minV:F5}");
                result.AppendLine("First 3 UV samples:");

                int samples = 0;

                for (int i = 0;
                     i < piece.Vertices.Length && samples < 3;
                     i++)
                {
                    var vertex = piece.Vertices[i];

                    if (vertex.TextureCoordinates.Length <= channel)
                        continue;

                    var uv = vertex.TextureCoordinates[channel];

                    result.AppendLine(
                        $"  V{i}: U={uv.X:F5}, V={uv.Y:F5}");

                    samples++;
                }
            }
        }

        result.AppendLine();
        result.AppendLine("END OF UV ANALYSIS");

        return result.ToString();
    }
}

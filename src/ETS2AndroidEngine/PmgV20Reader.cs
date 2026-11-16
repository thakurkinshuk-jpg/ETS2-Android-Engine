
using System;
using System.Buffers.Binary;
using System.Text;
using System.Numerics;
using System.Collections.Generic;

namespace ETS2AndroidEngine
{
    public sealed class PmgV20Model
    {
        public int PieceCount { get; init; }
        public int PartCount { get; init; }
        public int BoneCount { get; init; }
        public int WeightWidth { get; init; }
        public List<PmgV20Piece> Pieces { get; init; } = new();
    }

    public sealed class PmgV20Piece
    {
        public int Index { get; init; }
        public int EdgeCount { get; init; }
        public int VertexCount { get; init; }
        public uint TexCoordMask { get; init; }
        public int TexCoordWidth { get; init; }
        public int MaterialIndex { get; init; }
        public PmgV20Vertex[] Vertices { get; init; } = Array.Empty<PmgV20Vertex>();
        public ushort[] Indices { get; init; } = Array.Empty<ushort>();
    }

    public sealed class PmgV20Vertex
    {
        public bool HasPosition { get; set; }
        public bool HasNormal { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Normal { get; set; }
        public Vector4? Tangent { get; set; }
        public Vector2[] TextureCoordinates { get; set; } = Array.Empty<Vector2>();
        public byte[]? Color { get; set; }
        public byte[]? Factor { get; set; }
        public byte[]? BoneIndices { get; set; }
        public byte[]? BoneWeights { get; set; }
    }

    public static class PmgV20Reader
    {
        private const int HeaderSize = 104;
        private const int PieceSize = 100;

        private static int I32(byte[] d, int o) =>
            BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o, 4));

        private static uint U32(byte[] d, int o) =>
            BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o, 4));

        public static PmgV20Model Read(byte[] data)
        {
            if (data == null || data.Length < HeaderSize)
                throw new InvalidOperationException("Invalid PMG file.");

            if (data[0] != 0x14 || data[1] != 'g' ||
                data[2] != 'm' || data[3] != 'P')
                throw new InvalidOperationException("Not a PMG v20 file.");

            int pieceCount = I32(data, 4);
            int partCount = I32(data, 8);
            int boneCount = I32(data, 12);
            int weightWidth = I32(data, 16);
            int piecesOffset = I32(data, 76);

            if (pieceCount < 0 || pieceCount > 10000 ||
                piecesOffset < 0 ||
                (long)piecesOffset + (long)pieceCount * PieceSize > data.Length)
                throw new InvalidOperationException("Invalid PMG piece section.");

            int indexPoolOffset = I32(data, 96);
            int indexPoolSize = I32(data, 100);

            var report = new StringBuilder();

            report.AppendLine("PMG V20 ALL-PIECE DIAGNOSTIC");
            report.AppendLine($"Total pieces: {pieceCount}");
            report.AppendLine($"Index pool: {indexPoolOffset}, size: {indexPoolSize}");
            report.AppendLine();

            int limit = Math.Min(pieceCount, 32);

            for (int i = 0; i < limit; i++)
            {
                int p = piecesOffset + i * PieceSize;

                report.AppendLine($"PIECE {i}");
                report.AppendLine($"Record: {p}");
                report.AppendLine($"Edges: {I32(data, p)}");
                report.AppendLine($"Vertices: {I32(data, p + 4)}");
                report.AppendLine($"TexCoordMask: 0x{U32(data, p + 8):X8}");
                report.AppendLine($"TexCoordWidth: {I32(data, p + 12)}");
                report.AppendLine($"Material: {I32(data, p + 16)}");

                report.AppendLine($"Skeleton: {I32(data, p + 60)}");
                report.AppendLine($"Position: {I32(data, p + 64)}");
                report.AppendLine($"Normal: {I32(data, p + 68)}");
                report.AppendLine($"TexCoord: {I32(data, p + 72)}");
                report.AppendLine($"Color: {I32(data, p + 76)}");
                report.AppendLine($"Factor: {I32(data, p + 80)}");
                report.AppendLine($"Tangent: {I32(data, p + 84)}");
                report.AppendLine($"Index: {I32(data, p + 88)}");
                report.AppendLine($"BoneIndex: {I32(data, p + 92)}");
                report.AppendLine($"BoneWeight: {I32(data, p + 96)}");

                report.AppendLine(
                    $"Raw: {Convert.ToHexString(data.AsSpan(p, PieceSize))}");
                report.AppendLine("--------------------");
            }

            if (pieceCount > limit)
                report.AppendLine($"Remaining pieces not shown: {pieceCount - limit}");

            throw new InvalidOperationException(report.ToString());
        }
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

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

        private static ushort U16(byte[] d, int o) =>
            BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o, 2));

        private static float F32(byte[] d, int o) =>
            BitConverter.Int32BitsToSingle(I32(d, o));

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
            int vertexPoolOffset = I32(data, 88);
            int vertexPoolSize = I32(data, 92);
            int indexPoolOffset = I32(data, 96);
            int indexPoolSize = I32(data, 100);

            if (pieceCount < 0 || pieceCount > 10000 ||
                partCount < 0 || boneCount < 0 ||
                piecesOffset < 0 ||
                (long)piecesOffset + (long)pieceCount * PieceSize > data.Length)
                throw new InvalidOperationException("Invalid PMG header.");

            CheckRange(data, vertexPoolOffset, vertexPoolSize, "vertex pool");
            CheckRange(data, indexPoolOffset, indexPoolSize, "index pool");

            var model = new PmgV20Model
            {
                PieceCount = pieceCount,
                PartCount = partCount,
                BoneCount = boneCount,
                WeightWidth = weightWidth
            };

            for (int i = 0; i < pieceCount; i++)
            {
                int p = piecesOffset + i * PieceSize;

                int edges = I32(data, p);
                int verts = I32(data, p + 4);
                uint mask = U32(data, p + 8);
                int texWidth = I32(data, p + 12);
                int material = I32(data, p + 16);

                int position = I32(data, p + 64);
                int normal = I32(data, p + 68);
                int texCoord = I32(data, p + 72);
                int color = I32(data, p + 76);
                int factor = I32(data, p + 80);
                int tangent = I32(data, p + 84);

                // Actual asset evidence: +96 contains sequential index offsets.
                int triangleOffset = I32(data, p + 96);

                if (edges < 0 || verts < 0 || edges % 3 != 0)
                    throw new InvalidOperationException($"Invalid counts in piece {i}.");

                if (texWidth < 0 || texWidth > 8)
                    throw new InvalidOperationException($"Invalid texture width in piece {i}.");

                int stride = 0;
                if (position != -1) stride += 12;
                if (normal != -1) stride += 12;
                if (tangent != -1) stride += 16;
                if (texCoord != -1) stride += texWidth * 8;
                if (color != -1) stride += 4;
                if (factor != -1) stride += 4;

                if (verts > 0 && stride == 0)
                    throw new InvalidOperationException($"No vertex attributes in piece {i}.");

                CheckAttribute(position, 12, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "position");
                CheckAttribute(normal, 12, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "normal");
                CheckAttribute(tangent, 16, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "tangent");
                CheckAttribute(texCoord, texWidth * 8, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "texture coordinates");
                CheckAttribute(color, 4, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "color");
                CheckAttribute(factor, 4, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "factor");

                int triangleCount = edges / 3;
                int indexBytes = checked(triangleCount * 6);

                if (indexBytes > 0)
                    CheckPoolRange(triangleOffset, indexBytes,
                        indexPoolOffset, indexPoolSize, "indices");

                var vertices = new PmgV20Vertex[verts];

                for (int v = 0; v < verts; v++)
                {
                    int record = checked(v * stride);

                    var vertex = new PmgV20Vertex
                    {
                        HasPosition = position != -1,
                        HasNormal = normal != -1
                    };

                    if (position != -1)
                    {
                        int o = position + record;
                        vertex.Position = new Vector3(
                            F32(data, o), F32(data, o + 4), F32(data, o + 8));
                    }

                    if (normal != -1)
                    {
                        int o = normal + record;
                        vertex.Normal = new Vector3(
                            F32(data, o), F32(data, o + 4), F32(data, o + 8));
                    }

                    if (tangent != -1)
                    {
                        int o = tangent + record;
                        vertex.Tangent = new Vector4(
                            F32(data, o), F32(data, o + 4),
                            F32(data, o + 8), F32(data, o + 12));
                    }

                    if (texCoord != -1)
                    {
                        var uv = new Vector2[texWidth];

                        for (int t = 0; t < texWidth; t++)
                        {
                            int o = texCoord + record + t * 8;
                            uv[t] = new Vector2(F32(data, o), F32(data, o + 4));
                        }

                        vertex.TextureCoordinates = uv;
                    }

                    if (color != -1)
                        vertex.Color = data.AsSpan(color + record, 4).ToArray();

                    if (factor != -1)
                        vertex.Factor = data.AsSpan(factor + record, 4).ToArray();

                    vertices[v] = vertex;
                }

                var indices = new ushort[triangleCount * 3];

                for (int t = 0; t < triangleCount; t++)
                {
                    int o = triangleOffset + t * 6;

                    indices[t * 3] = U16(data, o);
                    indices[t * 3 + 1] = U16(data, o + 2);
                    indices[t * 3 + 2] = U16(data, o + 4);

                    if (indices[t * 3] >= verts ||
                        indices[t * 3 + 1] >= verts ||
                        indices[t * 3 + 2] >= verts)
                        throw new InvalidOperationException(
                            $"Invalid vertex index in piece {i}.");
                }

                model.Pieces.Add(new PmgV20Piece
                {
                    Index = i,
                    EdgeCount = edges,
                    VertexCount = verts,
                    TexCoordMask = mask,
                    TexCoordWidth = texWidth,
                    MaterialIndex = material,
                    Vertices = vertices,
                    Indices = indices
                });
            }

            return model;
        }

        private static void CheckRange(byte[] data, int offset, int size, string name)
        {
            if (offset < 0 || size < 0 ||
                (long)offset + size > data.Length)
                throw new InvalidOperationException($"Invalid {name} range.");
        }

        private static void CheckPoolRange(
            int offset, int length, int poolOffset, int poolSize, string name)
        {
            if (offset < poolOffset || length < 0 ||
                (long)offset + length > (long)poolOffset + poolSize)
                throw new InvalidOperationException($"PMG {name} outside pool.");
        }

        private static void CheckAttribute(
            int offset, int size, int count, int stride,
            int poolOffset, int poolSize, string name)
        {
            if (offset == -1) return;

            if (offset < 0 || size < 0 || count < 0)
                throw new InvalidOperationException($"Invalid {name} attribute.");

            if (count == 0) return;

            long length = (long)(count - 1) * stride + size;

            if (length > int.MaxValue)
                throw new InvalidOperationException($"{name} is too large.");

            CheckPoolRange(offset, (int)length, poolOffset, poolSize, name);
        }
    }
}

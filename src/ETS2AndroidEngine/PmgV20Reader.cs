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

        public Vector2[] TextureCoordinates { get; set; }
            = Array.Empty<Vector2>();

        public byte[]? Color { get; set; }
        public byte[]? Factor { get; set; }

        public byte[]? BoneIndices { get; set; }
        public byte[]? BoneWeights { get; set; }
    }

    public static class PmgV20Reader
    {
        private const int HeaderSize = 104;
        private const int PieceSize = 100;

        public static PmgV20Model Read(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (data.Length < HeaderSize)
                throw new InvalidOperationException(
                    "PMG file is smaller than its 104-byte header.");

            // Version and signature: 14 67 6D 50
            if (data[0] != 0x14 ||
                data[1] != (byte)'g' ||
                data[2] != (byte)'m' ||
                data[3] != (byte)'P')
            {
                throw new InvalidOperationException(
                    "This is not a PMG v20 file.");
            }

            int pieceCount = I32(data, 4);
            int partCount = I32(data, 8);
            int boneCount = I32(data, 12);
            int weightWidth = I32(data, 16);
            int locatorCount = I32(data, 20);

            int skeletonOffset = I32(data, 64);
            int partsOffset = I32(data, 68);
            int locatorsOffset = I32(data, 72);
            int piecesOffset = I32(data, 76);

            int stringPoolOffset = I32(data, 80);
            int stringPoolSize = I32(data, 84);

            int vertexPoolOffset = I32(data, 88);
            int vertexPoolSize = I32(data, 92);

            int indexPoolOffset = I32(data, 96);
            int indexPoolSize = I32(data, 100);

            if (pieceCount < 0 || partCount < 0 ||
                boneCount < 0 || locatorCount < 0 ||
                weightWidth < 0)
            {
                throw new InvalidOperationException(
                    "PMG header contains invalid counts.");
            }

            // Validate the major sections before reading them.
            CheckSection(data, skeletonOffset, boneCount, 200, "skeleton");
            CheckSection(data, partsOffset, partCount, 24, "parts");
            CheckSection(data, locatorsOffset, locatorCount, 44, "locators");
            CheckSection(data, piecesOffset, pieceCount, PieceSize, "pieces");

            CheckRange(data, stringPoolOffset, stringPoolSize, "string pool");
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
                int p = checked(piecesOffset + i * PieceSize);

                int edges = I32(data, p);
                int verts = I32(data, p + 4);
                uint texCoordMask = U32(data, p + 8);
                int texCoordWidth = I32(data, p + 12);
                int material = I32(data, p + 16);

                int positionOffset = I32(data, p + 64);
                int normalOffset = I32(data, p + 68);
                int texCoordOffset = I32(data, p + 72);
                int colorOffset = I32(data, p + 76);
                int factorOffset = I32(data, p + 80);
                int tangentOffset = I32(data, p + 84);
                int triangleOffset = I32(data, p + 88);
                int boneIndexOffset = I32(data, p + 92);
                int boneWeightOffset = I32(data, p + 96);

                if (edges < 0 || verts < 0 || edges % 3 != 0)
                    throw new InvalidOperationException(
                        $"Piece {i} has invalid vertex or edge counts.");

                if (texCoordWidth < 0 || texCoordWidth > 8)
                    throw new InvalidOperationException(
                        $"Piece {i} has an unsupported texture coordinate width.");

                if ((boneIndexOffset == -1) != (boneWeightOffset == -1))
                    throw new InvalidOperationException(
                        $"Piece {i} has incomplete bone data.");

                // Calculate the interleaved vertex-record stride.
                int stride = 0;

                if (positionOffset != -1) stride += 12;
                if (normalOffset != -1) stride += 12;
                if (tangentOffset != -1) stride += 16;

                if (texCoordOffset != -1)
                    stride += texCoordWidth * 8;

                if (colorOffset != -1) stride += 4;
                if (factorOffset != -1) stride += 4;

                if (boneIndexOffset != -1)
                    stride += 8;

                if (verts > 0 && stride == 0)
                    throw new InvalidOperationException(
                        $"Piece {i} has vertices but no vertex attributes.");

                // Verify each attribute stream stays inside the vertex pool.
                CheckVertexAttribute(positionOffset, 12, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "position");

                CheckVertexAttribute(normalOffset, 12, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "normal");

                CheckVertexAttribute(tangentOffset, 16, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "tangent");

                CheckVertexAttribute(texCoordOffset, texCoordWidth * 8,
                    verts, stride, vertexPoolOffset, vertexPoolSize, "texcoords");

                CheckVertexAttribute(colorOffset, 4, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "color");

                CheckVertexAttribute(factorOffset, 4, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "factor");

                CheckVertexAttribute(boneIndexOffset, 4, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "bone indices");

                CheckVertexAttribute(boneWeightOffset, 4, verts, stride,
                    vertexPoolOffset, vertexPoolSize, "bone weights");

                int triangleCount = edges / 3;
                int indexBytes = checked(triangleCount * 6);

                if (indexBytes > 0)
                {
                    CheckPoolRange(
                        triangleOffset, indexBytes,
                        indexPoolOffset, indexPoolSize, "triangle indices");
                }

                var vertices = new PmgV20Vertex[verts];

                for (int v = 0; v < verts; v++)
                {
                    int record = checked(v * stride);

                    var vertex = new PmgV20Vertex
                    {
                        HasPosition = positionOffset != -1,
                        HasNormal = normalOffset != -1
                    };

                    if (positionOffset != -1)
                    {
                        int o = positionOffset + record;
                        vertex.Position = new Vector3(
                            F32(data, o),
                            F32(data, o + 4),
                            F32(data, o + 8));
                    }

                    if (normalOffset != -1)
                    {
                        int o = normalOffset + record;
                        vertex.Normal = new Vector3(
                            F32(data, o),
                            F32(data, o + 4),
                            F32(data, o + 8));
                    }

                    if (tangentOffset != -1)
                    {
                        int o = tangentOffset + record;

                        vertex.Tangent = new Vector4(
                            F32(data, o),
                            F32(data, o + 4),
                            F32(data, o + 8),
                            F32(data, o + 12));
                    }

                    if (texCoordOffset != -1)
                    {
                        var coordinates = new Vector2[texCoordWidth];

                        for (int t = 0; t < texCoordWidth; t++)
                        {
                            int o = texCoordOffset + record + t * 8;

                            coordinates[t] = new Vector2(
                                F32(data, o),
                                F32(data, o + 4));
                        }

                        vertex.TextureCoordinates = coordinates;
                    }

                    if (colorOffset != -1)
                        vertex.Color = Bytes(data, colorOffset + record, 4);

                    if (factorOffset != -1)
                        vertex.Factor = Bytes(data, factorOffset + record, 4);

                    if (boneIndexOffset != -1)
                    {
                        vertex.BoneIndices =
                            Bytes(data, boneIndexOffset + record, 4);

                        vertex.BoneWeights =
                            Bytes(data, boneWeightOffset + record, 4);
                    }

                    vertices[v] = vertex;
                }

                var indices = new ushort[checked(triangleCount * 3)];

                for (int t = 0; t < triangleCount; t++)
                {
                    int o = triangleOffset + t * 6;

                    indices[t * 3] = U16(data, o);
                    indices[t * 3 + 1] = U16(data, o + 2);
                    indices[t * 3 + 2] = U16(data, o + 4);

                    if (indices[t * 3] >= verts ||
                        indices[t * 3 + 1] >= verts ||
                        indices[t * 3 + 2] >= verts)
                    {
                        throw new InvalidOperationException(
                            $"Piece {i} contains an invalid vertex index.");
                    }
                }

                model.Pieces.Add(new PmgV20Piece
                {
                    Index = i,
                    EdgeCount = edges,
                    VertexCount = verts,
                    TexCoordMask = texCoordMask,
                    TexCoordWidth = texCoordWidth,
                    MaterialIndex = material,
                    Vertices = vertices,
                    Indices = indices
                });
            }

            return model;
        }

        private static int I32(byte[] d, int o)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(
                d.AsSpan(o, 4));
        }

        private static uint U32(byte[] d, int o)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(
                d.AsSpan(o, 4));
        }

        private static ushort U16(byte[] d, int o)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(
                d.AsSpan(o, 2));
        }

        private static float F32(byte[] d, int o)
        {
            return BitConverter.Int32BitsToSingle(I32(d, o));
        }

        private static byte[] Bytes(byte[] d, int o, int count)
        {
            return d.AsSpan(o, count).ToArray();
        }

        private static void CheckRange(
            byte[] data, int offset, int size, string name)
        {
            if (offset < 0 || size < 0 ||
                (long)offset + size > data.Length)
            {
                throw new InvalidOperationException(
                    $"PMG {name} is outside the file.");
            }
        }

        private static void CheckSection(
            byte[] data, int offset, int count, int itemSize, string name)
        {
            if (count == 0)
                return;

            if (count < 0 || offset < 0)
                throw new InvalidOperationException(
                    $"PMG {name} section has invalid values.");

            long size = (long)count * itemSize;
            CheckRange(data, offset, checked((int)size), name);
        }

        private static void CheckPoolRange(
            int offset, int length,
            int poolOffset, int poolSize, string name)
        {
            long end = (long)offset + length;
            long poolEnd = (long)poolOffset + poolSize;

            if (offset < poolOffset || length < 0 || end > poolEnd)
            {
                throw new InvalidOperationException(
                    $"PMG {name} data is outside its declared pool.");
            }
        }

        private static void CheckVertexAttribute(
            int offset, int attributeSize, int vertexCount, int stride,
            int poolOffset, int poolSize, string name)
        {
            if (offset == -1)
                return;

            if (offset < 0 || attributeSize < 0 || vertexCount < 0)
                throw new InvalidOperationException(
                    $"Invalid {name} attribute.");

            if (vertexCount == 0)
                return;

            long length =
                (long)(vertexCount - 1) * stride + attributeSize;

            if (length > int.MaxValue)
                throw new InvalidOperationException(
                    $"{name} attribute is too large.");

            CheckPoolRange(
                offset, (int)length,
                poolOffset, poolSize, name);
        }
    }
}


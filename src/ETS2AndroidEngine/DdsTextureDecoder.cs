using System;
using System.Buffers.Binary;

namespace ETS2AndroidEngine;

public sealed class DecodedDdsTexture
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Rgba { get; init; } = Array.Empty<byte>();
}

public static class DdsTextureDecoder
{
    public static DecodedDdsTexture Decode(byte[] data)
    {
        if (data == null || data.Length < 128)
            throw new InvalidOperationException("Invalid DDS file.");

        if (data[0] != 'D' || data[1] != 'D' ||
            data[2] != 'S' || data[3] != ' ')
            throw new InvalidOperationException("Invalid DDS signature.");

        int headerSize = ReadInt(data, 4);
        int height = ReadInt(data, 12);
        int width = ReadInt(data, 16);

        if (headerSize != 124 || width <= 0 || height <= 0 ||
            width > 8192 || height > 8192)
            throw new InvalidOperationException("Invalid DDS dimensions.");

        string format = System.Text.Encoding.ASCII.GetString(data, 84, 4);

        int blockSize;

        if (format == "DXT1")
            blockSize = 8;
        else if (format == "DXT5")
            blockSize = 16;
        else
            throw new NotSupportedException(
                "Unsupported DDS compression: " + format);

        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;

        long required = 128L + (long)blocksX * blocksY * blockSize;

        if (required > data.Length)
            throw new InvalidOperationException("DDS pixel data is incomplete.");

        byte[] rgba = new byte[checked(width * height * 4)];

        int offset = 128;

        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (format == "DXT1")
                    DecodeDxt1Block(data, offset, rgba, width, height, bx, by);
                else
                    DecodeDxt5Block(data, offset, rgba, width, height, bx, by);

                offset += blockSize;
            }
        }

        return new DecodedDdsTexture
        {
            Width = width,
            Height = height,
            Rgba = rgba
        };
    }

    private static int ReadInt(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(
            data.AsSpan(offset, 4));
    }

    private static ushort ReadU16(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(
            data.AsSpan(offset, 2));
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(
            data.AsSpan(offset, 4));
    }

    private static void DecodeDxt1Block(
        byte[] data, int offset, byte[] output,
        int width, int height, int bx, int by)
    {
        ushort c0 = ReadU16(data, offset);
        ushort c1 = ReadU16(data, offset + 2);
        uint indices = ReadU32(data, offset + 4);

        var colors = BuildColors(c0, c1, c0 > c1);

        for (int i = 0; i < 16; i++)
        {
            int x = bx * 4 + (i % 4);
            int y = by * 4 + (i / 4);

            if (x >= width || y >= height)
                continue;

            int index = (int)((indices >> (i * 2)) & 3);
            WritePixel(output, width, x, y, colors[index]);
        }
    }

    private static void DecodeDxt5Block(
        byte[] data, int offset, byte[] output,
        int width, int height, int bx, int by)
    {
        byte a0 = data[offset];
        byte a1 = data[offset + 1];

        ulong alphaBits = 0;

        for (int i = 0; i < 6; i++)
            alphaBits |= (ulong)data[offset + 2 + i] << (8 * i);

        byte[] alpha = BuildAlphaTable(a0, a1);

        ushort c0 = ReadU16(data, offset + 8);
        ushort c1 = ReadU16(data, offset + 10);
        uint colorBits = ReadU32(data, offset + 12);

        var colors = BuildColors(c0, c1, true);

        for (int i = 0; i < 16; i++)
        {
            int x = bx * 4 + (i % 4);
            int y = by * 4 + (i / 4);

            if (x >= width || y >= height)
                continue;

            int colorIndex = (int)((colorBits >> (i * 2)) & 3);
            int alphaIndex = (int)((alphaBits >> (i * 3)) & 7);

            byte[] pixel = colors[colorIndex];
            pixel[3] = alpha[alphaIndex];

            WritePixel(output, width, x, y, pixel);
        }
    }

    private static byte[][] BuildColors(
        ushort c0, ushort c1, bool fourColorMode)
    {
        byte[] a = Decode565(c0);
        byte[] b = Decode565(c1);

        byte[][] colors =
        {
            new byte[] { a[0], a[1], a[2], 255 },
            new byte[] { b[0], b[1], b[2], 255 },
            new byte[4],
            new byte[4]
        };

        if (fourColorMode)
        {
            for (int c = 0; c < 3; c++)
            {
                colors[2][c] = (byte)((2 * a[c] + b[c]) / 3);
                colors[3][c] = (byte)((a[c] + 2 * b[c]) / 3);
            }

            colors[2][3] = 255;
            colors[3][3] = 255;
        }
        else
        {
            for (int c = 0; c < 3; c++)
                colors[2][c] = (byte)((a[c] + b[c]) / 2);

            colors[2][3] = 255;
            colors[3][0] = 0;
            colors[3][1] = 0;
            colors[3][2] = 0;
            colors[3][3] = 0;
        }

        return colors;
    }

    private static byte[] Decode565(ushort value)
    {
        int r = (value >> 11) & 31;
        int g = (value >> 5) & 63;
        int b = value & 31;

        return new byte[]
        {
            (byte)((r * 255 + 15) / 31),
            (byte)((g * 255 + 31) / 63),
            (byte)((b * 255 + 15) / 31)
        };
    }

    private static byte[] BuildAlphaTable(byte a0, byte a1)
    {
        byte[] table = new byte[8];

        table[0] = a0;
        table[1] = a1;

        if (a0 > a1)
        {
            for (int i = 1; i <= 6; i++)
                table[i + 1] = (byte)(
                    ((7 - i) * a0 + i * a1) / 7);
        }
        else
        {
            for (int i = 1; i <= 4; i++)
                table[i + 1] = (byte)(
                    ((5 - i) * a0 + i * a1) / 5);

            table[6] = 0;
            table[7] = 255;
        }

        return table;
    }

    private static void WritePixel(
        byte[] output, int width, int x, int y, byte[] color)
    {
        int index = (y * width + x) * 4;

        output[index] = color[0];
        output[index + 1] = color[1];
        output[index + 2] = color[2];
        output[index + 3] = color[3];
    }
}

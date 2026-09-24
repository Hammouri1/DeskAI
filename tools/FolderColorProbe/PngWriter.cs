using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DeskAI.FolderColorProbe;

/// <summary>
/// The smallest PNG writer the probe needs: 8-bit RGBA, no filtering. The BCL has no image
/// encoder, and the probe takes no package references (ADR 0046 review).
/// </summary>
internal static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    internal static byte[] Write(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4)
        {
            throw new ArgumentException("The pixels do not match the size.", nameof(rgba));
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // colour type: RGBA
        WriteChunk(png, "IHDR", header);

        using var data = new MemoryStream();
        using (var deflate = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = width * 4;
            for (var y = 0; y < height; y++)
            {
                deflate.WriteByte(0); // filter: none
                deflate.Write(rgba.Slice(y * row, row));
            }
        }

        WriteChunk(png, "IDAT", data.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream png, string type, ReadOnlySpan<byte> body)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, body.Length);
        png.Write(number);

        var typeAndBody = new byte[4 + body.Length];
        Encoding.ASCII.GetBytes(type, typeAndBody);
        body.CopyTo(typeAndBody.AsSpan(4));
        png.Write(typeAndBody);

        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typeAndBody));
        png.Write(number);
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

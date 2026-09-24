using System.Buffers.Binary;

namespace DeskAI.FolderColorProbe;

/// <summary>A plain folder shape in one colour, as an <c>.ico</c> holding PNG images.</summary>
internal static class FolderIcon
{
    private static readonly int[] Sizes = [16, 32, 48, 256];

    internal static byte[] Create(Rgb color)
    {
        var images = Sizes.Select(size => PngWriter.Write(size, size, Pixels(size, color))).ToList();

        using var ico = new MemoryStream();
        Span<byte> field = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(field, 0);
        ico.Write(field[..2]);
        BinaryPrimitives.WriteUInt16LittleEndian(field, 1); // type: icon
        ico.Write(field[..2]);
        BinaryPrimitives.WriteUInt16LittleEndian(field, (ushort)images.Count);
        ico.Write(field[..2]);

        var offset = 6 + (16 * images.Count);
        for (var i = 0; i < images.Count; i++)
        {
            var size = Sizes[i] == 256 ? (byte)0 : (byte)Sizes[i]; // 0 means 256
            ico.Write([size, size, 0, 0]);
            BinaryPrimitives.WriteUInt16LittleEndian(field, 1); // planes
            ico.Write(field[..2]);
            BinaryPrimitives.WriteUInt16LittleEndian(field, 32); // bits per pixel
            ico.Write(field[..2]);
            BinaryPrimitives.WriteInt32LittleEndian(field, images[i].Length);
            ico.Write(field);
            BinaryPrimitives.WriteInt32LittleEndian(field, offset);
            ico.Write(field);
            offset += images[i].Length;
        }

        foreach (var image in images)
        {
            ico.Write(image);
        }

        return ico.ToArray();
    }

    /// <summary>RGBA pixels: a darker tab and a body in <paramref name="color"/>, the rest clear.</summary>
    internal static byte[] Pixels(int size, Rgb color)
    {
        var tab = new Rgb((byte)(color.R * 3 / 4), (byte)(color.G * 3 / 4), (byte)(color.B * 3 / 4));
        var pixels = new byte[size * size * 4];
        Fill(pixels, size, 0.06, 0.16, 0.42, 0.28, tab);
        Fill(pixels, size, 0.06, 0.24, 0.94, 0.84, color);
        return pixels;
    }

    private static void Fill(byte[] pixels, int size, double left, double top, double right, double bottom, Rgb color)
    {
        for (var y = (int)(top * size); y < (int)(bottom * size); y++)
        {
            for (var x = (int)(left * size); x < (int)(right * size); x++)
            {
                var i = ((y * size) + x) * 4;
                pixels[i] = color.R;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.B;
                pixels[i + 3] = 255;
            }
        }
    }
}

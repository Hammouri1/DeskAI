using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DeskAI.FolderColorProbe;

namespace DeskAI.FolderColorProbe.Tests;

public sealed class PngWriterTests
{
    [Fact]
    public void Starts_with_the_png_signature_and_a_header_of_the_right_size()
    {
        var png = PngWriter.Write(3, 2, new byte[3 * 2 * 4]);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    [Fact]
    public void Ends_with_the_known_iend_chunk()
    {
        var png = PngWriter.Write(1, 1, new byte[4]);

        Assert.Equal(new byte[] { 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }, png[^12..]);
    }

    [Fact]
    public void The_pixels_come_back_out_of_the_image_data()
    {
        byte[] rgba = [1, 2, 3, 4, 5, 6, 7, 8];
        var png = PngWriter.Write(2, 1, rgba);

        var idatLength = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        Assert.Equal("IDAT", Encoding.ASCII.GetString(png, 37, 4));
        using var inflate = new ZLibStream(new MemoryStream(png, 41, idatLength), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);

        // One filter byte (0, none) in front of each row.
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, raw.ToArray());
    }

    [Fact]
    public void Refuses_pixels_of_the_wrong_length() =>
        Assert.Throws<ArgumentException>(() => PngWriter.Write(2, 2, new byte[4]));
}

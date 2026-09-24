using System.Buffers.Binary;
using DeskAI.FolderColorProbe;

namespace DeskAI.FolderColorProbe.Tests;

public sealed class FolderIconTests
{
    [Fact]
    public void Holds_four_png_images_of_the_usual_sizes()
    {
        var ico = FolderIcon.Create(new Rgb(230, 0, 230));

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));
        int[] sizes = [16, 32, 48, 256];
        for (var i = 0; i < sizes.Length; i++)
        {
            var entry = ico.AsSpan(6 + (i * 16));
            Assert.Equal(sizes[i] == 256 ? 0 : sizes[i], entry[0]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            Assert.Equal(0x89, ico[offset]);
            Assert.Equal((byte)'P', ico[offset + 1]);
            Assert.True(offset + length <= ico.Length);
        }
    }

    [Fact]
    public void Most_of_the_folder_shape_is_the_chosen_colour()
    {
        var pixels = FolderIcon.Pixels(48, new Rgb(0, 180, 0));

        var matching = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] == 0 && pixels[i + 1] == 180 && pixels[i + 2] == 0 && pixels[i + 3] == 255)
            {
                matching++;
            }
        }

        Assert.True(matching > 48 * 48 / 3, $"only {matching} pixels are the colour");
    }
}

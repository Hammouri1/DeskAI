using System.Runtime.InteropServices;

namespace DeskAI.FolderColorProbe;

/// <summary>The main screen's pixels, BGRA, top row first.</summary>
internal sealed record ScreenCapture(int Width, int Height, byte[] Bgra)
{
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    internal static ScreenCapture Take()
    {
        var width = GetSystemMetrics(0 /* SM_CXSCREEN */);
        var height = GetSystemMetrics(1 /* SM_CYSCREEN */);
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, 0, 0, SrcCopy | CaptureBlt))
            {
                throw new InvalidOperationException("Windows did not copy the screen.");
            }

            SelectObject(memory, old); // GetDIBits needs the bitmap not selected
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // negative: top row first
                Planes = 1,
                BitCount = 32,
            };
            var bgra = new byte[width * height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)height, bgra, ref header, 0) != height)
            {
                throw new InvalidOperationException("Windows did not give the screen's pixels.");
            }

            return new ScreenCapture(width, height, bgra);
        }
        finally
        {
            SelectObject(memory, old); // a bitmap still selected cannot be deleted
            DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    internal byte[] ToPng()
    {
        var rgba = new byte[Bgra.Length];
        for (var i = 0; i < Bgra.Length; i += 4)
        {
            rgba[i] = Bgra[i + 2];
            rgba[i + 1] = Bgra[i + 1];
            rgba[i + 2] = Bgra[i];
            rgba[i + 3] = 255;
        }

        return PngWriter.Write(Width, Height, rgba);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
}

using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Xml;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Content;

/// <summary>Opens only bounded, explicitly selected image sources under a connected root.</summary>
public sealed class VisualAssetReader(IPathPolicy pathPolicy, PdfProcessReader pdfReader) : IVisualAssetReader
{
    private const long MaxFileBytes = 8L * 1024 * 1024;
    private const int MaxImageBytes = 1024 * 1024;

    public async Task<IReadOnlyList<VisualAsset>> ReadAsync(AuthorizedRoot root,
        string relativePath, int remainingImages, CancellationToken cancellationToken)
    {
        if (!RootCapabilities.CanReadMetadata(root) || remainingImages <= 0
            || pathPolicy.ValidateRoot(root).Status == ValidationStatus.Blocked
            || pathPolicy.ValidateRelativePath(root, relativePath).Status == ValidationStatus.Blocked)
        {
            return [];
        }

        string fullPath;
        try
        {
            var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.CanonicalPath));
            fullPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
            if (!fullPath.StartsWith(basePath + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            if (ContainsReparsePoint(fullPath))
            {
                return [];
            }

            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            if (stream.Length is < 8 or > MaxFileBytes)
            {
                return [];
            }

            if (ContainsReparsePoint(fullPath) || !HandleStillInside(stream.SafeFileHandle,
                    basePath, fullPath))
            {
                return [];
            }

            var name = Path.GetFileName(relativePath);
            var extension = Path.GetExtension(relativePath).ToLowerInvariant();
            if (extension is ".jpg" or ".jpeg" or ".png" or ".webp")
            {
                if (stream.Length > MaxImageBytes)
                {
                    return [];
                }

                var bytes = new byte[(int)stream.Length];
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                var mime = Mime(bytes);
                return mime is null ? [] : [new VisualAsset(root.Id, root.DisplayName,
                    relativePath, name, Location(root.DisplayName, relativePath, null), mime, bytes)];
            }

            if (extension == ".pptx")
            {
                return await ReadSlidesAsync(stream, root, relativePath, remainingImages,
                    cancellationToken).ConfigureAwait(false);
            }

            if (extension == ".pdf")
            {
                var found = await pdfReader.ReadImagesAsync(stream, remainingImages, cancellationToken)
                    .ConfigureAwait(false);
                return found.Select(item => new VisualAsset(root.Id, root.DisplayName,
                    relativePath, name, Location(root.DisplayName, relativePath, $"Page {item.Page}"),
                    item.MediaType, item.Bytes)).ToArray();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or InvalidDataException or XmlException
            or ArgumentException or NotSupportedException)
        {
            return [];
        }

        return [];
    }

    private static async Task<IReadOnlyList<VisualAsset>> ReadSlidesAsync(Stream stream,
        AuthorizedRoot root, string relativePath, int remainingImages, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 1000)
        {
            return [];
        }

        var results = new List<VisualAsset>();
        var name = Path.GetFileName(relativePath);
        for (var slide = 1; slide <= 40 && results.Count < remainingImages; slide++)
        {
            var usedIds = await SlideImageIdsAsync(archive.GetEntry($"ppt/slides/slide{slide}.xml"),
                cancellationToken).ConfigureAwait(false);
            if (usedIds.Count == 0)
            {
                continue;
            }

            var rels = archive.GetEntry($"ppt/slides/_rels/slide{slide}.xml.rels");
            if (rels is null || rels.Length > 64 * 1024)
            {
                continue;
            }

            await using var relStream = rels.Open();
            using var xml = XmlReader.Create(relStream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 64 * 1024,
            });
            while (await xml.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (xml.NodeType != XmlNodeType.Element || xml.LocalName != "Relationship")
                {
                    continue;
                }

                if (xml.GetAttribute("Id") is not { } id || !usedIds.Contains(id)
                    || xml.GetAttribute("Type") is not { } type
                    || !type.EndsWith("/image", StringComparison.Ordinal)
                    || xml.GetAttribute("TargetMode") is not null)
                {
                    continue;
                }

                var target = xml.GetAttribute("Target");
                if (target is null || !target.StartsWith("../media/", StringComparison.Ordinal)
                    || target.Contains('\\') || target["../media/".Length..].Contains('/'))
                {
                    continue;
                }

                var entry = archive.GetEntry("ppt/media/" + target["../media/".Length..]);
                if (entry is null || entry.Length is < 8 or > MaxImageBytes)
                {
                    continue;
                }

                await using var imageStream = entry.Open();
                var bytes = new byte[(int)entry.Length];
                await imageStream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                var mime = Mime(bytes);
                if (mime is not null)
                {
                    results.Add(new VisualAsset(root.Id, root.DisplayName, relativePath, name,
                        Location(root.DisplayName, relativePath, $"Slide {slide}"), mime, bytes));
                    if (results.Count >= remainingImages)
                    {
                        break;
                    }
                }
            }
        }

        return results.AsReadOnly();
    }

    private static async Task<HashSet<string>> SlideImageIdsAsync(ZipArchiveEntry? entry,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (entry is null || entry.Length > 256 * 1024)
        {
            return ids;
        }

        await using var input = entry.Open();
        using var xml = XmlReader.Create(input, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 256 * 1024,
        });
        const string drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        const string relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        while (await xml.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "blip"
                && xml.NamespaceURI == drawing
                && xml.GetAttribute("embed", relationships) is { } id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static string? Mime(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ? "image/jpeg" :
        bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ? "image/png" :
        bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("WEBP"u8) ? "image/webp" : null;

    private static string Location(string rootName, string relativePath, string? section)
    {
        var folder = Path.GetDirectoryName(relativePath);
        return string.Join(" / ", new[] { rootName, folder, section }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static bool ContainsReparsePoint(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return true;
        }

        var current = root;
        foreach (var segment in fullPath[root.Length..]
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HandleStillInside(SafeHandle handle, string root, string requestedPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        var buffer = new char[32768];
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
        if (length is 0 or >= 32768)
        {
            return false;
        }

        var final = new string(buffer, 0, (int)length);
        if (!final.StartsWith(@"\\?\", StringComparison.Ordinal)
            || final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        final = final[4..];
        return final.StartsWith(root + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase)
            && string.Equals(final, requestedPath, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeHandle fileHandle,
        [Out] char[] filePath, uint filePathLength, uint flags);
}

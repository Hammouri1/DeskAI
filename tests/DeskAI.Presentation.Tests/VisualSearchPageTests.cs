using System.IO.Compression;
using System.Buffers.Binary;
using System.Net;
using DeskAI.AI.Transport;
using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;
using DeskAI.Core.Abstractions;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;

namespace DeskAI.Presentation.Tests;

/// <summary>Generated pictures and a fake AI transport: no personal folder or real key.</summary>
public sealed class VisualSearchPageTests
{
    private static readonly byte[] TinyPng = MakePng();

    private static byte[] MakePng()
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), 1);
        header[8] = 8;
        header[9] = 2; // RGB
        WriteChunk(png, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write([0, 255, 0, 0]); // filter byte and one red RGB pixel
        }

        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream png, string kind, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var type = System.Text.Encoding.ASCII.GetBytes(kind);
        png.Write(type);
        png.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (var value in type.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xEDB88320u : 0);
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        png.Write(length);
    }

    [Fact]
    public async Task Nested_PowerPoint_picture_waits_for_fresh_cloud_Send_and_shows_slide()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var root = app.MakeFolder("Pictures");
        var nested = Path.Combine(root, "one", "two");
        Directory.CreateDirectory(nested);
        var presentation = Path.Combine(nested, "untitled.PPTX");
        using (var archive = ZipFile.Open(presentation, ZipArchiveMode.Create))
        {
            using (var slide = new StreamWriter(archive.CreateEntry("ppt/slides/slide2.xml").Open()))
            {
                slide.Write("""
                    <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                           xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                    <a:blip r:embed="rId1"/>
                    </p:sld>
                    """);
            }

            using (var relation = new StreamWriter(archive.CreateEntry(
                "ppt/slides/_rels/slide2.xml.rels").Open()))
            {
                relation.Write("""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/image1.png"/>
                </Relationships>
                """);
            }
            using var image = archive.CreateEntry("ppt/media/image1.png").Open();
            image.Write(TinyPng);
        }

        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(root);
        search.Phrase = "PowerPoint with a dog smelling a flower";
        var batch = await search.PreparePictureSearchAsync(visualReadApproved: true);
        Assert.NotNull(batch);
        var offered = Assert.Single(batch.Images);
        Assert.Equal("untitled.PPTX", offered.FileName);
        Assert.Contains("Slide 2", offered.Location, StringComparison.Ordinal);
        Assert.Empty(app.Internet.Requests);

        await search.SearchPicturesAsync(batch, cloudSendApproved: false);
        Assert.Empty(app.Internet.Requests);
        Assert.Contains("not sent", search.VisualMessage, StringComparison.OrdinalIgnoreCase);

        batch = await search.PreparePictureSearchAsync(visualReadApproved: true);
        Assert.NotNull(batch);

        app.Internet.Reply = _ => new AiHttpResponse(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"{\"match\":true,\"reason\":\"A dog is near a flower.\"}"}}]}""");
        await search.SearchPicturesAsync(batch, cloudSendApproved: true);
        var hit = Assert.Single(search.VisualResults);
        Assert.Contains("Slide 2", hit.Location, StringComparison.Ordinal);
        Assert.Contains("dog", hit.Snippet, StringComparison.OrdinalIgnoreCase);
        var sent = Assert.Single(app.Internet.Requests).Body;
        Assert.Contains("data:image/png;base64", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("untitled.PPTX", sent, StringComparison.Ordinal);
        Assert.DoesNotContain(root, sent, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(presentation));
    }

    [Fact]
    public async Task Picture_search_refuses_after_AI_choice_changes_and_never_sends()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var root = app.MakeFolder("Pictures");
        File.WriteAllBytes(Path.Combine(root, "flower.png"), TinyPng);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(root);
        search.Phrase = "flower";
        var batch = await search.PreparePictureSearchAsync(visualReadApproved: true);
        Assert.NotNull(batch);

        await app.Get<DeskAI.Core.Abstractions.IAiSettingsRepository>()
            .SaveAsync(AiSettings.Default, TestContext.Current.CancellationToken);
        await search.SearchPicturesAsync(batch, cloudSendApproved: true);

        Assert.Empty(app.Internet.Requests);
        Assert.Contains("changed", search.VisualMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generated_PDF_picture_is_offered_with_its_page_without_PDF_text_grant()
    {
        await using var app = await TestApp.StartAsync();
        await TidyAiTests.TurnOnOpenRouterAsync(app);
        var root = app.MakeFolder("Pictures");
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4).AddPng(TinyPng, new PdfRectangle(25, 25, 100, 100));
        File.WriteAllBytes(Path.Combine(nested, "untitled.pdf"), builder.Build());

        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(root);
        search.Phrase = "PDF with a flower";
        var batch = await search.PreparePictureSearchAsync(visualReadApproved: true);

        Assert.NotNull(batch);
        var image = Assert.Single(batch.Images);
        Assert.Equal("untitled.pdf", image.FileName);
        Assert.Contains("Page 1", image.Location, StringComparison.Ordinal);
        Assert.Empty(app.Internet.Requests);
    }

    [Fact]
    public async Task Local_AI_reads_only_after_a_separate_picture_choice_and_uses_loopback()
    {
        await using var app = await TestApp.StartAsync();
        await app.Get<IAiSettingsRepository>().SaveAsync(AiSettings.Default with
        {
            Mode = AiMode.Local,
            Endpoint = "http://127.0.0.1:11434/v1/chat/completions",
            ModelId = "generated-vision-model",
        }, TestContext.Current.CancellationToken);
        var root = app.MakeFolder("Pictures");
        File.WriteAllBytes(Path.Combine(root, "untitled.png"), TinyPng);
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();
        await search.ConnectFolderAsync(root);
        search.Phrase = "a red flower";

        Assert.Null(await search.PreparePictureSearchAsync(visualReadApproved: false));
        Assert.Contains("not approved", search.VisualMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(app.Internet.Requests);

        var batch = await search.PreparePictureSearchAsync(visualReadApproved: true);
        Assert.NotNull(batch);
        app.Internet.Reply = _ => new AiHttpResponse(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"{\"match\":false,\"reason\":\"Only a plain red pixel.\"}"}}]}""");
        await search.SearchPicturesAsync(batch, cloudSendApproved: false);

        Assert.Empty(search.VisualResults);
        var request = Assert.Single(app.Internet.Requests);
        Assert.True(request.Endpoint.IsLoopback);
        Assert.Empty(request.Headers);
    }
}

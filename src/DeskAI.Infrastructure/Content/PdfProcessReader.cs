using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DeskAI.Infrastructure.Content;

/// <summary>Runs the PDF parser outside the UI process using bounded in-memory bytes.</summary>
public sealed class PdfProcessReader
{
    public const int MaxPdfBytes = 8 * 1024 * 1024;
    private const int MaxResponseBytes = 64 * 1024 + 1;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly string _workerPath = Path.Combine(
        AppContext.BaseDirectory, "PdfWorker", "DeskAI.PdfWorker.exe");

    public sealed record PdfImage(int Page, string MediaType, byte[] Bytes);

    public async Task<IReadOnlyList<PdfImage>> ReadImagesAsync(
        Stream file, int maximumImages, CancellationToken cancellationToken)
    {
        if (file.Length is < 8 or > MaxPdfBytes || maximumImages <= 0 || !File.Exists(_workerPath))
        {
            return [];
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(_workerPath, "images")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(_workerPath)!,
            },
        };
        var started = false;
        try
        {
            process.Start();
            started = true;
            var chunk = new byte[8192];
            var transferred = 0;
            int count;
            while ((count = await file.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0)
            {
                transferred += count;
                if (transferred > MaxPdfBytes)
                {
                    return [];
                }

                await process.StandardInput.BaseStream.WriteAsync(
                    chunk.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
            }

            process.StandardInput.Close();
            const int maxReply = 6 * 1024 * 1024;
            var output = new byte[maxReply + 1];
            var read = 0;
            while (read < output.Length)
            {
                var received = await process.StandardOutput.BaseStream.ReadAsync(
                    output.AsMemory(read), deadline.Token).ConfigureAwait(false);
                if (received == 0)
                {
                    break;
                }

                read += received;
            }

            if (read > maxReply)
            {
                return [];
            }

            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return [];
            }

            using var json = JsonDocument.Parse(output.AsMemory(0, read));
            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<PdfImage>();
            foreach (var item in json.RootElement.EnumerateArray().Take(Math.Min(maximumImages, 12)))
            {
                var page = item.GetProperty("page").GetInt32();
                var media = item.GetProperty("mediaType").GetString();
                var encoded = item.GetProperty("data").GetString();
                if (page is < 1 or > 20 || media is not ("image/jpeg" or "image/png")
                    || encoded is null || encoded.Length > 1_400_000)
                {
                    return [];
                }

                var bytes = Convert.FromBase64String(encoded);
                if (bytes.Length > 1024 * 1024)
                {
                    return [];
                }

                result.Add(new PdfImage(page, media, bytes));
            }

            return result.AsReadOnly();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (Exception exception) when (exception is IOException or Win32Exception
            or InvalidOperationException or JsonException or FormatException
            or KeyNotFoundException)
        {
            return [];
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
            }
        }
    }

    public async Task<(string Text, bool Truncated)?> ReadAsync(
        Stream file, CancellationToken cancellationToken)
    {
        if (file.Length is < 8 or > MaxPdfBytes)
        {
            return null;
        }

        if (!File.Exists(_workerPath))
        {
            return null;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(_workerPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                WorkingDirectory = Path.GetDirectoryName(_workerPath)!,
            },
        };

        var started = false;
        try
        {
            process.Start();
            started = true;
            var chunk = new byte[8192];
            var transferred = 0;
            int count;
            while ((count = await file.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0)
            {
                transferred += count;
                if (transferred > MaxPdfBytes)
                {
                    return null;
                }

                await process.StandardInput.BaseStream.WriteAsync(
                    chunk.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
            }

            process.StandardInput.Close();

            // The child is expected to produce at most one flag byte and 64 KB of text.
            // Bound the pipe anyway so a broken child cannot fill the parent's memory.
            var output = new byte[MaxResponseBytes + 1];
            var read = 0;
            while (read < output.Length)
            {
                var received = await process.StandardOutput.BaseStream.ReadAsync(
                    output.AsMemory(read), deadline.Token).ConfigureAwait(false);
                if (received == 0)
                {
                    break;
                }

                read += received;
            }

            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0 || read is < 1 or > MaxResponseBytes || output[0] is not (0 or 1))
            {
                return null;
            }

            return (Encoding.UTF8.GetString(output, 1, read - 1), output[0] == 1);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
            }
        }
    }
}

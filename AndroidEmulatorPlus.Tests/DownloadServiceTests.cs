using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AndroidEmulatorPlus.Services;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class DownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_ResumesInterruptedPartialWithRangeRequest()
    {
        var payload = Encoding.ASCII.GetBytes("0123456789abcdef");
        await using var server = new RangeResumeServer(payload, firstResponseBytes: 5);

        var log = new LogService();
        using var downloads = new DownloadService(log, new SettingsService(log));
        var dest = Path.Combine(Path.GetTempPath(), $"aep-download-{Guid.NewGuid():N}.bin");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => downloads.DownloadAsync(server.Url, dest));

            Assert.False(File.Exists(dest));
            Assert.True(File.Exists(dest + ".download"));
            Assert.Equal(5, new FileInfo(dest + ".download").Length);

            await downloads.DownloadAsync(server.Url, dest);

            Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
            Assert.Equal("bytes=5-", await server.SecondRangeHeader);
        }
        finally
        {
            try { File.Delete(dest); } catch { }
            try { File.Delete(dest + ".download"); } catch { }
        }
    }

    private sealed class RangeResumeServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _payload;
        private readonly int _firstResponseBytes;
        private readonly Task _serverTask;
        private readonly TaskCompletionSource<string?> _secondRangeHeader =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RangeResumeServer(byte[] payload, int firstResponseBytes)
        {
            _payload = payload;
            _firstResponseBytes = firstResponseBytes;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Url = $"http://127.0.0.1:{endpoint.Port}/payload.bin";
            _serverTask = Task.Run(RunAsync);
        }

        public string Url { get; }
        public Task<string?> SecondRangeHeader => _secondRangeHeader.Task;

        private async Task RunAsync()
        {
            using (var first = await _listener.AcceptTcpClientAsync())
            {
                var stream = first.GetStream();
                await ReadHeadersAsync(stream);
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Length: {_payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(_payload.AsMemory(0, _firstResponseBytes));
                await stream.FlushAsync();
            }

            using (var second = await _listener.AcceptTcpClientAsync())
            {
                var stream = second.GetStream();
                var request = await ReadHeadersAsync(stream);
                var range = FindHeader(request, "Range");
                _secondRangeHeader.SetResult(range);
                var start = ParseRangeStart(range);
                var remaining = _payload.Length - start;
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 206 Partial Content\r\n" +
                    $"Content-Length: {remaining}\r\n" +
                    $"Content-Range: bytes {start}-{_payload.Length - 1}/{_payload.Length}\r\n" +
                    "Accept-Ranges: bytes\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(_payload.AsMemory(start, remaining));
                await stream.FlushAsync();
            }
        }

        private static async Task<string> ReadHeadersAsync(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var used = 0;
            while (used < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(used, buffer.Length - used));
                if (n == 0) break;
                used += n;
                var text = Encoding.ASCII.GetString(buffer, 0, used);
                if (text.Contains("\r\n\r\n", StringComparison.Ordinal)) return text;
            }
            return Encoding.ASCII.GetString(buffer, 0, used);
        }

        private static string? FindHeader(string request, string name)
            => request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .FirstOrDefault(parts => parts[0].Equals(name, StringComparison.OrdinalIgnoreCase))?[1]
                .Trim();

        private static int ParseRangeStart(string? range)
        {
            Assert.NotNull(range);
            Assert.StartsWith("bytes=", range, StringComparison.OrdinalIgnoreCase);
            var end = range.IndexOf('-', "bytes=".Length);
            Assert.True(end > "bytes=".Length);
            return int.Parse(range["bytes=".Length..end], System.Globalization.CultureInfo.InvariantCulture);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try { await _serverTask; }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
    }
}

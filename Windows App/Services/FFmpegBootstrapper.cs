using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Voxa.Services
{
    public enum SetupStage
    {
        Checking,
        Downloading,
        Extracting,
        Installing,
        Ready,
        Failed
    }

    /// <summary>Snapshot of setup progress, safe to report from a background thread.</summary>
    public class SetupProgress
    {
        public SetupStage Stage { get; init; }
        public double Percent { get; init; }
        public string Message { get; init; } = string.Empty;
        public bool IsIndeterminate { get; init; }
    }

    public class FFmpegSetupException : Exception
    {
        public FFmpegSetupException(string message, Exception? inner) : base(message, inner) { }
    }

    /// <summary>
    /// Makes sure a working ffmpeg.exe is available before the main window opens - without
    /// asking a non-technical user to find, download, or install anything themselves.
    ///
    /// On first launch (and only if FFmpeg isn't already bundled, cached, or on PATH) this
    /// downloads a static Windows build to a per-user cache folder that never needs admin
    /// rights to write to. Every launch after that finds it there instantly and does not
    /// touch the network at all.
    /// </summary>
    public class FFmpegBootstrapper
    {
        /// <summary>Per-user cache folder a downloaded copy is installed into.</summary>
        public static string CachedFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Voxa", "ffmpeg");

        public static string CachedFFmpegPath => Path.Combine(CachedFolder, "ffmpeg.exe");

        // Evergreen links - both always resolve to a current Windows build, so there's no
        // version number to maintain here. Gyan.dev (small "essentials" build) is tried
        // first; the BtbN GitHub release is used as a fallback mirror if that fails.
        private static readonly string[] DownloadSources =
        {
            "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
            "https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip"
        };

        /// <summary>True if FFmpeg is already usable without downloading anything.</summary>
        public static bool IsAlreadyAvailable()
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(bundled)) return true;
            if (File.Exists(CachedFFmpegPath)) return true;
            return IsOnSystemPath();
        }

        private static bool IsOnSystemPath()
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            return pathVar.Split(Path.PathSeparator).Any(dir =>
            {
                try { return !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "ffmpeg.exe")); }
                catch { return false; }
            });
        }

        /// <summary>
        /// Ensures FFmpeg is ready to use. Instant no-op if a copy already exists anywhere;
        /// otherwise downloads and caches one, reporting progress as it goes.
        /// </summary>
        public async Task EnsureReadyAsync(IProgress<SetupProgress> progress, CancellationToken ct)
        {
            progress.Report(new SetupProgress
            {
                Stage = SetupStage.Checking,
                Message = LocalizationService.Instance["Runtime.Checking"],
                IsIndeterminate = true
            });

            if (IsAlreadyAvailable())
            {
                progress.Report(new SetupProgress { Stage = SetupStage.Ready, Percent = 100, Message = LocalizationService.Instance["Runtime.Ready"] });
                return;
            }

            Exception? lastError = null;
            foreach (var url in DownloadSources)
            {
                try
                {
                    await DownloadAndInstallAsync(url, progress, ct).ConfigureAwait(false);
                    progress.Report(new SetupProgress { Stage = SetupStage.Ready, Percent = 100, Message = LocalizationService.Instance["Runtime.Ready"] });
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex; // fall through and try the next mirror
                }
            }

            progress.Report(new SetupProgress { Stage = SetupStage.Failed, Message = LocalizationService.Instance["Runtime.SetupFailed"] });
            var reason = lastError != null ? $" ({lastError.Message})" : string.Empty;
            throw new FFmpegSetupException(
                "Couldn't download the audio engine this app needs (FFmpeg)." + reason +
                " Check your internet connection and try again - or see README.md for a manual, offline setup option.",
                lastError);
        }

        private async Task DownloadAndInstallAsync(string url, IProgress<SetupProgress> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(CachedFolder);
            var tempZip = Path.Combine(Path.GetTempPath(), $"ffmpeg_download_{Guid.NewGuid():N}.zip");
            var tempExtract = Path.Combine(Path.GetTempPath(), $"ffmpeg_extract_{Guid.NewGuid():N}");

            try
            {
                await DownloadToFileAsync(url, tempZip, progress, ct).ConfigureAwait(false);

                progress.Report(new SetupProgress { Stage = SetupStage.Extracting, Percent = 88, Message = LocalizationService.Instance["Runtime.Unpacking"] });
                Directory.CreateDirectory(tempExtract);
                ZipFile.ExtractToDirectory(tempZip, tempExtract, overwriteFiles: true);

                var extractedExe = Directory
                    .EnumerateFiles(tempExtract, "ffmpeg.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (extractedExe == null)
                    throw new InvalidOperationException("The downloaded package didn't contain ffmpeg.exe.");

                progress.Report(new SetupProgress { Stage = SetupStage.Installing, Percent = 96, Message = LocalizationService.Instance["Runtime.AlmostDone"] });
                File.Copy(extractedExe, CachedFFmpegPath, overwrite: true);

                var licenseSource =
                    Directory.EnumerateFiles(tempExtract, "LICENSE*", SearchOption.AllDirectories).FirstOrDefault() ??
                    Directory.EnumerateFiles(tempExtract, "COPYING*", SearchOption.AllDirectories).FirstOrDefault();
                if (licenseSource != null)
                {
                    try { File.Copy(licenseSource, Path.Combine(CachedFolder, "LICENSE.txt"), overwrite: true); }
                    catch { /* non-essential */ }
                }
            }
            finally
            {
                TryDelete(tempZip);
                TryDeleteDirectory(tempExtract);
            }
        }

        // How long to wait for the connection to respond with headers at all before
        // giving up on this mirror and trying the next one.
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

        // How long we'll tolerate zero new bytes arriving before deciding the connection
        // has stalled (blocked by a firewall/proxy, dead route, etc.) and bailing out.
        // This resets on every successful chunk, so a slow-but-alive connection is fine -
        // only a truly stuck one trips it. This is what actually fixes the "stuck for 15+
        // minutes with no feedback" problem: previously the only limit was a flat 10-minute
        // HttpClient.Timeout, so a connection that stalled at byte zero just hung silently
        // until that clock ran out - now it fails fast and clearly instead.
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(25);

        private static async Task DownloadToFileAsync(
            string url, string destinationPath, IProgress<SetupProgress> progress, CancellationToken ct)
        {
            using var http = new HttpClient(); // no blanket Timeout - see ConnectTimeout/StallTimeout below
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Voxa/1.0");

            HttpResponseMessage response;
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(ConnectTimeout);
                try
                {
                    response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, connectCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Couldn't reach {new Uri(url).Host} within {ConnectTimeout.TotalSeconds:0} seconds.");
                }
            }

            using (response)
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                await using var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                // Explicit block scope so the file is flushed and closed before this method
                // returns - ZipFile.ExtractToDirectory needs to open it right after.
                await using (var fileStream = new FileStream(
                    destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    long readTotal = 0;

                    while (true)
                    {
                        int bytesRead;
                        using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            readCts.CancelAfter(StallTimeout);
                            try
                            {
                                bytesRead = await httpStream.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                throw new TimeoutException(
                                    $"The download from {new Uri(url).Host} stalled for over " +
                                    $"{StallTimeout.TotalSeconds:0} seconds - it may be blocked by a firewall or proxy.");
                            }
                        }

                        if (bytesRead == 0) break; // end of stream

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                        readTotal += bytesRead;

                        if (totalBytes > 0)
                        {
                            // Reserve the last ~15% of the bar for unpack/install so it doesn't
                            // look "stuck" at 100% while the zip is being expanded.
                            var pct = readTotal / (double)totalBytes * 85.0;
                            progress.Report(new SetupProgress
                            {
                                Stage = SetupStage.Downloading,
                                Percent = pct,
                                Message = $"{LocalizationService.Instance.Format("Runtime.Downloading", readTotal / 1024.0 / 1024.0)} of {totalBytes / 1024.0 / 1024.0:0.#} MB"
                            });
                        }
                        else
                        {
                            progress.Report(new SetupProgress
                            {
                                Stage = SetupStage.Downloading,
                                IsIndeterminate = true,
                                Message = LocalizationService.Instance.Format("Runtime.Downloading", readTotal / 1024.0 / 1024.0)
                            });
                        }
                    }
                }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup only */ }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best-effort cleanup only */ }
        }
    }
}

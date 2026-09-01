using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Voxa.Models;

namespace Voxa.Services
{
    public class FFmpegResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? OutputPath { get; set; }
    }

    /// <summary>
    /// Result of a quick loudness scan (FFmpeg's volumedetect filter) used to flag files
    /// that may need extra attention before processing - clipped peaks or very low volume.
    /// </summary>
    public class QualityReport
    {
        public bool Success { get; set; }
        public double? MeanVolumeDb { get; set; }
        public double? MaxVolumeDb { get; set; }

        /// <summary>True if peaks are at or effectively at 0 dBFS, i.e. very likely clipped.</summary>
        public bool IsLikelyClipped => MaxVolumeDb is { } max && max > -0.3;

        /// <summary>True if the average level is unusually quiet and may need a volume boost.</summary>
        public bool IsVeryQuiet => MeanVolumeDb is { } mean && mean < -35;

        public bool HasWarning => IsLikelyClipped || IsVeryQuiet;

        public string WarningMessage
        {
            get
            {
                if (IsLikelyClipped && IsVeryQuiet) return "Clipped peaks and very quiet overall";
                if (IsLikelyClipped) return "Peaks may be clipped (distorted at loudest points)";
                if (IsVeryQuiet) return "Very quiet - may need a volume boost";
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Thin, robust wrapper around the bundled FFmpeg executable. Responsible for:
    ///   - locating ffmpeg.exe next to the running app (or falling back to PATH)
    ///   - turning a ProcessingParameters object into an FFmpeg filter/codec command line
    ///   - running FFmpeg asynchronously without blocking the UI thread
    ///   - parsing FFmpeg's own "-progress" output into a 0-100 percentage per file
    ///   - translating non-zero exit codes / stderr into a human-readable error
    /// </summary>
    public class FFmpegService
    {
        private readonly string _ffmpegPath;

        private static readonly Regex DurationRegex =
            new(@"Duration:\s*(\d+):(\d+):(\d+\.\d+)", RegexOptions.Compiled);

        private static readonly Regex MeanVolumeRegex =
            new(@"mean_volume:\s*(-?\d+(?:\.\d+)?)\s*dB", RegexOptions.Compiled);

        private static readonly Regex MaxVolumeRegex =
            new(@"max_volume:\s*(-?\d+(?:\.\d+)?)\s*dB", RegexOptions.Compiled);

        private static readonly Regex OutTimeMsRegex =
            new(@"out_time_ms=(-?\d+)", RegexOptions.Compiled);

        public FFmpegService(string? ffmpegPathOverride = null)
        {
            _ffmpegPath = ffmpegPathOverride ?? ResolveFFmpegPath();
        }

        /// <summary>True if a usable ffmpeg.exe was found, either bundled or on PATH.</summary>
        public bool IsAvailable => File.Exists(_ffmpegPath) || IsOnPath(_ffmpegPath);

        public string FFmpegPath => _ffmpegPath;

        private static string ResolveFFmpegPath()
        {
            // Preferred: shipped alongside the app under an "ffmpeg" subfolder
            // (this is where the .csproj copies ffmpeg\ffmpeg.exe to on build/publish,
            // and where PublishSingleFile content extraction lands it too).
            var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(bundled))
                return bundled;

            // Also accept it sitting directly next to the .exe.
            var sideBySide = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(sideBySide))
                return sideBySide;

            // Self-downloaded copy, cached per-user by FFmpegBootstrapper on first run.
            // This is what makes the app work with zero manual setup: SetupWindow runs
            // the bootstrapper before MainWindow ever opens, so by the time this class is
            // used this path is normally already populated.
            if (File.Exists(FFmpegBootstrapper.CachedFFmpegPath))
                return FFmpegBootstrapper.CachedFFmpegPath;

            // Last resort: whatever "ffmpeg.exe" resolves to on the system PATH.
            return FindOnPath("ffmpeg.exe") ?? "ffmpeg.exe";
        }

        private static bool IsOnPath(string exeName)
            => FindOnPath(exeName) != null;

        private static string? FindOnPath(string exeName)
        {
            if (Path.IsPathRooted(exeName)) return File.Exists(exeName) ? exeName : null;

            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    var candidate = Path.Combine(dir, exeName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // Ignore malformed PATH entries and keep looking.
                }
            }

            return null;
        }

        /// <summary>Probes a file's duration by asking FFmpeg to open it with no output.</summary>
        public async Task<TimeSpan?> GetDurationAsync(string filePath, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(filePath);

            var stderrBuilder = new StringBuilder();

            using var process = new Process { StartInfo = psi };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not launch FFmpeg at '{_ffmpegPath}'. ({ex.Message})", ex);
            }

            // ffmpeg -i with no output always "fails" - that's expected. We only care
            // about the Duration: line it prints to stderr before failing.
            var match = DurationRegex.Match(stderrBuilder.ToString());
            if (!match.Success) return null;

            var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            return new TimeSpan(0, hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// Quick pre-flight scan using FFmpeg's volumedetect filter (decodes the whole
        /// file but writes no output) to flag files that may be clipped or unusually
        /// quiet before the batch actually runs. Non-fatal by design: a failed scan just
        /// means no warning is shown for that file, not that processing should stop.
        /// </summary>
        public async Task<QualityReport> AnalyzeQualityAsync(string filePath, CancellationToken ct)
        {
            if (!File.Exists(filePath) || !IsAvailable)
                return new QualityReport { Success = false };

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-nostats");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(filePath);
            psi.ArgumentList.Add("-af");
            psi.ArgumentList.Add("volumedetect");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("null");
            psi.ArgumentList.Add("-");

            var stderrBuilder = new StringBuilder();

            using var process = new Process { StartInfo = psi };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
            catch
            {
                return new QualityReport { Success = false };
            }

            var text = stderrBuilder.ToString();
            var meanMatch = MeanVolumeRegex.Match(text);
            var maxMatch = MaxVolumeRegex.Match(text);

            if (!meanMatch.Success && !maxMatch.Success)
                return new QualityReport { Success = false };

            return new QualityReport
            {
                Success = true,
                MeanVolumeDb = meanMatch.Success
                    ? double.Parse(meanMatch.Groups[1].Value, CultureInfo.InvariantCulture)
                    : null,
                MaxVolumeDb = maxMatch.Success
                    ? double.Parse(maxMatch.Groups[1].Value, CultureInfo.InvariantCulture)
                    : null
            };
        }

        /// <summary>
        /// Decodes a file to raw mono 16-bit PCM (piped, nothing written to disk) and
        /// reduces it to a fixed number of min/max sample-peak pairs for drawing a
        /// waveform preview. A low decode sample rate (8kHz) keeps this fast even for
        /// long recordings, since only the amplitude envelope matters for the preview,
        /// not the actual audio quality.
        /// </summary>
        /// <param name="bucketCount">Number of left-to-right waveform bars to produce.</param>
        public async Task<float[]> GetWaveformPeaksAsync(
            string filePath,
            int bucketCount,
            CancellationToken ct,
            TimeSpan? knownDuration = null)
        {
            if (bucketCount <= 0) return Array.Empty<float>();
            if (!File.Exists(filePath) || !IsAvailable) return Array.Empty<float>();

            const int decodeSampleRate = 8000;
            var duration = knownDuration ?? await GetDurationAsync(filePath, ct).ConfigureAwait(false);
            if (duration is not { TotalSeconds: > 0 }) return Array.Empty<float>();

            var expectedSamples = Math.Max(1, (long)Math.Ceiling(duration.Value.TotalSeconds * decodeSampleRate));
            var samplesPerBucket = Math.Max(1, (long)Math.Ceiling(expectedSamples / (double)bucketCount));

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(filePath);
            psi.ArgumentList.Add("-ac");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("-ar");
            psi.ArgumentList.Add(decodeSampleRate.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("s16le");
            psi.ArgumentList.Add("-");

            using var process = new Process { StartInfo = psi };

            try
            {
                process.Start();
            }
            catch
            {
                return Array.Empty<float>();
            }

            // Drain stderr concurrently so it can never fill its pipe buffer and stall
            // the process while we're reading stdout below.
            var stderrDrainTask = process.StandardError.BaseStream.CopyToAsync(Stream.Null, ct);

            var peaks = new float[bucketCount];
            var readBuffer = new byte[8192];
            long sampleIndex = 0;
            byte? pendingByte = null;

            try
            {
                while (true)
                {
                    var bytesRead = await process.StandardOutput.BaseStream
                        .ReadAsync(readBuffer.AsMemory(0, readBuffer.Length), ct)
                        .ConfigureAwait(false);
                    if (bytesRead == 0) break;

                    var offset = 0;
                    if (pendingByte is { } firstByte)
                    {
                        var sample = (short)(firstByte | (readBuffer[0] << 8));
                        AddWaveformSample(sample, peaks, sampleIndex++, samplesPerBucket);
                        pendingByte = null;
                        offset = 1;
                    }

                    for (; offset + 1 < bytesRead; offset += 2)
                    {
                        var sample = (short)(readBuffer[offset] | (readBuffer[offset + 1] << 8));
                        AddWaveformSample(sample, peaks, sampleIndex++, samplesPerBucket);
                    }

                    if (offset < bytesRead)
                        pendingByte = readBuffer[offset];
                }
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            try { await process.WaitForExitAsync(ct).ConfigureAwait(false); } catch { /* best-effort */ }
            try { await stderrDrainTask.ConfigureAwait(false); } catch { /* best-effort */ }

            return peaks;
        }

        private static void AddWaveformSample(short sample, float[] peaks, long sampleIndex, long samplesPerBucket)
        {
            var bucket = (int)Math.Min(peaks.Length - 1, sampleIndex / samplesPerBucket);
            var abs = sample == short.MinValue ? short.MaxValue : Math.Abs(sample);
            var normalized = abs / (float)short.MaxValue;
            if (normalized > peaks[bucket]) peaks[bucket] = normalized;
        }

        public async Task<FFmpegResult> ProcessFileAsync(
            string inputPath,
            string outputPath,
            ProcessingParameters parameters,
            IProgress<double>? progress,
            CancellationToken ct)
        {
            if (!File.Exists(inputPath))
                return new FFmpegResult { Success = false, ErrorMessage = "Source file could not be found." };

            if (!IsAvailable)
                return new FFmpegResult
                {
                    Success = false,
                    ErrorMessage = "FFmpeg was not found. Reinstall the app or check that the 'ffmpeg' folder is present next to the executable."
                };

            TimeSpan? duration;
            try
            {
                duration = await GetDurationAsync(inputPath, ct).ConfigureAwait(false);

                // Padding and speed changes shift how long the output actually runs
                // compared to the source - account for both so the per-file progress bar
                // reaches ~100% right as FFmpeg finishes, instead of stalling short of it.
                if (duration is { } d)
                {
                    var speed = parameters.SpeedMultiplier > 0.001 ? parameters.SpeedMultiplier : 1.0;
                    var scaled = TimeSpan.FromSeconds(d.TotalSeconds / speed);
                    var padding = TimeSpan.FromSeconds(
                        Math.Max(0, parameters.SilencePaddingStartSec) + Math.Max(0, parameters.SilencePaddingEndSec));
                    duration = scaled + padding;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Non-fatal: we simply won't be able to report a percentage for this file.
                duration = null;
            }

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in BuildArgumentList(inputPath, outputPath, parameters))
                psi.ArgumentList.Add(arg);

            var stderrBuilder = new StringBuilder();

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                var match = OutTimeMsRegex.Match(e.Data);
                if (match.Success && duration is { } total && total.TotalMilliseconds > 0)
                {
                    var currentMs = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (currentMs < 0) currentMs = 0;
                    var pct = currentMs / 1000.0 / total.TotalMilliseconds * 100.0;
                    progress?.Report(Math.Clamp(pct, 0, 100));
                }
            };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return new FFmpegResult { Success = false, ErrorMessage = "Cancelled." };
            }
            catch (Exception ex)
            {
                return new FFmpegResult { Success = false, ErrorMessage = $"Could not run FFmpeg: {ex.Message}" };
            }

            if (process.ExitCode != 0)
            {
                var errorText = ExtractMeaningfulError(stderrBuilder.ToString());
                return new FFmpegResult
                {
                    Success = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(errorText)
                        ? $"FFmpeg exited with error code {process.ExitCode}."
                        : errorText
                };
            }

            if (!File.Exists(outputPath))
            {
                return new FFmpegResult
                {
                    Success = false,
                    ErrorMessage = "FFmpeg reported success but no output file was created."
                };
            }

            progress?.Report(100);
            return new FFmpegResult { Success = true, OutputPath = outputPath };
        }

        private static void TryKill(Process process)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* best-effort cleanup only */ }
        }

        private static string ExtractMeaningfulError(string stderr)
        {
            var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            var candidate = lines.LastOrDefault(l =>
                l.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Invalid", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Unable", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Unsupported", StringComparison.OrdinalIgnoreCase));

            return candidate ?? (lines.Count > 0 ? lines[^1] : string.Empty);
        }

        // ---- Command-line construction -------------------------------------------------

        public static List<string> BuildArgumentList(string inputPath, string outputPath, ProcessingParameters p)
        {
            var args = new List<string>
            {
                "-y",                 // overwrite output if it already exists
                "-hide_banner",
                "-loglevel", "error", // keep stderr focused on real problems
                "-progress", "pipe:1",// machine-readable progress on stdout
                "-nostats",
                "-i", inputPath,
                "-vn"                 // audio only - drop embedded cover art / video streams
            };

            var filters = BuildFilterChain(p);
            if (filters.Count > 0)
            {
                args.Add("-af");
                args.Add(string.Join(",", filters));
            }

            if (!p.KeepOriginalSampleRate && p.SampleRateHz > 0)
            {
                args.Add("-ar");
                args.Add(p.SampleRateHz.ToString(CultureInfo.InvariantCulture));
            }

            args.AddRange(CodecArgumentsFor(p.OutputFormat, p.BitrateKbps));

            args.Add(outputPath);
            return args;
        }

        private static List<string> BuildFilterChain(ProcessingParameters p)
        {
            var filters = new List<string>();

            // 1. Clarity enhancement first: cut low rumble, lightly denoise, add a
            //    gentle presence boost around the frequency range that carries speech.
            if (p.EnhanceClarity)
            {
                filters.Add("highpass=f=80");
                filters.Add("afftdn=nf=-25");
                filters.Add("equalizer=f=3500:t=q:w=1:g=3");
            }

            // 2. Manual gain.
            if (Math.Abs(p.VolumeChangeDb) > 0.001)
            {
                var db = p.VolumeChangeDb.ToString("0.##;-0.##;0", CultureInfo.InvariantCulture);
                filters.Add($"volume={db}dB");
            }

            // 3. Loudness normalization runs after manual gain so the target loudness wins.
            if (p.NormalizeVolume)
            {
                filters.Add("loudnorm=I=-16:LRA=11:TP=-1.5");
            }

            // 4. Speed change next. atempo only accepts 0.5-2.0 per stage, so extreme
            //    speeds are built by chaining multiple atempo stages together.
            if (Math.Abs(p.SpeedMultiplier - 1.0) > 0.001)
            {
                filters.AddRange(BuildAtempoChain(p.SpeedMultiplier));
            }

            // 5. Silence padding last, so it always adds clean silence at the true start/end
            //    of the file regardless of what other filters did to duration or timing.
            //    adelay shifts the whole signal later (all=1 applies it to every channel,
            //    including mono/multichannel sources); apad extends the tail with silence.
            if (p.SilencePaddingStartSec > 0.001)
            {
                var ms = (long)Math.Round(p.SilencePaddingStartSec * 1000);
                filters.Add($"adelay={ms}:all=1");
            }
            if (p.SilencePaddingEndSec > 0.001)
            {
                var sec = p.SilencePaddingEndSec.ToString("0.###", CultureInfo.InvariantCulture);
                filters.Add($"apad=pad_dur={sec}");
            }

            return filters;
        }

        private static IEnumerable<string> BuildAtempoChain(double targetSpeed)
        {
            var remaining = Math.Clamp(targetSpeed, 0.25, 4.0);
            var stages = new List<string>();

            while (remaining > 2.0)
            {
                stages.Add("atempo=2.0");
                remaining /= 2.0;
            }
            while (remaining < 0.5)
            {
                stages.Add("atempo=0.5");
                remaining /= 0.5;
            }
            stages.Add($"atempo={remaining.ToString("0.####", CultureInfo.InvariantCulture)}");
            return stages;
        }

        private static IEnumerable<string> CodecArgumentsFor(string format, int bitrateKbps)
        {
            var bitrate = Math.Clamp(bitrateKbps, 32, 320);
            return format.ToLowerInvariant() switch
            {
                "mp3" => new[] { "-c:a", "libmp3lame", "-b:a", $"{bitrate}k" },
                "m4a" or "aac" => new[] { "-c:a", "aac", "-b:a", $"{bitrate}k" },
                "wav" => new[] { "-c:a", "pcm_s16le" },
                "flac" => new[] { "-c:a", "flac" },
                "ogg" => new[] { "-c:a", "libvorbis", "-b:a", $"{bitrate}k" },
                _ => new[] { "-b:a", $"{bitrate}k" }
            };
        }
    }
}

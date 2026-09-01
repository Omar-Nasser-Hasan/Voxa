using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Voxa.Controls;
using Voxa.Models;
using Voxa.Services;

var root = FindProjectRoot();
var tempRoot = Path.Combine(Path.GetTempPath(), "VoxaLocalTests", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
var inputDir = Path.Combine(tempRoot, "input");
var outputDir = Path.Combine(tempRoot, "output");
Directory.CreateDirectory(inputDir);
Directory.CreateDirectory(outputDir);

var results = new List<TestResult>();

async Task RunAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        results.Add(new TestResult(name, true, null));
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        results.Add(new TestResult(name, false, ex.Message));
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

void Run(string name, Action test)
    => RunAsync(name, () =>
    {
        test();
        return Task.CompletedTask;
    }).GetAwaiter().GetResult();

var service = new FFmpegService();
var tonePath = Path.Combine(inputDir, "tone.wav");
var badAudioPath = Path.Combine(inputDir, "not_really_audio.wav");

await RunAsync("FFmpeg is available to Voxa", async () =>
{
    Assert(service.IsAvailable, $"FFmpegService could not resolve FFmpeg. Path: {service.FFmpegPath}");
    await RunProcessAsync(service.FFmpegPath, "-version", root);
});

await RunAsync("Generate WAV fixture", async () =>
{
    await RunProcessAsync(
        service.FFmpegPath,
        $"-y -hide_banner -loglevel error -f lavfi -i sine=frequency=440:duration=1.25 -ac 1 -ar 44100 \"{tonePath}\"",
        root);
    Assert(File.Exists(tonePath), "Fixture WAV was not created.");
    Assert(new FileInfo(tonePath).Length > 1000, "Fixture WAV is unexpectedly small.");
    await File.WriteAllTextAsync(badAudioPath, "this is not audio");
});

await RunAsync("Duration probe works", async () =>
{
    var duration = await service.GetDurationAsync(tonePath, CancellationToken.None);
    Assert(duration is { TotalSeconds: > 1.0 and < 1.6 }, $"Unexpected duration: {duration}");
});

await RunAsync("Waveform peaks load", async () =>
{
    var duration = await service.GetDurationAsync(tonePath, CancellationToken.None);
    var peaks = await service.GetWaveformPeaksAsync(tonePath, 120, CancellationToken.None, duration);
    Assert(peaks.Length == 120, $"Expected 120 peaks, got {peaks.Length}.");
    Assert(peaks.Any(p => p > 0.01f), "Waveform was all silence.");
    Assert(peaks.All(p => p is >= 0 and <= 1), "Waveform peak out of 0..1 range.");
});

Run("Output naming patterns work", () =>
{
    Assert(OutputNamer.BuildFileName("{name}_clean", tonePath, 7) == "tone_clean", "Name token failed.");
    Assert(OutputNamer.BuildFileName("voice_{n}", tonePath, 7) == "voice_7", "{n} token failed.");
    Assert(OutputNamer.BuildFileName("voice_{n2}", tonePath, 7) == "voice_07", "{n2} token failed.");
    Assert(OutputNamer.BuildFileName("voice_{n3}", tonePath, 7) == "voice_007", "{n3} token failed.");
    Assert(OutputNamer.BuildFileName("voice_{n4}", tonePath, 7) == "voice_0007", "{n4} token failed.");
    Assert(OutputNamer.BuildFileName("bad:*?name", tonePath, 7) == "badname", "Invalid filename characters were not removed.");
    Assert(new ProcessingParameters().UseCustomFileNames, "Custom output filenames should default on.");
});

Run("Parameter validation catches bad values", () =>
{
    var invalid = new ProcessingParameters
    {
        OutputFormat = "xyz",
        KeepOriginalSampleRate = false,
        SampleRateHz = 100,
        VolumeChangeDb = 99,
        SpeedMultiplier = 99,
        BitrateKbps = 999,
        SilencePaddingStartSec = 99,
        SilencePaddingEndSec = 99
    };
    var errors = ParameterValidator.Validate(invalid);
    Assert(errors.Count >= 6, $"Expected several validation errors, got {errors.Count}.");
});

Run("Silence trimming and metadata are passed to FFmpeg", () =>
{
    var args = FFmpegService.BuildArgumentList(tonePath, Path.Combine(outputDir, "tagged.mp3"), new ProcessingParameters
    {
        OutputFormat = "mp3",
        TrimSilence = true,
        WriteMetadata = true,
        MetadataTitle = "Episode 1",
        MetadataArtist = "Voxa Test",
        MetadataAlbum = "Launch"
    });
    Assert(args.Any(a => a.Contains("silenceremove=", StringComparison.Ordinal)), "Silence trimming filter was not added.");
    Assert(args.Contains("title=Episode 1"), "Title tag was not added.");
    Assert(args.Contains("artist=Voxa Test"), "Artist tag was not added.");
    Assert(args.Contains("album=Launch"), "Album tag was not added.");
});

Run("Built-in launch presets are available", () =>
{
    var presets = PresetCatalog.CreateBuiltInPresets().ToList();
    Assert(presets.Count == 2 && presets.All(p => p.IsBuiltIn), "Expected the two built-in launch presets.");
    Assert(presets.Any(p => p.LocalizationKey == "Preset.Podcast"), "Podcast preset is missing.");
    Assert(presets.Any(p => p.Parameters.TrimSilence), "Voice Note preset should trim silence.");
});

Run("Waveform control renders repeated progress updates", () =>
{
    Exception? threadError = null;
    var thread = new Thread(() =>
    {
        try
        {
            var peaks = Enumerable.Range(0, 120)
                .Select(i => (float)(0.15 + Math.Abs(Math.Sin(i / 7.0)) * 0.8))
                .ToArray();

            var view = new WaveformView
            {
                Width = 420,
                Height = 64,
                Peaks = peaks,
                PlayedBrush = Brushes.MediumPurple,
                UnplayedBrush = Brushes.Gray
            };

            view.Measure(new Size(420, 64));
            view.Arrange(new Rect(0, 0, 420, 64));
            view.UpdateLayout();

            for (var i = 0; i <= 60; i++)
            {
                view.Progress = i / 60.0;
                var bitmap = new RenderTargetBitmap(420, 64, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
            }
        }
        catch (Exception ex)
        {
            threadError = ex;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (threadError != null)
        throw new InvalidOperationException(threadError.Message, threadError);
});

foreach (var format in AudioFileFilter.SupportedOutputFormats)
{
    await RunAsync($"Convert WAV to {format}", async () =>
    {
        var outputPath = Path.Combine(outputDir, $"tone_basic.{format}");
        var parameters = new ProcessingParameters
        {
            OutputFormat = format,
            KeepOriginalSampleRate = false,
            SampleRateHz = 16000,
            BitrateKbps = 96,
            UseCustomFileNames = false
        };

        var progressValues = new List<double>();
        var result = await service.ProcessFileAsync(
            tonePath,
            outputPath,
            parameters,
            new Progress<double>(p => progressValues.Add(p)),
            CancellationToken.None);

        Assert(result.Success, result.ErrorMessage ?? "Conversion failed.");
        Assert(File.Exists(outputPath), "Output file was not created.");
        Assert(new FileInfo(outputPath).Length > 100, "Output file is unexpectedly small.");
        Assert(progressValues.Count > 0 && progressValues.Max() >= 99, "Progress did not reach 100%.");
    });
}

await RunAsync("Advanced filters convert successfully", async () =>
{
    var outputPath = Path.Combine(outputDir, "tone_advanced.mp3");
    var parameters = new ProcessingParameters
    {
        OutputFormat = "mp3",
        KeepOriginalSampleRate = false,
        SampleRateHz = 22050,
        BitrateKbps = 128,
        VolumeChangeDb = 3,
        EnhanceClarity = true,
        SpeedMultiplier = 1.25,
        SilencePaddingStartSec = 0.2,
        SilencePaddingEndSec = 0.2
    };

    var result = await service.ProcessFileAsync(tonePath, outputPath, parameters, null, CancellationToken.None);
    Assert(result.Success, result.ErrorMessage ?? "Advanced conversion failed.");
    Assert(File.Exists(outputPath), "Advanced output file was not created.");
});

await RunAsync("Bad audio fails without crashing service", async () =>
{
    var outputPath = Path.Combine(outputDir, "bad.mp3");
    var result = await service.ProcessFileAsync(
        badAudioPath,
        outputPath,
        new ProcessingParameters { OutputFormat = "mp3" },
        null,
        CancellationToken.None);

    Assert(!result.Success, "Bad audio unexpectedly converted successfully.");
    Assert(!string.IsNullOrWhiteSpace(result.ErrorMessage), "Bad audio failure did not include an error message.");
});

Run("Publish output is self-contained", () =>
{
    var runtimeConfigPath = Path.Combine(root, "publish", "Voxa.runtimeconfig.json");
    Assert(File.Exists(runtimeConfigPath), "publish/Voxa.runtimeconfig.json is missing.");

    using var doc = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));
    var frameworks = doc.RootElement
        .GetProperty("runtimeOptions")
        .GetProperty("includedFrameworks")
        .EnumerateArray()
        .Select(f => f.GetProperty("name").GetString())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert(frameworks.Contains("Microsoft.NETCore.App"), ".NET runtime is not included.");
    Assert(frameworks.Contains("Microsoft.WindowsDesktop.App"), "Windows Desktop runtime is not included.");
});

Run("Installer exists and packages publish folder by script", () =>
{
    var installerPath = Path.Combine(root, "installer", "Output", "VoxaSetup.exe");
    var scriptPath = Path.Combine(root, "installer", "Voxa.iss");
    Assert(File.Exists(installerPath), "VoxaSetup.exe is missing.");
    Assert(new FileInfo(installerPath).Length > 40 * 1024 * 1024, "Installer is too small for a self-contained WPF app.");

    var script = File.ReadAllText(scriptPath);
    Assert(script.Contains(@"Source: ""{#PublishDir}\*""", StringComparison.Ordinal), "Installer script does not include the publish folder recursively.");
    Assert(script.Contains("recursesubdirs", StringComparison.OrdinalIgnoreCase), "Installer script does not recurse through publish output.");
    Assert(script.Contains("PrivilegesRequired=lowest", StringComparison.OrdinalIgnoreCase), "Installer must support standard non-admin accounts.");
    Assert(script.Contains("DefaultDirName={localappdata}\\Programs", StringComparison.OrdinalIgnoreCase), "Installer must use a per-user install directory.");
});

Run("Release version is consistent", () =>
{
    var project = File.ReadAllText(Path.Combine(root, "Voxa.csproj"));
    var updater = File.ReadAllText(Path.Combine(root, "Services", "UpdateChecker.cs"));
    var installer = File.ReadAllText(Path.Combine(root, "installer", "Voxa.iss"));
    Assert(project.Contains("<AssemblyVersion>1.0.0.0</AssemblyVersion>", StringComparison.Ordinal), "Assembly version drifted.");
    Assert(project.Contains("<FileVersion>1.0.0.0</FileVersion>", StringComparison.Ordinal), "File version drifted.");
    Assert(updater.Contains("new(1, 0, 0)", StringComparison.Ordinal), "Update-check version drifted.");
    Assert(installer.Contains("#define MyAppVersion \"1.0.0\"", StringComparison.Ordinal), "Installer version drifted.");
});

Run("FFmpeg bundling status is understood", () =>
{
    var bundled = File.Exists(Path.Combine(root, "publish", "ffmpeg", "ffmpeg.exe")) ||
                  File.Exists(Path.Combine(root, "ffmpeg", "ffmpeg.exe"));
    if (!bundled)
        Console.WriteLine("WARN FFmpeg is not bundled. First launch on a clean machine needs internet for automatic FFmpeg download.");
});

Console.WriteLine();
Console.WriteLine("Summary");
Console.WriteLine("-------");
foreach (var result in results)
    Console.WriteLine(result.Passed ? $"PASS {result.Name}" : $"FAIL {result.Name}: {result.Error}");

var failed = results.Count(r => !r.Passed);
Console.WriteLine();
Console.WriteLine($"{results.Count - failed} passed, {failed} failed.");
Console.WriteLine($"Fixtures: {tempRoot}");

return failed == 0 ? 0 : 1;

static string FindProjectRoot()
{
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Voxa.csproj")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException("Could not find Voxa.csproj.");
}

static async Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var stdout = await stdoutTask;
    var stderr = await stderrTask;

    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{fileName} exited {process.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed record TestResult(string Name, bool Passed, string? Error);

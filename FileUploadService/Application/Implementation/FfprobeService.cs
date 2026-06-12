using FileUploadService.Application.DTOs;
using System.Diagnostics;
using System.Text.Json;

namespace FileUploadService.Application.Implementation;

/// <summary>
/// Runs FFprobe against a file path to extract video metadata.
///
/// FFprobe reads directly from disk — no video bytes are loaded into app memory.
/// Install: https://ffmpeg.org/download.html (Windows) | apt install ffmpeg (Linux) | brew install ffmpeg (Mac)
/// </summary>
public class FfprobeService
{
    private readonly ILogger<FfprobeService> _logger;

    // FFprobe arguments:
    // -v quiet          — suppress log output
    // -print_format json — output as JSON
    // -show_streams     — include stream info (codec, resolution, frame rate)
    // -show_format      — include format info (duration, bit_rate)
    private const string FfprobeArgs =
        "-v quiet -print_format json -show_streams -show_format \"{0}\"";

    public FfprobeService(ILogger<FfprobeService> logger)
    {
        _logger = logger;
    }

    public async Task<FfprobeResult> ExtractAsync(string absoluteFilePath)
    {
        var args = string.Format(FfprobeArgs, absoluteFilePath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFprobe could not be started. Is FFmpeg installed and on PATH?");
            // Return a non-fatal result — video still uploaded, metadata just won't be populated
            return new FfprobeResult
            {
                Success = false,
                ErrorMessage = "FFprobe not available. Metadata extraction skipped."
            };
        }

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            _logger.LogWarning(
                "FFprobe exited with code {Code}. File: {File}. Stderr: {Err}",
                process.ExitCode, absoluteFilePath, stdErr
            );
            return new FfprobeResult
            {
                Success = false,
                ErrorMessage = $"FFprobe failed (exit {process.ExitCode}): {stdErr}"
            };
        }

        return ParseFfprobeOutput(stdOut);
    }

    // ── PARSE ─────────────────────────────────────────────────

    private FfprobeResult ParseFfprobeOutput(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new FfprobeResult { Success = true };

            // Duration and bit rate come from the "format" object
            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("duration", out var dur) &&
                    decimal.TryParse(dur.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var durationSec))
                    result.DurationSeconds = Math.Round(durationSec, 3);

                if (format.TryGetProperty("bit_rate", out var br) &&
                    long.TryParse(br.GetString(), out var bitRate))
                    result.BitRate = bitRate;
            }

            // Codec, resolution, frame rate come from "streams" array
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var codecType = stream.TryGetProperty("codec_type", out var ct)
                        ? ct.GetString() : null;

                    if (codecType == "video" && result.VideoCodec is null)
                    {
                        result.VideoCodec = stream.TryGetProperty("codec_name", out var vc)
                            ? vc.GetString() : null;

                        result.Width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : null;
                        result.Height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : null;

                        result.FrameRate = stream.TryGetProperty("r_frame_rate", out var fr)
                            ? fr.GetString() : null;
                    }
                    else if (codecType == "audio" && result.AudioCodec is null)
                    {
                        result.AudioCodec = stream.TryGetProperty("codec_name", out var ac)
                            ? ac.GetString() : null;
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse FFprobe JSON output.");
            return new FfprobeResult
            {
                Success = false,
                ErrorMessage = "Failed to parse FFprobe output."
            };
        }
    }
}
using System.IO;
using System.Text;
using TheHub.Common.Config;

namespace TheHub.Core.Services;

public class VideoCodecInfo
{
    public string CodecFourCC { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = "Unknown";
    public bool IsUniversallySupported { get; set; } = true;
    public string WarningMessage { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public static class VideoCodecHelper
{
    /// <summary>
    /// Inspects the media file container headers (MP4 / WebM / MKV) to detect the video codec.
    /// Identifies AV1 (av01), HEVC (hvc1/hev1), and VP9 (vp09) which fail or blank out on Windows
    /// without optional Microsoft Store codecs.
    /// </summary>
    public static VideoCodecInfo DetectCodec(string filePath)
    {
        var info = new VideoCodecInfo();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return info;
        }

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".mp4" && ext != ".mov" && ext != ".m4v" && ext != ".webm" && ext != ".mkv")
        {
            return info;
        }

        try
        {
            // Read first 256 KB of the file to inspect the codec tags (stsd / moov boxes in MP4 or Matroska DocType)
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int bufferSize = (int)Math.Min(fs.Length, 262144L);
            byte[] buffer = new byte[bufferSize];
            int read = fs.Read(buffer, 0, bufferSize);

            string ascii = Encoding.ASCII.GetString(buffer, 0, read);

            if (ascii.Contains("av01"))
            {
                info.CodecFourCC = "av01";
                info.FriendlyName = "AV1 (DirectX 11 + FFmpeg)";
                info.IsUniversallySupported = true;
                AppLogger.Log($"[VideoCodecHelper] Detected AV1 video codec in '{Path.GetFileName(filePath)}' - Hardware Accelerated via Flyleaf.");
            }
            else if (ascii.Contains("hvc1") || ascii.Contains("hev1"))
            {
                info.CodecFourCC = "hvc1";
                info.FriendlyName = "HEVC / H.265 (DirectX 11 + FFmpeg)";
                info.IsUniversallySupported = true;
                AppLogger.Log($"[VideoCodecHelper] Detected HEVC video codec in '{Path.GetFileName(filePath)}' - Hardware Accelerated via Flyleaf.");
            }
            else if (ascii.Contains("avc1"))
            {
                info.CodecFourCC = "avc1";
                info.FriendlyName = "H.264 / AVC (DirectX 11 + FFmpeg)";
                info.IsUniversallySupported = true;
                AppLogger.Log($"[VideoCodecHelper] Detected H.264 (avc1) video codec in '{Path.GetFileName(filePath)}' - Hardware Accelerated via Flyleaf.");
            }
            else if (ascii.Contains("vp09"))
            {
                info.CodecFourCC = "vp09";
                info.FriendlyName = "VP9 (DirectX 11 + FFmpeg)";
                info.IsUniversallySupported = true;
                AppLogger.Log($"[VideoCodecHelper] Detected VP9 video codec in '{Path.GetFileName(filePath)}' - Hardware Accelerated via Flyleaf.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[VideoCodecHelper] Error detecting codec: {ex.Message}");
        }

        return info;
    }
}

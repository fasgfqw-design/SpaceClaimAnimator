using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SCAnimator.V261.Engine {
    // Encode SpaceClaim's lossless scene PNGs into a compatible video format.
    internal static class VideoTranscoder {
        public static string FindFfmpeg() {
            var roots = new List<string>();
            string environmentRoot = Environment.GetEnvironmentVariable("AWP_ROOT261");
            if (!String.IsNullOrWhiteSpace(environmentRoot)) roots.Add(environmentRoot);
            AddVersionRoot(roots, typeof(VideoTranscoder).Assembly.Location);
            AddVersionRoot(roots, Process.GetCurrentProcess().MainModule.FileName);
            foreach (string root in roots) {
                string bundled = Path.Combine(root, "tp", "ffmpeg");
                if (Directory.Exists(bundled)) {
                    foreach (string path in Directory.GetFiles(bundled, "ffmpeg.exe", SearchOption.AllDirectories))
                        return path;
                }
                foreach (string path in new[] { Path.Combine(root, "rocky", "bin", "ffmpeg.exe"),
                    Path.Combine(root, "AnsysEM", "ffmpeg.exe") })
                    if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException("The FFmpeg executable bundled with Ansys V261 was not found. " +
                "Video export requires an installed Ansys FFmpeg encoder.");
        }
        private static void AddVersionRoot(ICollection<string> roots, string filePath) {
            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(filePath));
            for (int i = 0; i < 8 && directory != null; i++, directory = directory.Parent)
                if (String.Equals(directory.Name, "v261", StringComparison.OrdinalIgnoreCase)) {
                    roots.Add(directory.FullName);
                    return;
                }
        }
        public static void TranscodePngSequence(string ffmpeg, string framesDirectory, int frameCount,
            string destination, double durationSeconds) {
            if (frameCount < 2 || double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) ||
                durationSeconds <= 0)
                throw new ArgumentOutOfRangeException("durationSeconds", "A video needs at least two frames and a positive duration.");
            string extension = Path.GetExtension(destination).ToLowerInvariant();
            if (extension != ".mp4" && extension != ".avi")
                throw new ArgumentException("Choose an MP4 or AVI destination.", "destination");
            long durationMicros = checked((long)Math.Round(durationSeconds * 1000000));
            if (durationMicros < 1) throw new ArgumentOutOfRangeException("durationSeconds");
            string firstFrame = Path.Combine(framesDirectory, "frame000000.png");
            string lastFrame = Path.Combine(framesDirectory,
                "frame" + (frameCount - 1).ToString("D6") + ".png");
            if (!File.Exists(firstFrame) || !File.Exists(lastFrame))
                throw new InvalidDataException("The PNG frame sequence is incomplete.");
            string frameRate = (frameCount * 1000000L) + "/" + durationMicros;
            string temporaryOutput = Path.Combine(Path.GetDirectoryName(destination),
                ".SCAnimator-" + Guid.NewGuid().ToString("N") + extension);
            try {
                string common = "-hide_banner -loglevel error -nostdin -y -framerate " + frameRate +
                    " -start_number 0 -i " + Quote(Path.Combine(framesDirectory, "frame%06d.png")) +
                    " -frames:v " + frameCount + " -an -vf pad=ceil(iw/2)*2:ceil(ih/2)*2";
                if (extension == ".mp4") {
                    string lastError = null;
                    bool encoded = false;
                    foreach (string encoder in new[] { "h264_mf", "libopenh264", "libx264" }) {
                        string quality = encoder == "h264_mf" ? " -rate_control quality -quality 95 -b:v 20M" :
                            encoder == "libx264" ? " -crf 16 -preset medium" : " -b:v 20M";
                        string arguments = common + " -c:v " + encoder + quality + " -pix_fmt yuv420p" +
                            " -movflags +faststart " + Quote(temporaryOutput);
                        if (Run(ffmpeg, arguments, out lastError)) { encoded = true; break; }
                    }
                    if (!encoded)
                        throw new InvalidOperationException("H.264/MP4 encoding failed. " + lastError);
                } else {
                    string error;
                    string arguments = common + " -c:v mjpeg -q:v 2 -pix_fmt yuvj420p " + Quote(temporaryOutput);
                    if (!Run(ffmpeg, arguments, out error))
                        throw new InvalidOperationException("Motion JPEG AVI encoding failed. " + error);
                }
                if (!File.Exists(temporaryOutput) || new FileInfo(temporaryOutput).Length == 0)
                    throw new InvalidDataException("Video conversion did not create a file.");
                if (File.Exists(destination)) File.Replace(temporaryOutput, destination, null);
                else File.Move(temporaryOutput, destination);
            } finally {
                try { if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput); } catch { }
            }
        }
        public static void Transcode(string ffmpeg, string capturedAvi, string destination,
            double durationSeconds) {
            if (!File.Exists(capturedAvi) || new FileInfo(capturedAvi).Length == 0)
                throw new InvalidDataException("SpaceClaim did not create a video to convert.");
            if (double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds <= 0)
                throw new ArgumentOutOfRangeException("durationSeconds");
            int frameCount = ReadAviFrameCount(capturedAvi);
            long durationMicros = checked((long)Math.Round(durationSeconds * 1000000));
            if (durationMicros < 1) throw new ArgumentOutOfRangeException("durationSeconds");
            // SpaceClaim's AVI is tagged as 10 fps even when it captures many more
            // animation frames per timeline second. Override the input timestamps
            // so each captured frame occupies an equal share of the real duration.
            string frameRate = (frameCount * 1000000L) + "/" + durationMicros;
            string extension = Path.GetExtension(destination).ToLowerInvariant();
            if (extension != ".mp4" && extension != ".avi")
                throw new ArgumentException("Choose an MP4 or AVI destination.", "destination");
            string directory = Path.GetDirectoryName(destination);
            string temporaryOutput = Path.Combine(directory,
                ".SCAnimator-" + Guid.NewGuid().ToString("N") + extension);
            try {
                string common = "-hide_banner -loglevel error -nostdin -y -r " + frameRate +
                    " -i " + Quote(capturedAvi) +
                    " -an -vf pad=ceil(iw/2)*2:ceil(ih/2)*2 -frames:v " + frameCount;
                if (extension == ".mp4") {
                    string lastError = null;
                    bool encoded = false;
                    foreach (string encoder in new[] { "h264_mf", "libopenh264", "libx264" }) {
                        string quality = encoder == "h264_mf" ? " -rate_control quality -quality 95 -b:v 20M" :
                            encoder == "libx264" ? " -crf 16 -preset medium" : " -b:v 20M";
                        string arguments = common + " -c:v " + encoder + quality + " -pix_fmt yuv420p" +
                            " -movflags +faststart " + Quote(temporaryOutput);
                        if (Run(ffmpeg, arguments, out lastError)) { encoded = true; break; }
                    }
                    if (!encoded)
                        throw new InvalidOperationException("H.264/MP4 encoding failed. " + lastError);
                } else {
                    string error;
                    string arguments = common + " -c:v mjpeg -q:v 2 -pix_fmt yuvj420p " + Quote(temporaryOutput);
                    if (!Run(ffmpeg, arguments, out error))
                        throw new InvalidOperationException("Motion JPEG AVI encoding failed. " + error);
                }
                if (!File.Exists(temporaryOutput) || new FileInfo(temporaryOutput).Length == 0)
                    throw new InvalidDataException("Video conversion did not create a file.");
                if (File.Exists(destination)) File.Replace(temporaryOutput, destination, null);
                else File.Move(temporaryOutput, destination);
            } finally {
                try { if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput); } catch { }
            }
        }
        private static int ReadAviFrameCount(string path) {
            byte[] header = new byte[65536];
            int length = 0;
            using (FileStream stream = File.OpenRead(path)) {
                while (length < header.Length) {
                    int read = stream.Read(header, length, header.Length - length);
                    if (read == 0) break;
                    length += read;
                }
            }
            if (length < 32 || header[0] != 'R' || header[1] != 'I' || header[2] != 'F' ||
                header[3] != 'F' || header[8] != 'A' || header[9] != 'V' || header[10] != 'I' ||
                header[11] != ' ')
                throw new InvalidDataException("SpaceClaim capture is not an AVI file.");
            for (int i = 12; i <= length - 28; i++) {
                if (header[i] != 'a' || header[i + 1] != 'v' || header[i + 2] != 'i' || header[i + 3] != 'h' ||
                    BitConverter.ToInt32(header, i + 4) < 56) continue;
                int count = BitConverter.ToInt32(header, i + 24);
                if (count > 0) return count;
            }
            throw new InvalidDataException("The AVI does not report its frame count; video timing cannot be corrected.");
        }
        private static string Quote(string path) { return "\"" + path + "\""; }
        private static bool Run(string executable, string arguments, out string error) {
            var info = new ProcessStartInfo(executable, arguments) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            using (Process process = Process.Start(info)) {
                var errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30 * 60 * 1000)) {
                    process.Kill();
                    process.WaitForExit();
                    throw new TimeoutException("Video conversion took longer than 30 minutes.");
                }
                error = errorTask.GetAwaiter().GetResult();
                if (error.Length > 1500) error = error.Substring(error.Length - 1500);
                return process.ExitCode == 0;
            }
        }
    }
}

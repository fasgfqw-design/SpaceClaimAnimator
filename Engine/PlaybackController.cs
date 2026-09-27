using System;
using System.IO;
using System.Windows.Forms;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Geometry;
using SCAnimator.V261.Commands;
using SpaceClaimApplication = SpaceClaim.Api.V261.Application;

namespace SCAnimator.V261.Engine {
    internal static class PlaybackController {
        private static TimelineAnimator currentAnimator;
        private static ScenarioAnimator currentScenarioAnimator;
        private static Window playbackWindow;
        private static IDocObject previousSelection;
        private static string exportPath;
        private static string capturePath;
        private static string ffmpegPath;
        private static double exportDurationSeconds;
        private static int capturedFrames;
        private static Exception captureError;
        private static bool videoRecording;
        public static bool IsOurAnimationRunning { get { return (currentAnimator != null || currentScenarioAnimator != null) && Animation.IsAnimating; } }
        public static bool IsExportingVideo { get { return videoRecording; } }
        public static string ScenarioStatus { get { return currentScenarioAnimator == null ? null : currentScenarioAnimator.Status; } }

        public static void TogglePlayPause(Command command) {
            if (videoRecording) return;
            if (IsOurAnimationRunning) { Animation.IsPaused = !Animation.IsPaused; return; }
            if (Animation.IsAnimating) return;
            AnimationProject project = AnimationProject.Current;
            if (!project.CanPlay || !project.CanUseCurrentAssembly) return;
            playbackWindow = Window.ActiveWindow;
            previousSelection = playbackWindow.ActiveContext.SingleSelection;
            SeekTime(project.EffectiveRangeStart);
            Window.ActiveWindow.ActiveContext.SingleSelection = null;
            StartAnimator(command, project);
        }

        public static void ExportVideo(Command command, string path) {
            if (Animation.IsAnimating || videoRecording || String.IsNullOrWhiteSpace(path)) return;
            AnimationProject project = AnimationProject.Current;
            if (!project.CanPlay || !project.CanUseCurrentAssembly) return;
            string encoder = VideoTranscoder.FindFfmpeg();
            double durationSeconds = (project.EffectiveRangeEnd - project.EffectiveRangeStart) / project.SpeedMultiplier;
            if (durationSeconds <= 0) throw new InvalidOperationException("The animation has no duration to export.");
            string framesDirectory = Path.Combine(Path.GetTempPath(),
                "SCAnimatorFrames-" + Guid.NewGuid().ToString("N"));
            SeekTime(project.EffectiveRangeStart);
            playbackWindow = Window.ActiveWindow;
            previousSelection = playbackWindow.ActiveContext.SingleSelection;
            try {
                Directory.CreateDirectory(framesDirectory);
                capturePath = framesDirectory;
                capturedFrames = 0;
                captureError = null;
                playbackWindow.ActiveContext.SingleSelection = null;
                WriteBlock.ExecuteTask("SC Animator - Prepare first video frame", delegate {
                    project.ApplyVisibility(project.EffectiveRangeStart, true);
                });
                WritePngFrame(); // Frame zero is the exact first keyframe.
                videoRecording = true;
                exportPath = path;
                ffmpegPath = encoder;
                exportDurationSeconds = durationSeconds;
                StartAnimator(command, project, CaptureFrameDuringAnimation);
            } catch {
                videoRecording = false;
                exportPath = null;
                capturePath = null;
                ffmpegPath = null;
                exportDurationSeconds = 0;
                capturedFrames = 0;
                captureError = null;
                TryDeleteCaptureDirectory(framesDirectory);
                project.RestoreAppearanceStylesInWriteBlock();
                RestoreSelection(project);
                throw;
            }
        }
        public static void PlayScenario(Command command) {
            if (Animation.IsAnimating || videoRecording) return;
            AnimationProject active = AnimationProject.Current;
            ScenarioDefinition definition = active.Scenario;
            AnimationProject[] clips = active.CreateScenarioProjects(definition);
            ScenarioAnimator animator = new ScenarioAnimator(definition, clips);
            playbackWindow = Window.ActiveWindow;
            previousSelection = playbackWindow.ActiveContext.SingleSelection;
            try {
                playbackWindow.ActiveContext.SingleSelection = null;
                StartScenarioAnimator(command, animator, active);
            } catch { RestoreSelection(active); throw; }
        }
        public static void ExportScenario(Command command, string path) {
            if (Animation.IsAnimating || videoRecording || String.IsNullOrWhiteSpace(path)) return;
            AnimationProject active = AnimationProject.Current;
            ScenarioDefinition definition = active.Scenario;
            AnimationProject[] clips = active.CreateScenarioProjects(definition);
            string encoder = VideoTranscoder.FindFfmpeg();
            ScenarioAnimator animator = new ScenarioAnimator(definition, clips, CaptureFrameDuringAnimation);
            string directory = Path.Combine(Path.GetTempPath(), "SCAnimatorFrames-" + Guid.NewGuid().ToString("N"));
            playbackWindow = Window.ActiveWindow;
            previousSelection = playbackWindow.ActiveContext.SingleSelection;
            try {
                Directory.CreateDirectory(directory);
                capturePath = directory; capturedFrames = 0; captureError = null;
                PrepareScenarioFirstFrame(animator);
                playbackWindow.ActiveContext.SingleSelection = null;
                WritePngFrame();
                videoRecording = true; exportPath = path; ffmpegPath = encoder;
                exportDurationSeconds = animator.DurationSeconds;
                StartScenarioAnimator(command, animator, active);
            } catch {
                videoRecording = false; exportPath = null; capturePath = null; ffmpegPath = null;
                exportDurationSeconds = 0; capturedFrames = 0; captureError = null;
                TryDeleteCaptureDirectory(directory);
                foreach (AnimationProject clip in clips) clip.RestoreAppearanceStylesInWriteBlock();
                RestoreSelection(active);
                throw;
            }
        }
        private static void PrepareScenarioFirstFrame(ScenarioAnimator animator) {
            AnimationProject clip = animator.FirstProject;
            double time = animator.FirstTime;
            Matrix[] poses = clip.EvaluateAt(time);
            Matrix[] planePoses = clip.EvaluatePlanesAt(time);
            Component[] components = clip.GetComponents();
            DatumPlane[] planes = clip.GetPlanes();
            WriteBlock.ExecuteTask("SC Animator - Prepare scenario", delegate {
                for (int i = 0; i < components.Length; i++) components[i].Placement = poses[i];
                for (int i = 0; i < planes.Length; i++) AnimationProject.ApplyPlane(planes[i], planePoses[i]);
                clip.ApplyVisibility(time, true);
            });
            Matrix? camera = clip.CameraEnabled ? clip.EvaluateCameraAt(time) : null;
            if (camera.HasValue) Window.ActiveWindow.SetProjection(camera.Value, false, false);
        }
        private static void StartScenarioAnimator(Command command, ScenarioAnimator animator, AnimationProject active) {
            currentScenarioAnimator = animator;
            animator.Completed += delegate(object sender, AnimationCompletedEventArgs e) {
                if (currentScenarioAnimator != animator) return;
                currentScenarioAnimator = null;
                string finishedPath = exportPath, framesDirectory = capturePath, encoder = ffmpegPath;
                double duration = exportDurationSeconds;
                int frameCount = capturedFrames;
                Exception frameError = captureError;
                bool wasExport = videoRecording;
                videoRecording = false; exportPath = null; capturePath = null; ffmpegPath = null;
                exportDurationSeconds = 0; capturedFrames = 0; captureError = null;
                RestoreSelection(active);
                if (wasExport) {
                    try {
                        if (e.Result == AnimationResult.Exhausted) {
                            if (frameError != null) throw new InvalidOperationException(
                                "SpaceClaim could not export a PNG frame: " + frameError.Message, frameError);
                            VideoTranscoder.TranscodePngSequence(encoder, framesDirectory,
                                frameCount, finishedPath, duration);
                            if (!HasVideoFile(finishedPath)) throw new InvalidDataException("The converted video file is empty.");
                            MessageBox.Show("Scenario video saved:\n" + finishedPath, "SC Animator",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    } catch (Exception ex) {
                        MessageBox.Show("Scenario export failed:\n" + ex.Message,
                            "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    } finally { TryDeleteCaptureDirectory(framesDirectory); }
                }
            };
            try {
                Animation.Start("SC Animator scenario", animator, command,
                    Command.GetCommand(CancelAnimationCapsule.CommandName));
            } catch { currentScenarioAnimator = null; throw; }
        }
        private static void WritePngFrame() {
            string stem = Path.Combine(capturePath, "frame" + capturedFrames.ToString("D6"));
            playbackWindow.Export(WindowExportFormat.Png, stem);
            if (!File.Exists(stem + ".png") || new FileInfo(stem + ".png").Length == 0)
                throw new InvalidDataException("SpaceClaim did not export the current PNG frame.");
            capturedFrames++;
        }
        private static void CaptureFrameDuringAnimation(int frame) {
            if (captureError != null) return;
            try {
                WritePngFrame();
            } catch (Exception ex) { captureError = ex; }
        }

        private static void StartAnimator(Command command, AnimationProject project,
            Action<int> captureFrame = null) {
            TimelineAnimator animator = new TimelineAnimator(project, captureFrame);
            currentAnimator = animator;
            project.SetCurrentKeyframe(-1);
            animator.Completed += delegate(object sender, AnimationCompletedEventArgs e) {
                if (currentAnimator != animator) return;
                if (e.Result == AnimationResult.Exhausted && project.Loop && project.IsActiveDocument && !videoRecording) {
                    if (animator.UndoStepAdded) SpaceClaimApplication.Undo(1);
                    StartAnimator(command, project);
                    return;
                }
                currentAnimator = null;
                string finishedPath = exportPath;
                string framesDirectory = capturePath;
                string encoder = ffmpegPath;
                double durationSeconds = exportDurationSeconds;
                int frameCount = capturedFrames;
                Exception frameError = captureError;
                bool wasExport = videoRecording;
                videoRecording = false;
                exportPath = null;
                capturePath = null;
                ffmpegPath = null;
                exportDurationSeconds = 0;
                capturedFrames = 0;
                captureError = null;
                RestoreSelection(project);
                if (e.Result == AnimationResult.Canceled) project.SetPlayhead(project.EffectiveRangeStart);
                else if (e.Result == AnimationResult.Exhausted) project.SetPlayhead(project.EffectiveRangeEnd);
                else project.SetCurrentKeyframe(-1);
                if (wasExport) {
                    try {
                        if (e.Result == AnimationResult.Exhausted) {
                            if (frameError != null)
                                throw new InvalidOperationException("SpaceClaim could not export a PNG frame: " +
                                    frameError.Message, frameError);
                            VideoTranscoder.TranscodePngSequence(encoder, framesDirectory,
                                frameCount, finishedPath, durationSeconds);
                            if (!HasVideoFile(finishedPath))
                                throw new InvalidDataException("The converted video file is empty.");
                            MessageBox.Show("Video saved:\n" + finishedPath, "SC Animator",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    } catch (Exception ex) {
                        MessageBox.Show("Video export failed:\n" + ex.Message,
                            "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    } finally { TryDeleteCaptureDirectory(framesDirectory); }
                }
            };
            try {
                Animation.Start("SC Animator", animator, command, Command.GetCommand(CancelAnimationCapsule.CommandName));
            } catch {
                currentAnimator = null;
                RestoreSelection(project);
                project.SetCurrentKeyframe(0);
                throw;
            }
        }
        private static bool HasVideoFile(string path) {
            try { return !String.IsNullOrEmpty(path) && File.Exists(path) && new FileInfo(path).Length > 0; }
            catch { return false; }
        }
        private static void TryDeleteCaptureDirectory(string path) {
            try {
                if (String.IsNullOrEmpty(path)) return;
                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                string parent = Path.GetDirectoryName(full);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                if (String.Equals(parent, temp, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(full).StartsWith("SCAnimatorFrames-", StringComparison.Ordinal) &&
                    Directory.Exists(full)) Directory.Delete(full, true);
            }
            catch { }
        }
        private static void RestoreSelection(AnimationProject project) {
            if (project.IsActiveDocument && Window.ActiveWindow == playbackWindow &&
                previousSelection != null && !previousSelection.IsDeleted)
                playbackWindow.ActiveContext.SingleSelection = previousSelection;
            previousSelection = null;
            playbackWindow = null;
        }
        public static void CancelAnimation() {
            if (IsOurAnimationRunning) Animation.Cancel();
        }
        public static void GoToKeyframe(int index) {
            AnimationProject project = AnimationProject.Current;
            if (!project.CanEdit || !project.CanUseCurrentAssembly || index < 0 || index >= project.KeyframeCount) return;
            SeekTime(project.GetKeyframeTime(index));
        }
        public static void SeekTime(double seconds) {
            AnimationProject project = AnimationProject.Current;
            if (!project.CanEdit || !project.CanUseCurrentAssembly) return;
            Component[] components = project.GetComponents();
            DatumPlane[] planes = project.GetPlanes();
            double start = project.PlayheadSeconds;
            // One large placement change can be only partially resolved by an
            // assembly with mating conditions. Use the same intermediate poses
            // that produce the correct result when the user scrubs slowly.
            int steps = Math.Max(1, Math.Min(120,
                (int)Math.Ceiling(Math.Abs(seconds - start) * project.FramesPerSecond)));
            bool constrained = SequentialPoseWriter.HasEnabledAssemblyCondition(Window.ActiveWindow.Document);
            var loop = constrained ? null : new TimelineApplyLoop(project, components, planes);
            var writer = constrained ? new SequentialPoseWriter("SC Animator - Seek timeline", null) : null;
            Matrix? initialCamera = Window.ActiveWindow == null ? null : (Matrix?)Window.ActiveWindow.Projection;
            try {
                for (int step = 1; step <= steps; step++) {
                    double time = step == steps ? seconds : start + (seconds - start) * step / steps;
                    Matrix[] poses = project.EvaluateAt(time), planePoses = project.EvaluatePlanesAt(time);
                    if (writer != null) writer.Apply(project, components, planes, poses, planePoses, time);
                    else loop.ApplyPoses(poses, planePoses, time);
                    Matrix? camera = project.CameraEnabled ? project.EvaluateCameraAt(time) : null;
                    if (camera.HasValue) Window.ActiveWindow.SetProjection(camera.Value, false, false);
                    if (Window.ActiveWindow != null) Window.ActiveWindow.RefreshRendering();
                }
                if (loop != null) loop.Complete();
                if (Window.ActiveWindow != null) Window.ActiveWindow.RefreshRendering();
            } catch {
                if (loop != null) loop.Cancel();
                else if (writer.HasPoseUndoStep) SpaceClaimApplication.Undo(1);
                if (initialCamera.HasValue && Window.ActiveWindow != null)
                    Window.ActiveWindow.SetProjection(initialCamera.Value, false, false);
                throw;
            }
            Action finish = delegate {
                project.SetPlayhead(seconds);
                project.MarkNavigationDirty();
            };
            if (writer != null) writer.Append(finish);
            else WriteBlock.ExecuteTask("SC Animator - Seek position", delegate { finish(); });
        }
        public static void GoToAnimationStart() {
            AnimationProject project = AnimationProject.Current;
            if (!project.CanEdit || !project.CanUseCurrentAssembly) return;
            Component[] components = project.GetComponents();
            Matrix[] poses = project.GetInitialPlacements();
            DatumPlane[] planes = project.GetPlanes();
            Matrix[] planePoses = project.EvaluatePlanesAt(0);
            Matrix? camera = project.CameraEnabled ? project.GetInitialCamera() : null;
            WriteBlock.ExecuteTask("SC Animator - Restore animation start", delegate {
                for (int i = 0; i < components.Length; i++) components[i].Placement = poses[i];
                for (int i = 0; i < planes.Length; i++) AnimationProject.ApplyPlane(planes[i], planePoses[i]);
                project.ApplyVisibility(0, true);
                project.SetPlayhead(0);
                project.MarkNavigationDirty();
            });
            if (camera.HasValue) Window.ActiveWindow.SetProjection(camera.Value, false, false);
        }
        public static void ResetToFirstKeyframe() { GoToKeyframe(0); }
        public static void GoToLastCapturedKeyframe() { GoToKeyframe(AnimationProject.Current.KeyframeCount - 1); }
    }
}


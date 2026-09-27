using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Geometry;
using SpaceClaimApplication = SpaceClaim.Api.V261.Application;

namespace SCAnimator.V261.Engine {
    internal sealed class TimelineAnimator : Animator {
        private readonly AnimationProject project;
        private readonly TimelineApplyLoop applyLoop;
        private readonly SequentialPoseWriter sequentialWriter;
        private readonly Component[] components;
        private readonly DatumPlane[] planes;
        private readonly Matrix? initialCamera;
        private readonly Action<int> captureFrame;
        private int lastAppliedFrame = -1;
        public TimelineAnimator(AnimationProject project, Action<int> captureFrame = null) {
            this.project = project;
            this.captureFrame = captureFrame;
            // ExportVideo has already applied and saved frame zero before Animation.Start.
            if (captureFrame != null) lastAppliedFrame = 0;
            components = project.GetComponents();
            planes = project.GetPlanes();
            if (SequentialPoseWriter.HasEnabledAssemblyCondition(Window.ActiveWindow == null ? null : Window.ActiveWindow.Document))
                sequentialWriter = new SequentialPoseWriter("SC Animator", project);
            else applyLoop = new TimelineApplyLoop(project, components, planes);
            initialCamera = Window.ActiveWindow == null ? null : (Matrix?)Window.ActiveWindow.Projection;
        }
        public bool UndoStepAdded { get; private set; }
        public override int Advance(int frame) {
            int target = Math.Max(0, Math.Min(project.TotalFrames, frame));
            // SpaceClaim may skip native Advance calls. Applying only the destination
            // makes constrained assemblies settle partway toward the recorded pose.
            // Walk through the missed poses, just as slow ruler scrubbing does.
            int stride = captureFrame == null ? Math.Max(1, (target - lastAppliedFrame + 119) / 120) : 1;
            for (int next = lastAppliedFrame + stride; next < target; next += stride) ApplyFrame(next);
            if (target > lastAppliedFrame) ApplyFrame(target);
            lastAppliedFrame = Math.Max(lastAppliedFrame, target);
            return project.TotalFrames;
        }
        private void ApplyFrame(int frame) {
            double time = project.PlaybackTimeAtFrame(frame);
            Matrix[] poses = project.EvaluateAt(time), planePoses = project.EvaluatePlanesAt(time);
            if (sequentialWriter != null)
                sequentialWriter.Apply(project, components, planes, poses, planePoses, time);
            else applyLoop.ApplyPoses(poses, planePoses, time);
            Matrix? camera = project.CameraEnabled ? project.EvaluateCameraAt(time) : null;
            if (camera.HasValue && Window.ActiveWindow != null)
                Window.ActiveWindow.SetProjection(camera.Value, false, false);
            if (Window.ActiveWindow != null) Window.ActiveWindow.RefreshRendering();
            if (captureFrame != null) captureFrame(frame);
        }
        protected override void OnCompleted(AnimationCompletedEventArgs args) {
            if (args.Result == AnimationResult.Canceled) {
                if (sequentialWriter != null) {
                    if (sequentialWriter.HasPoseUndoStep) SpaceClaimApplication.Undo(1);
                } else applyLoop.Cancel();
                if (initialCamera.HasValue && Window.ActiveWindow != null)
                    Window.ActiveWindow.SetProjection(initialCamera.Value, false, false);
                UndoStepAdded = false;
            } else {
                if (args.Result == AnimationResult.Exhausted && lastAppliedFrame < project.TotalFrames)
                    Advance(project.TotalFrames);
                if (sequentialWriter != null) {
                    sequentialWriter.Append(project.RestoreAppearanceStylesInWriteBlock);
                    UndoStepAdded = sequentialWriter.HasPoseUndoStep;
                } else UndoStepAdded = applyLoop.Complete();
                if (Window.ActiveWindow != null) Window.ActiveWindow.RefreshRendering();
            }
            if (sequentialWriter == null || args.Result == AnimationResult.Canceled)
                project.RestoreAppearanceStylesInWriteBlock();
            base.OnCompleted(args);
        }
    }

    internal sealed class TimelineApplyLoop : ApplyLoop {
        private readonly AnimationProject project;
        private readonly Component[] components;
        private readonly DatumPlane[] planes;
        private Matrix[] poses;
        private Matrix[] planePoses;
        private double time;
        public TimelineApplyLoop(AnimationProject project, Component[] components, DatumPlane[] planes) : base("SC Animator") {
            this.project = project;
            this.components = components;
            this.planes = planes;
            foreach (Component component in components) component.KeepAlive(true);
            foreach (DatumPlane plane in planes) plane.KeepAlive(true);
        }
        public void ApplyPoses(Matrix[] placements, Matrix[] planePlacements, double time) {
            poses = placements;
            planePoses = planePlacements;
            this.time = time;
            Apply();
        }
        protected override bool OnApply() {
            for (int i = 0; i < components.Length; i++) components[i].Placement = poses[i];
            for (int i = 0; i < planes.Length; i++) AnimationProject.ApplyPlane(planes[i], planePoses[i]);
            project.ApplyVisibility(time, true);
            return true;
        }
    }

    internal sealed class ScenarioAnimator : Animator {
        private sealed class Segment {
            internal AnimationProject Project;
            internal string Name;
            internal int Index;
            internal bool Reverse;
            internal int FirstFrame;
            internal int FrameCount;
        }
        private readonly List<Segment> segments = new List<Segment>();
        private readonly ScenarioApplyLoop applyLoop;
        private readonly SequentialPoseWriter sequentialWriter;
        private readonly Matrix? initialCamera;
        private readonly Action<int> captureFrame;
        private readonly AnimationProject[] projects;
        private AnimationProject previousProject;
        private int lastAppliedFrame = -1;
        internal int TotalFrames { get; private set; }
        internal double DurationSeconds { get; private set; }
        internal bool UndoStepAdded { get; private set; }
        internal string Status { get; private set; }
        internal AnimationProject FirstProject { get { return segments[0].Project; } }
        internal double FirstTime { get { return segments[0].Reverse ?
            segments[0].Project.EffectiveRangeEnd : segments[0].Project.EffectiveRangeStart; } }
        internal ScenarioAnimator(ScenarioDefinition definition, AnimationProject[] projects, Action<int> capture = null) {
            if (definition == null || projects == null || projects.Length != definition.AnimationNames.Count ||
                projects.Length == 0) throw new ArgumentException("The scenario is empty.");
            this.projects = projects;
            captureFrame = capture;
            // ExportScenario has already applied and saved frame zero.
            if (captureFrame != null) lastAppliedFrame = 0;
            initialCamera = Window.ActiveWindow == null ? null : (Matrix?)Window.ActiveWindow.Projection;
            int fps = projects[0].FramesPerSecond;
            foreach (ScenarioEntry entry in definition.BuildOrder()) {
                int index = definition.AnimationNames.FindIndex(name =>
                    String.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase));
                AnimationProject project = projects[index];
                double duration = (project.EffectiveRangeEnd - project.EffectiveRangeStart) / project.SpeedMultiplier;
                int frames = Math.Max(2, (int)Math.Ceiling(duration * fps));
                if (TotalFrames > 100000 - frames)
                    throw new InvalidOperationException("Scenario is too long; reduce repeats or the playback range.");
                var segment = new Segment { Project = project, Name = entry.Name, Reverse = entry.Reverse,
                    Index = segments.Count + 1, FirstFrame = TotalFrames, FrameCount = frames };
                segments.Add(segment);
                TotalFrames += segment.FrameCount;
                DurationSeconds += duration;
            }
            if (SequentialPoseWriter.HasEnabledAssemblyCondition(Window.ActiveWindow == null ? null : Window.ActiveWindow.Document))
                sequentialWriter = new SequentialPoseWriter("SC Animator scenario", null);
            else applyLoop = new ScenarioApplyLoop(projects);
        }
        public override int Advance(int frame) {
            int target = Math.Max(0, Math.Min(TotalFrames - 1, frame));
            int stride = captureFrame == null ? Math.Max(1, (target - lastAppliedFrame + 119) / 120) : 1;
            for (int next = lastAppliedFrame + stride; next < target; next += stride) ApplyFrame(next);
            if (target > lastAppliedFrame) ApplyFrame(target);
            lastAppliedFrame = Math.Max(lastAppliedFrame, target);
            return TotalFrames;
        }
        private void ApplyFrame(int frame) {
            Segment segment = segments[segments.Count - 1];
            for (int i = 0; i < segments.Count; i++)
                if (frame < segments[i].FirstFrame + segments[i].FrameCount) { segment = segments[i]; break; }
            double progress = Math.Min(1, Math.Max(0,
                (frame - segment.FirstFrame) / (double)(segment.FrameCount - 1)));
            double time = segment.Reverse
                ? segment.Project.EffectiveRangeEnd - progress * (segment.Project.EffectiveRangeEnd - segment.Project.EffectiveRangeStart)
                : segment.Project.EffectiveRangeStart + progress * (segment.Project.EffectiveRangeEnd - segment.Project.EffectiveRangeStart);
            AnimationProject project = segment.Project;
            AnimationProject previous = previousProject != project ? previousProject : null;
            if (previous != null && sequentialWriter == null)
                previous.RestoreAppearanceStylesInWriteBlock();
            previousProject = project;
            Status = "Scenario: " + segment.Index + "/" + segments.Count +
                "  " + (segment.Reverse ? "← " : "→ ") + segment.Name;
            Matrix[] poses = project.EvaluateAt(time), planePoses = project.EvaluatePlanesAt(time);
            if (sequentialWriter != null)
                sequentialWriter.Apply(project, project.GetComponents(), project.GetPlanes(), poses,
                    planePoses, time, previous == null ? (Action)null : previous.RestoreAppearanceStylesInWriteBlock);
            else applyLoop.ApplySegment(project, poses, planePoses, time);
            Matrix? camera = project.CameraEnabled ? project.EvaluateCameraAt(time) : null;
            if (camera.HasValue && Window.ActiveWindow != null)
                Window.ActiveWindow.SetProjection(camera.Value, false, false);
            if (Window.ActiveWindow != null) Window.ActiveWindow.RefreshRendering();
            if (captureFrame != null) captureFrame(frame);
        }
        protected override void OnCompleted(AnimationCompletedEventArgs args) {
            if (args.Result == AnimationResult.Canceled) {
                if (sequentialWriter != null) {
                    if (sequentialWriter.HasPoseUndoStep) SpaceClaimApplication.Undo(1);
                } else applyLoop.Cancel();
                if (initialCamera.HasValue && Window.ActiveWindow != null)
                    Window.ActiveWindow.SetProjection(initialCamera.Value, false, false);
                UndoStepAdded = false;
            } else {
                if (args.Result == AnimationResult.Exhausted && lastAppliedFrame < TotalFrames - 1)
                    Advance(TotalFrames - 1);
                if (sequentialWriter != null) {
                    sequentialWriter.Append(delegate {
                        foreach (AnimationProject project in projects)
                            project.RestoreAppearanceStylesInWriteBlock();
                    });
                    UndoStepAdded = sequentialWriter.HasPoseUndoStep;
                } else UndoStepAdded = applyLoop.Complete();
                if (Window.ActiveWindow != null) Window.ActiveWindow.RefreshRendering();
            }
            if (sequentialWriter == null || args.Result == AnimationResult.Canceled)
                foreach (AnimationProject project in projects) project.RestoreAppearanceStylesInWriteBlock();
            base.OnCompleted(args);
        }
    }

    internal sealed class ScenarioApplyLoop : ApplyLoop {
        private AnimationProject project;
        private Matrix[] poses;
        private Matrix[] planePoses;
        private double time;
        internal ScenarioApplyLoop(AnimationProject[] projects) : base("SC Animator scenario") {
            foreach (AnimationProject clip in projects) {
                foreach (Component component in clip.GetComponents()) component.KeepAlive(true);
                foreach (DatumPlane plane in clip.GetPlanes()) plane.KeepAlive(true);
            }
        }
        internal void ApplySegment(AnimationProject clip, Matrix[] placements, Matrix[] planes, double seconds) {
            project = clip; poses = placements; planePoses = planes; time = seconds;
            Apply();
        }
        protected override bool OnApply() {
            Component[] components = project.GetComponents();
            DatumPlane[] planes = project.GetPlanes();
            for (int i = 0; i < components.Length; i++) components[i].Placement = poses[i];
            for (int i = 0; i < planes.Length; i++) AnimationProject.ApplyPlane(planes[i], planePoses[i]);
            project.ApplyVisibility(time, true);
            return true;
        }
    }
}

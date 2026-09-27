using System;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal sealed partial class AnimationProject {
        internal bool InsertPause(double at, double duration) {
            return CanEdit && CanUseCurrentAssembly && InsertPauseCore(at, duration);
        }

        internal bool InsertPauseCore(double at, double duration) {
            if (!FiniteTime(at) || at > DurationSeconds || Double.IsNaN(duration) ||
                Double.IsInfinity(duration) || duration <= 0 || !FiniteTime(DurationSeconds + duration)) return false;
            // Holding a pose adds keys on every track, so no locked track may be changed.
            for (int track = 0; track < TrackCount; track++) if (IsTrackLocked(track)) return false;
            Matrix[] componentPose = EvaluateAt(at);
            Matrix[] planePose = EvaluatePlanesAt(at);
            Matrix? cameraPose = EvaluateCameraAt(at);
            ProjectSnapshot draft = CreateSnapshot();
            ShiftAfter(draft, at, duration);
            AddHoldKeys(draft, at, duration, componentPose, planePose, cameraPose);
            if (!ValidTimes(draft)) return false;
            try { ProjectPersistence.Decode(ProjectPersistence.Encode(draft)); }
            catch { return false; }
            SaveUndoState();
            Restore(draft, true);
            playheadSeconds = at;
            CurrentKeyframeIndex = FindEvent(at);
            MarkDirty();
            return true;
        }

        private static bool FiniteTime(double value) {
            return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0 && value <= 3600;
        }

        internal static void ShiftAfter(ProjectSnapshot draft, double at, double duration) {
            foreach (KeyframeSnapshot frame in draft.Keyframes)
                if (frame.TimeSeconds > at) frame.TimeSeconds += duration;
            foreach (PlaneTrackSnapshot plane in draft.PlaneTracks)
                foreach (PlaneKeySnapshot key in plane.Keys)
                    if (key.Time > at) key.Time += duration;
            foreach (VisibilityKeySnapshot key in draft.VisibilityKeys)
                if (key.Time > at) key.Time += duration;
            foreach (TimelineMarker marker in draft.Markers)
                if (marker.Time > at) marker.Time += duration;
            if (draft.HasPlaybackRange) {
                if (draft.PlaybackRangeStart > at) draft.PlaybackRangeStart += duration;
                if (draft.PlaybackRangeEnd >= at) draft.PlaybackRangeEnd += duration;
            }
        }

        internal static void AddHoldKeys(ProjectSnapshot draft, double at, double duration,
            Matrix[] components, Matrix[] planes, Matrix? camera) {
            EnsureComponentHold(draft, at, components, camera);
            EnsureComponentHold(draft, at + duration, components, camera);
            for (int i = 0; i < draft.PlaneTracks.Count; i++) {
                EnsurePlaneHold(draft.PlaneTracks[i], at, planes[i]);
                EnsurePlaneHold(draft.PlaneTracks[i], at + duration, planes[i]);
            }
        }

        private static void EnsureComponentHold(ProjectSnapshot draft, double time,
            Matrix[] poses, Matrix? camera) {
            KeyframeSnapshot frame = draft.Keyframes.Find(item => Math.Abs(item.TimeSeconds - time) < 1e-7);
            if (frame == null) {
                frame = new KeyframeSnapshot { TimeSeconds = time, Placements = (Matrix[])poses.Clone(),
                    HasPoses = new bool[poses.Length], CameraProjection = camera,
                    PoseEasing = new EasingMode?[poses.Length] };
                for (int i = 0; i < poses.Length; i++) frame.HasPoses[i] = true;
                draft.Keyframes.Add(frame);
            } else {
                for (int i = 0; i < poses.Length; i++)
                    if (!frame.HasPoses[i]) { frame.Placements[i] = poses[i]; frame.HasPoses[i] = true; }
                if (!frame.CameraProjection.HasValue) frame.CameraProjection = camera;
            }
            draft.Keyframes.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
        }

        private static void EnsurePlaneHold(PlaneTrackSnapshot plane, double time, Matrix pose) {
            if (!plane.Keys.Exists(key => Math.Abs(key.Time - time) < 1e-7))
                plane.Keys.Add(new PlaneKeySnapshot { Time = time, Placement = pose });
            plane.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        private static bool ValidTimes(ProjectSnapshot draft) {
            double previous = -1;
            foreach (KeyframeSnapshot frame in draft.Keyframes) {
                if (!FiniteTime(frame.TimeSeconds) || frame.TimeSeconds - previous < 1e-6) return false;
                previous = frame.TimeSeconds;
            }
            foreach (PlaneTrackSnapshot plane in draft.PlaneTracks) {
                previous = -1;
                foreach (PlaneKeySnapshot key in plane.Keys) {
                    if (!FiniteTime(key.Time) || key.Time - previous < 1e-6) return false;
                    previous = key.Time;
                }
            }
            previous = -1;
            draft.Markers.Sort((a, b) => a.Time.CompareTo(b.Time));
            foreach (TimelineMarker marker in draft.Markers) {
                if (!FiniteTime(marker.Time) || marker.Time - previous < 1e-6) return false;
                previous = marker.Time;
            }
            return true;
        }
    }
}

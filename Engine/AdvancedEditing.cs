using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal sealed partial class AnimationProject {
        private readonly List<bool> lockedTracks = new List<bool>();
        private readonly Dictionary<TrackKeyRef, EasingMode> keyEasing = new Dictionary<TrackKeyRef, EasingMode>();
        private bool cameraEnabled = true;
        private bool hasPlaybackRange;
        private double playbackRangeStart, playbackRangeEnd;
        private Matrix[] initialPlacements;
        private Matrix? initialCamera;

        internal bool CameraEnabled {
            get { return cameraEnabled; }
            set { if (cameraEnabled != value) { cameraEnabled = value; MarkDirty(); } }
        }
        internal bool HasPlaybackRange { get { return hasPlaybackRange && EffectiveRangeEnd > EffectiveRangeStart; } }
        internal double EffectiveRangeStart {
            get { return hasPlaybackRange && playbackRangeStart < DurationSeconds ? playbackRangeStart : 0; }
        }
        internal double EffectiveRangeEnd {
            get {
                double end = hasPlaybackRange ? Math.Min(playbackRangeEnd, DurationSeconds) : DurationSeconds;
                return end > EffectiveRangeStart ? end : DurationSeconds;
            }
        }
        internal bool SetPlaybackIn(double time) {
            if (Double.IsNaN(time) || Double.IsInfinity(time) || time < 0 || time >= EffectiveRangeEnd) return false;
            hasPlaybackRange = true; playbackRangeStart = time;
            if (playbackRangeEnd <= time) playbackRangeEnd = DurationSeconds;
            MarkDirty(); return true;
        }
        internal bool SetPlaybackOut(double time) {
            if (Double.IsNaN(time) || Double.IsInfinity(time) || time <= EffectiveRangeStart || time > DurationSeconds) return false;
            if (!hasPlaybackRange) playbackRangeStart = 0;
            hasPlaybackRange = true; playbackRangeEnd = time;
            MarkDirty(); return true;
        }
        internal void ClearPlaybackRange() {
            if (!hasPlaybackRange) return;
            hasPlaybackRange = false; playbackRangeStart = playbackRangeEnd = 0;
            MarkDirty();
        }
        internal bool IsTrackLocked(int track) {
            return track >= 0 && track < lockedTracks.Count && lockedTracks[track];
        }
        internal bool SetTrackLocked(int track, bool locked) {
            if (track < 0 || track >= TrackCount) return false;
            EnsureLockCount();
            if (lockedTracks[track] == locked) return true;
            SaveUndoState();
            lockedTracks[track] = locked;
            MarkDirty(); return true;
        }
        private void EnsureLockCount() {
            while (lockedTracks.Count < TrackCount) lockedTracks.Add(false);
            while (lockedTracks.Count > TrackCount) lockedTracks.RemoveAt(lockedTracks.Count - 1);
        }
        internal EasingMode? GetKeyEasing(int track, double time) {
            EasingMode mode;
            return keyEasing.TryGetValue(new TrackKeyRef(track, time), out mode) ? (EasingMode?)mode : null;
        }
        internal bool SetKeyEasing(int track, double time, EasingMode? mode) {
            if (!HasTrackKey(track, time) || IsTrackLocked(track) ||
                mode.HasValue && !Enum.IsDefined(typeof(EasingMode), mode.Value)) return false;
            SaveUndoState();
            var key = new TrackKeyRef(track, time);
            if (mode.HasValue) keyEasing[key] = mode.Value;
            else keyEasing.Remove(key);
            MarkDirty(); return true;
        }
        private double EaseTrack(double t, int track, double leftTime) {
            return EaseMode(t, Easing);
        }
        private static double EaseMode(double t, EasingMode mode) {
            switch (mode) {
                case EasingMode.Smooth: return t * t * (3 - 2 * t);
                case EasingMode.EaseIn: return t * t;
                case EasingMode.EaseOut: return 1 - (1 - t) * (1 - t);
                default: return t;
            }
        }
        internal Matrix[] GetInitialPlacements() {
            return initialPlacements != null && initialPlacements.Length == components.Count
                ? (Matrix[])initialPlacements.Clone() : EvaluateAt(0);
        }
        internal Matrix? GetInitialCamera() { return initialCamera ?? EvaluateCameraAt(0); }
        internal double PlaybackTimeAtFrame(int frame) {
            return Math.Min(EffectiveRangeEnd, EffectiveRangeStart + frame * SpeedMultiplier / FramesPerSecond);
        }
        internal void SetInitialState(Matrix[] poses, Matrix? camera) {
            if (poses == null || poses.Length != components.Count) throw new ArgumentException("Invalid initial pose count.");
            initialPlacements = (Matrix[])poses.Clone(); initialCamera = camera;
            MarkDirty();
        }
    }
}

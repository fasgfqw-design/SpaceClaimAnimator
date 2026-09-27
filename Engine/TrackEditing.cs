using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal struct TrackKeyRef : IEquatable<TrackKeyRef> {
        public readonly int Track;
        public readonly double Time;
        public TrackKeyRef(int track, double time) { Track = track; Time = time; }
        public bool Equals(TrackKeyRef other) { return Track == other.Track && Time.Equals(other.Time); }
        public override bool Equals(object obj) { return obj is TrackKeyRef && Equals((TrackKeyRef)obj); }
        public override int GetHashCode() { return Track * 397 ^ Time.GetHashCode(); }
    }

    internal sealed class TrackKeyClipboard {
        internal readonly AnimationProject Source;
        internal readonly int Generation;
        internal readonly TrackKeyValue[] Keys;
        internal readonly double FirstTime;
        internal TrackKeyClipboard(AnimationProject source, int generation, TrackKeyValue[] keys, double firstTime) {
            Source = source; Generation = generation; Keys = keys; FirstTime = firstTime;
        }
    }

    internal struct TrackKeyValue {
        internal TrackKeyRef Source;
        internal Matrix Pose;
        internal Matrix? Camera;
        internal Matrix? Plane;
        internal VisibilityMode Visibility;
        internal EasingMode? Easing;
    }

    internal sealed partial class AnimationProject {
        private sealed class EditState {
            internal List<Keyframe> Keys;
            internal int CurrentIndex;
            internal int SelectedTrack;
            internal double Playhead;
            internal Dictionary<TrackKeyRef, EasingMode> Easing;
            internal bool[] Locks;
            internal AnimationLibrarySnapshot Library;
            internal Component[] Components;
            internal List<PlaneTrack> Planes;
            internal Dictionary<TrackKeyRef, VisibilityMode> Visibility;
            internal List<TimelineMarker> Markers;
            internal HashSet<string> Favorites;
            internal bool ReversedVisibility;
            internal bool HasPlaybackRange;
            internal double PlaybackRangeStart, PlaybackRangeEnd;
        }
        private readonly List<EditState> undoEdits = new List<EditState>();
        private readonly List<EditState> redoEdits = new List<EditState>();
        private int editGeneration;
        internal int EditGeneration { get { return editGeneration; } }
        private const int HistoryLimit = 50;
        public bool CanUndoTrackEdit { get { return undoEdits.Count > 0 && document != null &&
            SpaceClaim.Api.V261.Window.ActiveWindow != null && SpaceClaim.Api.V261.Window.ActiveWindow.Document == document &&
            !SpaceClaim.Api.V261.Animation.IsAnimating; } }
        public bool CanRedoTrackEdit { get { return redoEdits.Count > 0 && document != null &&
            SpaceClaim.Api.V261.Window.ActiveWindow != null && SpaceClaim.Api.V261.Window.ActiveWindow.Document == document &&
            !SpaceClaim.Api.V261.Animation.IsAnimating; } }
        private EditState CaptureEditState() {
            // Keyframe objects are immutable; copying the ordered list is sufficient.
            return new EditState { Keys = new List<Keyframe>(keyframes), CurrentIndex = CurrentKeyframeIndex,
                SelectedTrack = selectedTrackIndex, Playhead = playheadSeconds,
                Easing = new Dictionary<TrackKeyRef, EasingMode>(keyEasing), Locks = lockedTracks.ToArray(),
                Planes = ClonePlaneTracks(), Visibility = new Dictionary<TrackKeyRef, VisibilityMode>(visibilityKeys),
                Markers = timelineMarkers.ConvertAll(marker => marker.Clone()),
                Favorites = new HashSet<string>(favoriteTracks, StringComparer.Ordinal),
                ReversedVisibility = reversedVisibility,
                HasPlaybackRange = hasPlaybackRange,
                PlaybackRangeStart = playbackRangeStart, PlaybackRangeEnd = playbackRangeEnd };
        }
        private List<PlaneTrack> ClonePlaneTracks() {
            var result = new List<PlaneTrack>();
            foreach (PlaneTrack plane in planeTracks) result.Add(plane.Clone());
            return result;
        }
        private void RestoreEditState(EditState state) {
            RestoreAppearancesInWriteBlock();
            keyframes.Clear();
            keyframes.AddRange(state.Keys);
            CurrentKeyframeIndex = state.CurrentIndex;
            selectedTrackIndex = state.SelectedTrack;
            playheadSeconds = state.Playhead;
            keyEasing.Clear(); foreach (var pair in state.Easing) keyEasing.Add(pair.Key, pair.Value);
            lockedTracks.Clear(); lockedTracks.AddRange(state.Locks);
            planeTracks.Clear(); foreach (PlaneTrack plane in state.Planes) planeTracks.Add(plane.Clone());
            visibilityKeys.Clear(); foreach (var visible in state.Visibility) visibilityKeys.Add(visible.Key, visible.Value);
            reversedVisibility = state.ReversedVisibility;
            hasPlaybackRange = state.HasPlaybackRange;
            playbackRangeStart = state.PlaybackRangeStart;
            playbackRangeEnd = state.PlaybackRangeEnd;
            timelineMarkers.Clear(); foreach (TimelineMarker marker in state.Markers) timelineMarkers.Add(marker.Clone());
            favoriteTracks.Clear(); foreach (string id in state.Favorites) favoriteTracks.Add(id);
            if (state.Library != null) {
                animations.Clear();
                foreach (NamedAnimationSnapshot clip in state.Library.Animations)
                    animations.Add(new NamedAnimationSnapshot { Name = clip.Name, Project = clip.Project });
                activeAnimationIndex = state.Library.ActiveIndex;
                scenario = state.Library.Scenario.Clone();
                components.Clear(); components.AddRange(state.Components);
                componentMonikers.Clear();
                componentMonikers.AddRange(state.Library.Animations[activeAnimationIndex].Project.Monikers);
                ProjectSnapshot project = state.Library.Animations[activeAnimationIndex].Project;
                framesPerSecond = project.FramesPerSecond; speedMultiplier = project.SpeedMultiplier;
                fadeDurationSeconds = project.FadeDurationSeconds;
                SecondsPerSegment = project.SecondsPerSegment; easing = project.Easing; loop = project.Loop;
                cameraEnabled = project.CameraEnabled;
                reversedVisibility = project.ReversedVisibility;
                hasPlaybackRange = project.HasPlaybackRange;
                playbackRangeStart = project.PlaybackRangeStart; playbackRangeEnd = project.PlaybackRangeEnd;
                initialPlacements = project.InitialPlacements == null ? null : (Matrix[])project.InitialPlacements.Clone();
                initialCamera = project.InitialCamera;
                editGeneration++;
                lastLivenessCheck = 0;
            }
            MarkDirty();
        }
        private static void PushHistory(List<EditState> history, EditState state) {
            if (history.Count == HistoryLimit) history.RemoveAt(0);
            history.Add(state);
        }
        private void SaveUndoState() {
            PushHistory(undoEdits, CaptureEditState());
            redoEdits.Clear();
        }
        private EditState CaptureStructuralState() {
            EditState state = CaptureEditState();
            state.Library = CreateLibrarySnapshot();
            state.Components = components.ToArray();
            return state;
        }
        private void SaveStructuralUndoState() {
            PushHistory(undoEdits, CaptureStructuralState());
            redoEdits.Clear();
        }
        private void ClearEditHistory() { undoEdits.Clear(); redoEdits.Clear(); editGeneration++; }
        public bool UndoTrackEdit() { return CanUndoTrackEdit && UndoTrackEditCore(); }
        public bool RedoTrackEdit() { return CanRedoTrackEdit && RedoTrackEditCore(); }
        internal bool UndoTrackEditCore() {
            if (undoEdits.Count == 0) return false;
            EditState state = undoEdits[undoEdits.Count - 1];
            PushHistory(redoEdits, state.Library == null ? CaptureEditState() : CaptureStructuralState());
            undoEdits.RemoveAt(undoEdits.Count - 1);
            RestoreEditState(state);
            return true;
        }
        internal bool RedoTrackEditCore() {
            if (redoEdits.Count == 0) return false;
            EditState state = redoEdits[redoEdits.Count - 1];
            PushHistory(undoEdits, state.Library == null ? CaptureEditState() : CaptureStructuralState());
            redoEdits.RemoveAt(redoEdits.Count - 1);
            RestoreEditState(state);
            return true;
        }

        private List<TrackKeyRef> ValidateSelection(IEnumerable<TrackKeyRef> selection, bool requireUnlocked = true) {
            if (selection == null) return null;
            var unique = new HashSet<TrackKeyRef>();
            foreach (TrackKeyRef key in selection)
                if (!HasTrackKey(key.Track, key.Time) || requireUnlocked && IsTrackLocked(key.Track) || !unique.Add(key)) return null;
            if (unique.Count == 0) return null;
            var keys = new List<TrackKeyRef>(unique);
            keys.Sort((a, b) => { int byTime = a.Time.CompareTo(b.Time); return byTime != 0 ? byTime : a.Track.CompareTo(b.Track); });
            return keys;
        }
        private TrackKeyValue[] ReadValues(IList<TrackKeyRef> keys) {
            var values = new TrackKeyValue[keys.Count];
            for (int i = 0; i < keys.Count; i++) {
                TrackKeyRef key = keys[i];
                if (IsPlaneTrack(key.Track)) {
                    values[i] = new TrackKeyValue { Source = key,
                        Plane = planeTracks[key.Track - components.Count].Keys[key.Time],
                        Visibility = GetKeyVisibility(key.Track, key.Time) };
                    continue;
                }
                Keyframe frame = keyframes[FindEvent(key.Time)];
                values[i] = new TrackKeyValue { Source = key,
                    Pose = key.Track < components.Count ? frame[key.Track] : Matrix.Identity,
                    Camera = key.Track == CameraTrackIndex ? frame.CameraProjection : null,
                    Visibility = GetKeyVisibility(key.Track, key.Time),
                    Easing = GetKeyEasing(key.Track, key.Time) };
            }
            return values;
        }
        private void RemoveValues(IList<TrackKeyRef> keys) {
            foreach (TrackKeyRef key in keys) {
                visibilityKeys.Remove(key);
                if (IsPlaneTrack(key.Track)) { planeTracks[key.Track - components.Count].Keys.Remove(key.Time); continue; }
                int index = FindEvent(key.Time);
                Keyframe frame = keyframes[index];
                keyframes[index] = key.Track == CameraTrackIndex
                    ? frame.WithCamera(null) : frame.WithoutPose(key.Track);
                keyEasing.Remove(key);
            }
            keyframes.RemoveAll(frame => frame.IsEmpty);
        }
        private void InsertValue(TrackKeyValue value, double time) {
            if (IsPlaneTrack(value.Source.Track)) {
                planeTracks[value.Source.Track - components.Count].Keys.Add(time, value.Plane.Value);
                if (value.Visibility != VisibilityMode.Default)
                    visibilityKeys[new TrackKeyRef(value.Source.Track, time)] = value.Visibility;
                return;
            }
            int index = FindEvent(time);
            if (index < 0) {
                Keyframe created = new Keyframe(new Matrix[components.Count], new bool[components.Count], null, time);
                index = keyframes.FindIndex(frame => frame.TimeSeconds > time);
                if (index < 0) index = keyframes.Count;
                keyframes.Insert(index, created);
            }
            keyframes[index] = value.Source.Track == CameraTrackIndex
                ? keyframes[index].WithCamera(value.Camera)
                : keyframes[index].WithPose(value.Source.Track, value.Pose);
            if (value.Easing.HasValue) keyEasing[new TrackKeyRef(value.Source.Track, time)] = value.Easing.Value;
            if (value.Visibility != VisibilityMode.Default)
                visibilityKeys[new TrackKeyRef(value.Source.Track, time)] = value.Visibility;
        }
        private static bool InRange(double time) {
            return !Double.IsNaN(time) && !Double.IsInfinity(time) && time >= 0 && time <= 3600;
        }
        public bool MoveTrackKeys(IEnumerable<TrackKeyRef> selection, double delta) {
            return CanEdit && CanUseCurrentAssembly && MoveTrackKeysCore(selection, delta);
        }
        internal bool MoveTrackKeysCore(IEnumerable<TrackKeyRef> selection, double delta) {
            List<TrackKeyRef> keys = ValidateSelection(selection);
            if (keys == null || Double.IsNaN(delta) || Double.IsInfinity(delta) || Math.Abs(delta) < 1e-6)
                return false;
            var selected = new HashSet<TrackKeyRef>(keys);
            foreach (TrackKeyRef key in keys) {
                double target = key.Time + delta;
                if (!InRange(target) || HasTrackKey(key.Track, target) &&
                    !ContainsTime(selected, key.Track, target)) return false;
            }
            TrackKeyValue[] values = ReadValues(keys);
            SaveUndoState();
            RemoveValues(keys);
            foreach (TrackKeyValue value in values) InsertValue(value, value.Source.Time + delta);
            selectedTrackIndex = keys[0].Track;
            playheadSeconds = keys[0].Time + delta;
            CurrentKeyframeIndex = FindEvent(playheadSeconds);
            MarkDirty();
            return true;
        }
        private static bool ContainsTime(HashSet<TrackKeyRef> selected, int track, double time) {
            foreach (TrackKeyRef key in selected)
                if (key.Track == track && Math.Abs(key.Time - time) < 1e-6) return true;
            return false;
        }
        public bool DeleteTrackKeys(IEnumerable<TrackKeyRef> selection) {
            return CanEdit && CanUseCurrentAssembly && DeleteTrackKeysCore(selection);
        }
        internal bool DeleteTrackKeysCore(IEnumerable<TrackKeyRef> selection) {
            List<TrackKeyRef> keys = ValidateSelection(selection);
            if (keys == null) return false;
            for (int track = 0; track < components.Count; track++) {
                int deleting = 0;
                foreach (TrackKeyRef key in keys) if (key.Track == track) deleting++;
                if (deleting > 0 && deleting >= GetTrackTimes(track).Length) return false;
            }
            for (int track = components.Count; track < CameraTrackIndex; track++) {
                int deleting = 0;
                foreach (TrackKeyRef key in keys) if (key.Track == track) deleting++;
                if (deleting > 0 && deleting >= GetTrackTimes(track).Length) return false;
            }
            SaveUndoState();
            RemoveValues(keys);
            selectedTrackIndex = keys[0].Track;
            playheadSeconds = keys[0].Time;
            CurrentKeyframeIndex = FindEvent(playheadSeconds);
            MarkDirty();
            return true;
        }
        public TrackKeyClipboard CopyTrackKeys(IEnumerable<TrackKeyRef> selection) {
            List<TrackKeyRef> keys = ValidateSelection(selection, false);
            if (keys == null) return null;
            double first = Double.MaxValue;
            foreach (TrackKeyRef key in keys) first = Math.Min(first, key.Time);
            return new TrackKeyClipboard(this, editGeneration, ReadValues(keys), first);
        }
        public bool PasteTrackKeys(TrackKeyClipboard clipboard, double time, out TrackKeyRef[] pasted) {
            pasted = null;
            return CanEdit && CanUseCurrentAssembly && PasteTrackKeysCore(clipboard, time, out pasted);
        }
        internal bool PasteTrackKeysCore(TrackKeyClipboard clipboard, double time, out TrackKeyRef[] pasted) {
            pasted = null;
            if (clipboard == null || !Object.ReferenceEquals(clipboard.Source, this) ||
                clipboard.Generation != editGeneration || !InRange(time)) return false;
            var targets = new TrackKeyRef[clipboard.Keys.Length];
            for (int i = 0; i < targets.Length; i++) {
                TrackKeyValue value = clipboard.Keys[i];
                double target = time + value.Source.Time - clipboard.FirstTime;
                if (value.Source.Track < 0 || value.Source.Track >= TrackCount || IsTrackLocked(value.Source.Track) ||
                    !InRange(target) || HasTrackKey(value.Source.Track, target)) return false;
                targets[i] = new TrackKeyRef(value.Source.Track, target);
            }
            SaveUndoState();
            for (int i = 0; i < targets.Length; i++) InsertValue(clipboard.Keys[i], targets[i].Time);
            selectedTrackIndex = targets[0].Track;
            playheadSeconds = time;
            CurrentKeyframeIndex = FindEvent(playheadSeconds);
            MarkDirty();
            pasted = targets;
            return true;
        }
    }
}

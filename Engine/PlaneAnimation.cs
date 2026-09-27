using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal sealed class PlaneTrack {
        internal DatumPlane Plane;
        internal string Moniker;
        internal readonly SortedDictionary<double, Matrix> Keys = new SortedDictionary<double, Matrix>();
        internal PlaneTrack Clone() {
            var copy = new PlaneTrack { Plane = Plane, Moniker = Moniker };
            foreach (var key in Keys) copy.Keys.Add(key.Key, key.Value);
            return copy;
        }
    }

    internal sealed partial class AnimationProject {
        private readonly List<PlaneTrack> planeTracks = new List<PlaneTrack>();
        internal int PlaneCount { get { return planeTracks.Count; } }
        internal int CameraTrackIndex { get { return components.Count + planeTracks.Count; } }
        internal bool IsPlaneTrack(int track) { return track >= components.Count && track < CameraTrackIndex; }
        internal DatumPlane[] GetPlanes() {
            var result = new DatumPlane[planeTracks.Count];
            for (int i = 0; i < result.Length; i++) result[i] = planeTracks[i].Plane;
            return result;
        }
        internal Matrix[] EvaluatePlanesAt(double time) {
            var result = new Matrix[planeTracks.Count];
            for (int i = 0; i < result.Length; i++) {
                PlaneTrack track = planeTracks[i];
                KeyValuePair<double, Matrix>? left = null, right = null;
                foreach (var key in track.Keys) {
                    if (key.Key <= time) left = key;
                    if (key.Key >= time) { right = key; break; }
                }
                if (!left.HasValue) left = right;
                if (!right.HasValue) right = left;
                if (!left.HasValue) result[i] = PlanePlacement(track.Plane);
                else if (left.Value.Key == right.Value.Key) result[i] = left.Value.Value;
                else {
                    double t = EaseTrack((time - left.Value.Key) / (right.Value.Key - left.Value.Key),
                        components.Count + i, left.Value.Key);
                    result[i] = PlacementInterpolator.Interpolate(left.Value.Value, right.Value.Value, t);
                }
            }
            return result;
        }
        internal static Matrix PlanePlacement(DatumPlane plane) {
            return Matrix.CreateMapping(plane.AnnotationPlane.Frame);
        }
        internal static void ApplyPlane(DatumPlane plane, Matrix target) {
            Matrix current = PlanePlacement(plane);
            plane.Transform(target * current.Inverse);
        }
        internal bool AddPlaneTrackCore(DatumPlane plane, string moniker, Matrix placement, double time,
            bool saveUndo = true) {
            if (String.IsNullOrEmpty(moniker) || keyframes.Count == 0 || time < 0 || time > 3600 ||
                Double.IsNaN(time) || Double.IsInfinity(time)) return false;
            foreach (PlaneTrack existing in planeTracks)
                if (String.Equals(existing.Moniker, moniker, StringComparison.Ordinal)) return false;
            if (saveUndo) SaveStructuralUndoState();
            var added = new PlaneTrack { Plane = plane, Moniker = moniker };
            added.Keys.Add(time, placement);
            int insert = CameraTrackIndex;
            planeTracks.Add(added);
            lockedTracks.Insert(insert, false);
            ShiftVisibilityTracks(insert, 1);
            EnsureLockCount();
            selectedTrackIndex = insert;
            playheadSeconds = time;
            editGeneration++;
            MarkDirty();
            return true;
        }
        internal bool RemovePlaneTrack(int track) {
            if (!CanEdit || !CanUseCurrentAssembly || !IsPlaneTrack(track) || IsTrackLocked(track)) return false;
            return RemovePlaneTrackCore(track);
        }
        internal bool RemovePlaneTrackCore(int track) {
            if (!IsPlaneTrack(track) || IsTrackLocked(track)) return false;
            RestoreAppearancesInWriteBlock();
            SaveStructuralUndoState();
            planeTracks.RemoveAt(track - components.Count);
            lockedTracks.RemoveAt(track);
            RemoveVisibilityTrack(track);
            EnsureLockCount();
            selectedTrackIndex = Math.Min(track, CameraTrackIndex);
            editGeneration++;
            MarkDirty();
            return true;
        }
        internal bool AddPlaneKey(int track, double time, bool replace) {
            if (!IsPlaneTrack(track) || !ValidTrackEdit(track, time)) return false;
            PlaneTrack plane = planeTracks[track - components.Count];
            if (plane.Keys.ContainsKey(time) != replace) return false;
            SaveUndoState();
            plane.Keys[time] = PlanePlacement(plane.Plane);
            selectedTrackIndex = track; playheadSeconds = time;
            MarkDirty(); return true;
        }
        internal bool DeletePlaneKey(int track, double time) {
            if (!IsPlaneTrack(track) || !ValidTrackEdit(track, time)) return false;
            PlaneTrack plane = planeTracks[track - components.Count];
            if (!plane.Keys.ContainsKey(time) || plane.Keys.Count <= 1) return false;
            SaveUndoState(); plane.Keys.Remove(time);
            visibilityKeys.Remove(new TrackKeyRef(track, time));
            selectedTrackIndex = track; playheadSeconds = time;
            MarkDirty(); return true;
        }
        private void ShiftVisibilityTracks(int from, int delta) {
            var shifted = new Dictionary<TrackKeyRef, VisibilityMode>();
            foreach (var item in visibilityKeys) {
                int track = item.Key.Track >= from ? item.Key.Track + delta : item.Key.Track;
                shifted[new TrackKeyRef(track, item.Key.Time)] = item.Value;
            }
            visibilityKeys.Clear();
            foreach (var item in shifted) visibilityKeys.Add(item.Key, item.Value);
        }
        private void RemoveVisibilityTrack(int removed) {
            var shifted = new Dictionary<TrackKeyRef, VisibilityMode>();
            foreach (var item in visibilityKeys) {
                if (item.Key.Track == removed) continue;
                int track = item.Key.Track > removed ? item.Key.Track - 1 : item.Key.Track;
                shifted[new TrackKeyRef(track, item.Key.Time)] = item.Value;
            }
            visibilityKeys.Clear();
            foreach (var item in shifted) visibilityKeys.Add(item.Key, item.Value);
        }
    }
}

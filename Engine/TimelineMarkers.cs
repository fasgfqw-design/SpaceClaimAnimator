using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261;

namespace SCAnimator.V261.Engine {
    internal sealed class TimelineMarker {
        internal string Name;
        internal double Time;
        internal TimelineMarker Clone() { return new TimelineMarker { Name = Name, Time = Time }; }
    }

    internal sealed partial class AnimationProject {
        private readonly List<TimelineMarker> timelineMarkers = new List<TimelineMarker>();
        private readonly HashSet<string> favoriteTracks = new HashSet<string>(StringComparer.Ordinal);

        internal TimelineMarker[] TimelineMarkers {
            get { return timelineMarkers.ConvertAll(marker => marker.Clone()).ToArray(); }
        }
        internal double LastMarkerTime {
            get { return timelineMarkers.Count == 0 ? 0 : timelineMarkers[timelineMarkers.Count - 1].Time; }
        }
        private static bool ValidMarkerName(string name) {
            return !String.IsNullOrWhiteSpace(name) && name == name.Trim() && name.Length <= 80;
        }
        private bool MarkerNameAvailable(string name, int except) {
            if (!ValidMarkerName(name)) return false;
            for (int i = 0; i < timelineMarkers.Count; i++)
                if (i != except && String.Equals(timelineMarkers[i].Name, name,
                    StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        internal bool AddMarker(string name, double time) {
            return CanEdit && CanUseCurrentAssembly && AddMarkerCore(name, time);
        }
        internal bool AddMarkerCore(string name, double time) {
            if (timelineMarkers.Count >= 200 || !MarkerNameAvailable(name, -1) ||
                Double.IsNaN(time) || Double.IsInfinity(time) || time < 0 || time > 3600 ||
                timelineMarkers.Exists(marker => Math.Abs(marker.Time - time) < 1e-6)) return false;
            SaveUndoState();
            timelineMarkers.Add(new TimelineMarker { Name = name, Time = time });
            timelineMarkers.Sort((a, b) => a.Time.CompareTo(b.Time));
            MarkDirty();
            return true;
        }
        internal bool RenameMarker(int index, string name) {
            return CanEdit && CanUseCurrentAssembly && RenameMarkerCore(index, name);
        }
        internal bool RenameMarkerCore(int index, string name) {
            if (index < 0 || index >= timelineMarkers.Count || !MarkerNameAvailable(name, index)) return false;
            if (timelineMarkers[index].Name == name) return true;
            SaveUndoState(); timelineMarkers[index].Name = name; MarkDirty(); return true;
        }
        internal bool MoveMarker(int index, double time) {
            return CanEdit && CanUseCurrentAssembly && MoveMarkerCore(index, time);
        }
        internal bool MoveMarkerCore(int index, double time) {
            if (index < 0 || index >= timelineMarkers.Count || Double.IsNaN(time) ||
                Double.IsInfinity(time) || time < 0 || time > 3600) return false;
            if (Math.Abs(timelineMarkers[index].Time - time) < 1e-6) return true;
            for (int i = 0; i < timelineMarkers.Count; i++)
                if (i != index && Math.Abs(timelineMarkers[i].Time - time) < 1e-6) return false;
            SaveUndoState(); timelineMarkers[index].Time = time;
            timelineMarkers.Sort((a, b) => a.Time.CompareTo(b.Time));
            MarkDirty(); return true;
        }
        internal bool DeleteMarker(int index) {
            return CanEdit && CanUseCurrentAssembly && DeleteMarkerCore(index);
        }
        internal bool DeleteMarkerCore(int index) {
            if (index < 0 || index >= timelineMarkers.Count) return false;
            SaveUndoState(); timelineMarkers.RemoveAt(index); MarkDirty(); return true;
        }
        internal string GetTrackIdentifier(int track) {
            if (track == CameraTrackIndex) return "Camera";
            if (track >= 0 && track < components.Count) return "C:" + componentMonikers[track];
            if (IsPlaneTrack(track)) return "P:" + planeTracks[track - components.Count].Moniker;
            return null;
        }
        internal IDocObject GetTrackMaster(int track) {
            if (track >= 0 && track < components.Count) return components[track];
            if (IsPlaneTrack(track)) return planeTracks[track - components.Count].Plane;
            return null;
        }
        internal bool IsFavoriteTrack(int track) {
            string id = GetTrackIdentifier(track);
            return id != null && favoriteTracks.Contains(id);
        }
        internal bool ToggleFavoriteTrack(int track) {
            if (!CanEdit || !CanUseCurrentAssembly) return false;
            return ToggleFavoriteTrackCore(track);
        }
        internal bool ToggleFavoriteTrackCore(int track) {
            string id = GetTrackIdentifier(track);
            if (id == null) return false;
            SaveUndoState();
            if (!favoriteTracks.Add(id)) favoriteTracks.Remove(id);
            MarkDirty(); return true;
        }
    }
}

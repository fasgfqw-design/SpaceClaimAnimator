using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal sealed partial class AnimationProject {
        private readonly List<NamedAnimationSnapshot> animations = new List<NamedAnimationSnapshot>();
        private int activeAnimationIndex;
        internal int AnimationCount { get { return animations.Count; } }
        internal int ActiveAnimationIndex { get { return activeAnimationIndex; } }
        internal string[] AnimationNames {
            get {
                var names = new string[animations.Count];
                for (int i = 0; i < names.Length; i++) names[i] = animations[i].Name;
                return names;
            }
        }
        private void InitializeAnimationLibrary() {
            animations.Add(new NamedAnimationSnapshot { Name = "Animation 1", Project = CreateSnapshot() });
        }
        private AnimationLibrarySnapshot CreateLibrarySnapshot() {
            var library = new AnimationLibrarySnapshot { ActiveIndex = activeAnimationIndex,
                Scenario = scenario.Clone() };
            for (int i = 0; i < animations.Count; i++)
                library.Animations.Add(new NamedAnimationSnapshot { Name = animations[i].Name,
                    Project = i == activeAnimationIndex ? CreateSnapshot() : animations[i].Project });
            return library;
        }
        private void RestoreLibrary(AnimationLibrarySnapshot library) {
            if (library == null || library.Animations.Count == 0 ||
                library.ActiveIndex < 0 || library.ActiveIndex >= library.Animations.Count)
                throw new ArgumentException("Invalid animation list.");
            Restore(library.Animations[library.ActiveIndex].Project);
            animations.Clear();
            animations.AddRange(library.Animations);
            activeAnimationIndex = library.ActiveIndex;
            scenario = library.Scenario.Clone();
        }
        internal bool SwitchAnimation(int index) {
            if (Animation.IsAnimating || document == null || Window.ActiveWindow == null ||
                Window.ActiveWindow.Document != document || index < 0 || index >= animations.Count) return false;
            if (index == activeAnimationIndex) return true;
            ProjectSnapshot current = CreateSnapshot();
            Restore(animations[index].Project); // resolves every component before replacing live state
            animations[activeAnimationIndex].Project = current;
            activeAnimationIndex = index;
            MarkDirty();
            return true;
        }
        internal bool CreateAnimation(string name) {
            if (Animation.IsAnimating || document == null || Window.ActiveWindow == null ||
                Window.ActiveWindow.Document != document || animations.Count >= 100 ||
                !NameAvailable(name, -1)) return false;
            var blank = new ProjectSnapshot { FramesPerSecond = FramesPerSecond,
                FadeDurationSeconds = FadeDurationSeconds,
                SpeedMultiplier = SpeedMultiplier, SecondsPerSegment = SecondsPerSegment,
                Easing = Easing, Loop = Loop, CameraEnabled = CameraEnabled };
            ProjectSnapshot current = CreateSnapshot();
            Restore(blank);
            animations[activeAnimationIndex].Project = current;
            animations.Add(new NamedAnimationSnapshot { Name = name, Project = blank });
            activeAnimationIndex = animations.Count - 1;
            MarkDirty();
            return true;
        }
        internal bool DuplicateAnimation(string name, bool reverseMotion) {
            if (Animation.IsAnimating || document == null || Window.ActiveWindow == null ||
                Window.ActiveWindow.Document != document || animations.Count >= 100 ||
                !NameAvailable(name, -1)) return false;
            ProjectSnapshot current = CreateSnapshot();
            ProjectSnapshot copy = ProjectPersistence.Decode(ProjectPersistence.Encode(current));
            if (reverseMotion) ReverseSnapshot(copy, Math.Max(DurationSeconds, LastMarkerTime),
                components.Count > 0 ? EvaluateAt(DurationSeconds) : null,
                EvaluateCameraAt(DurationSeconds));
            SaveStructuralUndoState();
            Restore(copy, true);
            animations[activeAnimationIndex].Project = current;
            animations.Add(new NamedAnimationSnapshot { Name = name, Project = copy });
            activeAnimationIndex = animations.Count - 1;
            MarkDirty();
            return true;
        }
        internal static void ReverseSnapshot(ProjectSnapshot snapshot, double duration,
            Matrix[] endingPlacements, Matrix? endingCamera) {
            if (snapshot == null || duration < 0 || duration > 3600)
                throw new ArgumentException("Invalid reverse animation duration.");
            foreach (KeyframeSnapshot frame in snapshot.Keyframes) frame.TimeSeconds = duration - frame.TimeSeconds;
            snapshot.Keyframes.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));
            foreach (PlaneTrackSnapshot plane in snapshot.PlaneTracks) {
                foreach (PlaneKeySnapshot key in plane.Keys) key.Time = duration - key.Time;
                plane.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
            foreach (TimelineMarker marker in snapshot.Markers) marker.Time = duration - marker.Time;
            snapshot.Markers.Sort((a, b) => a.Time.CompareTo(b.Time));
            if (snapshot.VisibilityKeys.Count > 0) {
                foreach (VisibilityKeySnapshot key in snapshot.VisibilityKeys) key.Time = duration - key.Time;
                snapshot.ReversedVisibility = !snapshot.ReversedVisibility;
            }
            if (snapshot.HasPlaybackRange) {
                double start = snapshot.PlaybackRangeStart;
                snapshot.PlaybackRangeStart = duration - snapshot.PlaybackRangeEnd;
                snapshot.PlaybackRangeEnd = duration - start;
            }
            if (snapshot.Easing == EasingMode.EaseIn) snapshot.Easing = EasingMode.EaseOut;
            else if (snapshot.Easing == EasingMode.EaseOut) snapshot.Easing = EasingMode.EaseIn;
            if (endingPlacements != null) snapshot.InitialPlacements = (Matrix[])endingPlacements.Clone();
            if (endingCamera.HasValue) snapshot.InitialCamera = endingCamera;
            snapshot.CurrentKeyframeIndex = snapshot.Keyframes.Count == 0 ? -1 : 0;
        }
        internal bool RenameAnimation(string name) {
            if (Animation.IsAnimating || document == null || Window.ActiveWindow == null ||
                Window.ActiveWindow.Document != document || !NameAvailable(name, activeAnimationIndex)) return false;
            string previous = animations[activeAnimationIndex].Name;
            SaveStructuralUndoState();
            animations[activeAnimationIndex].Name = name;
            for (int i = 0; i < scenario.AnimationNames.Count; i++)
                if (String.Equals(scenario.AnimationNames[i], previous, StringComparison.OrdinalIgnoreCase))
                    scenario.AnimationNames[i] = name;
            MarkDirty();
            return true;
        }
        internal bool DeleteActiveAnimation() {
            return !Animation.IsAnimating && document != null && Window.ActiveWindow != null &&
                Window.ActiveWindow.Document == document && DeleteAnimationCore(activeAnimationIndex);
        }
        internal bool DeleteAnimationCore(int index) {
            if (index < 0 || index >= animations.Count) return false;
            SaveStructuralUndoState();
            string deletedName = animations[index].Name;
            scenario.AnimationNames.RemoveAll(name => String.Equals(name, deletedName, StringComparison.OrdinalIgnoreCase));
            if (animations.Count == 1) {
                // Keep one empty slot ready for Add components.
                var blank = new ProjectSnapshot { FramesPerSecond = FramesPerSecond,
                    FadeDurationSeconds = FadeDurationSeconds,
                    SpeedMultiplier = SpeedMultiplier, SecondsPerSegment = SecondsPerSegment,
                    Easing = Easing, Loop = Loop, CameraEnabled = CameraEnabled };
                Restore(blank, true);
                animations[0] = new NamedAnimationSnapshot { Name = "Animation 1", Project = blank };
                activeAnimationIndex = 0;
            } else if (index == activeAnimationIndex) {
                int next = index + 1 < animations.Count ? index + 1 : index - 1;
                Restore(animations[next].Project, true);
                animations.RemoveAt(index);
                activeAnimationIndex = next > index ? next - 1 : next;
            } else {
                animations.RemoveAt(index);
                if (index < activeAnimationIndex) activeAnimationIndex--;
            }
            MarkDirty();
            return true;
        }
        private bool NameAvailable(string name, int except) {
            if (!ProjectPersistence.ValidAnimationName(name)) return false;
            for (int i = 0; i < animations.Count; i++)
                if (i != except && String.Equals(name, animations[i].Name, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        internal bool AddSelectedComponents() {
            Window window = Window.ActiveWindow;
            if (window == null || document == null || window.Document != document ||
                !document.IsComplete || Animation.IsAnimating ||
                keyframes.Count > 0 && (!CanEdit || !CanUseCurrentAssembly)) return false;
            List<ComponentBinding> available = CollectComponents(document);
            var selected = new HashSet<Component>();
            foreach (IComponent occurrence in window.ActiveContext.GetSelection<IComponent>())
                if (!occurrence.Master.IsDeleted) selected.Add(occurrence.Master);
            var selectedPlanes = new List<IDatumPlane>();
            foreach (IDatumPlane occurrence in window.ActiveContext.GetSelection<IDatumPlane>())
                if (!occurrence.Master.IsDeleted && !occurrence.Master.IsLocked && !occurrence.Master.IsReference)
                    selectedPlanes.Add(occurrence);
            var recorded = new HashSet<Component>(components);
            bool planeOnly = selectedPlanes.Count > 0 && selected.Count == 0;
            available.RemoveAll(binding => recorded.Contains(binding.Master) ||
                (selected.Count > 0 || planeOnly && keyframes.Count > 0) && !selected.Contains(binding.Master));
            selectedPlanes.RemoveAll(plane => planeTracks.Exists(existing => existing.Plane == plane.Master));
            if (available.Count == 0 && selectedPlanes.Count == 0) return false;
            var masters = new List<Component>();
            var ids = new List<string>();
            foreach (ComponentBinding binding in available) {
                masters.Add(binding.Master);
                ids.Add(binding.Moniker);
            }
            Matrix[] poses = Capture(masters);
            foreach (Component component in masters) component.KeepAlive(true);
            double time = keyframes.Count == 0 ? 0 : playheadSeconds;
            bool changed = masters.Count > 0 && AddComponentsCore(masters, ids, poses, time,
                keyframes.Count == 0 ? (Matrix?)window.Projection : null);
            if (keyframes.Count == 0) return changed;
            foreach (IDatumPlane plane in selectedPlanes) {
                plane.Master.KeepAlive(true);
                bool added = AddPlaneTrackCore(plane.Master, plane.Moniker.ToString(),
                    PlanePlacement(plane.Master), time, !changed);
                changed |= added;
            }
            return changed;
        }

        // New tracks receive one anchor at the current playhead. Their pose is
        // held before that time; existing tracks and their sparse keys are copied.
        internal bool AddComponentsCore(IList<Component> masters, IList<string> ids, Matrix[] poses, double time,
            Matrix? startingCamera = null) {
            if (masters == null || ids == null || poses == null || masters.Count == 0 ||
                masters.Count != ids.Count || masters.Count != poses.Length ||
                Double.IsNaN(time) || Double.IsInfinity(time) || time < 0 || time > 3600) return false;
            var seen = new HashSet<string>(componentMonikers, StringComparer.Ordinal);
            foreach (string id in ids) if (String.IsNullOrEmpty(id) || !seen.Add(id)) return false;
            if (keyframes.Count == 0) {
                if (components.Count != 0 || time != 0) return false;
                SaveStructuralUndoState();
                components.AddRange(masters);
                componentMonikers.AddRange(ids);
                keyframes.Add(new Keyframe(poses, AllPresent(poses.Length), startingCamera, 0));
                initialPlacements = (Matrix[])poses.Clone();
                initialCamera = startingCamera;
                lockedTracks.Clear(); EnsureLockCount();
                CurrentKeyframeIndex = 0;
                playheadSeconds = 0;
                selectedTrackIndex = 0;
                editGeneration++;
                lastLivenessCheck = 0;
                MarkDirty();
                return true;
            }
            int oldCount = components.Count, newCount = oldCount + masters.Count;
            var expanded = new List<Keyframe>(keyframes.Count + 1);
            foreach (Keyframe frame in keyframes) {
                Matrix[] prior = frame.CopyPlacements();
                bool[] presence = frame.CopyPresence();
                var placements = new Matrix[newCount];
                var hasPoses = new bool[newCount];
                Array.Copy(prior, placements, oldCount);
                Array.Copy(presence, hasPoses, oldCount);
                expanded.Add(new Keyframe(placements, hasPoses, frame.CameraProjection, frame.TimeSeconds));
            }
            int anchor = expanded.FindIndex(frame => Math.Abs(frame.TimeSeconds - time) < 1e-6);
            if (anchor < 0) {
                anchor = expanded.FindIndex(frame => frame.TimeSeconds > time);
                if (anchor < 0) anchor = expanded.Count;
                expanded.Insert(anchor, new Keyframe(new Matrix[newCount], new bool[newCount], null, time));
            }
            for (int i = 0; i < poses.Length; i++)
                expanded[anchor] = expanded[anchor].WithPose(oldCount + i, poses[i]);
            SaveStructuralUndoState();
            components.AddRange(masters);
            componentMonikers.AddRange(ids);
            // Camera is always the last row; extend component locks before it.
            EnsureLockCount();
            for (int i = 0; i < masters.Count; i++) lockedTracks.Insert(oldCount, false);
            while (lockedTracks.Count > TrackCount) lockedTracks.RemoveAt(lockedTracks.Count - 1);
            ShiftVisibilityTracks(oldCount, masters.Count);
            var shiftedEasing = new Dictionary<TrackKeyRef, EasingMode>();
            foreach (var pair in keyEasing) shiftedEasing[new TrackKeyRef(
                pair.Key.Track >= oldCount ? pair.Key.Track + masters.Count : pair.Key.Track,
                pair.Key.Time)] = pair.Value;
            keyEasing.Clear(); foreach (var pair in shiftedEasing) keyEasing.Add(pair.Key, pair.Value);
            if (initialPlacements != null && initialPlacements.Length == oldCount) {
                var expandedInitial = new Matrix[newCount];
                Array.Copy(initialPlacements, expandedInitial, oldCount);
                Array.Copy(poses, 0, expandedInitial, oldCount, poses.Length);
                initialPlacements = expandedInitial;
            }
            keyframes.Clear(); keyframes.AddRange(expanded);
            CurrentKeyframeIndex = anchor;
            playheadSeconds = expanded[anchor].TimeSeconds;
            selectedTrackIndex = oldCount;
            editGeneration++;
            lastLivenessCheck = 0;
            MarkDirty();
            return true;
        }

        internal bool RemoveComponentTrack(int track) {
            return CanEdit && CanUseCurrentAssembly && RemoveComponentTrackCore(track);
        }
        internal bool RemoveComponentTrackCore(int track) {
            if (track < 0 || track >= components.Count) return false; // Camera is not a component.
            if (IsTrackLocked(track)) return false;
            if (components.Count == 1 && planeTracks.Count > 0) return false;
            RestoreAppearancesInWriteBlock();
            SaveStructuralUndoState();
            if (components.Count == 1) {
                ResetState(true);
                MarkDirty();
                return true;
            }
            int newCount = components.Count - 1;
            var remaining = new List<Keyframe>(keyframes.Count);
            foreach (Keyframe frame in keyframes) {
                Matrix[] prior = frame.CopyPlacements();
                bool[] presence = frame.CopyPresence();
                var placements = new Matrix[newCount];
                var hasPoses = new bool[newCount];
                if (track > 0) {
                    Array.Copy(prior, 0, placements, 0, track);
                    Array.Copy(presence, 0, hasPoses, 0, track);
                }
                if (track < newCount) {
                    Array.Copy(prior, track + 1, placements, track, newCount - track);
                    Array.Copy(presence, track + 1, hasPoses, track, newCount - track);
                }
                Keyframe trimmed = new Keyframe(placements, hasPoses, frame.CameraProjection, frame.TimeSeconds);
                if (!trimmed.IsEmpty) remaining.Add(trimmed);
            }
            components.RemoveAt(track);
            componentMonikers.RemoveAt(track);
            if (lockedTracks.Count > track) lockedTracks.RemoveAt(track);
            RemoveVisibilityTrack(track);
            var shiftedEasing = new Dictionary<TrackKeyRef, EasingMode>();
            foreach (var pair in keyEasing)
                if (pair.Key.Track != track)
                    shiftedEasing[new TrackKeyRef(pair.Key.Track > track ? pair.Key.Track - 1 : pair.Key.Track,
                        pair.Key.Time)] = pair.Value;
            keyEasing.Clear(); foreach (var pair in shiftedEasing) keyEasing.Add(pair.Key, pair.Value);
            if (initialPlacements != null && initialPlacements.Length == newCount + 1) {
                var trimmedInitial = new Matrix[newCount];
                if (track > 0) Array.Copy(initialPlacements, 0, trimmedInitial, 0, track);
                if (track < newCount) Array.Copy(initialPlacements, track + 1, trimmedInitial, track, newCount - track);
                initialPlacements = trimmedInitial;
            }
            keyframes.Clear(); keyframes.AddRange(remaining);
            CurrentKeyframeIndex = FindEvent(playheadSeconds);
            selectedTrackIndex = Math.Min(track, newCount - 1);
            editGeneration++;
            lastLivenessCheck = 0;
            MarkDirty();
            return true;
        }
    }
}

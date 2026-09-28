using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal enum EasingMode { Linear, Smooth, EaseIn, EaseOut }

    internal sealed partial class AnimationProject {
        private static readonly Dictionary<Document, AnimationProject> projects = new Dictionary<Document, AnimationProject>();
        private static readonly AnimationProject empty = new AnimationProject();
        private readonly List<Component> components = new List<Component>();
        private readonly List<string> componentMonikers = new List<string>();
        private readonly List<Keyframe> keyframes = new List<Keyframe>();
        private Document document;
        private long lastLivenessCheck;
        private bool liveComponents;
        private bool dirty;
        private bool embedded;
        private double playheadSeconds;
        private int selectedTrackIndex;
        public string LoadError { get; private set; }
        public string PersistenceError { get; private set; }

        private AnimationProject() {
            framesPerSecond = 30;
            fadeDurationSeconds = .4;
            speedMultiplier = 1.0;
            SecondsPerSegment = 1.0;
            easing = EasingMode.Smooth;
            CurrentKeyframeIndex = -1;
            selectedTrackIndex = 0;
            EnsureLockCount();
            InitializeAnimationLibrary();
        }

        public static AnimationProject Current {
            get {
                Window window = Window.ActiveWindow;
                if (window == null || window.Document == null) return empty;
                Document activeDocument = window.Document;
                // Lightweight background loading has structure but cannot be modified yet.
                if (!activeDocument.IsComplete) return empty;
                AnimationProject project;
                if (!projects.TryGetValue(activeDocument, out project)) {
                    project = new AnimationProject();
                    project.document = activeDocument;
                    projects.Add(activeDocument, project);
                    try {
                        AnimationLibrarySnapshot saved = ProjectPersistence.ReadLibrary(activeDocument);
                        if (saved != null) project.RestoreLibrary(saved);
                    }
                    catch (Exception ex) { project.LoadError = ex.Message; }
                }
                return project;
            }
        }
        public static void ReleaseAll() { projects.Clear(); }
        public static void Release(Document doc) { projects.Remove(doc); }
        public static bool TryGetLoaded(Document doc, out AnimationProject project) {
            return projects.TryGetValue(doc, out project);
        }
        public bool IsDirty { get { return dirty; } }
        public bool IsEmbedded { get { return embedded; } }
        public void MarkSaved() { dirty = false; }
        private void MarkDirty() {
            if (document == null) return;
            dirty = true;
            document.IsModified = true;
            embedded = false;
            try {
                AnimationLibrarySnapshot snapshot = CreateLibrarySnapshot();
                // DocumentSaving is not a write block. Keep the document properties
                // current while the user edits, before SpaceClaim starts saving.
                if (WriteBlock.IsActive) ProjectPersistence.WriteLibrary(document, snapshot);
                else WriteBlock.ExecuteTask("SC Animator - Save animation data", delegate {
                    ProjectPersistence.WriteLibrary(document, snapshot);
                });
                embedded = true;
                PersistenceError = null;
            }
            catch (Exception ex) {
                PersistenceError = ex.Message;
            }
        }
        private int framesPerSecond;
        private double fadeDurationSeconds;
        public double FadeDurationSeconds {
            get { return fadeDurationSeconds; }
            set {
                if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentOutOfRangeException("value");
                double next = Math.Max(.1, Math.Min(5, Math.Round(value, 2)));
                if (Math.Abs(fadeDurationSeconds - next) < 1e-9) return;
                fadeDurationSeconds = next;
                MarkDirty();
            }
        }
        public int FramesPerSecond {
            get { return framesPerSecond; }
            set {
                int next = Math.Max(1, Math.Min(120, value));
                if (framesPerSecond == next) return;
                framesPerSecond = next;
                MarkDirty();
            }
        }
        private double speedMultiplier;
        public double SpeedMultiplier {
            get { return speedMultiplier; }
            set {
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentOutOfRangeException("value");
                double next = Math.Max(0.25, Math.Min(4.0, value));
                if (speedMultiplier == next) return;
                speedMultiplier = next;
                MarkDirty();
            }
        }
        public double SecondsPerSegment { get; set; }
        private EasingMode easing;
        public EasingMode Easing {
            get { return easing; }
            set { if (easing != value) { easing = value; MarkDirty(); } }
        }
        private bool loop;
        public bool Loop {
            get { return loop; }
            set { if (loop != value) { loop = value; MarkDirty(); } }
        }
        public int CurrentKeyframeIndex { get; private set; }
        public double PlayheadSeconds { get { return playheadSeconds; } }
        public int SelectedTrackIndex {
            get { return selectedTrackIndex; }
            set { selectedTrackIndex = Math.Max(0, Math.Min(CameraTrackIndex, value)); }
        }
        public int TrackCount { get { return CameraTrackIndex + 1; } }
        public double DurationSeconds {
            get {
                double end = keyframes.Count == 0 ? 0 : keyframes[keyframes.Count - 1].TimeSeconds;
                foreach (PlaneTrack plane in planeTracks)
                    foreach (double time in plane.Keys.Keys) end = Math.Max(end, time);
                return end;
            }
        }
        public int KeyframeCount { get { return keyframes.Count; } }
        public int ComponentCount { get { return components.Count; } }
        public bool HasComponent { get { return components.Count > 0; } }
        public bool IsActiveDocument {
            get {
                Window window = Window.ActiveWindow;
                if (document == null || window == null || window.Document != document)
                    return false;
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (lastLivenessCheck == 0 || now - lastLivenessCheck > System.Diagnostics.Stopwatch.Frequency / 2) {
                    liveComponents = CheckComponentsAlive();
                    lastLivenessCheck = now;
                }
                return liveComponents;
            }
        }
        private bool CheckComponentsAlive() {
            if (!HasComponent) return false;
            foreach (Component component in components)
                if (component.IsDeleted) return false;
            foreach (PlaneTrack plane in planeTracks)
                if (plane.Plane == null || plane.Plane.IsDeleted) return false;
            return true;
        }
        public bool CanUseCurrentAssembly {
            get {
                Window window = Window.ActiveWindow;
                return document != null && window != null && window.Document == document && CheckComponentsAlive();
            }
        }
        public bool CanEdit { get { return IsActiveDocument && !Animation.IsAnimating; } }
        public bool CanPlay { get { return IsActiveDocument && DurationSeconds > 0; } }

        // Placements are local to each master component's parent. A shared nested
        // master must occur only once, otherwise it would be written repeatedly.
        private sealed class ComponentBinding {
            public Component Master;
            public string Moniker;
        }
        private static List<ComponentBinding> CollectComponents(Document source) {
            var result = new List<ComponentBinding>();
            var seen = new HashSet<Component>();
            foreach (IComponent occurrence in source.MainPart.GetDescendants<IComponent>()) {
                Component master = occurrence.Master;
                if (!master.IsDeleted && seen.Add(master))
                    result.Add(new ComponentBinding { Master = master, Moniker = occurrence.Moniker.ToString() });
            }
            return result;
        }

        private static Matrix[] Capture(IList<Component> targets) {
            var poses = new Matrix[targets.Count];
            for (int i = 0; i < targets.Count; i++) poses[i] = targets[i].Placement;
            return poses;
        }
        private static bool[] AllPresent(int count) {
            var present = new bool[count];
            for (int i = 0; i < count; i++) present[i] = true;
            return present;
        }
        private int FindEvent(double time) {
            for (int i = 0; i < keyframes.Count; i++)
                if (Math.Abs(keyframes[i].TimeSeconds - time) < 1e-6) return i;
            return -1;
        }
        public string GetTrackName(int track) {
            if (track == CameraTrackIndex) return "Camera";
            if (IsPlaneTrack(track)) {
                DatumPlane plane = planeTracks[track - components.Count].Plane;
                return plane == null || plane.IsDeleted ? "Plane" : "Plane: " + plane.Name;
            }
            if (track < 0 || track >= components.Count) return "";
            Component component = components[track];
            if (component == null || component.IsDeleted) return "Component " + (track + 1);
            // The Structure tree primarily displays the instantiated part's
            // DisplayName. Component.Name is only an optional occurrence suffix.
            Part template = component.Template;
            return PreferredTrackName(template == null ? null : template.DisplayName,
                component.Name, track);
        }
        internal static string PreferredTrackName(string partName, string componentName, int track) {
            string part = String.IsNullOrWhiteSpace(partName) ? null : partName.Trim();
            string instance = String.IsNullOrWhiteSpace(componentName) ? null : componentName.Trim();
            if (part != null) {
                int number;
                bool generated = instance != null && instance.StartsWith("Component ", StringComparison.OrdinalIgnoreCase) &&
                    Int32.TryParse(instance.Substring("Component ".Length), out number);
                if (instance != null && !generated && !String.Equals(part, instance, StringComparison.OrdinalIgnoreCase))
                    return part + " (" + instance + ")";
                return part;
            }
            return instance ?? "Component " + (track + 1);
        }
        public double[] GetTrackTimes(int track) {
            var times = new List<double>();
            if (track < 0 || track >= TrackCount) return times.ToArray();
            if (IsPlaneTrack(track)) {
                times.AddRange(planeTracks[track - components.Count].Keys.Keys);
                return times.ToArray();
            }
            foreach (Keyframe frame in keyframes)
                if (track == CameraTrackIndex ? frame.CameraProjection.HasValue : frame.HasPose(track))
                    times.Add(frame.TimeSeconds);
            return times.ToArray();
        }
        public bool HasTrackKey(int track, double time) {
            if (IsPlaneTrack(track)) return planeTracks[track - components.Count].Keys.ContainsKey(time);
            int index = FindEvent(time);
            return index >= 0 && track >= 0 && track < TrackCount &&
                (track == CameraTrackIndex ? keyframes[index].CameraProjection.HasValue : keyframes[index].HasPose(track));
        }
        private bool ValidTrackEdit(int track, double time) {
            return CanEdit && CanUseCurrentAssembly && track >= 0 && track < TrackCount && !IsTrackLocked(track) &&
                !Double.IsNaN(time) && !Double.IsInfinity(time) && time >= 0 && time <= 3600;
        }
        public bool AddTrackKey(int track, double time) {
            if (IsPlaneTrack(track)) return AddPlaneKey(track, time, false);
            if (!ValidTrackEdit(track, time) || HasTrackKey(track, time)) return false;
            SaveUndoState();
            int index = FindEvent(time);
            if (index < 0) {
                var frame = new Keyframe(new Matrix[components.Count], new bool[components.Count], null, time);
                index = keyframes.FindIndex(k => k.TimeSeconds > time);
                if (index < 0) index = keyframes.Count;
                keyframes.Insert(index, frame);
            }
            keyframes[index] = track == CameraTrackIndex
                ? keyframes[index].WithCamera(Window.ActiveWindow.Projection)
                : keyframes[index].WithPose(track, components[track].Placement);
            CurrentKeyframeIndex = index;
            playheadSeconds = time;
            selectedTrackIndex = track;
            MarkDirty();
            return true;
        }
        public bool UpdateTrackKey(int track, double time) {
            if (IsPlaneTrack(track)) return AddPlaneKey(track, time, true);
            if (!ValidTrackEdit(track, time) || !HasTrackKey(track, time)) return false;
            SaveUndoState();
            int index = FindEvent(time);
            keyframes[index] = track == CameraTrackIndex
                ? keyframes[index].WithCamera(Window.ActiveWindow.Projection)
                : keyframes[index].WithPose(track, components[track].Placement);
            CurrentKeyframeIndex = index;
            playheadSeconds = time;
            selectedTrackIndex = track;
            MarkDirty();
            return true;
        }
        public bool DeleteTrackKey(int track, double time) {
            if (IsPlaneTrack(track)) return DeletePlaneKey(track, time);
            if (!ValidTrackEdit(track, time) || !HasTrackKey(track, time)) return false;
            // Keep an anchor for every component so interpolation remains defined.
            if (track < components.Count && GetTrackTimes(track).Length <= 1) return false;
            SaveUndoState();
            int index = FindEvent(time);
            keyframes[index] = track == CameraTrackIndex
                ? keyframes[index].WithCamera(null) : keyframes[index].WithoutPose(track);
            keyEasing.Remove(new TrackKeyRef(track, time));
            visibilityKeys.Remove(new TrackKeyRef(track, time));
            if (keyframes[index].IsEmpty) keyframes.RemoveAt(index);
            playheadSeconds = time;
            CurrentKeyframeIndex = FindEvent(time);
            selectedTrackIndex = track;
            MarkDirty();
            return true;
        }
        public bool RetimeTrackKey(int track, double from, double to) {
            if (!ValidTrackEdit(track, from) || !ValidTrackEdit(track, to)) return false;
            return RetimeTrackKeyCore(track, from, to);
        }
        // A track owns its pose; retiming transfers exactly that pose and leaves
        // keys on other tracks at their original times.
        internal bool RetimeTrackKeyCore(int track, double from, double to) {
            if (track < 0 || track >= TrackCount || IsTrackLocked(track) || Double.IsNaN(to) || Double.IsInfinity(to) ||
                to < 0 || to > 3600 || !HasTrackKey(track, from)) return false;
            int source = FindEvent(from);
            if (Math.Abs(from - to) < 1e-6) return true;
            if (HasTrackKey(track, to)) return false;
            if (IsPlaneTrack(track)) {
                PlaneTrack plane = planeTracks[track - components.Count];
                Matrix placement = plane.Keys[from];
                SaveUndoState();
                plane.Keys.Remove(from); plane.Keys.Add(to, placement);
                VisibilityMode mode = GetKeyVisibility(track, from);
                visibilityKeys.Remove(new TrackKeyRef(track, from));
                if (mode != VisibilityMode.Default)
                    visibilityKeys[new TrackKeyRef(track, to)] = mode;
                selectedTrackIndex = track; playheadSeconds = to;
                MarkDirty(); return true;
            }
            SaveUndoState();
            Keyframe old = keyframes[source];
            EasingMode transition;
            bool hadTransition = keyEasing.TryGetValue(new TrackKeyRef(track, from), out transition);
            keyEasing.Remove(new TrackKeyRef(track, from));
            VisibilityMode visibility = GetKeyVisibility(track, from);
            visibilityKeys.Remove(new TrackKeyRef(track, from));
            Matrix pose = track < components.Count ? old[track] : Matrix.Identity;
            Matrix? camera = track == CameraTrackIndex ? old.CameraProjection : null;
            keyframes[source] = track == CameraTrackIndex ? old.WithCamera(null) : old.WithoutPose(track);
            if (keyframes[source].IsEmpty) keyframes.RemoveAt(source);
            int target = FindEvent(to);
            if (target < 0) {
                var created = new Keyframe(new Matrix[old.Count], new bool[old.Count], null, to);
                target = keyframes.FindIndex(frame => frame.TimeSeconds > to);
                if (target < 0) target = keyframes.Count;
                keyframes.Insert(target, created);
            }
            keyframes[target] = track == CameraTrackIndex
                ? keyframes[target].WithCamera(camera) : keyframes[target].WithPose(track, pose);
            if (hadTransition) keyEasing[new TrackKeyRef(track, keyframes[target].TimeSeconds)] = transition;
            if (visibility != VisibilityMode.Default)
                visibilityKeys[new TrackKeyRef(track, keyframes[target].TimeSeconds)] = visibility;
            CurrentKeyframeIndex = target;
            playheadSeconds = keyframes[target].TimeSeconds;
            selectedTrackIndex = track;
            MarkDirty();
            return true;
        }

        public bool CaptureStart() {
            Window window = Window.ActiveWindow;
            if (window == null || !window.Document.IsComplete || Animation.IsAnimating) return false;
            List<ComponentBinding> targets = CollectComponents(window.Document);
            var selected = new HashSet<Component>();
            foreach (IComponent occurrence in window.ActiveContext.GetSelection<IComponent>())
                if (!occurrence.Master.IsDeleted) selected.Add(occurrence.Master);
            if (selected.Count > 0) targets.RemoveAll(target => !selected.Contains(target.Master));
            if (targets.Count == 0) return false;
            var masters = new List<Component>();
            foreach (ComponentBinding target in targets) masters.Add(target.Master);
            Matrix[] poses = Capture(masters);
            foreach (Component target in masters) target.KeepAlive(true);
            ResetState();
            document = window.Document;
            foreach (ComponentBinding target in targets) {
                components.Add(target.Master);
                componentMonikers.Add(target.Moniker);
            }
            keyframes.Add(new Keyframe(poses, AllPresent(poses.Length), window.Projection, 0));
            initialPlacements = (Matrix[])poses.Clone();
            initialCamera = window.Projection;
            EnsureLockCount();
            CurrentKeyframeIndex = 0;
            playheadSeconds = 0;
            selectedTrackIndex = 0;
            lastLivenessCheck = 0;
            MarkDirty();
            return true;
        }

        public bool AddCurrentPose() {
            if (!CanEdit || !CanUseCurrentAssembly) return false;
            // Do not silently omit components inserted after Set Start.
            var currentComponents = new HashSet<Component>();
            foreach (ComponentBinding binding in CollectComponents(document)) currentComponents.Add(binding.Master);
            if (!currentComponents.IsSupersetOf(components)) return false;
            Matrix[] poses = Capture(components);
            double time = keyframes[keyframes.Count - 1].TimeSeconds + SecondsPerSegment;
            var present = new bool[components.Count];
            for (int i = 0; i < present.Length; i++) present[i] = !IsTrackLocked(i);
            Matrix? camera = IsTrackLocked(CameraTrackIndex) ? null : (Matrix?)Window.ActiveWindow.Projection;
            if (Array.TrueForAll(present, value => !value) && !camera.HasValue) return false;
            SaveUndoState();
            keyframes.Add(new Keyframe(poses, present, camera, time));
            CurrentKeyframeIndex = keyframes.Count - 1;
            playheadSeconds = time;
            MarkDirty();
            return true;
        }

        public bool UpdateSelectedKeyframe() {
            if (!CanEdit || !CanUseCurrentAssembly ||
                CurrentKeyframeIndex < 0 || CurrentKeyframeIndex >= keyframes.Count) return false;
            var currentComponents = new HashSet<Component>();
            foreach (ComponentBinding binding in CollectComponents(document)) currentComponents.Add(binding.Master);
            if (!currentComponents.IsSupersetOf(components)) return false;
            SaveUndoState();
            Keyframe old = keyframes[CurrentKeyframeIndex];
            Matrix[] poses = Capture(components);
            bool[] present = old.CopyPresence();
            Matrix[] updated = old.CopyPlacements();
            for (int i = 0; i < updated.Length; i++)
                if (!IsTrackLocked(i)) { updated[i] = poses[i]; present[i] = true; }
            Matrix? camera = IsTrackLocked(CameraTrackIndex) ? old.CameraProjection : (Matrix?)Window.ActiveWindow.Projection;
            keyframes[CurrentKeyframeIndex] = new Keyframe(updated, present, camera, old.TimeSeconds);
            MarkDirty();
            return true;
        }

        internal void ReplaceKeyframe(int index, Matrix[] placements) {
            if (index < 0 || index >= keyframes.Count || placements.Length != keyframes[index].Count)
                throw new ArgumentOutOfRangeException("index");
            SaveUndoState();
            Matrix[] updated = keyframes[index].CopyPlacements();
            bool[] present = keyframes[index].CopyPresence();
            for (int i = 0; i < placements.Length; i++)
                if (!IsTrackLocked(i)) { updated[i] = placements[i]; present[i] = true; }
            keyframes[index] = new Keyframe(updated, present,
                keyframes[index].CameraProjection, keyframes[index].TimeSeconds);
            MarkDirty();
        }

        public bool RemoveLast() {
            if (!CanEdit || keyframes.Count <= 1) return false;
            Keyframe last = keyframes[keyframes.Count - 1];
            for (int track = 0; track < TrackCount; track++)
                if (IsTrackLocked(track) && (track == CameraTrackIndex ? last.CameraProjection.HasValue :
                    IsPlaneTrack(track) ? false : last.HasPose(track))) return false;
            for (int track = 0; track < components.Count; track++)
                if (last.HasPose(track) && GetTrackTimes(track).Length <= 1) return false;
            SaveUndoState();
            keyframes.RemoveAt(keyframes.Count - 1);
            for (int track = 0; track < TrackCount; track++) keyEasing.Remove(new TrackKeyRef(track, last.TimeSeconds));
            for (int track = 0; track < TrackCount; track++) visibilityKeys.Remove(new TrackKeyRef(track, last.TimeSeconds));
            CurrentKeyframeIndex = keyframes.Count - 1;
            playheadSeconds = keyframes[CurrentKeyframeIndex].TimeSeconds;
            MarkDirty();
            return true;
        }

        public void Clear() {
            ResetState();
            MarkDirty();
        }
        private void ResetState(bool preserveHistory = false) {
            RestoreAppearancesInWriteBlock();
            if (preserveHistory) editGeneration++;
            else ClearEditHistory();
            lockedTracks.Clear(); keyEasing.Clear(); planeTracks.Clear(); visibilityKeys.Clear();
            reversedVisibility = false;
            timelineMarkers.Clear(); favoriteTracks.Clear(); hinges.Clear();
            initialPlacements = null; initialCamera = null;
            hasPlaybackRange = false; playbackRangeStart = playbackRangeEnd = 0;
            components.Clear();
            componentMonikers.Clear();
            keyframes.Clear();
            CurrentKeyframeIndex = -1;
            playheadSeconds = 0;
            selectedTrackIndex = 0;
            lastLivenessCheck = 0;
            liveComponents = false;
            LoadError = null;
            EnsureLockCount();
        }

        public Component[] GetComponents() { return components.ToArray(); }
        public Matrix[] GetKeyframe(int index) { return EvaluateAt(keyframes[index].TimeSeconds); }
        public double GetKeyframeTime(int index) { return keyframes[index].TimeSeconds; }
        public void SetCurrentKeyframe(int index) {
            CurrentKeyframeIndex = index;
            if (index >= 0 && index < keyframes.Count) playheadSeconds = keyframes[index].TimeSeconds;
        }
        public void SetPlayhead(double seconds) {
            playheadSeconds = Math.Max(0, seconds);
            CurrentKeyframeIndex = FindEvent(playheadSeconds);
        }
        public void MarkNavigationDirty() { MarkDirty(); }
        public ProjectSnapshot CreateSnapshot() {
            EnsureLockCount();
            var snapshot = new ProjectSnapshot {
                FramesPerSecond = FramesPerSecond,
                FadeDurationSeconds = FadeDurationSeconds,
                SpeedMultiplier = SpeedMultiplier,
                SecondsPerSegment = SecondsPerSegment,
                Easing = Easing,
                Loop = Loop,
                CameraEnabled = cameraEnabled,
                ReversedVisibility = reversedVisibility,
                HasPlaybackRange = hasPlaybackRange,
                PlaybackRangeStart = playbackRangeStart,
                PlaybackRangeEnd = playbackRangeEnd,
                LockedTracks = lockedTracks.ToArray(),
                InitialPlacements = initialPlacements == null ? null : (Matrix[])initialPlacements.Clone(),
                InitialCamera = initialCamera,
                CurrentKeyframeIndex = CurrentKeyframeIndex
            };
            snapshot.Monikers.AddRange(componentMonikers);
            foreach (HingeSnapshot hinge in hinges) snapshot.Hinges.Add(hinge.Clone());
            foreach (PlaneTrack plane in planeTracks) {
                var saved = new PlaneTrackSnapshot { Moniker = plane.Moniker };
                foreach (var key in plane.Keys) saved.Keys.Add(new PlaneKeySnapshot { Time = key.Key, Placement = key.Value });
                snapshot.PlaneTracks.Add(saved);
            }
            foreach (var visible in visibilityKeys)
                snapshot.VisibilityKeys.Add(new VisibilityKeySnapshot { Track = visible.Key.Track,
                    Time = visible.Key.Time, Mode = visible.Value });
            foreach (TimelineMarker marker in timelineMarkers) snapshot.Markers.Add(marker.Clone());
            snapshot.FavoriteTracks.AddRange(favoriteTracks);
            snapshot.FavoriteTracks.Sort(StringComparer.Ordinal);
            foreach (Keyframe frame in keyframes)
                snapshot.Keyframes.Add(CreateKeyframeSnapshot(frame));
            return snapshot;
        }
        private KeyframeSnapshot CreateKeyframeSnapshot(Keyframe frame) {
            var saved = new KeyframeSnapshot { TimeSeconds = frame.TimeSeconds,
                Placements = frame.CopyPlacements(), HasPoses = frame.CopyPresence(),
                CameraProjection = frame.CameraProjection, PoseEasing = new EasingMode?[components.Count] };
            for (int track = 0; track < components.Count; track++)
                saved.PoseEasing[track] = GetKeyEasing(track, frame.TimeSeconds);
            saved.CameraEasing = GetKeyEasing(CameraTrackIndex, frame.TimeSeconds);
            return saved;
        }
        private void Restore(ProjectSnapshot snapshot, bool preserveHistory = false) {
            var masters = new List<Component>();
            var seen = new HashSet<Component>();
            foreach (string id in snapshot.Monikers) {
                IDocObject obj = Moniker<IDocObject>.FromString(id).Resolve(document);
                IComponent occurrence = obj as IComponent;
                if (occurrence == null || occurrence.Master.IsDeleted || !seen.Add(occurrence.Master))
                    throw new InvalidOperationException("A recorded component is missing or ambiguous. Animation was not loaded.");
                masters.Add(occurrence.Master);
            }
            var restoredPlanes = new List<PlaneTrack>();
            foreach (PlaneTrackSnapshot saved in snapshot.PlaneTracks) {
                IDocObject obj = Moniker<IDocObject>.FromString(saved.Moniker).Resolve(document);
                IDatumPlane occurrence = obj as IDatumPlane;
                if (occurrence == null || occurrence.Master.IsDeleted)
                    throw new InvalidOperationException("A recorded clipping plane is missing. Animation was not loaded.");
                var plane = new PlaneTrack { Plane = occurrence.Master, Moniker = saved.Moniker };
                foreach (PlaneKeySnapshot key in saved.Keys) plane.Keys.Add(key.Time, key.Placement);
                restoredPlanes.Add(plane);
            }
            if ((snapshot.Keyframes.Count > 0) != (masters.Count > 0))
                throw new InvalidOperationException("Animation component count does not match its keyframes.");
            foreach (KeyframeSnapshot frame in snapshot.Keyframes)
                if (frame.Placements.Length != masters.Count)
                    throw new InvalidOperationException("A keyframe has an incorrect number of component poses.");
            ResetState(preserveHistory);
            components.AddRange(masters);
            componentMonikers.AddRange(snapshot.Monikers);
            planeTracks.AddRange(restoredPlanes);
            lockedTracks.Clear();
            foreach (KeyframeSnapshot frame in snapshot.Keyframes)
                keyframes.Add(new Keyframe(frame.Placements,
                    frame.HasPoses ?? AllPresent(frame.Placements.Length), frame.CameraProjection, frame.TimeSeconds));
            foreach (VisibilityKeySnapshot visible in snapshot.VisibilityKeys)
                visibilityKeys[new TrackKeyRef(visible.Track, visible.Time)] = visible.Mode;
            foreach (TimelineMarker marker in snapshot.Markers) timelineMarkers.Add(marker.Clone());
            timelineMarkers.Sort((a, b) => a.Time.CompareTo(b.Time));
            foreach (string favorite in snapshot.FavoriteTracks) favoriteTracks.Add(favorite);
            foreach (HingeSnapshot hinge in snapshot.Hinges) hinges.Add(hinge.Clone());
            // Earlier releases stored optional per-key transitions. The global
            // Ribbon transition is authoritative, including for old documents.
            if (snapshot.LockedTracks != null) lockedTracks.AddRange(snapshot.LockedTracks);
            EnsureLockCount();
            initialPlacements = snapshot.InitialPlacements == null ? null : (Matrix[])snapshot.InitialPlacements.Clone();
            initialCamera = snapshot.InitialCamera;
            cameraEnabled = snapshot.CameraEnabled;
            reversedVisibility = snapshot.ReversedVisibility;
            hasPlaybackRange = snapshot.HasPlaybackRange;
            playbackRangeStart = snapshot.PlaybackRangeStart;
            playbackRangeEnd = snapshot.PlaybackRangeEnd;
            framesPerSecond = snapshot.FramesPerSecond;
            fadeDurationSeconds = snapshot.FadeDurationSeconds;
            speedMultiplier = snapshot.SpeedMultiplier;
            SecondsPerSegment = snapshot.SecondsPerSegment;
            easing = snapshot.Easing;
            loop = snapshot.Loop;
            CurrentKeyframeIndex = snapshot.CurrentKeyframeIndex;
            playheadSeconds = CurrentKeyframeIndex < 0 ? 0 : keyframes[CurrentKeyframeIndex].TimeSeconds;
            foreach (Component component in components) component.KeepAlive(true);
            lastLivenessCheck = 0;
            dirty = false;
            embedded = true;
            PersistenceError = null;
        }
        public int TotalFrames {
            get {
                if (DurationSeconds <= 0) return 1;
                return Math.Max(1, (int)Math.Ceiling((EffectiveRangeEnd - EffectiveRangeStart) * FramesPerSecond / SpeedMultiplier));
            }
        }

        public Matrix[] EvaluateAt(double time) {
            if (keyframes.Count == 0) return new Matrix[0];
            var poses = new Matrix[components.Count == 0 ? keyframes[0].Count : components.Count];
            for (int track = 0; track < poses.Length; track++) {
                Keyframe left = null, right = null;
                foreach (Keyframe frame in keyframes) {
                    if (!frame.HasPose(track)) continue;
                    if (frame.TimeSeconds <= time) left = frame;
                    if (frame.TimeSeconds >= time) { right = frame; break; }
                }
                if (left == null) left = right;
                if (right == null) right = left;
                if (left == null) { poses[track] = components.Count > track ? components[track].Placement : Matrix.Identity; continue; }
                if (left == right) { poses[track] = left[track]; continue; }
                double t = EaseTrack((time - left.TimeSeconds) / (right.TimeSeconds - left.TimeSeconds),
                    track, left.TimeSeconds);
                poses[track] = PlacementInterpolator.Interpolate(left[track], right[track], t);
            }
            return poses;
        }
        public Matrix? EvaluateCameraAt(double time) {
            Keyframe left = null, right = null;
            foreach (Keyframe frame in keyframes) {
                if (!frame.CameraProjection.HasValue) continue;
                if (frame.TimeSeconds <= time) left = frame;
                if (frame.TimeSeconds >= time) { right = frame; break; }
            }
            if (left == null) left = right;
            if (right == null) right = left;
            if (left == null) return null;
            if (left == right) return left.CameraProjection;
            double t = EaseTrack((time - left.TimeSeconds) / (right.TimeSeconds - left.TimeSeconds),
                CameraTrackIndex, left.TimeSeconds);
            Matrix a = left.CameraProjection.Value, b = right.CameraProjection.Value;
            Matrix rotation = PlacementInterpolator.Interpolate(a.Rotation, b.Rotation, t);
            Vector translation = a.Translation * (1 - t) + b.Translation * t;
            double scale = a.Scale * (1 - t) + b.Scale * t;
            return Matrix.CreateTranslation(translation) * Matrix.CreateScale(scale) * rotation;
        }
        public Matrix[] EvaluateFrame(int frame) { return EvaluateAt(frame * SpeedMultiplier / FramesPerSecond); }
    }
}

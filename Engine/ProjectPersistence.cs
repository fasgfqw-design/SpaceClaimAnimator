using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal sealed class KeyframeSnapshot {
        public double TimeSeconds;
        public Matrix[] Placements;
        public bool[] HasPoses;
        public Matrix? CameraProjection;
        public EasingMode?[] PoseEasing;
        public EasingMode? CameraEasing;
    }

    internal sealed class PlaneKeySnapshot {
        public double Time;
        public Matrix Placement;
    }
    internal sealed class PlaneTrackSnapshot {
        public string Moniker;
        public readonly List<PlaneKeySnapshot> Keys = new List<PlaneKeySnapshot>();
    }
    internal sealed class VisibilityKeySnapshot {
        public int Track;
        public double Time;
        public VisibilityMode Mode;
    }

    internal sealed class ProjectSnapshot {
        public int FramesPerSecond = 30;
        public double FadeDurationSeconds = .4;
        public double SpeedMultiplier = 1.0;
        public double SecondsPerSegment = 1.0;
        public EasingMode Easing = EasingMode.Smooth;
        public bool Loop;
        public bool CameraEnabled = true;
        public bool ReversedVisibility;
        public bool HasPlaybackRange;
        public double PlaybackRangeStart, PlaybackRangeEnd;
        public bool[] LockedTracks;
        public Matrix[] InitialPlacements;
        public Matrix? InitialCamera;
        public int CurrentKeyframeIndex = -1;
        public readonly List<string> Monikers = new List<string>();
        public readonly List<PlaneTrackSnapshot> PlaneTracks = new List<PlaneTrackSnapshot>();
        public readonly List<VisibilityKeySnapshot> VisibilityKeys = new List<VisibilityKeySnapshot>();
        public readonly List<TimelineMarker> Markers = new List<TimelineMarker>();
        public readonly List<string> FavoriteTracks = new List<string>();
        public readonly List<KeyframeSnapshot> Keyframes = new List<KeyframeSnapshot>();
        public readonly List<HingeSnapshot> Hinges = new List<HingeSnapshot>();
    }

    // Hidden custom document properties travel with the native SpaceClaim file.
    // Write into the inactive bank, then switch the manifest. A failed write
    // leaves the last complete bank available to the document.
    internal static partial class ProjectPersistence {
        private const string Prefix = "SCAnimator.V261.Animation.";
        private const string MetaName = Prefix + "Meta";
        private const int ChunkLength = 8000;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly Frame LocalFrame = Frame.Create(Point.Origin, Direction.DirX, Direction.DirY);

        public static void Write(Document doc, ProjectSnapshot snapshot) {
            WriteEncoded(doc, Encode(snapshot));
        }
        private static void WriteEncoded(Document doc, string payload) {
            int count = (payload.Length + ChunkLength - 1) / ChunkLength;
            if (count < 1 || count > 512) throw new InvalidOperationException("Animation is too large to embed in this document.");
            string oldMeta;
            string[] oldFields = TryGetProperty(doc, MetaName, out oldMeta) ? oldMeta.Split('|') : new string[0];
            string bank = oldFields.Length == 4 && oldFields[1] == "A" ? "B" : "A";
            for (int i = 0; i < count; i++) {
                int offset = i * ChunkLength;
                SetProperty(doc, Prefix + bank + ".Chunk" + i.ToString("D4", Invariant),
                    payload.Substring(offset, Math.Min(ChunkLength, payload.Length - offset)));
            }
            SetProperty(doc, MetaName, "1|" + bank + "|" + count.ToString(Invariant) + "|" + Sha256(payload));
        }

        public static ProjectSnapshot Read(Document doc) {
            string encoded = ReadEncoded(doc);
            return encoded == null ? null : Decode(encoded);
        }
        private static string ReadEncoded(Document doc) {
            string meta;
            if (!TryGetProperty(doc, MetaName, out meta)) return null;
            string[] fields = meta.Split('|');
            int count;
            if (fields.Length != 4 || fields[0] != "1" || (fields[1] != "A" && fields[1] != "B") ||
                !int.TryParse(fields[2], NumberStyles.None, Invariant, out count) || count < 1 || count > 512)
                throw new InvalidDataException("Unsupported or damaged SC Animator data in this document.");
            var payload = new StringBuilder();
            for (int i = 0; i < count; i++) {
                string chunk;
                if (!TryGetProperty(doc, Prefix + fields[1] + ".Chunk" + i.ToString("D4", Invariant), out chunk))
                    throw new InvalidDataException("An SC Animator data chunk is missing.");
                payload.Append(chunk);
            }
            string encoded = payload.ToString();
            if (!String.Equals(Sha256(encoded), fields[3], StringComparison.Ordinal))
                throw new InvalidDataException("SC Animator data checksum does not match.");
            return encoded;
        }

        private static bool TryGetProperty(Document doc, string name, out string value) {
            value = null;
            CustomProperty property;
            if (!doc.CustomProperties.TryGetValue(name, out property)) return false;
            value = property.Value as string;
            if (value == null) throw new InvalidDataException("SC Animator document property has the wrong type: " + name);
            return true;
        }

        private static void SetProperty(Document doc, string name, string value) {
            CustomProperty property;
            if (doc.CustomProperties.TryGetValue(name, out property)) property.Value = value;
            else property = CustomProperty.Create(doc, name, value);
            property.IsVisible = false;
        }

        private static string Sha256(string value) {
            using (SHA256 hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        public static string Encode(ProjectSnapshot snapshot) {
            var raw = new StringBuilder();
            var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false };
            using (XmlWriter writer = XmlWriter.Create(raw, settings)) {
                writer.WriteStartElement("SCAnimator");
                writer.WriteAttributeString("version", "2");
                writer.WriteAttributeString("fps", snapshot.FramesPerSecond.ToString(Invariant));
                writer.WriteAttributeString("fadeDuration", Number(snapshot.FadeDurationSeconds));
                writer.WriteAttributeString("speed", Number(snapshot.SpeedMultiplier));
                writer.WriteAttributeString("segment", Number(snapshot.SecondsPerSegment));
                writer.WriteAttributeString("easing", snapshot.Easing.ToString());
                writer.WriteAttributeString("loop", snapshot.Loop ? "true" : "false");
                writer.WriteAttributeString("cameraEnabled", snapshot.CameraEnabled ? "true" : "false");
                if (snapshot.ReversedVisibility)
                    writer.WriteAttributeString("reversedVisibility", "true");
                if (snapshot.HasPlaybackRange) {
                    writer.WriteAttributeString("rangeStart", Number(snapshot.PlaybackRangeStart));
                    writer.WriteAttributeString("rangeEnd", Number(snapshot.PlaybackRangeEnd));
                }
                if (snapshot.LockedTracks != null) {
                    if (snapshot.LockedTracks.Length != snapshot.Monikers.Count + snapshot.PlaneTracks.Count + 1)
                        throw new InvalidDataException("Invalid track lock count.");
                    var locks = new char[snapshot.LockedTracks.Length];
                    for (int i = 0; i < locks.Length; i++) locks[i] = snapshot.LockedTracks[i] ? '1' : '0';
                    writer.WriteAttributeString("locks", new String(locks));
                }
                writer.WriteAttributeString("current", snapshot.CurrentKeyframeIndex.ToString(Invariant));
                foreach (string moniker in snapshot.Monikers) {
                    writer.WriteStartElement("component");
                    writer.WriteAttributeString("id", moniker);
                    writer.WriteEndElement();
                }
                foreach (string favorite in snapshot.FavoriteTracks) {
                    writer.WriteStartElement("favorite");
                    writer.WriteAttributeString("id", favorite);
                    writer.WriteEndElement();
                }
                foreach (HingeSnapshot hinge in snapshot.Hinges) {
                    writer.WriteStartElement("hinge");
                    writer.WriteAttributeString("name", hinge.Name);
                    writer.WriteAttributeString("fixed", hinge.FixedId);
                    writer.WriteAttributeString("moving", hinge.MovingId);
                    if (!String.IsNullOrEmpty(hinge.AlignId))
                        writer.WriteAttributeString("align", hinge.AlignId);
                    if (!String.IsNullOrEmpty(hinge.OwnedAlignId))
                        writer.WriteAttributeString("ownedAlign", hinge.OwnedAlignId);
                    writer.WriteAttributeString("ox", Number(hinge.Origin.X));
                    writer.WriteAttributeString("oy", Number(hinge.Origin.Y));
                    writer.WriteAttributeString("oz", Number(hinge.Origin.Z));
                    writer.WriteAttributeString("dx", Number(hinge.Direction.X));
                    writer.WriteAttributeString("dy", Number(hinge.Direction.Y));
                    writer.WriteAttributeString("dz", Number(hinge.Direction.Z));
                    writer.WriteEndElement();
                }
                foreach (PlaneTrackSnapshot plane in snapshot.PlaneTracks) {
                    writer.WriteStartElement("plane");
                    writer.WriteAttributeString("id", plane.Moniker);
                    foreach (PlaneKeySnapshot key in plane.Keys) {
                        writer.WriteStartElement("key");
                        writer.WriteAttributeString("time", Number(key.Time));
                        WritePose(writer, 0, key.Placement, null);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                if (snapshot.InitialPlacements != null) {
                    if (snapshot.InitialPlacements.Length != snapshot.Monikers.Count)
                        throw new InvalidDataException("Invalid initial pose count.");
                    writer.WriteStartElement("initial");
                    for (int i = 0; i < snapshot.InitialPlacements.Length; i++)
                        WritePose(writer, i, snapshot.InitialPlacements[i], null);
                    if (snapshot.InitialCamera.HasValue)
                        WriteCamera(writer, snapshot.InitialCamera.Value, null);
                    writer.WriteEndElement();
                }
                foreach (VisibilityKeySnapshot visible in snapshot.VisibilityKeys) {
                    writer.WriteStartElement("visibility");
                    writer.WriteAttributeString("track", visible.Track.ToString(Invariant));
                    writer.WriteAttributeString("time", Number(visible.Time));
                    writer.WriteAttributeString("mode", visible.Mode.ToString());
                    writer.WriteEndElement();
                }
                foreach (TimelineMarker marker in snapshot.Markers) {
                    writer.WriteStartElement("marker");
                    writer.WriteAttributeString("name", marker.Name);
                    writer.WriteAttributeString("time", Number(marker.Time));
                    writer.WriteEndElement();
                }
                foreach (KeyframeSnapshot keyframe in snapshot.Keyframes) {
                    writer.WriteStartElement("keyframe");
                    writer.WriteAttributeString("time", Number(keyframe.TimeSeconds));
                    if (keyframe.Placements.Length != snapshot.Monikers.Count ||
                        keyframe.HasPoses != null && keyframe.HasPoses.Length != keyframe.Placements.Length)
                        throw new InvalidDataException("A keyframe has an incorrect pose count.");
                    for (int i = 0; i < keyframe.Placements.Length; i++) {
                        if (keyframe.HasPoses != null && !keyframe.HasPoses[i]) continue;
                        WritePose(writer, i, keyframe.Placements[i],
                            keyframe.PoseEasing != null && i < keyframe.PoseEasing.Length ? keyframe.PoseEasing[i] : null);
                    }
                    if (keyframe.CameraProjection.HasValue) {
                        WriteCamera(writer, keyframe.CameraProjection.Value, keyframe.CameraEasing);
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            byte[] bytes = Encoding.UTF8.GetBytes(raw.ToString());
            using (var output = new MemoryStream()) {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(bytes, 0, bytes.Length);
                return Convert.ToBase64String(output.ToArray());
            }
        }

        public static ProjectSnapshot Decode(string encoded) {
            byte[] compressed = Convert.FromBase64String(encoded);
            byte[] plain;
            using (var input = new MemoryStream(compressed))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream()) {
                var buffer = new byte[8192];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) != 0) {
                    if (output.Length + read > 32 * 1024 * 1024)
                        throw new InvalidDataException("SC Animator data is too large.");
                    output.Write(buffer, 0, read);
                }
                plain = output.ToArray();
            }
            var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 32 * 1024 * 1024 };
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(Encoding.UTF8.GetString(plain)), readerSettings))
                document.Load(reader);
            XmlElement root = document.DocumentElement;
            if (root == null || root.Name != "SCAnimator" ||
                (root.GetAttribute("version") != "1" && root.GetAttribute("version") != "2"))
                throw new InvalidDataException("Unsupported SC Animator format.");
            bool legacy = root.GetAttribute("version") == "1";
            var snapshot = new ProjectSnapshot {
                FramesPerSecond = int.Parse(root.GetAttribute("fps"), Invariant),
                FadeDurationSeconds = root.HasAttribute("fadeDuration") ? ParseNumber(root.GetAttribute("fadeDuration")) : .4,
                SpeedMultiplier = ParseNumber(root.GetAttribute("speed")),
                SecondsPerSegment = ParseNumber(root.GetAttribute("segment")),
                Easing = (EasingMode)Enum.Parse(typeof(EasingMode), root.GetAttribute("easing"), false),
                Loop = XmlConvert.ToBoolean(root.GetAttribute("loop")),
                CameraEnabled = !root.HasAttribute("cameraEnabled") || XmlConvert.ToBoolean(root.GetAttribute("cameraEnabled")),
                ReversedVisibility = root.HasAttribute("reversedVisibility") &&
                    XmlConvert.ToBoolean(root.GetAttribute("reversedVisibility")),
                CurrentKeyframeIndex = int.Parse(root.GetAttribute("current"), Invariant)
            };
            if (root.HasAttribute("rangeStart") || root.HasAttribute("rangeEnd")) {
                if (!root.HasAttribute("rangeStart") || !root.HasAttribute("rangeEnd"))
                    throw new InvalidDataException("Incomplete playback range.");
                snapshot.HasPlaybackRange = true;
                snapshot.PlaybackRangeStart = ParseNumber(root.GetAttribute("rangeStart"));
                snapshot.PlaybackRangeEnd = ParseNumber(root.GetAttribute("rangeEnd"));
                if (snapshot.PlaybackRangeStart < 0 || snapshot.PlaybackRangeEnd <= snapshot.PlaybackRangeStart)
                    throw new InvalidDataException("Invalid playback range.");
            }
            if (snapshot.FramesPerSecond < 1 || snapshot.FramesPerSecond > 120 ||
                snapshot.FadeDurationSeconds < .1 || snapshot.FadeDurationSeconds > 5 ||
                snapshot.SpeedMultiplier < .25 || snapshot.SpeedMultiplier > 4 ||
                snapshot.SecondsPerSegment <= 0 || !Enum.IsDefined(typeof(EasingMode), snapshot.Easing))
                throw new InvalidDataException("Invalid SC Animator playback settings.");
            bool sawFrame = false, sawInitial = false;
            var markerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var favoriteIds = new HashSet<string>(StringComparer.Ordinal);
            var hingeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string savedLocks = root.GetAttribute("locks");
            double previousTime = -1;
            foreach (XmlNode node in root.ChildNodes) {
                XmlElement element = node as XmlElement;
                if (element == null) continue;
                if (element.Name == "component" && !sawFrame && !sawInitial) {
                    string id = element.GetAttribute("id");
                    if (String.IsNullOrEmpty(id)) throw new InvalidDataException("A component identifier is missing.");
                    snapshot.Monikers.Add(id);
                }
                else if (element.Name == "favorite" && !sawFrame) {
                    string id = element.GetAttribute("id");
                    if (String.IsNullOrEmpty(id) || id.Length > 4096 || !favoriteIds.Add(id))
                        throw new InvalidDataException("Invalid favorite track.");
                    snapshot.FavoriteTracks.Add(id);
                }
                else if (element.Name == "hinge" && !legacy && !sawFrame) {
                    string name = element.GetAttribute("name"), fixedId = element.GetAttribute("fixed"),
                        movingId = element.GetAttribute("moving"), alignId = element.GetAttribute("align"),
                        ownedAlignId = element.GetAttribute("ownedAlign");
                    double ox = ParseNumber(element.GetAttribute("ox")),
                        oy = ParseNumber(element.GetAttribute("oy")), oz = ParseNumber(element.GetAttribute("oz")),
                        dx = ParseNumber(element.GetAttribute("dx")),
                        dy = ParseNumber(element.GetAttribute("dy")), dz = ParseNumber(element.GetAttribute("dz"));
                    double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (String.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 80 ||
                        String.IsNullOrEmpty(fixedId) || String.IsNullOrEmpty(movingId) ||
                        fixedId == movingId || fixedId.Length > 4096 || movingId.Length > 4096 ||
                        !hingeNames.Add(name) || snapshot.Hinges.Count >= 100 ||
                        length < .999 || length > 1.001 || alignId.Length > 4096 ||
                        ownedAlignId.Length > 4096 ||
                        (!String.IsNullOrEmpty(ownedAlignId) && ownedAlignId != alignId))
                        throw new InvalidDataException("Invalid hinge definition.");
                    snapshot.Hinges.Add(new HingeSnapshot { Name = name, FixedId = fixedId,
                        MovingId = movingId, AlignId = alignId, OwnedAlignId = ownedAlignId,
                        Origin = Point.Create(ox, oy, oz),
                        Direction = Direction.Create(dx, dy, dz) });
                }
                else if (element.Name == "plane" && !legacy && !sawInitial && !sawFrame) {
                    string id = element.GetAttribute("id");
                    if (String.IsNullOrEmpty(id)) throw new InvalidDataException("A plane identifier is missing.");
                    var plane = new PlaneTrackSnapshot { Moniker = id };
                    double lastTime = -1;
                    foreach (XmlNode child in element.ChildNodes) {
                        XmlElement key = child as XmlElement;
                        if (key == null || key.Name != "key" || key.ChildNodes.Count != 1 ||
                            !(key.FirstChild is XmlElement) || key.FirstChild.Name != "pose")
                            throw new InvalidDataException("Invalid plane key.");
                        double time = ParseNumber(key.GetAttribute("time"));
                        if (time < 0 || time > 3600 || time <= lastTime)
                            throw new InvalidDataException("Invalid plane key time.");
                        plane.Keys.Add(new PlaneKeySnapshot { Time = time,
                            Placement = ReadPose((XmlElement)key.FirstChild) });
                        lastTime = time;
                    }
                    if (plane.Keys.Count == 0) throw new InvalidDataException("A plane track has no keys.");
                    snapshot.PlaneTracks.Add(plane);
                }
                else if (element.Name == "initial" && !legacy && !sawInitial && !sawFrame) {
                    sawInitial = true;
                    var poses = new Matrix[snapshot.Monikers.Count];
                    var seenInitial = new bool[poses.Length];
                    Matrix? camera = null;
                    foreach (XmlNode child in element.ChildNodes) {
                        XmlElement pose = child as XmlElement;
                        if (pose == null) throw new InvalidDataException("Invalid initial view.");
                        if (pose.Name == "camera" && !camera.HasValue) { camera = ReadCamera(pose); continue; }
                        if (pose.Name != "pose") throw new InvalidDataException("Invalid initial pose.");
                        int index = int.Parse(pose.GetAttribute("index"), Invariant);
                        if (index < 0 || index >= poses.Length || seenInitial[index])
                            throw new InvalidDataException("Invalid initial track index.");
                        poses[index] = ReadPose(pose); seenInitial[index] = true;
                    }
                    if (Array.Exists(seenInitial, value => !value)) throw new InvalidDataException("Incomplete initial view.");
                    snapshot.InitialPlacements = poses;
                    snapshot.InitialCamera = camera;
                }
                else if (element.Name == "visibility" && !legacy && !sawFrame) {
                    int track = int.Parse(element.GetAttribute("track"), Invariant);
                    double time = ParseNumber(element.GetAttribute("time"));
                    VisibilityMode mode;
                    if (track < 0 || track >= snapshot.Monikers.Count + snapshot.PlaneTracks.Count ||
                        time < 0 || time > 3600 ||
                        !Enum.TryParse(element.GetAttribute("mode"), false, out mode) ||
                        !Enum.IsDefined(typeof(VisibilityMode), mode) || mode == VisibilityMode.Default ||
                        track >= snapshot.Monikers.Count &&
                        (mode == VisibilityMode.FadeIn || mode == VisibilityMode.FadeOut))
                        throw new InvalidDataException("Invalid visibility key.");
                    snapshot.VisibilityKeys.Add(new VisibilityKeySnapshot { Track = track, Time = time, Mode = mode });
                }
                else if (element.Name == "marker" && !sawFrame) {
                    string name = element.GetAttribute("name");
                    double time = ParseNumber(element.GetAttribute("time"));
                    if (String.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 80 ||
                        time < 0 || time > 3600 || !markerNames.Add(name) ||
                        snapshot.Markers.Exists(marker => Math.Abs(marker.Time - time) < 1e-6) ||
                        snapshot.Markers.Count >= 200) throw new InvalidDataException("Invalid timeline marker.");
                    snapshot.Markers.Add(new TimelineMarker { Name = name, Time = time });
                }
                else if (element.Name == "preset" && !sawFrame && !legacy) {
                    // v1.7.0 stored pose presets. The feature was removed, but
                    // its models must still load; the next save omits these nodes.
                }
                else if (element.Name == "keyframe") {
                    sawFrame = true;
                    double time = ParseNumber(element.GetAttribute("time"));
                    if (time < 0 || time <= previousTime && snapshot.Keyframes.Count > 0 ||
                        legacy && snapshot.Keyframes.Count == 0 && time != 0)
                        throw new InvalidDataException("Invalid SC Animator keyframe time.");
                    previousTime = time;
                    var poses = new Matrix[snapshot.Monikers.Count];
                    var present = new bool[poses.Length];
                    var poseEasing = new EasingMode?[poses.Length];
                    Matrix? camera = null;
                    EasingMode? cameraEasing = null;
                    int legacyIndex = 0;
                    foreach (XmlNode poseNode in element.ChildNodes) {
                        XmlElement pose = poseNode as XmlElement;
                        if (pose == null) throw new InvalidDataException("Invalid keyframe pose.");
                        if (pose.Name == "camera" && !legacy && !camera.HasValue) {
                            cameraEasing = ParseTransition(pose);
                            camera = ReadCamera(pose);
                            continue;
                        }
                        if (pose.Name != "pose") throw new InvalidDataException("Invalid keyframe pose.");
                        int poseIndex = legacy ? legacyIndex++ : int.Parse(pose.GetAttribute("index"), Invariant);
                        if (poseIndex < 0 || poseIndex >= poses.Length || present[poseIndex])
                            throw new InvalidDataException("Invalid component track index.");
                        poses[poseIndex] = ReadPose(pose);
                        present[poseIndex] = true;
                        poseEasing[poseIndex] = ParseTransition(pose);
                    }
                    if (legacy && legacyIndex != poses.Length || !legacy && !camera.HasValue && Array.TrueForAll(present, x => !x))
                        throw new InvalidDataException("A keyframe is empty or missing component poses.");
                    snapshot.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = time, Placements = poses,
                        HasPoses = present, CameraProjection = camera,
                        PoseEasing = poseEasing, CameraEasing = cameraEasing });
                }
                else throw new InvalidDataException("Unknown SC Animator data element.");
            }
            if ((snapshot.Keyframes.Count > 0) != (snapshot.Monikers.Count > 0) ||
                snapshot.CurrentKeyframeIndex < -1 || snapshot.CurrentKeyframeIndex >= snapshot.Keyframes.Count)
                throw new InvalidDataException("SC Animator data has inconsistent counts.");
            if (snapshot.InitialPlacements != null && snapshot.InitialPlacements.Length != snapshot.Monikers.Count)
                throw new InvalidDataException("Invalid initial pose count.");
            if (savedLocks.Length > 0) {
                if (savedLocks.Length != snapshot.Monikers.Count + snapshot.PlaneTracks.Count + 1)
                    throw new InvalidDataException("Invalid track lock count.");
                snapshot.LockedTracks = new bool[savedLocks.Length];
                for (int i = 0; i < savedLocks.Length; i++) {
                    if (savedLocks[i] != '0' && savedLocks[i] != '1') throw new InvalidDataException("Invalid track lock.");
                    snapshot.LockedTracks[i] = savedLocks[i] == '1';
                }
            }
            for (int track = 0; track < snapshot.Monikers.Count; track++) {
                bool found = false;
                foreach (KeyframeSnapshot frame in snapshot.Keyframes) if (frame.HasPoses[track]) { found = true; break; }
                if (!found) throw new InvalidDataException("A component track has no keyframes.");
            }
            var uniqueVisibility = new HashSet<string>();
            foreach (VisibilityKeySnapshot visible in snapshot.VisibilityKeys) {
                bool exists = false;
                if (visible.Track < snapshot.Monikers.Count) {
                    foreach (KeyframeSnapshot frame in snapshot.Keyframes)
                        if (Math.Abs(frame.TimeSeconds - visible.Time) < 1e-6 && frame.HasPoses[visible.Track])
                            { exists = true; break; }
                } else {
                    foreach (PlaneKeySnapshot key in snapshot.PlaneTracks[visible.Track - snapshot.Monikers.Count].Keys)
                        if (Math.Abs(key.Time - visible.Time) < 1e-6) { exists = true; break; }
                }
                if (!exists || !uniqueVisibility.Add(visible.Track + ":" + Number(visible.Time)))
                    throw new InvalidDataException("Visibility does not match a unique track key.");
            }
            return snapshot;
        }

        private static string Number(double value) {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new InvalidDataException("Non-finite animation value.");
            return value.ToString("R", Invariant);
        }
        private static EasingMode? ParseTransition(XmlElement element) {
            if (!element.HasAttribute("transition")) return null;
            EasingMode mode;
            if (!Enum.TryParse(element.GetAttribute("transition"), false, out mode) ||
                !Enum.IsDefined(typeof(EasingMode), mode)) throw new InvalidDataException("Invalid key transition.");
            return mode;
        }
        private static double ParseNumber(string value) {
            double result = Double.Parse(value, NumberStyles.Float, Invariant);
            if (Double.IsNaN(result) || Double.IsInfinity(result)) throw new InvalidDataException("Non-finite animation value.");
            return result;
        }
        private static double Coordinate(Vector v, Direction axis) { return Vector.Dot(v, axis.UnitVector); }
        private static void WritePose(XmlWriter writer, int index, Matrix placement, EasingMode? transition) {
            Frame frame = placement * LocalFrame;
            writer.WriteStartElement("pose");
            writer.WriteAttributeString("index", index.ToString(Invariant));
            if (transition.HasValue) writer.WriteAttributeString("transition", transition.Value.ToString());
            WriteVector(writer, "p", frame.Origin.Vector);
            WriteVector(writer, "x", frame.DirX.UnitVector);
            WriteVector(writer, "y", frame.DirY.UnitVector);
            writer.WriteEndElement();
        }
        private static void WriteCamera(XmlWriter writer, Matrix camera, EasingMode? transition) {
            Frame frame = camera.Rotation * LocalFrame;
            writer.WriteStartElement("camera");
            if (transition.HasValue) writer.WriteAttributeString("transition", transition.Value.ToString());
            WriteVector(writer, "p", camera.Translation);
            WriteVector(writer, "x", frame.DirX.UnitVector);
            WriteVector(writer, "y", frame.DirY.UnitVector);
            writer.WriteAttributeString("s", Number(camera.Scale));
            writer.WriteEndElement();
        }
        private static Matrix ReadPose(XmlElement pose) {
            Vector p = ReadVector(pose, "p"), x = ReadVector(pose, "x"), y = ReadVector(pose, "y");
            return Matrix.CreateMapping(Frame.Create(
                Point.Create(Coordinate(p, Direction.DirX), Coordinate(p, Direction.DirY), Coordinate(p, Direction.DirZ)),
                Direction.Create(Coordinate(x, Direction.DirX), Coordinate(x, Direction.DirY), Coordinate(x, Direction.DirZ)),
                Direction.Create(Coordinate(y, Direction.DirX), Coordinate(y, Direction.DirY), Coordinate(y, Direction.DirZ))));
        }
        private static Matrix ReadCamera(XmlElement pose) {
            Vector p = ReadVector(pose, "p"), x = ReadVector(pose, "x"), y = ReadVector(pose, "y");
            double scale = ParseNumber(pose.GetAttribute("s"));
            if (scale <= 0) throw new InvalidDataException("Invalid camera scale.");
            Matrix rotation = Matrix.CreateMapping(Frame.Create(Point.Origin,
                Direction.Create(Coordinate(x, Direction.DirX), Coordinate(x, Direction.DirY), Coordinate(x, Direction.DirZ)),
                Direction.Create(Coordinate(y, Direction.DirX), Coordinate(y, Direction.DirY), Coordinate(y, Direction.DirZ))));
            return Matrix.CreateTranslation(p) * Matrix.CreateScale(scale) * rotation;
        }
        private static void WriteVector(XmlWriter writer, string prefix, Vector vector) {
            writer.WriteAttributeString(prefix + "0", Number(Coordinate(vector, Direction.DirX)));
            writer.WriteAttributeString(prefix + "1", Number(Coordinate(vector, Direction.DirY)));
            writer.WriteAttributeString(prefix + "2", Number(Coordinate(vector, Direction.DirZ)));
        }
        private static Vector ReadVector(XmlElement element, string prefix) {
            return Vector.Create(ParseNumber(element.GetAttribute(prefix + "0")),
                ParseNumber(element.GetAttribute(prefix + "1")), ParseNumber(element.GetAttribute(prefix + "2")));
        }
    }
}

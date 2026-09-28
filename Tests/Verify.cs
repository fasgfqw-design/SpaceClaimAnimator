using System;
using System.Collections.Generic;
using System.Reflection;
using Color = System.Drawing.Color;
using System.IO;
using System.IO.Compression;
using System.Text;
using SCAnimator.V261.Engine;
using SpaceClaim.Api.V261.Geometry;
class Verify {
    static int checks;
    static readonly Frame Basis = Frame.Create(Point.Origin, Direction.DirX, Direction.DirY);
    static void Near(double actual, double expected, string label) {
        if (Math.Abs(actual - expected) > 1e-9 || double.IsNaN(actual)) throw new Exception(label + ": " + actual + " != " + expected);
        checks++;
    }
    static void Pose(Matrix actual, Matrix expected, string label) {
        Frame a = actual * Basis, b = expected * Basis;
        Near((a.Origin - b.Origin).Magnitude, 0, label + " origin");
        Near((a.DirX.UnitVector - b.DirX.UnitVector).Magnitude, 0, label + " X");
        Near((a.DirY.UnitVector - b.DirY.UnitVector).Magnitude, 0, label + " Y");
        Near((a.DirZ.UnitVector - b.DirZ.UnitVector).Magnitude, 0, label + " Z");
    }
    static Matrix Rotate(Direction axis, double degrees) { double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r); if (axis == Direction.DirX) return Matrix.CreateMapping(Frame.Create(Point.Origin, Direction.DirX, Direction.Create(0,c,s))); if (axis == Direction.DirY) return Matrix.CreateMapping(Frame.Create(Point.Origin, Direction.Create(c,0,-s), Direction.DirY)); return Matrix.CreateMapping(Frame.Create(Point.Origin, Direction.Create(c,s,0), Direction.Create(-s,c,0))); }
    static Matrix Shift(double x, double y, double z) { return Matrix.CreateTranslation(Vector.Create(x,y,z)); }
    static int Main() {
      try {
        Matrix a = Shift(1,2,3) * Rotate(Direction.DirZ, 20);
        Matrix b = Shift(3,4,5) * Rotate(Direction.DirZ, 100);
        Pose(PlacementInterpolator.Interpolate(a,b,0), a, "start");
        Pose(PlacementInterpolator.Interpolate(a,b,1), b, "end");
        Pose(PlacementInterpolator.Interpolate(a,b,.5), Shift(2,3,4)*Rotate(Direction.DirZ,60), "combined midpoint");
        foreach (Direction axis in new[] { Direction.DirX, Direction.DirY, Direction.DirZ }) {
          Pose(PlacementInterpolator.Interpolate(Matrix.Identity,Rotate(axis,180),.5),Rotate(axis,90),"180 degrees");
          Pose(PlacementInterpolator.Interpolate(Rotate(axis,170),Rotate(axis,-170),.5),Rotate(axis,180),"shortest arc");
          Pose(PlacementInterpolator.Interpolate(Rotate(axis,10),Rotate(axis,10.001),.5),Rotate(axis,10.0005),"near equal");
        }
        var project = (AnimationProject)typeof(AnimationProject).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(null);
        Near(AnimationProject.PreferredTrackName("Valve housing", "Component 1", 0) == "Valve housing" ? 1 : 0,
            1,"part display name replaces generated component name");
        Near(AnimationProject.PreferredTrackName("Valve housing", "Left", 0) == "Valve housing (Left)" ? 1 : 0,
            1,"custom instance suffix remains visible");
        Near(AnimationProject.PreferredTrackName("", "Actuator", 1) == "Actuator" ? 1 : 0,
            1,"component name fallback");
        Near(AnimationProject.PreferredTrackName(null, null, 2) == "Component 3" ? 1 : 0,
            1,"unnamed component fallback");
        var frames = (List<Keyframe>)typeof(AnimationProject).GetField("keyframes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(project);
        frames.Add(new Keyframe(new[] { Matrix.Identity },0));
        frames.Add(new Keyframe(new[] { Shift(1,0,0) },1));
        frames.Add(new Keyframe(new[] { Shift(1,1,0) },2));
        Near(project.TotalFrames,60,"total frames");
        Pose(project.EvaluateFrame(0)[0],Matrix.Identity,"timeline start");
        Pose(project.EvaluateFrame(15)[0],Shift(.5,0,0),"segment 1 midpoint");
        Pose(project.EvaluateFrame(30)[0],Shift(1,0,0),"segment boundary");
        Pose(project.EvaluateFrame(45)[0],Shift(1,.5,0),"segment 2 midpoint");
        Pose(project.EvaluateFrame(60)[0],Shift(1,1,0),"timeline end");
        Pose(project.EvaluateFrame(100)[0],Shift(1,1,0),"clamp end");
        double t = 10.0/30;
        Pose(project.EvaluateFrame(10)[0],Shift(t*t*(3-2*t),0,0),"smoothstep");
        project.Easing = EasingMode.Linear;
        Pose(project.EvaluateFrame(10)[0],Shift(t,0,0),"linear");
        frames.Clear();
        Matrix stationary = Shift(-4,2,1) * Rotate(Direction.DirY,35);
        Matrix[] original = { Matrix.Identity, Shift(0,2,0), stationary };
        frames.Add(new Keyframe(original,0));
        original[0] = Shift(99,0,0);
        frames.Add(new Keyframe(new[] { Shift(2,0,0), Shift(0,4,0)*Rotate(Direction.DirZ,90), stationary },1));
        frames.Add(new Keyframe(new[] { Shift(4,0,0), Shift(0,6,0)*Rotate(Direction.DirZ,180), stationary },2));
        Pose(project.GetKeyframe(0)[0],Matrix.Identity,"capture owns its snapshot");
        Matrix[] copy = project.GetKeyframe(0); copy[0] = Shift(88,0,0);
        Pose(project.GetKeyframe(0)[0],Matrix.Identity,"navigation copy is isolated");
        Near(project.EvaluateFrame(15).Length,3,"three tracks");
        Pose(project.EvaluateFrame(15)[0],Shift(1,0,0),"track A midpoint");
        Pose(project.EvaluateFrame(15)[1],Shift(0,3,0)*Rotate(Direction.DirZ,45),"track B midpoint");
        Pose(project.EvaluateFrame(15)[2],stationary,"unchanged track");
        Pose(project.EvaluateFrame(30)[1],Shift(0,4,0)*Rotate(Direction.DirZ,90),"track B boundary");
        Pose(project.EvaluateFrame(45)[0],Shift(3,0,0),"track A second segment");
        Pose(project.EvaluateFrame(45)[1],Shift(0,5,0)*Rotate(Direction.DirZ,135),"track B second segment");
        // Local child placement stays relative to its moving parent, not world space.
        Matrix[] midpoint = project.EvaluateFrame(15);
        Pose(midpoint[0]*midpoint[1],Shift(1,3,0)*Rotate(Direction.DirZ,45),"parent-child composition");
        Pose(project.EvaluateFrame(60)[2],stationary,"unchanged final track");
        project.Easing = EasingMode.Smooth;
        double eased = t*t*(3-2*t);
        Pose(project.EvaluateFrame(10)[0],Shift(2*eased,0,0),"multi smooth translation");
        Pose(project.EvaluateFrame(10)[1],Shift(0,2+2*eased,0)*Rotate(Direction.DirZ,90*eased),"multi smooth rotation");
        project.SetCurrentKeyframe(2);
        project.Clear();
        Near(project.KeyframeCount,0,"clear keyframes");
        Near(project.ComponentCount,0,"clear components");
        Near(project.CurrentKeyframeIndex,-1,"clear navigation");
        Near(project.EvaluateFrame(0).Length,0,"empty timeline");
        frames.Add(new Keyframe(new[] { Matrix.Identity },0));
        frames.Add(new Keyframe(new[] { Shift(2,0,0) },1));
        frames.Add(new Keyframe(new[] { Shift(4,0,0) },2));
        project.ReplaceKeyframe(1,new[] { Shift(7,0,0) });
        Pose(project.GetKeyframe(0)[0],Matrix.Identity,"update leaves prior keyframe");
        Pose(project.GetKeyframe(1)[0],Shift(7,0,0),"update replaces selected keyframe");
        Pose(project.GetKeyframe(2)[0],Shift(4,0,0),"update leaves next keyframe");
        Near(frames[1].TimeSeconds,1,"update preserves time");
        Pose(project.EvaluateFrame(30)[0],Shift(7,0,0),"playback uses updated pose");
        bool rejectedWrongTrackCount = false;
        try { project.ReplaceKeyframe(1,new Matrix[0]); }
        catch (ArgumentOutOfRangeException) { rejectedWrongTrackCount = true; }
        Near(rejectedWrongTrackCount ? 1 : 0,1,"update rejects incomplete snapshot");
        Pose(project.GetKeyframe(1)[0],Shift(7,0,0),"rejected update is atomic");
        frames.Clear();
        frames.Add(new Keyframe(new[] { Matrix.Identity },0));
        frames.Add(new Keyframe(new[] { Shift(1,0,0) },1));
        frames.Add(new Keyframe(new[] { Shift(1,1,0) },2));
        project.FramesPerSecond = 30;
        project.SpeedMultiplier = 1;
        project.Easing = EasingMode.Linear;
        Near(project.TotalFrames,60,"default total frames");
        Pose(project.EvaluateFrame(15)[0],Shift(.5,0,0),"default sample");
        project.Easing = EasingMode.Smooth;
        Pose(project.EvaluateFrame(6)[0],Shift(.104,0,0),"smooth at 20 percent");
        project.Easing = EasingMode.EaseIn;
        Pose(project.EvaluateFrame(6)[0],Shift(.04,0,0),"ease in at 20 percent");
        project.Easing = EasingMode.EaseOut;
        Pose(project.EvaluateFrame(6)[0],Shift(.36,0,0),"ease out at 20 percent");
        project.Easing = EasingMode.Linear;
        Pose(project.EvaluateFrame(6)[0],Shift(.2,0,0),"linear at 20 percent");
        project.FramesPerSecond = 60;
        Near(project.TotalFrames,120,"60 FPS frame budget");
        Pose(project.EvaluateFrame(30)[0],Shift(.5,0,0),"60 FPS half second");
        project.SpeedMultiplier = 2;
        Near(project.TotalFrames,60,"2x frame budget");
        Pose(project.EvaluateFrame(30)[0],Shift(1,0,0),"2x first boundary");
        project.SpeedMultiplier = .5;
        Near(project.TotalFrames,240,"half-speed frame budget");
        Pose(project.EvaluateFrame(30)[0],Shift(.25,0,0),"half-speed quarter segment");
        Near(frames[1].TimeSeconds,1,"timing setting preserves keyframe time");
        project.FramesPerSecond = 0;
        Near(project.FramesPerSecond,1,"FPS minimum");
        project.FramesPerSecond = 200;
        Near(project.FramesPerSecond,120,"FPS maximum");
        project.SpeedMultiplier = 0;
        Near(project.SpeedMultiplier,.25,"speed minimum");
        project.SpeedMultiplier = 10;
        Near(project.SpeedMultiplier,4,"speed maximum");
        bool rejectedNaN = false;
        try { project.SpeedMultiplier = double.NaN; }
        catch (ArgumentOutOfRangeException) { rejectedNaN = true; }
        Near(rejectedNaN ? 1 : 0,1,"reject NaN speed");
        var saved = new ProjectSnapshot {
            FramesPerSecond = 48, SpeedMultiplier = 2, SecondsPerSegment = 1.25,
            Easing = EasingMode.EaseOut, Loop = true, CurrentKeyframeIndex = 1
        };
        saved.Monikers.Add("persistent-component-A");
        saved.Monikers.Add("persistent-component-B");
        saved.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 0,
            Placements = new[] { Matrix.Identity, Shift(1,2,3)*Rotate(Direction.DirX,37) } });
        saved.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 1.25,
            Placements = new[] { Shift(2,0,0)*Rotate(Direction.DirZ,119), Shift(-1,4,2)*Rotate(Direction.DirY,92) } });
        saved.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 2.5,
            Placements = new[] { Shift(4,0,0)*Rotate(Direction.DirZ,178), Shift(-2,5,2)*Rotate(Direction.DirY,145) } });
        string serialized = ProjectPersistence.Encode(saved);
        ProjectSnapshot loaded = ProjectPersistence.Decode(serialized);
        Near(loaded.FramesPerSecond,48,"persist FPS");
        Near(loaded.SpeedMultiplier,2,"persist speed");
        Near(loaded.SecondsPerSegment,1.25,"persist segment spacing");
        Near((int)loaded.Easing,(int)EasingMode.EaseOut,"persist easing");
        Near(loaded.Loop ? 1 : 0,1,"persist loop");
        Near(loaded.CurrentKeyframeIndex,1,"persist selected index");
        Near(loaded.Monikers.Count,2,"persist component count");
        Near(loaded.Monikers[0] == saved.Monikers[0] ? 1 : 0,1,"persist component id");
        Near(loaded.Keyframes.Count,3,"persist keyframe count");
        for (int i=0; i<saved.Keyframes.Count; i++) {
            Near(loaded.Keyframes[i].TimeSeconds,saved.Keyframes[i].TimeSeconds,"persist keyframe time");
            for (int j=0; j<2; j++) Pose(loaded.Keyframes[i].Placements[j],saved.Keyframes[i].Placements[j],"persist pose");
        }
        bool rejectedTruncated = false;
        try { ProjectPersistence.Decode(serialized.Substring(0,serialized.Length/2)); }
        catch (Exception) { rejectedTruncated = true; }
        Near(rejectedTruncated ? 1 : 0,1,"reject truncated recording");
        var invalid = new ProjectSnapshot();
        invalid.Monikers.Add("A");
        invalid.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 0, Placements = new Matrix[0] });
        bool rejectedIncomplete = false;
        try { ProjectPersistence.Decode(ProjectPersistence.Encode(invalid)); }
        catch (System.IO.InvalidDataException) { rejectedIncomplete = true; }
        Near(rejectedIncomplete ? 1 : 0,1,"reject incomplete recording");
        // Different components can have keys at different times.
        frames.Clear();
        frames.Add(new Keyframe(new[] { Matrix.Identity, Shift(0,0,0) },
            new[] { true, true }, null, 0));
        frames.Add(new Keyframe(new[] { Shift(10,0,0), Matrix.Identity },
            new[] { true, false }, null, 1));
        frames.Add(new Keyframe(new[] { Matrix.Identity, Shift(0,20,0) },
            new[] { false, true }, null, 2));
        project.Easing = EasingMode.Linear;
        Pose(project.EvaluateAt(1)[0],Shift(10,0,0),"sparse A at its key");
        Pose(project.EvaluateAt(1)[1],Shift(0,10,0),"sparse B interpolates across A key");
        Pose(project.EvaluateAt(2)[0],Shift(10,0,0),"sparse A holds last pose");
        var sparse = new ProjectSnapshot();
        sparse.Monikers.Add("A"); sparse.Monikers.Add("B");
        sparse.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 0,
            Placements = new[] { Matrix.Identity, Matrix.Identity }, HasPoses = new[] { true, true },
            CameraProjection = Matrix.CreateTranslation(Vector.Create(0,0,10)) * Matrix.CreateScale(2) });
        sparse.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 1,
            Placements = new[] { Shift(10,0,0), Matrix.Identity }, HasPoses = new[] { true, false } });
        sparse.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 2,
            Placements = new[] { Matrix.Identity, Shift(0,20,0) }, HasPoses = new[] { false, true },
            CameraProjection = Matrix.CreateTranslation(Vector.Create(0,0,30)) * Matrix.CreateScale(4) });
        ProjectSnapshot sparseLoaded = ProjectPersistence.Decode(ProjectPersistence.Encode(sparse));
        Near(sparseLoaded.Keyframes[1].HasPoses[0] ? 1 : 0,1,"sparse A present");
        Near(sparseLoaded.Keyframes[1].HasPoses[1] ? 1 : 0,0,"sparse B absent");
        Near(sparseLoaded.Keyframes[0].CameraProjection.Value.Scale,2,"camera scale saved");
        Near(sparseLoaded.Keyframes[2].CameraProjection.Value.Translation.Magnitude,30,"camera position saved");
        Pose(sparseLoaded.Keyframes[2].Placements[1],Shift(0,20,0),"sparse B saved");
        var later = new ProjectSnapshot();
        later.Monikers.Add("A");
        later.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 1, Placements = new[] { Shift(1,0,0) } });
        later.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 2, Placements = new[] { Shift(2,0,0) } });
        Near(ProjectPersistence.Decode(ProjectPersistence.Encode(later)).Keyframes[0].TimeSeconds,1,
            "v2 permits removed start key");
        // Repackage the old full-pose XML format to exercise the v1 reader.
        byte[] v2 = Convert.FromBase64String(ProjectPersistence.Encode(saved));
        string xml;
        using (var input = new MemoryStream(v2))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream()) { gzip.CopyTo(output); xml = Encoding.UTF8.GetString(output.ToArray()); }
        xml = xml.Replace("version=\"2\"", "version=\"1\"").Replace(" index=\"0\"", "").Replace(" index=\"1\"", "");
        string oldEncoded;
        using (var output = new MemoryStream()) {
            using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) {
                byte[] bytes = Encoding.UTF8.GetBytes(xml);
                gzip.Write(bytes, 0, bytes.Length);
            }
            oldEncoded = Convert.ToBase64String(output.ToArray());
        }
        ProjectSnapshot oldLoaded = ProjectPersistence.Decode(oldEncoded);
        Near(oldLoaded.Keyframes.Count,3,"v1 recording loads");
        Pose(oldLoaded.Keyframes[2].Placements[1],saved.Keyframes[2].Placements[1],"v1 pose loads");
        AnimationLibrarySnapshot migrated = ProjectPersistence.DecodeLibrary(serialized);
        Near(migrated.Animations.Count,1,"v2 becomes one animation");
        Near(migrated.Animations[0].Name == "Animation 1" ? 1 : 0,1,"legacy animation name");
        Near(ProjectPersistence.DecodeLibrary(oldEncoded).Animations.Count,1,"v1 becomes one animation");
        var library = new AnimationLibrarySnapshot { ActiveIndex = 1 };
        library.Animations.Add(new NamedAnimationSnapshot { Name = "Opening", Project = saved });
        library.Animations.Add(new NamedAnimationSnapshot { Name = "Closing", Project = sparse });
        AnimationLibrarySnapshot reloaded = ProjectPersistence.DecodeLibrary(ProjectPersistence.EncodeLibrary(library));
        Near(reloaded.ActiveIndex,1,"active animation persists");
        Near(reloaded.Animations.Count,2,"two animations persist");
        Near(reloaded.Animations[0].Name == "Opening" ? 1 : 0,1,"first name persists");
        Near(reloaded.Animations[1].Name == "Closing" ? 1 : 0,1,"second name persists");
        Near(reloaded.Animations[0].Project.FramesPerSecond,48,"first clip settings persist");
        Near(reloaded.Animations[1].Project.FramesPerSecond,30,"second clip settings remain independent");
        library.Scenario.AnimationNames.Add("Opening");
        library.Scenario.AnimationNames.Add("Closing");
        library.Scenario.AnimationNames.Add("Opening");
        library.Scenario.Repeats = 2;
        library.Scenario.PingPong = true;
        var scenarioReloaded = ProjectPersistence.DecodeLibrary(ProjectPersistence.EncodeLibrary(library)).Scenario;
        Near(scenarioReloaded.AnimationNames.Count,3,"scenario order persists");
        Near(scenarioReloaded.AnimationNames[2] == "Opening" ? 1 : 0,1,"repeated clip persists");
        Near(scenarioReloaded.Repeats,2,"scenario repeat count persists");
        Near(scenarioReloaded.PingPong ? 1 : 0,1,"scenario pingpong persists");
        List<ScenarioEntry> order = scenarioReloaded.BuildOrder();
        Near(order.Count,12,"scenario playback expands repeat and reverse passes");
        Near(order[3].Name == "Opening" && order[3].Reverse ? 1 : 0,1,"reverse pass starts with last clip");
        Near(order[5].Name == "Opening" && order[5].Reverse ? 1 : 0,1,"reverse pass ends with first clip");
        Near(order[6].Name == "Opening" && !order[6].Reverse ? 1 : 0,1,"second repeat restarts forward");
        string v4Encoded = ProjectPersistence.EncodeLibrary(library), v4Xml;
        using (var input = new MemoryStream(Convert.FromBase64String(v4Encoded)))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream()) { gzip.CopyTo(output); v4Xml = Encoding.UTF8.GetString(output.ToArray()); }
        int scenarioStart = v4Xml.IndexOf("<scenario", StringComparison.Ordinal);
        int scenarioEnd = v4Xml.IndexOf("</scenario>", scenarioStart, StringComparison.Ordinal) + "</scenario>".Length;
        string v3Xml = v4Xml.Replace("version=\"4\"", "version=\"3\"").Remove(scenarioStart, scenarioEnd - scenarioStart);
        string v3Encoded;
        using (var output = new MemoryStream()) {
            using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) {
                byte[] bytes = Encoding.UTF8.GetBytes(v3Xml);
                gzip.Write(bytes, 0, bytes.Length);
            }
            v3Encoded = Convert.ToBase64String(output.ToArray());
        }
        Near(ProjectPersistence.DecodeLibrary(v3Encoded).Scenario.AnimationNames.Count,0,
            "v3 animation library loads with empty scenario");
        library.Scenario.AnimationNames[0] = "Missing";
        bool rejectedMissingScenarioClip = false;
        try { ProjectPersistence.EncodeLibrary(library); } catch (InvalidDataException) { rejectedMissingScenarioClip = true; }
        Near(rejectedMissingScenarioClip ? 1 : 0,1,"reject scenario reference to missing clip");
        library.Scenario.AnimationNames[0] = "Opening";
        library.Animations.Add(new NamedAnimationSnapshot { Name = "New", Project = new ProjectSnapshot() });
        Near(ProjectPersistence.DecodeLibrary(ProjectPersistence.EncodeLibrary(library)).Animations[2].Project.Keyframes.Count,
            0,"empty new animation persists");
        library.Animations.RemoveAt(2);
        Pose(reloaded.Animations[0].Project.Keyframes[2].Placements[1],saved.Keyframes[2].Placements[1],"first clip pose persists");
        Pose(reloaded.Animations[1].Project.Keyframes[2].Placements[1],sparse.Keyframes[2].Placements[1],"second clip pose persists");
        library.Animations[1].Name = "Opening";
        bool rejectedDuplicateName = false;
        try { ProjectPersistence.EncodeLibrary(library); } catch (InvalidDataException) { rejectedDuplicateName = true; }
        Near(rejectedDuplicateName ? 1 : 0,1,"reject duplicate animation names");
        var targets = (List<SpaceClaim.Api.V261.Component>)typeof(AnimationProject)
            .GetField("components",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(project);
        targets.Add(null); targets.Add(null); // Only track count is needed for core timeline tests.
        frames.Clear();
        frames.Add(new Keyframe(new[] { Matrix.Identity, Matrix.Identity }, new[] { true, true },
            Matrix.CreateTranslation(Vector.Create(0,0,10)), 0));
        frames.Add(new Keyframe(new[] { Shift(10,0,0), Matrix.Identity }, new[] { true, false }, null, 1));
        frames.Add(new Keyframe(new[] { Matrix.Identity, Shift(0,20,0) }, new[] { false, true },
            Matrix.CreateTranslation(Vector.Create(0,0,30)), 2));
        Near(project.RetimeTrackKeyCore(0,1,1.5) ? 1 : 0,1,"retime component succeeds");
        Pose(project.EvaluateAt(1.5)[0],Shift(10,0,0),"retime keeps component pose");
        Near(project.HasTrackKey(0,1) ? 1 : 0,0,"retime clears old marker");
        Near(project.HasTrackKey(0,1.5) ? 1 : 0,1,"retime creates new marker");
        Near(project.RetimeTrackKeyCore(0,1.5,0) ? 1 : 0,0,"retime rejects same-track collision");
        Near(project.RetimeTrackKeyCore(0,1.5,2) ? 1 : 0,1,"retime merges other-track event");
        Pose(project.EvaluateAt(2)[0],Shift(10,0,0),"merged A pose");
        Pose(project.EvaluateAt(2)[1],Shift(0,20,0),"merged B pose");
        Near(project.RetimeTrackKeyCore(2,0,1) ? 1 : 0,1,"retime camera succeeds");
        Near(project.HasTrackKey(2,0) ? 1 : 0,0,"camera old marker removed");
        Near(project.EvaluateCameraAt(1).Value.Translation.Magnitude,10,"camera projection retained");
        frames.Clear();
        frames.Add(new Keyframe(new[] { Matrix.Identity, Matrix.Identity }, new[] { true, true }, null, 0));
        frames.Add(new Keyframe(new[] { Shift(3,0,0), Matrix.Identity }, new[] { true, false }, null, 1));
        frames.Add(new Keyframe(new[] { Matrix.Identity, Shift(0,4,0) }, new[] { false, true }, null, 2));
        Near(project.MoveTrackKeysCore(new[] { new TrackKeyRef(0,0), new TrackKeyRef(0,1) },1) ? 1 : 0,1,"selected keys can shift into each other's former times");
        Near(project.HasTrackKey(0,2) ? 1 : 0,1,"overlapping shift reaches target");
        Near(project.UndoTrackEditCore() ? 1 : 0,1,"undo overlapping shift");
        var group = new[] { new TrackKeyRef(0,1), new TrackKeyRef(1,2) };
        Near(project.MoveTrackKeysCore(group,-1) ? 1 : 0,0,"group collision rejects atomically");
        Near(project.HasTrackKey(0,1) ? 1 : 0,1,"collision preserves source A");
        Near(project.HasTrackKey(1,2) ? 1 : 0,1,"collision preserves source B");
        Near(project.MoveTrackKeysCore(group,1) ? 1 : 0,1,"group move succeeds");
        Pose(project.EvaluateAt(2)[0],Shift(3,0,0),"group move pose A");
        Pose(project.EvaluateAt(3)[1],Shift(0,4,0),"group move pose B");
        Near(project.UndoTrackEditCore() ? 1 : 0,1,"undo group move");
        Near(project.HasTrackKey(0,1) ? 1 : 0,1,"undo restores A");
        Near(project.RedoTrackEditCore() ? 1 : 0,1,"redo group move");
        var moved = new[] { new TrackKeyRef(0,2), new TrackKeyRef(1,3) };
        TrackKeyClipboard copied = project.CopyTrackKeys(moved);
        TrackKeyRef[] pasted;
        Near(project.PasteTrackKeysCore(copied,0,out pasted) ? 1 : 0,0,"paste collision rejects atomically");
        Near(project.PasteTrackKeysCore(copied,4,out pasted) ? 1 : 0,1,"paste group succeeds");
        Near(pasted.Length,2,"paste selects both markers");
        Pose(project.EvaluateAt(4)[0],Shift(3,0,0),"pasted pose A");
        Pose(project.EvaluateAt(5)[1],Shift(0,4,0),"pasted pose B");
        Near(project.DeleteTrackKeysCore(pasted) ? 1 : 0,1,"group delete succeeds");
        Near(project.UndoTrackEditCore() ? 1 : 0,1,"undo group delete");
        Near(project.HasTrackKey(0,4) ? 1 : 0,1,"undo restores pasted A");
        Near(project.DeleteTrackKeysCore(new[] { new TrackKeyRef(0,0), new TrackKeyRef(0,2), new TrackKeyRef(0,4) }) ? 1 : 0,0,"delete preserves component anchor");
        Near(project.HasTrackKey(0,4) ? 1 : 0,1,"rejected delete keeps keys");
        project.Clear();
        Near(project.PasteTrackKeysCore(copied,6,out pasted) ? 1 : 0,0,"clipboard invalid after project reset");
        targets.Add(null); targets.Add(null);
        var monikers = (List<string>)typeof(AnimationProject)
            .GetField("componentMonikers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(project);
        monikers.Add("A"); monikers.Add("B");
        frames.Add(new Keyframe(new[] { Matrix.Identity, Matrix.Identity }, new[] { true, true },
            Matrix.CreateTranslation(Vector.Create(0,0,10)), 0));
        frames.Add(new Keyframe(new[] { Shift(2,0,0), Shift(0,2,0) }, new[] { true, true }, null, 1));
        Near(project.AddComponentsCore(new[] { (SpaceClaim.Api.V261.Component)null }, new[] { "C" },
            new[] { Shift(0,0,5) }, .5) ? 1 : 0,1,"add component midway");
        Near(project.ComponentCount,3,"component count grows");
        Near(project.GetTrackTimes(2).Length,1,"new component has one anchor");
        Near(project.GetTrackTimes(2)[0],.5,"anchor at playhead");
        Pose(project.EvaluateAt(0)[0],Matrix.Identity,"existing start pose survives expansion");
        Pose(project.EvaluateAt(1)[1],Shift(0,2,0),"existing end pose survives expansion");
        Pose(project.EvaluateAt(0)[2],Shift(0,0,5),"new component held before anchor");
        Near(project.HasTrackKey(3,0) ? 1 : 0,1,"camera key survives track expansion");
        Near(project.AddComponentsCore(new[] { (SpaceClaim.Api.V261.Component)null }, new[] { "C" },
            new[] { Matrix.Identity }, .75) ? 1 : 0,0,"duplicate component rejected");
        Near(project.ComponentCount,3,"rejected add is atomic");
        var captureLibrary = (AnimationLibrarySnapshot)typeof(AnimationProject)
            .GetMethod("CreateLibrarySnapshot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(project,null);
        AnimationLibrarySnapshot recordedLibrary = ProjectPersistence.DecodeLibrary(ProjectPersistence.EncodeLibrary(captureLibrary));
        Near(recordedLibrary.Animations[0].Project.Monikers.Count,3,"active animation is included in save");
        Near(recordedLibrary.Animations[0].Project.Keyframes.Count,3,"new anchor persists in active animation");
        Near(project.RemoveComponentTrackCore(1) ? 1 : 0,1,"remove middle component track");
        Near(project.ComponentCount,2,"remaining component count");
        Near(project.GetTrackTimes(1)[0],.5,"later component shifts to former track index");
        Pose(project.EvaluateAt(1)[0],Shift(2,0,0),"first component keys survive removal");
        Pose(project.EvaluateAt(0)[1],Shift(0,0,5),"later component pose survives removal");
        Near(project.HasTrackKey(2,0) ? 1 : 0,1,"camera shifts after component removal");
        var prunedLibrary = (AnimationLibrarySnapshot)typeof(AnimationProject)
            .GetMethod("CreateLibrarySnapshot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(project,null);
        var prunedReloaded = ProjectPersistence.DecodeLibrary(ProjectPersistence.EncodeLibrary(prunedLibrary));
        Near(prunedReloaded.Animations[0].Project.Monikers.Count,2,"removed component absent after save");
        Near(prunedReloaded.Animations[0].Project.Keyframes.Count,3,"remaining timeline persists");
        Near(project.RemoveComponentTrackCore(2) ? 1 : 0,0,"camera cannot be removed as component");
        Near(project.RemoveComponentTrackCore(1) ? 1 : 0,1,"remove newly added track");
        Near(project.RemoveComponentTrackCore(0) ? 1 : 0,1,"remove final component");
        Near(project.ComponentCount,0,"final removal empties animation");
        Near(project.KeyframeCount,0,"final removal clears camera-only frames");
        var slots = (List<NamedAnimationSnapshot>)typeof(AnimationProject)
            .GetField("animations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(project);
        slots.Add(new NamedAnimationSnapshot { Name = "Other", Project = new ProjectSnapshot() });
        Near(project.DeleteAnimationCore(0) ? 1 : 0,1,"delete active animation with another available");
        Near(project.AnimationCount,1,"one animation remains");
        Near(project.AnimationNames[0] == "Other" ? 1 : 0,1,"remaining animation selected");
        Near(project.DeleteAnimationCore(0) ? 1 : 0,1,"delete last animation");
        Near(project.AnimationCount,1,"empty placeholder remains");
        Near(project.AnimationNames[0] == "Animation 1" ? 1 : 0,1,"placeholder name");
        var afterDelete = (AnimationLibrarySnapshot)typeof(AnimationProject)
            .GetMethod("CreateLibrarySnapshot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(project,null);
        Near(ProjectPersistence.DecodeLibrary(ProjectPersistence.EncodeLibrary(afterDelete)).Animations[0].Project.Keyframes.Count,
            0,"deleted final animation persists as empty slot");
        var advanced = (AnimationProject)typeof(AnimationProject)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(null);
        var advancedFrames = (List<Keyframe>)typeof(AnimationProject)
            .GetField("keyframes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(advanced);
        var advancedComponents = (List<SpaceClaim.Api.V261.Component>)typeof(AnimationProject)
            .GetField("components",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(advanced);
        var advancedMonikers = (List<string>)typeof(AnimationProject)
            .GetField("componentMonikers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(advanced);
        advancedComponents.Add(null); advancedMonikers.Add("advanced-A");
        advancedFrames.Add(new Keyframe(new[] { Matrix.Identity }, new[] { true },
            Matrix.CreateTranslation(Vector.Create(0,0,10)), 0));
        advancedFrames.Add(new Keyframe(new[] { Shift(10,0,0) }, new[] { true },
            Matrix.CreateTranslation(Vector.Create(0,0,20)), 1));
        advanced.Easing = EasingMode.Linear;
        Near(advanced.SetKeyEasing(0,0,EasingMode.EaseIn) ? 1 : 0,1,"per-key ease set");
        Pose(advanced.EvaluateAt(.5)[0],Shift(5,0,0),"global easing overrides old per-key setting");
        Near(advanced.SetKeyEasing(1,0,EasingMode.EaseOut) ? 1 : 0,1,"camera key has its own transition");
        Near(advanced.EvaluateCameraAt(.5).Value.Translation.Magnitude,15,"camera follows the global easing");
        advanced.Easing = EasingMode.EaseIn;
        Pose(advanced.EvaluateAt(.5)[0],Shift(2.5,0,0),"global Ease In applies to every component");
        Near(advanced.EvaluateCameraAt(.5).Value.Translation.Magnitude,12.5,"global Ease In applies to camera");
        Near(advanced.SetPlaybackIn(.25) ? 1 : 0,1,"set playback in");
        Near(advanced.SetPlaybackOut(.75) ? 1 : 0,1,"set playback out");
        Near(advanced.TotalFrames,15,"range changes frame budget");
        Near(advanced.PlaybackTimeAtFrame(0),.25,"range starts at in point");
        Near(advanced.PlaybackTimeAtFrame(15),.75,"range clamps at out point");
        advanced.CameraEnabled = false;
        advanced.FadeDurationSeconds = 1.25;
        advanced.SetInitialState(new[] { Shift(-2,0,0) }, Matrix.CreateTranslation(Vector.Create(0,0,8)));
        Near(advanced.SetTrackLocked(0,true) ? 1 : 0,1,"lock component track");
        Near(advanced.RetimeTrackKeyCore(0,0,.2) ? 1 : 0,0,"locked key cannot move");
        Near(advanced.RemoveComponentTrackCore(0) ? 1 : 0,0,"locked track cannot be removed");
        ProjectSnapshot advancedSaved = ProjectPersistence.Decode(ProjectPersistence.Encode(advanced.CreateSnapshot()));
        Near(advancedSaved.CameraEnabled ? 1 : 0,0,"camera toggle persists");
        Near(advancedSaved.FadeDurationSeconds,1.25,"fade duration persists with its animation");
        Near(advancedSaved.HasPlaybackRange ? 1 : 0,1,"range persists");
        Near(advancedSaved.PlaybackRangeEnd,.75,"out point persists");
        Near(advancedSaved.LockedTracks[0] ? 1 : 0,1,"lock persists");
        Near((int)advancedSaved.Keyframes[0].PoseEasing[0],(int)EasingMode.EaseIn,"key easing persists");
        Near((int)advancedSaved.Keyframes[0].CameraEasing.Value,(int)EasingMode.EaseOut,"camera transition persists");
        Pose(advancedSaved.InitialPlacements[0],Shift(-2,0,0),"initial component view persists");
        Near(advancedSaved.InitialCamera.Value.Translation.Magnitude,8,"initial camera persists");
        Near(advanced.SetTrackLocked(0,false) ? 1 : 0,1,"unlock component track");
        Near(advanced.MoveTrackKeysCore(new[] { new TrackKeyRef(0,0) },.1) ? 1 : 0,1,"retime eased key");
        Near((int)advanced.GetKeyEasing(0,.1).Value,(int)EasingMode.EaseIn,"transition follows retimed key");
        Near(advanced.UndoTrackEditCore() ? 1 : 0,1,"undo retimed eased key");
        Near(advanced.RemoveComponentTrackCore(0) ? 1 : 0,1,"remove unlocked final track");
        Near(advanced.UndoTrackEditCore() ? 1 : 0,1,"undo deleted component track");
        Near(advanced.ComponentCount,1,"undo restores component binding");
        Pose(advanced.GetInitialPlacements()[0],Shift(-2,0,0),"undo restores initial view");
        Near(advanced.RedoTrackEditCore() ? 1 : 0,1,"redo deleted component track");
        Near(advanced.ComponentCount,0,"redo removes component again");
        Near(ProjectPersistence.Decode(ProjectPersistence.Encode(advanced.CreateSnapshot())).Monikers.Count,
            0,"empty animation with camera row serializes");
        Near(advanced.UndoTrackEditCore() ? 1 : 0,1,"restore track before deleting animation");
        Near(advanced.DeleteAnimationCore(0) ? 1 : 0,1,"delete last animation with undo history");
        Near(advanced.UndoTrackEditCore() ? 1 : 0,1,"undo deleted animation");
        Near(advanced.ComponentCount,1,"undo animation restores component");
        Near(advanced.RedoTrackEditCore() ? 1 : 0,1,"redo deleted animation");
        Near(advanced.ComponentCount,0,"redo animation removes component");
        Near(advanced.UndoTrackEditCore() ? 1 : 0,1,"undo animation after redo");
        var emptyLock = (AnimationProject)typeof(AnimationProject)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(null);
        Near(emptyLock.AddComponentsCore(new[] { (SpaceClaim.Api.V261.Component)null },
            new[] { "first" }, new[] { Matrix.Identity }, 0, Matrix.Identity) ? 1 : 0,
            1,"Add components initializes an empty animation");
        Near(emptyLock.KeyframeCount,1,"initial component key is created");
        Near(emptyLock.ComponentCount,1,"initial component track is created");
        Near(emptyLock.UndoTrackEditCore() ? 1 : 0,1,"undo first track addition");
        Near(emptyLock.KeyframeCount,0,"undo returns to empty animation");
        Near(emptyLock.RedoTrackEditCore() ? 1 : 0,1,"redo first track addition");
        Near(emptyLock.ComponentCount,1,"redo restores initial track");
        var restoreMethod = typeof(AnimationProject).GetMethod("Restore",BindingFlags.Instance|BindingFlags.NonPublic);
        restoreMethod.Invoke(emptyLock,new object[] { new ProjectSnapshot { LockedTracks = new[] { true } }, false });
        Near(emptyLock.IsTrackLocked(0) ? 1 : 0,1,"camera lock restores without shifting");
        var planeTest = (AnimationProject)typeof(AnimationProject)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(null);
        Near(planeTest.AddComponentsCore(new[] { (SpaceClaim.Api.V261.Component)null },
            new[] { "body-id" }, new[] { Matrix.Identity }, 0) ? 1 : 0,1,"prepare plane animation");
        Near(planeTest.AddPlaneTrackCore(null,"plane-id",Matrix.Identity,0) ? 1 : 0,1,"add plane track");
        var planeList = (List<PlaneTrack>)typeof(AnimationProject)
            .GetField("planeTracks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(planeTest);
        planeList[0].Keys.Add(1,Shift(10,0,0));
        Near(planeTest.SetKeyVisibilityCore(1,0,VisibilityMode.Hide) ? 1 : 0,1,
            "plane visibility can be keyed");
        Near(planeTest.UndoTrackEditCore() ? 1 : 0,1,"undo plane visibility key");
        Near(planeTest.GetKeyVisibility(1,0) == VisibilityMode.Default ? 1 : 0,1,
            "undo clears plane visibility");
        Near(planeTest.RedoTrackEditCore() ? 1 : 0,1,"redo plane visibility key");
        Near(planeTest.SetKeyVisibilityCore(1,0,VisibilityMode.FadeIn) ? 1 : 0,0,
            "plane fade is rejected");
        Near(planeTest.SetKeyVisibilityCore(1,0,VisibilityMode.FadeOut) ? 1 : 0,0,
            "plane fade out is rejected");
        double visibilityTime;
        Near(planeTest.VisibilityAt(1,.5,out visibilityTime) == VisibilityMode.Hide ? 1 : 0,1,
            "plane visibility holds between keys");
        Near(planeTest.SetKeyVisibilityCore(0,0,VisibilityMode.FadeIn) ? 1 : 0,1,
            "component fade can be keyed");
        Near(AnimationProject.FadeOpacity(VisibilityMode.FadeIn,0,0,.40),0,
            "fade starts fully hidden");
        Near(AnimationProject.FadeOpacity(VisibilityMode.FadeIn,.20,0,.40),.5,
            "fade reaches half opacity halfway through");
        Near(AnimationProject.FadeOpacity(VisibilityMode.FadeIn,.5,0,1),.5,
            "fade duration changes transition timing");
        Near(AnimationProject.FadeOpacity(VisibilityMode.FadeOut,0,0,.4),1,
            "fade out starts fully visible");
        Near(AnimationProject.FadeOpacity(VisibilityMode.FadeOut,.2,0,.4),.5,
            "fade out reaches half opacity halfway through");
        Near(AnimationProject.FadeOpacity(VisibilityMode.FadeOut,.4,0,.4),0,
            "fade out ends hidden");
        Near(planeTest.SetKeyVisibilityCore(0,0,VisibilityMode.FadeOut) ? 1 : 0,1,
            "component fade out can be keyed");
        ProjectSnapshot fadeOutSaved = ProjectPersistence.Decode(ProjectPersistence.Encode(planeTest.CreateSnapshot()));
        Near(fadeOutSaved.VisibilityKeys.Exists(key => key.Track == 0 && key.Mode == VisibilityMode.FadeOut) ? 1 : 0,1,
            "fade out survives model persistence");
        Near(AnimationProject.FadeAlpha(255,0),0,"alpha begins at zero");
        Near(AnimationProject.FadeAlpha(255,.5),128,"alpha has intermediate values");
        Near(AnimationProject.FadeAlpha(128,1),128,"alpha ends at original transparency");
        Color fadeHalf = AnimationProject.FadeColor(Color.White, Color.Black, .5);
        Near(fadeHalf.R,128,"fade blends red channel at midpoint");
        Near(fadeHalf.G,128,"fade blends green channel at midpoint");
        Near(fadeHalf.B,128,"fade blends blue channel at midpoint");
        Near(AnimationProject.FadeColor(Color.White,Color.Blue,1).ToArgb(),Color.Blue.ToArgb(),
            "fade reaches the target color");
        Near(planeTest.CameraTrackIndex,2,"camera follows plane track");
        Pose(planeTest.EvaluatePlanesAt(.5)[0],Shift(5,0,0),"plane pose interpolates");
        Near(planeTest.MoveTrackKeysCore(new[] { new TrackKeyRef(1,1) },.5) ? 1 : 0,1,"retime plane key");
        Near(planeTest.HasTrackKey(1,1.5) ? 1 : 0,1,"plane key moved");
        Near(planeTest.UndoTrackEditCore() ? 1 : 0,1,"undo plane key retime");
        Near(planeTest.HasTrackKey(1,1) ? 1 : 0,1,"plane key restored");
        Near(planeTest.MoveTrackKeysCore(new[] { new TrackKeyRef(1,0) },.25) ? 1 : 0,1,
            "retime plane key with visibility");
        Near(planeTest.GetKeyVisibility(1,.25) == VisibilityMode.Hide ? 1 : 0,1,
            "visibility follows the retimed plane key");
        Near(planeTest.UndoTrackEditCore() ? 1 : 0,1,"undo plane visibility retime");
        ProjectSnapshot planeSaved = ProjectPersistence.Decode(ProjectPersistence.Encode(planeTest.CreateSnapshot()));
        Near(planeSaved.PlaneTracks.Count,1,"plane track persists");
        Near(planeSaved.PlaneTracks[0].Keys.Count,2,"plane keys persist");
        Near(planeSaved.VisibilityKeys.Count,2,"visibility keys persist");
        Near(planeTest.RemovePlaneTrackCore(1) ? 1 : 0,1,"remove plane track");
        Near(planeTest.PlaneCount,0,"plane track removed");
        Near(planeTest.UndoTrackEditCore() ? 1 : 0,1,"undo plane track removal");
        Near(planeTest.PlaneCount,1,"undo restores plane track");
        var planeBatch = (AnimationProject)typeof(AnimationProject)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(null);
        Near(planeBatch.AddComponentsCore(new[] { (SpaceClaim.Api.V261.Component)null },
            new[] { "batch-body" }, new[] { Matrix.Identity }, 0) ? 1 : 0,1,"prepare grouped plane addition");
        Near(planeBatch.AddPlaneTrackCore(null,"plane-A",Matrix.Identity,0) ? 1 : 0,1,"add first selected plane");
        Near(planeBatch.AddPlaneTrackCore(null,"plane-B",Shift(2,0,0),0,false) ? 1 : 0,1,
            "add second selected plane in same operation");
        Near(planeBatch.UndoTrackEditCore() ? 1 : 0,1,"undo grouped plane addition");
        Near(planeBatch.PlaneCount,0,"one undo removes both selected planes");
        Near(planeBatch.RedoTrackEditCore() ? 1 : 0,1,"redo grouped plane addition");
        Near(planeBatch.PlaneCount,2,"redo restores both selected planes");
        Near(planeBatch.AddMarkerCore("Open",.25) ? 1 : 0,1,"add timeline marker");
        Near(planeBatch.AddMarkerCore("Open",.5) ? 1 : 0,0,"duplicate marker name rejected");
        Near(planeBatch.AddMarkerCore("Middle",.25) ? 1 : 0,0,"duplicate marker time rejected");
        Near(planeBatch.RenameMarkerCore(0,"Opening") ? 1 : 0,1,"rename marker");
        Near(planeBatch.MoveMarkerCore(0,.5) ? 1 : 0,1,"move marker");
        Near(planeBatch.TimelineMarkers[0].Time,.5,"marker moved to new time");
        Near(planeBatch.ToggleFavoriteTrackCore(0) ? 1 : 0,1,"favorite component track");
        Near(planeBatch.IsFavoriteTrack(0) ? 1 : 0,1,"favorite is active");
        Near(planeBatch.UndoTrackEditCore() ? 1 : 0,1,"undo favorite change");
        Near(planeBatch.IsFavoriteTrack(0) ? 1 : 0,0,"favorite undo restores state");
        Near(planeBatch.RedoTrackEditCore() ? 1 : 0,1,"redo favorite change");
        ProjectSnapshot markedSaved = ProjectPersistence.Decode(ProjectPersistence.Encode(planeBatch.CreateSnapshot()));
        Near(markedSaved.Markers.Count,1,"timeline marker persists");
        Near(markedSaved.Markers[0].Name == "Opening" ? 1 : 0,1,"marker name persists");
        Near(markedSaved.FavoriteTracks.Count,1,"favorite track persists");
        Near(planeBatch.DeleteMarkerCore(0) ? 1 : 0,1,"delete timeline marker");
        Near(planeBatch.UndoTrackEditCore() ? 1 : 0,1,"undo marker deletion");
        Near(planeBatch.TimelineMarkers.Length,1,"undo restores marker");
        var reversed = new ProjectSnapshot { Easing = EasingMode.EaseIn };
        reversed.Monikers.Add("reverse-body");
        reversed.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 0,
            Placements = new[] { Matrix.Identity }, HasPoses = new[] { true } });
        reversed.Keyframes.Add(new KeyframeSnapshot { TimeSeconds = 2,
            Placements = new[] { Shift(4,0,0) }, HasPoses = new[] { true } });
        reversed.Markers.Add(new TimelineMarker { Name = "Half", Time = .5 });
        ProjectSnapshot sourceCopy = ProjectPersistence.Decode(ProjectPersistence.Encode(reversed));
        AnimationProject.ReverseSnapshot(reversed,2,new[] { Shift(4,0,0) },null);
        Near(reversed.Keyframes[0].TimeSeconds,0,"reversed last key begins at zero");
        Pose(reversed.Keyframes[0].Placements[0],Shift(4,0,0),"reversed first pose is former end pose");
        Near(reversed.Keyframes[1].TimeSeconds,2,"reversed first key ends at original duration");
        Near(reversed.Markers[0].Time,1.5,"marker time reverses with motion");
        Near(reversed.Easing == EasingMode.EaseOut ? 1 : 0,1,"reverse swaps Ease In and Ease Out");
        Pose(reversed.InitialPlacements[0],Shift(4,0,0),"reversed initial pose is source final pose");
        Near(sourceCopy.Keyframes[0].Placements[0].Translation.Magnitude,0,
            "reversing a duplicate does not change the source pose");
        Near(sourceCopy.Markers[0].Time,.5,"reversing a duplicate does not move source markers");
        var visibilityReverse = ProjectPersistence.Decode(ProjectPersistence.Encode(sourceCopy));
        visibilityReverse.VisibilityKeys.Add(new VisibilityKeySnapshot { Track = 0, Time = 0,
            Mode = VisibilityMode.FadeIn });
        AnimationProject.ReverseSnapshot(visibilityReverse,2,new[] { Shift(4,0,0) },null);
        Near(visibilityReverse.ReversedVisibility ? 1 : 0,1,"reverse enables backward visibility evaluation");
        Near(visibilityReverse.VisibilityKeys[0].Time,2,"reverse mirrors fade key time");
        var persistedReverse = ProjectPersistence.Decode(ProjectPersistence.Encode(visibilityReverse));
        Near(persistedReverse.ReversedVisibility ? 1 : 0,1,"reverse visibility mode persists");
        Near(persistedReverse.VisibilityKeys[0].Time,2,"reverse fade key survives persistence");
        var reverseVisibilityProject = (AnimationProject)typeof(AnimationProject)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(null);
        ((List<SpaceClaim.Api.V261.Component>)typeof(AnimationProject)
            .GetField("components",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reverseVisibilityProject)).Add(null);
        var reverseFrames = (List<Keyframe>)typeof(AnimationProject)
            .GetField("keyframes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reverseVisibilityProject);
        reverseFrames.Add(new Keyframe(new[] { Shift(4,0,0) },0));
        reverseFrames.Add(new Keyframe(new[] { Shift(2,0,0) },1));
        reverseFrames.Add(new Keyframe(new[] { Matrix.Identity },2));
        ((Dictionary<TrackKeyRef,VisibilityMode>)typeof(AnimationProject)
            .GetField("visibilityKeys",BindingFlags.Instance|BindingFlags.NonPublic)
            .GetValue(reverseVisibilityProject)).Add(new TrackKeyRef(0,2),VisibilityMode.FadeIn);
        ((Dictionary<TrackKeyRef,VisibilityMode>)typeof(AnimationProject)
            .GetField("visibilityKeys",BindingFlags.Instance|BindingFlags.NonPublic)
            .GetValue(reverseVisibilityProject)).Add(new TrackKeyRef(0,1),VisibilityMode.FadeOut);
        typeof(AnimationProject).GetField("reversedVisibility",BindingFlags.Instance|BindingFlags.NonPublic)
            .SetValue(reverseVisibilityProject,true);
        double reverseChange;
        Near(reverseVisibilityProject.VisibilityAt(0,1.8,out reverseChange) == VisibilityMode.FadeIn ? 1 : 0,
            1,"reverse fade is active before mirrored key");
        Near(reverseChange,2,"reverse fade looks toward mirrored key");
        Near(AnimationProject.ReversedFadeOpacity(VisibilityMode.FadeIn,1.8,2,.4),.5,
            "reversed Fade in becomes half-visible Fade out");
        Near(reverseVisibilityProject.VisibilityAt(0,.8,out reverseChange) == VisibilityMode.FadeOut ? 1 : 0,
            1,"reverse Fade out is selected before its mirrored key");
        Near(reverseChange,1,"reverse Fade out uses nearest future key");
        Near(AnimationProject.ReversedFadeOpacity(VisibilityMode.FadeOut,.8,1,.4),.5,
            "reversed Fade out becomes half-visible Fade in");
        Near(reverseVisibilityProject.VisibilityAt(0,2.1,out reverseChange) == VisibilityMode.Default ? 1 : 0,
            1,"reverse visibility returns to original baseline after key");
        AnimationProject.ReverseSnapshot(visibilityReverse,2,null,null);
        Near(visibilityReverse.ReversedVisibility ? 1 : 0,0,"second reverse restores forward visibility");
        Near(visibilityReverse.VisibilityKeys[0].Time,0,"second reverse restores fade time");
        var paused = ProjectPersistence.Decode(ProjectPersistence.Encode(sourceCopy));
        paused.PlaneTracks.Add(new PlaneTrackSnapshot { Moniker = "pause-plane" });
        paused.PlaneTracks[0].Keys.Add(new PlaneKeySnapshot { Time = 0, Placement = Matrix.Identity });
        paused.PlaneTracks[0].Keys.Add(new PlaneKeySnapshot { Time = 2, Placement = Shift(2,0,0) });
        paused.VisibilityKeys.Add(new VisibilityKeySnapshot { Track = 0, Time = 2,
            Mode = VisibilityMode.Hide });
        paused.HasPlaybackRange = true; paused.PlaybackRangeStart = 0; paused.PlaybackRangeEnd = 2;
        AnimationProject.ShiftAfter(paused,0,1);
        AnimationProject.AddHoldKeys(paused,0,1,new[] { Matrix.Identity },new[] { Matrix.Identity },null);
        var pauseSaved = ProjectPersistence.Decode(ProjectPersistence.Encode(paused));
        Near(pauseSaved.Keyframes.Count,3,"pause adds a held component key");
        Near(pauseSaved.Keyframes[1].TimeSeconds,1,"pause ends at requested time");
        Pose(pauseSaved.Keyframes[1].Placements[0],Matrix.Identity,"pause holds component pose");
        Near(pauseSaved.Keyframes[2].TimeSeconds,3,"pause shifts later component key");
        Near(pauseSaved.PlaneTracks[0].Keys[1].Time,1,"pause holds plane pose");
        Near(pauseSaved.PlaneTracks[0].Keys[2].Time,3,"pause shifts later plane key");
        Near(pauseSaved.VisibilityKeys[0].Time,3,"pause shifts visibility key");
        Near(pauseSaved.Markers[0].Time,1.5,"pause shifts timeline marker");
        Near(pauseSaved.PlaybackRangeEnd,3,"pause extends Out boundary");
        typeof(AnimationProject).GetMethod("SaveUndoState",BindingFlags.Instance|BindingFlags.NonPublic)
            .Invoke(planeBatch,null);
        typeof(AnimationProject).GetField("hasPlaybackRange",BindingFlags.Instance|BindingFlags.NonPublic)
            .SetValue(planeBatch,true);
        typeof(AnimationProject).GetField("playbackRangeEnd",BindingFlags.Instance|BindingFlags.NonPublic)
            .SetValue(planeBatch,2.0);
        Near(planeBatch.UndoTrackEditCore() ? 1 : 0,1,"undo pause edit");
        Near((bool)typeof(AnimationProject).GetField("hasPlaybackRange",BindingFlags.Instance|BindingFlags.NonPublic)
            .GetValue(planeBatch) ? 1 : 0,0,"undo restores playback range");
        string cleanEncoded = ProjectPersistence.Encode(sourceCopy), cleanXml;
        using (var input = new MemoryStream(Convert.FromBase64String(cleanEncoded)))
        using (var gzip = new GZipStream(input,CompressionMode.Decompress))
        using (var output = new MemoryStream()) { gzip.CopyTo(output); cleanXml = Encoding.UTF8.GetString(output.ToArray()); }
        string priorXml = cleanXml.Insert(cleanXml.IndexOf("<keyframe",StringComparison.Ordinal),
            "<preset name=\"Old pose\"><entry id=\"C:old\"/></preset>");
        string priorEncoded;
        using (var output = new MemoryStream()) {
            using (var gzip = new GZipStream(output,CompressionMode.Compress,true)) {
                byte[] bytes = Encoding.UTF8.GetBytes(priorXml); gzip.Write(bytes,0,bytes.Length);
            }
            priorEncoded = Convert.ToBase64String(output.ToArray());
        }
        var migratedPose = ProjectPersistence.Decode(priorEncoded);
        Near(migratedPose.Keyframes.Count,sourceCopy.Keyframes.Count,"v1.7.0 preset data does not block model loading");
        string migratedXml;
        using (var input = new MemoryStream(Convert.FromBase64String(ProjectPersistence.Encode(migratedPose))))
        using (var gzip = new GZipStream(input,CompressionMode.Decompress))
        using (var output = new MemoryStream()) { gzip.CopyTo(output); migratedXml = Encoding.UTF8.GetString(output.ToArray()); }
        Near(migratedXml.Contains("<preset") ? 1 : 0,0,"removed pose feature is not written again");
        Near(HingeDetector.Coaxial(Point.Origin,Direction.DirZ,
            Point.Create(0.0001,0,2),Direction.DirZ,.0002) ? 1 : 0,
            1,"coaxial detector accepts axially separated cylinders");
        Near(HingeDetector.Coaxial(Point.Origin,Direction.DirZ,
            Point.Create(.01,0,0),Direction.DirZ,.0002) ? 1 : 0,
            0,"coaxial detector rejects offset axes");
        Near(HingeDetector.Coaxial(Point.Origin,Direction.DirZ,
            Point.Origin,Direction.DirX,.0002) ? 1 : 0,
            0,"coaxial detector rejects crossing axes");
        Near(HingeDetector.Coaxial(Point.Origin, Direction.DirZ,
            Point.Create(.0008, 0, 2), Direction.DirZ, .001) ? 1 : 0,
            1, "1 mm setting finds axes separated by 0.8 mm");
        Near(HingeDetector.Coaxial(Point.Origin, Direction.DirZ,
            Point.Create(.0008, 0, 2), Direction.DirZ, .0005) ? 1 : 0,
            0, "0.5 mm setting rejects axes separated by 0.8 mm");
        double faceGap, faceCenterDistance, markerPosition;
        HingeDetector.AxialRelation(0, 10, 20, 30,
            out faceGap, out faceCenterDistance, out markerPosition);
        Near(faceGap, 10, "separated coaxial faces have an axial gap");
        Near(markerPosition, 15, "marker sits between nearest face ends");
        HingeDetector.AxialRelation(0, 100, 95, 105,
            out faceGap, out faceCenterDistance, out markerPosition);
        Near(faceGap, 0, "overlapping coaxial faces have no axial gap");
        Near(markerPosition, 97.5, "marker sits in face overlap, not between distant centers");
        var farFace = new HingeCandidate { Radius = .01, OtherRadius = .01,
            AxialGap = .02, AxialCenterDistance = .04 };
        var nearFace = new HingeCandidate { Radius = .01, OtherRadius = .0101,
            AxialGap = 0, AxialCenterDistance = .003 };
        Near(HingeDetector.IsCloser(nearFace, farFace) ? 1 : 0, 1,
            "same-axis proposal prefers nearby matching faces over first encountered");
        Near(HingeDetector.IsCloser(farFace, nearFace) ? 1 : 0, 0,
            "distant same-axis face does not replace nearby proposal");
        var matchingPin = new HingeCandidate { Radius = .01, OtherRadius = .0102 };
        var tenMm = new HingeCandidate { Radius = .005, OtherRadius = .005 };
        var twentyMm = new HingeCandidate { Radius = .01, OtherRadius = .01 };
        Near(tenMm.IsWithinDiameterRange(10, 20) ? 1 : 0, 1,
            "diameter filter includes 10 mm lower boundary");
        Near(twentyMm.IsWithinDiameterRange(10, 20) ? 1 : 0, 1,
            "diameter filter includes 20 mm upper boundary");
        Near(tenMm.IsWithinDiameterRange(11, 20) ? 1 : 0, 0,
            "diameter filter excludes smaller cylinders");
        Near(twentyMm.IsWithinDiameterRange(10, 19) ? 1 : 0, 0,
            "diameter filter excludes larger cylinders");
        Near(tenMm.IsWithinDiameterRange(20, 10) ? 1 : 0, 0,
            "reversed diameter range matches no proposals");
        var sixPairings = new[] {
            new AutoHingeOption { Index = 0, FixedId = "1", MovingId = "2",
                AxisDistance = 0, AxialCenterDistance = 0 },
            new AutoHingeOption { Index = 1, FixedId = "1", MovingId = "3" },
            new AutoHingeOption { Index = 2, FixedId = "1", MovingId = "4" },
            new AutoHingeOption { Index = 3, FixedId = "2", MovingId = "3" },
            new AutoHingeOption { Index = 4, FixedId = "2", MovingId = "4" },
            new AutoHingeOption { Index = 5, FixedId = "3", MovingId = "4" }
        };
        var independent = AutoHingeSelection.Choose(sixPairings);
        Near(independent.Count, 3, "six pairings produce three independent hinges");
        Near(independent.Contains(0) && independent.Contains(1) && independent.Contains(2) ? 1 : 0,
            1, "automatic choice uses one fixed partner per moving component");
        sixPairings[2].RadiusMismatch = .04;
        Near(AutoHingeSelection.Choose(sixPairings).Contains(4) ? 1 : 0, 1,
            "automatic choice takes another close-fitting pairing when first is unsuitable");
        sixPairings[1].AxialGap = .02;
        Near(AutoHingeSelection.Choose(sixPairings).Contains(3) ? 1 : 0, 1,
            "automatic choice prefers nearest independent coaxial pair");
        Near(matchingPin.RadiusMismatch < .03 ? 1 : 0,1,
            "auto hinge accepts matching pin and hole radii");
        matchingPin.OtherRadius = .012;
        Near(matchingPin.RadiusMismatch < .03 ? 1 : 0,0,
            "auto hinge rejects unequal pin and hole radii");
        var mechanismSaved = ProjectPersistence.Decode(ProjectPersistence.Encode(sourceCopy));
        mechanismSaved.Hinges.Add(new HingeSnapshot { Name = "Rotor", FixedId = "fixed-id",
            MovingId = "moving-id", AlignId = "created-align-id", OwnedAlignId = "created-align-id", Origin = Point.Create(.01,.02,.03),
            Direction = Direction.DirZ });
        var mechanismLoaded = ProjectPersistence.Decode(ProjectPersistence.Encode(mechanismSaved));
        Near(mechanismLoaded.Hinges.Count,1,"hinge survives model data roundtrip");
        Near(mechanismLoaded.Hinges[0].Origin.X,.01,"hinge axis origin survives persistence");
        Near(mechanismLoaded.Hinges[0].Direction.Z,1,"hinge axis direction survives persistence");
        Near(mechanismLoaded.Hinges[0].Name == "Rotor" ? 1 : 0,1,
            "hinge name survives persistence");
        Near(mechanismLoaded.Hinges[0].OwnedAlignId == "created-align-id" ? 1 : 0,1,
            "owned SpaceClaim Align identifier survives persistence");
        Near(mechanismLoaded.Hinges[0].AlignId == "created-align-id" ? 1 : 0,1,
            "referenced SpaceClaim Align identifier survives persistence");
        var hingeOnly = new ProjectSnapshot();
        hingeOnly.Hinges.Add(mechanismLoaded.Hinges[0].Clone());
        Near(ProjectPersistence.Decode(ProjectPersistence.Encode(hingeOnly)).Hinges.Count,1,
            "hinge can be saved before any animation track exists");
        var hingeLibrary = new AnimationLibrarySnapshot();
        hingeLibrary.Animations.Add(new NamedAnimationSnapshot { Name = "With hinge", Project = mechanismLoaded });
        hingeLibrary.Animations.Add(new NamedAnimationSnapshot { Name = "Without hinge", Project = sourceCopy });
        var loadedHingeLibrary = ProjectPersistence.DecodeLibrary(ProjectPersistence.EncodeLibrary(hingeLibrary));
        Near(loadedHingeLibrary.Animations[0].Project.Hinges.Count,1,
            "hinge belongs to its own animation");
        Near(loadedHingeLibrary.Animations[1].Project.Hinges.Count,0,
            "another animation has no inherited hinge");
        Console.WriteLine("PASS: " + checks + " numerical assertions against real V261 geometry API.");
        return 0;
      } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}






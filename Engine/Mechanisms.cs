using System;
using System.Collections.Generic;
using System.Linq;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal sealed class HingeSnapshot {
        internal string Name, FixedId, MovingId, AlignId, OwnedAlignId;
        // The joint axis is expressed in the fixed component's local coordinates.
        internal Point Origin;
        internal Direction Direction;
        internal HingeSnapshot Clone() {
            return new HingeSnapshot { Name = Name, FixedId = FixedId, MovingId = MovingId,
                AlignId = AlignId, OwnedAlignId = OwnedAlignId,
                Origin = Origin, Direction = Direction };
        }
    }

    internal sealed class HingeCandidate {
        internal IComponent Fixed, Moving;
        internal IDesignFace FixedFace, MovingFace;
        internal Point Origin;
        internal Direction Direction;
        internal double Radius, OtherRadius, AxisDistance, AxialGap, AxialCenterDistance;
        internal double AllowedAxisOffset;
        internal Point MarkerCenter;
        internal double RadiusMismatch { get {
            return Math.Abs(Radius - OtherRadius) / Math.Max(Radius, OtherRadius);
        } }
        internal double DiameterMm { get { return Radius * 2000; } }
        internal bool IsWithinDiameterRange(double minimumMm, double maximumMm) {
            const double roundingToleranceMm = .001;
            return minimumMm <= maximumMm &&
                DiameterMm >= minimumMm - roundingToleranceMm &&
                DiameterMm <= maximumMm + roundingToleranceMm;
        }
        internal string Label = "Hinge candidate";
        public override string ToString() { return Label; }
    }

    internal sealed class AutoHingeOption {
        internal int Index;
        internal string FixedId, MovingId;
        internal double RadiusMismatch, AxisDistance, AxialGap, AxialCenterDistance;
    }

    internal static class AutoHingeSelection {
        internal static List<int> Choose(IEnumerable<AutoHingeOption> options) {
            return options.Where(option => option.RadiusMismatch <= .03)
                .GroupBy(option => option.MovingId, StringComparer.Ordinal)
                .Select(group => group.OrderBy(option => option.AxialGap)
                    .ThenBy(option => option.AxialCenterDistance)
                    .ThenBy(option => option.RadiusMismatch)
                    .ThenBy(option => option.AxisDistance)
                    .ThenBy(option => option.FixedId, StringComparer.Ordinal)
                    .First().Index).ToList();
        }
    }

    internal static class HingeDetector {
        private sealed class SurfaceAxis {
            internal IDesignFace Face;
            internal Point Origin;
            internal Direction Direction;
            internal double Radius;
            internal Point BoundsCenter;
            internal Vector BoundsSize;
        }
        private static double Dot(Direction a, Direction b) {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }
        private static double Dot(Vector a, Direction b) {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }
        private static void AxialSpan(SurfaceAxis face, Point origin, Direction axis,
            out double min, out double max) {
            double midpoint = Dot(face.BoundsCenter - origin, axis);
            double extent = (Math.Abs(axis.X) * face.BoundsSize.X +
                Math.Abs(axis.Y) * face.BoundsSize.Y +
                Math.Abs(axis.Z) * face.BoundsSize.Z) / 2;
            min = midpoint - extent; max = midpoint + extent;
        }
        internal static void AxialRelation(double minA, double maxA, double minB, double maxB,
            out double gap, out double centerDistance, out double markerPosition) {
            centerDistance = Math.Abs((minA + maxA - minB - maxB) / 2);
            if (maxA < minB) {
                gap = minB - maxA; markerPosition = (maxA + minB) / 2;
            } else if (maxB < minA) {
                gap = minA - maxB; markerPosition = (maxB + minA) / 2;
            } else {
                gap = 0;
                markerPosition = (Math.Max(minA, minB) + Math.Min(maxA, maxB)) / 2;
            }
        }
        internal static bool IsCloser(HingeCandidate candidate, HingeCandidate current) {
            bool candidateFits = candidate.RadiusMismatch <= .03;
            bool currentFits = current.RadiusMismatch <= .03;
            if (candidateFits != currentFits) return candidateFits;
            if (Math.Abs(candidate.AxialGap - current.AxialGap) > 1e-8)
                return candidate.AxialGap < current.AxialGap;
            if (Math.Abs(candidate.AxialCenterDistance - current.AxialCenterDistance) > 1e-8)
                return candidate.AxialCenterDistance < current.AxialCenterDistance;
            if (Math.Abs(candidate.RadiusMismatch - current.RadiusMismatch) > 1e-8)
                return candidate.RadiusMismatch < current.RadiusMismatch;
            return candidate.AxisDistance < current.AxisDistance;
        }
        private static double LineDistance(Point a, Direction direction, Point b) {
            double x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z;
            double projection = x * direction.X + y * direction.Y + z * direction.Z;
            x -= projection * direction.X; y -= projection * direction.Y; z -= projection * direction.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }
        // Geometry from an occurrence is in scene coordinates. Only direct
        // siblings are considered, so they share one parent coordinate frame.
        private static List<SurfaceAxis> Cylinders(IComponent component, int limit) {
            var result = new List<SurfaceAxis>();
            foreach (IDesignBody body in component.Content.GetDescendants<IDesignBody>()) {
                foreach (IDesignFace face in body.Faces) {
                    var shaped = face as IHasShape;
                    var surface = shaped == null ? null : shaped.Shape as ISurfaceShape;
                    Cylinder cylinder = surface == null ? null : surface.Geometry as Cylinder;
                    if (cylinder == null || cylinder.Radius <= 0) continue;
                    Line axis = cylinder.Axis;
                    var bounded = shaped.Shape as IBounded;
                    Point boundsCenter = axis.Origin;
                    Vector boundsSize = Vector.Create(0, 0, 0);
                    if (bounded != null) {
                        var box = bounded.GetBoundingBox(Matrix.Identity);
                        boundsCenter = box.Center;
                        boundsSize = box.Size;
                    }
                    result.Add(new SurfaceAxis { Face = face, Origin = axis.Origin,
                        Direction = axis.Direction, Radius = cylinder.Radius,
                        BoundsCenter = boundsCenter, BoundsSize = boundsSize });
                    if (result.Count >= limit) return result;
                }
            }
            return result;
        }
        internal static List<HingeCandidate> Detect(IList<IComponent> components,
            double maxAxisOffset) {
            if (Double.IsNaN(maxAxisOffset) || Double.IsInfinity(maxAxisOffset) ||
                maxAxisOffset < 0 || maxAxisOffset > .01)
                throw new ArgumentOutOfRangeException("maxAxisOffset");
            var result = new List<HingeCandidate>();
            var geometry = new List<SurfaceAxis>[components.Count];
            int faceLimit = components.Count == 2 ? 2000 : 512;
            for (int i = 0; i < components.Count; i++) geometry[i] = Cylinders(components[i], faceLimit);
            for (int i = 0; i < components.Count; i++)
                for (int j = i + 1; j < components.Count; j++) {
                    if (!String.Equals(components[i].Parent.Moniker.ToString(),
                        components[j].Parent.Moniker.ToString(), StringComparison.Ordinal) ||
                        components[i].Master == components[j].Master) continue;
                    var pair = new List<HingeCandidate>();
                    foreach (SurfaceAxis first in geometry[i]) {
                        foreach (SurfaceAxis second in geometry[j]) {
                            double distance = LineDistance(first.Origin, first.Direction, second.Origin);
                            double tolerance = Math.Max(1e-7, maxAxisOffset);
                            if (!Coaxial(first.Origin, first.Direction, second.Origin,
                                second.Direction, tolerance)) continue;
                            double axisMergeTolerance = Math.Max(1e-6,
                                Math.Min(.00002, Math.Min(first.Radius, second.Radius) * .002));
                            int existingIndex = pair.FindIndex(existing =>
                                Math.Abs(Dot(existing.Direction, first.Direction)) > .99999 &&
                                LineDistance(existing.Origin, existing.Direction, first.Origin) < axisMergeTolerance);
                            if (existingIndex < 0 && pair.Count >= 8) continue;
                            double minFirst, maxFirst, minSecond, maxSecond;
                            AxialSpan(first, first.Origin, first.Direction, out minFirst, out maxFirst);
                            AxialSpan(second, first.Origin, first.Direction, out minSecond, out maxSecond);
                            double axialGap, axialCenterDistance, markerPosition;
                            AxialRelation(minFirst, maxFirst, minSecond, maxSecond,
                                out axialGap, out axialCenterDistance, out markerPosition);
                            var candidate = new HingeCandidate { Fixed = components[i], Moving = components[j],
                                FixedFace = first.Face, MovingFace = second.Face,
                                Origin = first.Origin, Direction = first.Direction,
                                Radius = first.Radius, OtherRadius = second.Radius,
                                AxisDistance = distance, AxialGap = axialGap,
                                AllowedAxisOffset = maxAxisOffset,
                                AxialCenterDistance = axialCenterDistance,
                                MarkerCenter = first.Origin + first.Direction * markerPosition };
                            if (existingIndex < 0) pair.Add(candidate);
                            else if (IsCloser(candidate, pair[existingIndex])) pair[existingIndex] = candidate;
                        }
                    }
                    result.AddRange(pair);
                }
            return result;
        }
        internal static bool Coaxial(Point a, Direction x, Point b, Direction y, double tolerance) {
            return Math.Abs(Dot(x, y)) >= .99939 && LineDistance(a, x, b) <= tolerance;
        }
    }

    internal sealed partial class AnimationProject {
        private readonly List<HingeSnapshot> hinges = new List<HingeSnapshot>();
        internal int HingeCount { get { return hinges.Count; } }
        internal HingeSnapshot GetHinge(int index) { return hinges[index].Clone(); }
        internal bool HasHingeForMoving(IComponent moving) {
            if (moving == null) return false;
            string id = moving.Moniker.ToString();
            return hinges.Exists(hinge => hinge.MovingId == id);
        }
        private static bool SameGeometry(IHasShape first, IHasShape second) {
            var left = first as IDocObject;
            var right = second as IDocObject;
            return left != null && right != null &&
                String.Equals(left.Moniker.ToString(), right.Moniker.ToString(),
                    StringComparison.Ordinal);
        }
        internal bool HasHingeForCandidate(HingeCandidate candidate) {
            if (candidate == null || document == null) return false;
            foreach (HingeSnapshot hinge in hinges) {
                if (String.IsNullOrEmpty(hinge.AlignId)) continue;
                IAlignCondition aligned;
                try {
                    aligned = Moniker<IDocObject>.FromString(hinge.AlignId)
                        .Resolve(document) as IAlignCondition;
                } catch (ArgumentException) { continue; }
                  catch (InvalidOperationException) { continue; }
                if (aligned == null || aligned.IsDeleted) continue;
                if ((SameGeometry(aligned.GeometricA, candidate.MovingFace) &&
                     SameGeometry(aligned.GeometricB, candidate.FixedFace)) ||
                    (SameGeometry(aligned.GeometricA, candidate.FixedFace) &&
                     SameGeometry(aligned.GeometricB, candidate.MovingFace))) return true;
            }
            return false;
        }
        internal bool AddAlignedHinge(string name, HingeCandidate candidate, out string error) {
            error = null;
            if (candidate == null || candidate.FixedFace == null ||
                candidate.MovingFace == null || candidate.Fixed == null || candidate.Moving == null) {
                error = "The proposed Align faces are no longer available."; return false;
            }
            if (!WriteBlock.IsAvailable && !WriteBlock.IsActive) {
                error = "Finish the current SpaceClaim interaction before creating Align.";
                return false;
            }
            var fixedFaceShape = candidate.FixedFace as IHasShape;
            var movingFaceShape = candidate.MovingFace as IHasShape;
            var fixedFaceSurface = fixedFaceShape == null ? null : fixedFaceShape.Shape as ISurfaceShape;
            var movingFaceSurface = movingFaceShape == null ? null : movingFaceShape.Shape as ISurfaceShape;
            var fixedFaceCylinder = fixedFaceSurface == null ? null : fixedFaceSurface.Geometry as Cylinder;
            var movingFaceCylinder = movingFaceSurface == null ? null : movingFaceSurface.Geometry as Cylinder;
            if (fixedFaceCylinder == null || movingFaceCylinder == null ||
                !HingeDetector.Coaxial(fixedFaceCylinder.Axis.Origin, fixedFaceCylinder.Axis.Direction,
                    movingFaceCylinder.Axis.Origin, movingFaceCylinder.Axis.Direction,
                    Math.Max(1e-7, candidate.AllowedAxisOffset + 1e-7))) {
                error = "These faces no longer match the selected axis offset. Use Find axes again.";
                return false;
            }
            if (String.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 80 ||
                Animation.IsAnimating || hinges.Count >= 100) {
                error = "Enter a hinge name first."; return false;
            }
            foreach (HingeSnapshot hinge in hinges)
                if (String.Equals(hinge.Name, name, StringComparison.OrdinalIgnoreCase)) {
                    error = "This hinge name is already used."; return false;
                }
            if (HasHingeForCandidate(candidate)) {
                error = "This face pair already has an animation hinge."; return false;
            }
            bool reusedAlign = false;
            string createdAlignId = null;
            string alignId = null;
            try {
                SpaceClaim.Api.V261.Task create = delegate {
                    foreach (IMatingCondition existing in candidate.Fixed.Parent.MatingConditions) {
                        var aligned = existing as IAlignCondition;
                        if (aligned == null || !aligned.IsEnabled) continue;
                        if ((SameGeometry(aligned.GeometricA, candidate.MovingFace) &&
                             SameGeometry(aligned.GeometricB, candidate.FixedFace)) ||
                            (SameGeometry(aligned.GeometricA, candidate.FixedFace) &&
                             SameGeometry(aligned.GeometricB, candidate.MovingFace))) {
                            reusedAlign = true; alignId = aligned.Moniker.ToString(); break;
                        }
                    }
                    if (!reusedAlign) {
                        IAlignCondition created = AlignCondition.Create(candidate.Fixed.Parent,
                            candidate.MovingFace, candidate.FixedFace);
                        if (created == null || !created.IsValid)
                            throw new CommandException("SpaceClaim could not create a valid Align condition for these faces.",
                                StatusMessageType.Error);
                        createdAlignId = created.Moniker.ToString();
                        alignId = createdAlignId;
                    }
                    if (reusedAlign && hinges.Exists(hinge => hinge.AlignId == alignId))
                        throw new CommandException("This Align already has an animation hinge.",
                            StatusMessageType.Error);
                    var updated = fixedFaceShape.Shape as ISurfaceShape;
                    var fixedCylinder = updated == null ? null : updated.Geometry as Cylinder;
                    if (fixedCylinder != null) {
                        candidate.Origin = fixedCylinder.Axis.Origin;
                        candidate.Direction = fixedCylinder.Axis.Direction;
                    }
                    if (!AddHinge(name, candidate.Fixed, candidate.Moving,
                        candidate.Origin, candidate.Direction))
                        throw new CommandException("Could not add the animation track for this Align condition.",
                            StatusMessageType.Error);
                    hinges[hinges.Count - 1].AlignId = alignId;
                    hinges[hinges.Count - 1].OwnedAlignId = createdAlignId;
                };
                if (WriteBlock.IsActive) create();
                else WriteBlock.ExecuteTask("SC Animator - Create Align and hinge", create);
                error = reusedAlign ? "Existing SpaceClaim Align reused; animation hinge added." :
                    "SpaceClaim Align and animation hinge added.";
                return true;
            } catch (Exception ex) { error = ex.Message; return false; }
        }
        internal static string MechanismName(IComponent component) {
            string part = component.Content == null ? null : component.Content.Master.DisplayName;
            return String.IsNullOrWhiteSpace(part) ? component.Name : part;
        }
        private static Matrix ParentPlacement(IComponent component) {
            Matrix result = Matrix.Identity;
            IPart part = component.Parent;
            int depth = 0;
            while (part != null && part.Parent != null) {
                if (++depth > 64) throw new InvalidOperationException("Component hierarchy is too deep.");
                result = part.Parent.Master.Placement * result;
                IComponent owner = part.Parent as IComponent;
                part = owner == null ? null : owner.Parent;
            }
            return result;
        }
        internal bool AddHinge(string name, IComponent fixedPart, IComponent movingPart,
            Point worldOrigin, Direction worldDirection) {
            if (document == null || Window.ActiveWindow == null || Window.ActiveWindow.Document != document ||
                Animation.IsAnimating || fixedPart == null || movingPart == null ||
                fixedPart.Master.IsDeleted || movingPart.Master.IsDeleted ||
                fixedPart.Master == movingPart.Master ||
                !String.Equals(fixedPart.Parent.Moniker.ToString(),
                    movingPart.Parent.Moniker.ToString(), StringComparison.Ordinal) ||
                String.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 80 || hinges.Count >= 100)
                return false;
            int fixedOccurrences = 0, movingOccurrences = 0;
            foreach (IComponent occurrence in document.MainPart.GetDescendants<IComponent>()) {
                if (occurrence.Master == fixedPart.Master) fixedOccurrences++;
                if (occurrence.Master == movingPart.Master) movingOccurrences++;
            }
            if (fixedOccurrences != 1 || movingOccurrences != 1) return false;
            foreach (HingeSnapshot hinge in hinges)
                if (String.Equals(hinge.Name, name, StringComparison.OrdinalIgnoreCase)) return false;
            Matrix local = (ParentPlacement(fixedPart) * fixedPart.Master.Placement).Inverse;
            Line axis = Line.Create(worldOrigin, worldDirection).CreateTransformedCopy(local);
            var saved = new HingeSnapshot { Name = name, FixedId = fixedPart.Moniker.ToString(),
                MovingId = movingPart.Moniker.ToString(), Origin = axis.Origin,
                Direction = axis.Direction };
            if (!componentMonikers.Contains(saved.MovingId)) {
                if (!AddComponentsCore(new[] { movingPart.Master }, new[] { saved.MovingId },
                    new[] { movingPart.Master.Placement }, keyframes.Count == 0 ? 0 : playheadSeconds,
                    keyframes.Count == 0 ? (Matrix?)Window.ActiveWindow.Projection : null)) return false;
            }
            SaveStructuralUndoState();
            hinges.Add(saved);
            MarkDirty();
            return true;
        }
        internal bool RemoveHinge(int index, out string result) {
            result = null;
            if (index < 0 || index >= hinges.Count || Animation.IsAnimating || document == null ||
                Window.ActiveWindow == null || Window.ActiveWindow.Document != document) return false;
            HingeSnapshot hinge = hinges[index];
            string alignId = hinge.OwnedAlignId;
            bool referencedElsewhere = false;
            if (!String.IsNullOrEmpty(alignId)) {
                for (int i = 0; i < hinges.Count; i++)
                    if (i != index && hinges[i].AlignId == alignId) referencedElsewhere = true;
                for (int i = 0; i < animations.Count; i++) {
                    if (i == activeAnimationIndex) continue;
                    foreach (HingeSnapshot other in animations[i].Project.Hinges)
                        if (other.AlignId == alignId) referencedElsewhere = true;
                }
            }
            if (!String.IsNullOrEmpty(alignId) && !referencedElsewhere) {
                if (!WriteBlock.IsAvailable && !WriteBlock.IsActive) {
                    result = "Finish the current SpaceClaim interaction before removing Align.";
                    return false;
                }
                try {
                    SpaceClaim.Api.V261.Task remove = delegate {
                        var aligned = Moniker<IDocObject>.FromString(alignId).Resolve(document) as IAlignCondition;
                        if (aligned != null && !aligned.IsDeleted) aligned.Delete();
                        SaveStructuralUndoState(); hinges.RemoveAt(index); MarkDirty();
                    };
                    if (WriteBlock.IsActive) remove();
                    else WriteBlock.ExecuteTask("SC Animator - Remove Align and hinge", remove);
                } catch (Exception ex) { result = "Could not remove Align: " + ex.Message; return false; }
                result = "Animation hinge and its SpaceClaim Align removed. Recorded keys remain.";
                return true;
            }
            SaveStructuralUndoState();
            if (!String.IsNullOrEmpty(alignId) && referencedElsewhere) {
                bool transferred = false;
                for (int i = 0; i < hinges.Count && !transferred; i++) {
                    if (i == index || hinges[i].AlignId != alignId) continue;
                    hinges[i].OwnedAlignId = alignId; transferred = true;
                }
                for (int i = 0; i < animations.Count && !transferred; i++) {
                    if (i == activeAnimationIndex) continue;
                    foreach (HingeSnapshot other in animations[i].Project.Hinges) {
                        if (other.AlignId != alignId) continue;
                        other.OwnedAlignId = alignId; transferred = true; break;
                    }
                }
            }
            hinges.RemoveAt(index); MarkDirty();
            result = referencedElsewhere ? "Animation hinge removed. Align is shared by another animation and remains." :
                "Animation hinge removed. Pre-existing or older Align remains; recorded keys remain.";
            return true;
        }
        private IComponent ResolveHingePart(string id) {
            if (document == null) return null;
            return Moniker<IDocObject>.FromString(id).Resolve(document) as IComponent;
        }
    }
}

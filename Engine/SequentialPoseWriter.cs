using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    // ApplyLoop previews each frame from its initial model state. With an active
    // assembly condition, SpaceClaim can resolve one large placement change only
    // partway. Separate write blocks preserve the previous resolved pose while
    // AppendTask keeps the sequence in one native Undo step.
    internal sealed class SequentialPoseWriter {
        private static readonly Frame Basis = Frame.Create(Point.Origin, Direction.DirX, Direction.DirY);
        private readonly string undoName;
        private readonly AnimationProject navigation;
        private bool hasPoseUndoStep;

        internal SequentialPoseWriter(string undoName, AnimationProject navigation) {
            this.undoName = undoName;
            this.navigation = navigation;
        }

        internal bool HasPoseUndoStep { get { return hasPoseUndoStep; } }

        internal void Apply(AnimationProject clip, Component[] components, DatumPlane[] planes,
            Matrix[] poses, Matrix[] planePoses, double time, Action before = null) {
            bool changesPose = false;
            for (int i = 0; i < components.Length && !changesPose; i++)
                changesPose = Differs(components[i].Placement, poses[i]);
            for (int i = 0; i < planes.Length && !changesPose; i++)
                changesPose = Differs(AnimationProject.PlanePlacement(planes[i]), planePoses[i]);
            Action apply = delegate {
                if (before != null) before();
                for (int i = 0; i < components.Length; i++) components[i].Placement = poses[i];
                for (int i = 0; i < planes.Length; i++) AnimationProject.ApplyPlane(planes[i], planePoses[i]);
                clip.ApplyVisibility(time, true);
            };
            if (hasPoseUndoStep) WriteBlock.AppendTask(delegate { apply(); });
            else WriteBlock.ExecuteTask(undoName, delegate {
                // Persist navigation in the same native step as the placement.
                if (changesPose && navigation != null) {
                    navigation.SetPlayhead(time);
                    navigation.MarkNavigationDirty();
                }
                apply();
            });
            if (changesPose) hasPoseUndoStep = true;
        }

        internal void Append(Action action) {
            if (hasPoseUndoStep) WriteBlock.AppendTask(delegate { action(); });
            else WriteBlock.ExecuteTask(undoName, delegate { action(); });
        }

        private static bool Differs(Matrix a, Matrix b) {
            Frame fa = a * Basis, fb = b * Basis;
            return (fa.Origin - fb.Origin).Magnitude > 1e-9 ||
                (fa.DirX.UnitVector - fb.DirX.UnitVector).Magnitude > 1e-8 ||
                (fa.DirY.UnitVector - fb.DirY.UnitVector).Magnitude > 1e-8;
        }

        internal static bool HasEnabledAssemblyCondition(Document document) {
            if (document == null) return false;
            var pending = new Stack<IPart>();
            var visited = new HashSet<Part>();
            pending.Push(document.MainPart);
            while (pending.Count > 0) {
                IPart part = pending.Pop();
                if (part == null || !visited.Add(part.Master)) continue;
                foreach (IMatingCondition condition in part.MatingConditions)
                    if (condition.IsEnabled) return true;
                foreach (IComponent component in part.Components) pending.Push(component.Content);
            }
            return false;
        }
    }
}

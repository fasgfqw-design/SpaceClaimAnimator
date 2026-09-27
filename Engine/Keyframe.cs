using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal sealed class Keyframe {
        private readonly Matrix[] placements;
        private readonly bool[] hasPose;
        public Keyframe(Matrix[] placements, double timeSeconds) {
            this.placements = (Matrix[])placements.Clone();
            hasPose = new bool[placements.Length];
            for (int i = 0; i < hasPose.Length; i++) hasPose[i] = true;
            TimeSeconds = timeSeconds;
        }
        public Keyframe(Matrix[] placements, bool[] hasPose, Matrix? cameraProjection, double timeSeconds) {
            if (placements.Length != hasPose.Length) throw new System.ArgumentException("Pose count mismatch.");
            this.placements = (Matrix[])placements.Clone();
            this.hasPose = (bool[])hasPose.Clone();
            CameraProjection = cameraProjection;
            TimeSeconds = timeSeconds;
        }
        public int Count { get { return placements.Length; } }
        public Matrix this[int index] { get { return placements[index]; } }
        public Matrix[] CopyPlacements() { return (Matrix[])placements.Clone(); }
        public bool[] CopyPresence() { return (bool[])hasPose.Clone(); }
        public bool HasPose(int index) { return hasPose[index]; }
        public Matrix? CameraProjection { get; private set; }
        public bool IsEmpty {
            get {
                if (CameraProjection.HasValue) return false;
                foreach (bool present in hasPose) if (present) return false;
                return true;
            }
        }
        public Keyframe WithPose(int index, Matrix placement) {
            Matrix[] values = CopyPlacements();
            bool[] present = CopyPresence();
            values[index] = placement;
            present[index] = true;
            return new Keyframe(values, present, CameraProjection, TimeSeconds);
        }
        public Keyframe WithoutPose(int index) {
            bool[] present = CopyPresence();
            present[index] = false;
            return new Keyframe(placements, present, CameraProjection, TimeSeconds);
        }
        public Keyframe WithCamera(Matrix? projection) {
            return new Keyframe(placements, hasPose, projection, TimeSeconds);
        }
        public double TimeSeconds { get; private set; }
    }
}

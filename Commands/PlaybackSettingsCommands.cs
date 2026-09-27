using System;
using System.Drawing;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Extensibility;
using SCAnimator.V261.Engine;

namespace SCAnimator.V261.Commands {
    internal sealed class FpsCapsule : CommandCapsule {
        private bool synchronizing;
        public FpsCapsule() : base("SCAnimator.V261.Fps", "FPS", null,
            "Frames sampled per nominal second (1-120). Speed controls how quickly the motion plays.") { }
        protected override void OnInitialize(Command command) {
            command.IsWriteBlock = false;
            command.UpdateFrequency = UpdateFrequency.Always;
            command.ControlState = SpinBoxState.Create(AnimationProject.Current.FramesPerSecond, 1, 120, 1, 0);
            command.TextChanged += delegate {
                if (synchronizing || Animation.IsAnimating) return;
                var state = command.ControlState as SpinBoxState;
                if (state != null) AnimationProject.Current.FramesPerSecond = (int)Math.Round(state.Value);
            };
        }
        protected override void OnUpdate(Command command) {
            command.IsEnabled = !Animation.IsAnimating;
            var state = command.ControlState as SpinBoxState;
            int fps = AnimationProject.Current.FramesPerSecond;
            if (state != null && (int)Math.Round(state.Value) == fps) return;
            synchronizing = true;
            try { command.ControlState = SpinBoxState.Create(fps, 1, 120, 1, 0); }
            finally { synchronizing = false; }
        }
        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) { }
    }

    internal sealed class SpeedCapsule : CommandCapsule {
        private static readonly string[] Labels = { "0.25x", "0.5x", "1x", "2x", "4x" };
        private static readonly double[] Values = { 0.25, 0.5, 1.0, 2.0, 4.0 };
        private bool synchronizing;
        public SpeedCapsule() : base("SCAnimator.V261.Speed", "Speed", null,
            "Playback speed: 0.25x to 4x. Existing keyframes keep their positions and times.") { }
        protected override void OnInitialize(Command command) {
            command.IsWriteBlock = false;
            command.UpdateFrequency = UpdateFrequency.Always;
            command.ControlState = ComboBoxState.CreateFixed(Labels, 2);
            command.TextChanged += delegate {
                if (synchronizing || Animation.IsAnimating) return;
                var state = command.ControlState as ComboBoxState;
                if (state != null && state.SelectedIndex >= 0 && state.SelectedIndex < Values.Length)
                    AnimationProject.Current.SpeedMultiplier = Values[state.SelectedIndex];
            };
        }
        protected override void OnUpdate(Command command) {
            command.IsEnabled = !Animation.IsAnimating;
            int index = Array.IndexOf(Values, AnimationProject.Current.SpeedMultiplier);
            if (index < 0) index = 2;
            var state = command.ControlState as ComboBoxState;
            if (state != null && state.SelectedIndex == index) return;
            synchronizing = true;
            try { command.ControlState = ComboBoxState.CreateFixed(Labels, index); }
            finally { synchronizing = false; }
        }
        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) { }
    }

    internal sealed class EasingCapsule : CommandCapsule {
        private static readonly string[] Labels = { "Linear", "Smooth", "Ease In", "Ease Out" };
        private bool synchronizing;
        public EasingCapsule() : base("SCAnimator.V261.Easing", "Transition", null,
            "Choose timing within each keyframe segment: Linear, Smooth, Ease In or Ease Out.") { }
        protected override void OnInitialize(Command command) {
            command.IsWriteBlock = false;
            command.UpdateFrequency = UpdateFrequency.Always;
            command.ControlState = ComboBoxState.CreateFixed(Labels, (int)AnimationProject.Current.Easing);
            command.TextChanged += delegate {
                if (synchronizing || Animation.IsAnimating) return;
                var state = command.ControlState as ComboBoxState;
                if (state != null && state.SelectedIndex >= 0 && state.SelectedIndex < Labels.Length)
                    AnimationProject.Current.Easing = (EasingMode)state.SelectedIndex;
            };
        }
        protected override void OnUpdate(Command command) {
            command.IsEnabled = !Animation.IsAnimating;
            int index = (int)AnimationProject.Current.Easing;
            var state = command.ControlState as ComboBoxState;
            if (state != null && state.SelectedIndex == index) return;
            synchronizing = true;
            try { command.ControlState = ComboBoxState.CreateFixed(Labels, index); }
            finally { synchronizing = false; }
        }
        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) { }
    }
}

using System.Drawing;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Extensibility;
using SCAnimator.V261.Engine;
using SCAnimator.V261.UI;

namespace SCAnimator.V261.Commands {
    internal sealed class PlayPauseCapsule : CommandCapsule {
        public const string CommandName = "SCAnimator.V261.PlayPause";

        public PlayPauseCapsule()
            : base(CommandName, "Play", Icons.Play, "Play or pause the keyframe animation") {
        }

        protected override void OnInitialize(Command command) {
            command.IsWriteBlock = false; command.UpdateFrequency = UpdateFrequency.Always;
        }

        protected override void OnUpdate(Command command) {
            bool ours = PlaybackController.IsOurAnimationRunning;
            command.IsEnabled = !PlaybackController.IsExportingVideo &&
                (ours || (!Animation.IsAnimating && AnimationProject.Current.CanPlay));

            if (ours && !Animation.IsPaused) {
                command.Text = "Pause";
                command.Image = Icons.Pause;
                command.Hint = "Pause the current animation";
            }
            else {
                command.Text = Animation.IsPaused && ours ? "Resume" : "Play";
                command.Image = Icons.Play;
                command.Hint = "Play the captured keyframes";
            }
        }

        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) {
            PlaybackController.TogglePlayPause(command);
        }
    }

    internal sealed class CancelAnimationCapsule : CommandCapsule {
        public const string CommandName = "SCAnimator.V261.CancelAnimation";

        public CancelAnimationCapsule()
            : base(CommandName, "Cancel (Esc)", Icons.Cancel, "Cancel the animation exactly like pressing Esc") {
        }

        protected override void OnInitialize(Command command) {
            command.IsWriteBlock = false; command.UpdateFrequency = UpdateFrequency.Always;
        }

        protected override void OnUpdate(Command command) {
            command.IsEnabled = PlaybackController.IsOurAnimationRunning;
        }

        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) {
            PlaybackController.CancelAnimation();
        }
    }

    internal sealed class ResetPoseCapsule : CommandCapsule {
        public const string CommandName = "SCAnimator.V261.ResetPose";

        protected override void OnInitialize(Command command) { command.IsWriteBlock = false; command.UpdateFrequency = UpdateFrequency.Always; }

        public ResetPoseCapsule()
            : base(CommandName, "Reset", Icons.Reset, "Return all recorded components to the start keyframe") {
        }

        protected override void OnUpdate(Command command) {
            command.IsEnabled = AnimationProject.Current.KeyframeCount > 0 && AnimationProject.Current.CanEdit;
        }

        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) {
            PlaybackController.ResetToFirstKeyframe();
        }
    }

    internal sealed class LoopCapsule : CommandCapsule {
        public const string CommandName = "SCAnimator.V261.Loop";

        protected override void OnInitialize(Command command) { command.IsWriteBlock = false; command.UpdateFrequency = UpdateFrequency.Always; }

        public LoopCapsule()
            : base(CommandName, "Loop", null, "Repeat the animation continuously") {
        }

        protected override void OnUpdate(Command command) {
            command.IsEnabled = !Animation.IsAnimating;
            command.IsChecked = AnimationProject.Current.Loop;
        }

        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) {
            AnimationProject.Current.Loop = !AnimationProject.Current.Loop;
            command.IsChecked = AnimationProject.Current.Loop;
        }
    }
}




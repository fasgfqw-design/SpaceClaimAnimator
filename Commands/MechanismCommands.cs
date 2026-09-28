using System.Drawing;
using System.Windows.Forms;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Extensibility;
using SCAnimator.V261.Engine;
using SCAnimator.V261.UI;

namespace SCAnimator.V261.Commands {
    internal sealed class MechanismsCapsule : CommandCapsule {
        private static MechanismsDialog openDialog;
        internal const string Name = "SCAnimator.V261.Mechanisms";
        internal MechanismsCapsule() : base(Name, "Mechanisms", Icons.AddKeyframe,
            "Manage saved hinge joints and bake driven motion to Tracks") { }
        protected override void OnInitialize(Command command) {
            command.IsWriteBlock = false; command.UpdateFrequency = UpdateFrequency.Always;
        }
        protected override void OnUpdate(Command command) {
            command.IsEnabled = Window.ActiveWindow != null && Window.ActiveWindow.Document != null &&
                Window.ActiveWindow.Document.IsComplete && !Animation.IsAnimating;
        }
        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) {
            if (openDialog != null && !openDialog.IsDisposed) {
                if (openDialog.IsForCurrentProject) {
                    openDialog.BringToFront(); openDialog.Activate(); return;
                }
                openDialog.Close();
            }
            openDialog = new MechanismsDialog(AnimationProject.Current);
            openDialog.FormClosed += delegate { openDialog = null; };
            openDialog.Show();
        }
    }
}

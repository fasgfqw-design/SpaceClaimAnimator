using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Extensibility;
using SCAnimator.V261.Engine;
using SCAnimator.V261.UI;

namespace SCAnimator.V261.Commands {
    internal sealed class ExportVideoCapsule : CommandCapsule {
        public const string CommandName = "SCAnimator.V261.ExportVideo";
        public ExportVideoCapsule() : base(CommandName, "Export Video", Icons.Export,
            "Record one playthrough and create a compatible H.264 MP4 or Motion JPEG AVI") { }
        protected override void OnInitialize(Command command) {
            command.IsWriteBlock = false;
            command.UpdateFrequency = UpdateFrequency.Always;
        }
        protected override void OnUpdate(Command command) {
            command.IsEnabled = !Animation.IsAnimating && !PlaybackController.IsExportingVideo &&
                AnimationProject.Current.CanPlay && AnimationProject.Current.CanUseCurrentAssembly;
        }
        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) {
            using (var dialog = new SaveFileDialog {
                Title = "Export SC Animator video",
                Filter = "MP4 video (*.mp4)|*.mp4|Motion JPEG AVI (*.avi)|*.avi",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "SCAnimator"
            }) {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                string extension = Path.GetExtension(dialog.FileName);
                if (!String.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase)) {
                    MessageBox.Show("Choose an .mp4 or .avi file name.",
                        "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { PlaybackController.ExportVideo(command, dialog.FileName); }
                catch (Exception ex) {
                    MessageBox.Show("Could not start video export:\n" + ex.Message,
                        "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}

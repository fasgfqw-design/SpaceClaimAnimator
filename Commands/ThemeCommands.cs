using System;
using System.Drawing;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Extensibility;
using SCAnimator.V261.UI;

namespace SCAnimator.V261.Commands {
    internal sealed class ThemeCapsule : CommandCapsule {
        private static readonly string[] Labels = { "Dark", "Light" };
        private bool synchronizing;
        public ThemeCapsule() : base("SCAnimator.V261.Theme", "Theme", null,
            "Choose the appearance of the Tracks panel. This preference is saved for your Windows account.") { }
        protected override void OnInitialize(Command command) {
            command.IsWriteBlock = false;
            command.UpdateFrequency = UpdateFrequency.Always;
            command.ControlState = ComboBoxState.CreateFixed(Labels, (int)ThemeSettings.Current);
            command.TextChanged += delegate {
                if (synchronizing) return;
                var state = command.ControlState as ComboBoxState;
                if (state != null && state.SelectedIndex >= 0 && state.SelectedIndex < Labels.Length)
                    ThemeSettings.Set((ThemeChoice)state.SelectedIndex);
            };
        }
        protected override void OnUpdate(Command command) {
            var state = command.ControlState as ComboBoxState;
            if (state != null && state.SelectedIndex == (int)ThemeSettings.Current) return;
            synchronizing = true;
            try { command.ControlState = ComboBoxState.CreateFixed(Labels, (int)ThemeSettings.Current); }
            finally { synchronizing = false; }
        }
        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) { }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using SCAnimator.V261.Engine;

namespace SCAnimator.V261.UI {
    internal enum ScenarioAction { Save, Play, Export }

    internal sealed class ScenarioDialog : Form {
        private readonly AnimationProject project;
        private readonly List<string> steps = new List<string>();
        private readonly ListBox sequence;
        private readonly ComboBox animation;
        private readonly NumericUpDown repeats;
        private readonly CheckBox pingPong;
        private readonly Label preview;
        private readonly Button remove, up, down, play, export;
        internal ScenarioAction Action { get; private set; }
        internal ScenarioDialog(AnimationProject project) {
            this.project = project;
            ScenarioDefinition saved = project.Scenario;
            steps.AddRange(saved.AnimationNames);
            Text = "Animation scenario";
            ClientSize = new Size(590, 402);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            bool light = ThemeSettings.Current == ThemeChoice.Light;
            BackColor = light ? Color.FromArgb(249, 250, 252) : Color.FromArgb(43, 46, 51);
            ForeColor = light ? Color.FromArgb(32, 40, 48) : Color.Gainsboro;
            Controls.Add(new Label { Text = "Arrange animations in the order they should play. A clip can appear more than once.",
                Left = 14, Top = 12, Width = 555, Height = 34, ForeColor = ForeColor });
            sequence = new ListBox { Left = 14, Top = 52, Width = 352, Height = 238,
                IntegralHeight = false, Font = new Font("Segoe UI", 10),
                BackColor = light ? Color.White : Color.FromArgb(65, 70, 76), ForeColor = ForeColor };
            Controls.Add(sequence);
            animation = new ComboBox { Left = 380, Top = 52, Width = 195,
                DropDownStyle = ComboBoxStyle.DropDownList };
            animation.Items.AddRange(project.AnimationNames);
            if (animation.Items.Count > 0) animation.SelectedIndex = project.ActiveAnimationIndex;
            Controls.Add(animation);
            Button add = AddButton("Add →", 380, 85, 195, delegate {
                if (animation.SelectedIndex < 0) return;
                int at = sequence.SelectedIndex < 0 ? steps.Count : sequence.SelectedIndex + 1;
                steps.Insert(at, animation.SelectedItem.ToString());
                RefreshSequence(at);
            });
            remove = AddButton("Remove", 380, 121, 195, delegate {
                int at = sequence.SelectedIndex;
                if (at < 0) return;
                steps.RemoveAt(at);
                RefreshSequence(Math.Min(at, steps.Count - 1));
            });
            up = AddButton("Move up", 380, 157, 195, delegate { MoveSelected(-1); });
            down = AddButton("Move down", 380, 193, 195, delegate { MoveSelected(1); });
            Controls.Add(new Label { Text = "Repeats:", Left = 14, Top = 307, Width = 72, Height = 24,
                ForeColor = ForeColor });
            repeats = new NumericUpDown { Left = 89, Top = 303, Width = 62, Minimum = 1, Maximum = 20,
                Value = saved.Repeats };
            Controls.Add(repeats);
            pingPong = new CheckBox { Text = "There and back", Left = 172, Top = 305, Width = 151,
                Checked = saved.PingPong, ForeColor = ForeColor };
            Controls.Add(pingPong);
            preview = new Label { Left = 14, Top = 337, Width = 548, Height = 24, ForeColor = ForeColor };
            Controls.Add(preview);
            play = AddButton("Play scenario", 14, 367, 128, delegate { Commit(ScenarioAction.Play); });
            export = AddButton("Export video", 151, 367, 128, delegate { Commit(ScenarioAction.Export); });
            AddButton("Save", 416, 367, 76, delegate { Commit(ScenarioAction.Save); });
            Button cancel = AddButton("Cancel", 501, 367, 76, delegate { DialogResult = DialogResult.Cancel; });
            CancelButton = cancel;
            sequence.SelectedIndexChanged += delegate { RefreshButtons(); };
            pingPong.CheckedChanged += delegate { RefreshButtons(); };
            repeats.ValueChanged += delegate { RefreshButtons(); };
            RefreshSequence(steps.Count > 0 ? 0 : -1);
        }
        private Button AddButton(string text, int x, int y, int width, System.Action click) {
            var button = new Button { Text = text, Left = x, Top = y, Width = width, Height = 27 };
            button.Click += delegate { click(); };
            Controls.Add(button);
            return button;
        }
        private void MoveSelected(int offset) {
            int at = sequence.SelectedIndex, target = at + offset;
            if (at < 0 || target < 0 || target >= steps.Count) return;
            string name = steps[at]; steps[at] = steps[target]; steps[target] = name;
            RefreshSequence(target);
        }
        private void RefreshSequence(int selected) {
            sequence.Items.Clear();
            for (int i = 0; i < steps.Count; i++) sequence.Items.Add((i + 1) + ".  " + steps[i]);
            if (selected >= 0 && selected < steps.Count) sequence.SelectedIndex = selected;
            RefreshButtons();
        }
        private void RefreshButtons() {
            int at = sequence.SelectedIndex;
            remove.Enabled = at >= 0;
            up.Enabled = at > 0;
            down.Enabled = at >= 0 && at < steps.Count - 1;
            play.Enabled = export.Enabled = steps.Count > 0;
            int passes = (int)repeats.Value * (pingPong.Checked ? 2 : 1);
            preview.Text = steps.Count == 0 ? "Add at least one animation to play a scenario." :
                steps.Count + " animation(s) × " + passes + " pass(es) = " +
                (steps.Count * passes) + " clip(s). Reverse pass plays clips in reverse order.";
        }
        private void Commit(ScenarioAction action) {
            var definition = new ScenarioDefinition { Repeats = (int)repeats.Value, PingPong = pingPong.Checked };
            definition.AnimationNames.AddRange(steps);
            if (!project.SetScenario(definition)) {
                MessageBox.Show("The scenario could not be saved for this model.", "SC Animator",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Action = action;
            DialogResult = DialogResult.OK;
        }
    }
}

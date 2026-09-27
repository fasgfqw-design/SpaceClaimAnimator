using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Globalization;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FormsPanel = System.Windows.Forms.Panel;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Extensibility;
using SCAnimator.V261.Engine;
using SCAnimator.V261.Commands;
using System.IO;

namespace SCAnimator.V261.UI {
    internal sealed class ShowTimelineCapsule : CommandCapsule {
        public const string CommandName = "SCAnimator.V261.ShowTimeline";
        public ShowTimelineCapsule() : base(CommandName, "Tracks", Icons.AddKeyframe,
            "Show or hide the docked animation tracks") { }
        protected override void OnInitialize(Command command) { command.IsWriteBlock = false; }
        protected override void OnExecute(Command command, ExecutionContext context, Rectangle buttonRect) {
            TimelinePanelHost.Toggle();
        }
    }

    internal static class TimelinePanelHost {
        private static PanelTab tab;
        private static TimelinePanel panel;
        public static void Initialize() {
            if (tab != null && !tab.IsDeleted) return;
            if (panel != null) panel.Dispose();
            panel = new TimelinePanel();
            tab = PanelTab.Create(Command.GetCommand(ShowTimelineCapsule.CommandName), panel,
                DockLocation.Bottom, 300, false);
        }
        public static void Toggle() {
            if (tab != null && !tab.IsDeleted && panel != null && panel.Visible) {
                Dispose();
                return;
            }
            if (tab == null || tab.IsDeleted) Initialize();
            tab.Activate();
        }
        public static void Dispose() {
            if (tab != null && !tab.IsDeleted) tab.Close();
            tab = null;
            if (panel != null) panel.Dispose();
            panel = null;
        }
    }

    internal sealed class TimelinePanel : UserControl {
        private const int TrackLabelWidth = 230;
        private readonly TrackCanvas canvas;
        private readonly Label status;
        private readonly FormsPanel bar;
        private readonly List<FormsPanel> toolbarSections = new List<FormsPanel>();
        private readonly ContextMenuStrip keyContextMenu;
        private readonly Button add, update, delete, undo, redo, copy, paste;
        private readonly Button newAnimation, duplicateAnimation, renameAnimation, deleteAnimation, addComponents, removeComponent, scenarioButton;
        private readonly Button rangeIn, rangeOut, rangeFull, zoomIn, zoomOut, saveStart;
        private readonly Button markersButton;
        private readonly Button insertPause;
        private readonly ComboBox animationPicker;
        private readonly Label searchLabel;
        private readonly TextBox trackSearch;
        private readonly CheckBox onlySelected, onlyFavorites;
        private readonly List<int> visibleTracks = new List<int>();
        private readonly HashSet<string> selectedFilterIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly CheckBox animateCamera;
        private bool updatingAnimations;
        private bool updatingCamera;
        private bool fittingToolbar;
        private readonly Timer timer;
        private readonly HashSet<TrackKeyRef> selectedKeys = new HashSet<TrackKeyRef>();
        private TrackKeyClipboard clipboard;
        private AnimationProject selectionProject;
        private int selectionGeneration;
        public TimelinePanel() {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(43, 46, 51);
            keyContextMenu = new ContextMenuStrip();
            bar = new FormsPanel { Dock = DockStyle.Top, Height = 100,
                BackColor = Color.FromArgb(55, 58, 63), AutoScroll = false };
            bar.Resize += delegate { FitToolbarHeight(); };
            FlowLayoutPanel animationsGroup = AddToolbarGroup("Animations");
            FlowLayoutPanel componentsGroup = AddToolbarGroup("Components");
            FlowLayoutPanel keysGroup = AddToolbarGroup("Keyframes");
            FlowLayoutPanel timelineGroup = AddToolbarGroup("Timeline & view");
            animationPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 155, Height = 26, Margin = new Padding(4, 3, 0, 0) };
            new ToolTip().SetToolTip(animationPicker, "Choose an animation stored in this model");
            animationsGroup.Controls.Add(animationPicker);
            newAnimation = MakeButton("New", "Create a separate animation in this model", animationsGroup);
            duplicateAnimation = MakeButton("Duplicate", "Copy this animation; optionally reverse its motion and visibility", animationsGroup);
            renameAnimation = MakeButton("Rename", "Rename the current animation", animationsGroup);
            deleteAnimation = MakeButton("Delete animation", "Delete the selected animation from this model", animationsGroup);
            scenarioButton = MakeButton("Scenario...", "Arrange animations into a sequence, repeat or play there and back", animationsGroup);
            addComponents = MakeButton("Add track", "Add selected components or movable planes; with no selection, add all remaining components", componentsGroup);
            removeComponent = MakeButton("Remove track", "Remove the selected component or plane track and its keys", componentsGroup);
            searchLabel = new Label { Text = "Find:", AutoSize = true,
                Margin = new Padding(8, 8, 0, 0), ForeColor = Color.White };
            componentsGroup.Controls.Add(searchLabel);
            trackSearch = new TextBox { Width = 145, Height = 26, Margin = new Padding(8, 3, 0, 0) };
            new ToolTip().SetToolTip(trackSearch, "Search tracks by name; press Esc to clear");
            componentsGroup.Controls.Add(trackSearch);
            onlySelected = new CheckBox { Text = "Selected", AutoSize = true,
                Margin = new Padding(5, 7, 0, 0), ForeColor = Color.White };
            new ToolTip().SetToolTip(onlySelected, "Show components selected in SpaceClaim when this filter is enabled; toggle to recapture");
            componentsGroup.Controls.Add(onlySelected);
            onlyFavorites = new CheckBox { Text = "★", AutoSize = true,
                Margin = new Padding(5, 7, 0, 0), ForeColor = Color.White };
            new ToolTip().SetToolTip(onlyFavorites, "Show only favorite tracks");
            componentsGroup.Controls.Add(onlyFavorites);
            add = MakeButton("Add key", "Capture the selected component or camera at the playhead", keysGroup);
            update = MakeButton("Update key", "Replace only the selected key after Move or camera adjustment", keysGroup);
            delete = MakeButton("Delete key", "Delete the selected diamond (Delete key also works)", keysGroup);
            undo = MakeButton("Undo", "Undo the last key edit (Ctrl+Z)", keysGroup);
            redo = MakeButton("Redo", "Redo the last undone edit (Ctrl+Y)", keysGroup);
            copy = MakeButton("Copy", "Copy selected keys (Ctrl+C)", keysGroup);
            paste = MakeButton("Paste", "Paste keys at the playhead (Ctrl+V)", keysGroup);
            rangeIn = MakeButton("Set In", "Set playback/export start to the playhead", timelineGroup);
            rangeOut = MakeButton("Set Out", "Set playback/export end to the playhead", timelineGroup);
            rangeFull = MakeButton("Full range", "Play and export the complete animation", timelineGroup);
            markersButton = MakeButton("Markers", "Add and navigate timeline markers", timelineGroup);
            insertPause = MakeButton("Insert pause", "Hold the current pose and shift all later keys", timelineGroup);
            zoomOut = MakeButton("Zoom −", "Show more time on the timeline", timelineGroup);
            zoomIn = MakeButton("Zoom +", "Magnify the timeline around the playhead", timelineGroup);
            saveStart = MakeButton("Save start", "Use the current component poses and camera as this animation's initial view", timelineGroup);
            animateCamera = new CheckBox { Text = "Animate camera", AutoSize = true,
                Margin = new Padding(8, 7, 0, 0), ForeColor = Color.White };
            timelineGroup.Controls.Add(animateCamera);
            animationPicker.SelectionChangeCommitted += OnAnimationSelected;
            newAnimation.Click += delegate { CreateAnimation(); };
            duplicateAnimation.Click += delegate { DuplicateAnimation(); };
            renameAnimation.Click += delegate { RenameAnimation(); };
            deleteAnimation.Click += delegate { DeleteAnimation(); };
            scenarioButton.Click += delegate { OpenScenario(); };
            removeComponent.Click += delegate { RemoveComponent(); };
            trackSearch.TextChanged += delegate { selectedKeys.Clear(); RefreshView(); };
            onlySelected.CheckedChanged += delegate { selectedKeys.Clear(); CaptureSelectedTrackFilter(); RefreshView(); };
            onlyFavorites.CheckedChanged += delegate { selectedKeys.Clear(); RefreshView(); };
            trackSearch.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape) { trackSearch.Clear(); e.Handled = true; }
            };
            rangeIn.Click += delegate { if (!AnimationProject.Current.SetPlaybackIn(AnimationProject.Current.PlayheadSeconds))
                MessageBox.Show("Choose a time before the current Out point.", "SC Animator"); RefreshView(); };
            rangeOut.Click += delegate { if (!AnimationProject.Current.SetPlaybackOut(AnimationProject.Current.PlayheadSeconds))
                MessageBox.Show("Choose a time after the current In point and within the animation.", "SC Animator"); RefreshView(); };
            rangeFull.Click += delegate { AnimationProject.Current.ClearPlaybackRange(); RefreshView(); };
            markersButton.Click += delegate { ShowMarkersMenu(); };
            insertPause.Click += delegate { InsertPauseAtPlayhead(); };
            zoomOut.Click += delegate { canvas.Zoom(-1); };
            zoomIn.Click += delegate { canvas.Zoom(1); };
            saveStart.Click += delegate {
                AnimationProject project = AnimationProject.Current;
                project.SetInitialState(project.GetComponents().Select(component => component.Placement).ToArray(),
                    Window.ActiveWindow.Projection);
                RefreshView();
            };
            animateCamera.CheckedChanged += delegate {
                if (updatingCamera) return;
                AnimationProject.Current.CameraEnabled = animateCamera.Checked;
                RefreshView();
            };
            addComponents.Click += delegate {
                if (!AnimationProject.Current.AddSelectedComponents())
                    MessageBox.Show("Select unrecorded components or movable planes. With no selection, all remaining assembly components are added.",
                        "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshView();
            };
            add.Click += delegate { Edit(0); };
            update.Click += delegate { Edit(1); };
            delete.Click += delegate { DeleteSelected(); };
            undo.Click += delegate { UndoEdit(); };
            redo.Click += delegate { RedoEdit(); };
            copy.Click += delegate { CopySelected(); };
            paste.Click += delegate { PasteSelected(); };
            status = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(55, 58, 63), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0) };
            canvas = new TrackCanvas(this) { Dock = DockStyle.Fill };
            Controls.Add(canvas);
            Controls.Add(status);
            Controls.Add(bar);
            timer = new Timer { Interval = 250 };
            timer.Tick += delegate { RefreshView(); };
            timer.Start();
            ThemeSettings.Changed += OnThemeChanged;
            ApplyTheme();
            FitToolbarHeight();
            RefreshView();
        }
        private FlowLayoutPanel AddToolbarGroup(string title) {
            var section = new FormsPanel { Height = 60, BackColor = Color.FromArgb(55, 58, 63),
                BorderStyle = BorderStyle.FixedSingle };
            var heading = new Label { Text = title, Left = 7, Top = 3, Width = 160, Height = 17,
                ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            var flow = new FlowLayoutPanel { Left = 3, Top = 21, Height = 30,
                WrapContents = true, AutoScroll = false, Margin = Padding.Empty };
            section.Controls.Add(heading); section.Controls.Add(flow);
            toolbarSections.Add(section);
            bar.Controls.Add(section);
            return flow;
        }
        private void FitToolbarHeight() {
            if (fittingToolbar) return;
            fittingToolbar = true;
            try {
                int width = Math.Max(1, bar.ClientSize.Width - 4);
                int columns = width >= 800 ? 4 : width >= 400 ? 2 : 1;
                const int gap = 3;
                int cellWidth = Math.Max(1, (width - (columns - 1) * gap) / columns);
                var preferredHeights = new int[toolbarSections.Count];
                for (int i = 0; i < toolbarSections.Count; i++) {
                    FormsPanel section = toolbarSections[i];
                    FlowLayoutPanel flow = section.Controls.OfType<FlowLayoutPanel>().First();
                    flow.Width = Math.Max(1, cellWidth - flow.Left - 4);
                    section.Controls.OfType<Label>().First().Width = Math.Max(1, cellWidth - 12);
                    int used = 0, lineHeight = 0, contentHeight = 0;
                    foreach (Control control in flow.Controls) {
                        int itemWidth = control.Width + control.Margin.Horizontal;
                        int itemHeight = control.Height + control.Margin.Vertical;
                        if (used > 0 && used + itemWidth > flow.Width) {
                            contentHeight += lineHeight; used = 0; lineHeight = 0;
                        }
                        used += itemWidth;
                        lineHeight = Math.Max(lineHeight, itemHeight);
                    }
                    flow.Height = Math.Max(29, contentHeight + lineHeight);
                    preferredHeights[i] = flow.Top + flow.Height + 3;
                }
                int top = 2;
                for (int row = 0; row * columns < toolbarSections.Count; row++) {
                    int first = row * columns;
                    int last = Math.Min(toolbarSections.Count, first + columns);
                    int height = 0;
                    for (int i = first; i < last; i++) height = Math.Max(height, preferredHeights[i]);
                    for (int i = first; i < last; i++)
                        toolbarSections[i].SetBounds(2 + (i - first) * (cellWidth + gap), top, cellWidth, height);
                    top += height + gap;
                }
                if (bar.Height != top) bar.Height = top;
            } finally { fittingToolbar = false; }
        }
        private void OnThemeChanged(object sender, EventArgs args) { ApplyTheme(); }
        private void ApplyTheme() {
            bool light = ThemeSettings.Current == ThemeChoice.Light;
            BackColor = light ? Color.FromArgb(249, 250, 252) : Color.FromArgb(43, 46, 51);
            bar.BackColor = status.BackColor = light ? Color.FromArgb(230, 234, 239) : Color.FromArgb(55, 58, 63);
            status.ForeColor = light ? Color.FromArgb(32, 40, 48) : Color.Gainsboro;
            animationPicker.BackColor = light ? Color.White : Color.FromArgb(70, 76, 83);
            animationPicker.ForeColor = light ? Color.FromArgb(32, 40, 48) : Color.White;
            trackSearch.BackColor = animationPicker.BackColor;
            trackSearch.ForeColor = animationPicker.ForeColor;
            animateCamera.ForeColor = searchLabel.ForeColor = onlySelected.ForeColor = onlyFavorites.ForeColor =
                light ? Color.FromArgb(32, 40, 48) : Color.White;
            foreach (FormsPanel section in toolbarSections) {
                section.BackColor = bar.BackColor;
                foreach (Control child in section.Controls) {
                    var heading = child as Label;
                    if (heading != null) heading.ForeColor = status.ForeColor;
                    foreach (Control control in child.Controls) {
                        var button = control as Button;
                        if (button == null) continue;
                        button.BackColor = light ? Color.FromArgb(248, 249, 251) : Color.FromArgb(70, 76, 83);
                        button.ForeColor = light ? Color.FromArgb(32, 40, 48) : Color.White;
                        button.FlatAppearance.BorderColor = light ? Color.FromArgb(170, 180, 190) : Color.FromArgb(96, 104, 112);
                    }
                }
            }
            canvas.BackColor = BackColor;
            canvas.Invalidate();
        }
        private static Button MakeButton(string label, string tip, Control parent) {
            var button = new Button { Text = label, Image = Icons.Toolbar(label), ImageAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText, AutoSize = true, Height = 26, FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White, BackColor = Color.FromArgb(70, 76, 83), Margin = new Padding(4, 3, 0, 0) };
            button.FlatAppearance.BorderColor = Color.FromArgb(96, 104, 112);
            new ToolTip().SetToolTip(button, tip);
            parent.Controls.Add(button);
            return button;
        }
        private void OnAnimationSelected(object sender, EventArgs args) {
            if (updatingAnimations) return;
            AnimationProject project = AnimationProject.Current;
            int index = animationPicker.SelectedIndex;
            if (index == project.ActiveAnimationIndex) return;
            try {
                if (!project.SwitchAnimation(index)) throw new InvalidOperationException("Finish playback before switching animations.");
                if (project.HasComponent) PlaybackController.GoToAnimationStart();
            } catch (Exception ex) {
                MessageBox.Show("Cannot open this animation: " + ex.Message, "SC Animator",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshView();
        }
        private void OpenScenario() {
            AnimationProject project = AnimationProject.Current;
            using (var dialog = new ScenarioDialog(project)) {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                RefreshView();
                if (dialog.Action == ScenarioAction.Save) return;
                try {
                    Command command = Command.GetCommand(PlayPauseCapsule.CommandName);
                    if (dialog.Action == ScenarioAction.Play) PlaybackController.PlayScenario(command);
                    else using (var save = new SaveFileDialog { Title = "Export SC Animator scenario",
                        Filter = "MP4 video (*.mp4)|*.mp4|Motion JPEG AVI (*.avi)|*.avi",
                        AddExtension = true, OverwritePrompt = true, FileName = "SCAnimator_Scenario" }) {
                        if (save.ShowDialog(FindForm()) == DialogResult.OK) {
                            string extension = Path.GetExtension(save.FileName);
                            if (!String.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase) &&
                                !String.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("Choose an .mp4 or .avi file name.");
                            PlaybackController.ExportScenario(command, save.FileName);
                        }
                    }
                } catch (Exception ex) {
                    MessageBox.Show("Could not start scenario:\n" + ex.Message,
                        "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        private static string AskName(string title, string initial) {
            using (var dialog = new Form { Text = title, Width = 360, Height = 135,
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false }) {
                var field = new TextBox { Left = 12, Top = 12, Width = 320, Text = initial };
                var okay = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 176, Top = 48, Width = 75 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 257, Top = 48, Width = 75 };
                dialog.Controls.Add(field); dialog.Controls.Add(okay); dialog.Controls.Add(cancel);
                dialog.AcceptButton = okay; dialog.CancelButton = cancel;
                return dialog.ShowDialog() == DialogResult.OK ? field.Text.Trim() : null;
            }
        }
        private void CreateAnimation() {
            AnimationProject project = AnimationProject.Current;
            string name = AskName("New animation", "Animation " + (project.AnimationCount + 1));
            if (name == null) return;
            if (!project.CreateAnimation(name))
                MessageBox.Show("Choose a unique name (up to 80 characters) and finish playback first.",
                    "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshView();
        }
        private void DuplicateAnimation() {
            AnimationProject project = AnimationProject.Current;
            string source = project.AnimationNames[project.ActiveAnimationIndex];
            string proposed = source + " copy";
            int suffix = 2;
            while (project.AnimationNames.Any(name => String.Equals(name, proposed, StringComparison.OrdinalIgnoreCase)))
                proposed = source + " copy " + suffix++;
            using (var dialog = new Form { Text = "Duplicate animation", Width = 390, Height = 197,
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false }) {
                var name = new TextBox { Left = 12, Top = 14, Width = 350, Text = proposed };
                var reverse = new CheckBox { Left = 12, Top = 47, Width = 350,
                    Text = "Reverse motion, markers and visibility" };
                var hint = new Label { Left = 32, Top = 73, Width = 330, Height = 28,
                    Text = "Fade reverses in time and acts before its mirrored key." };
                var okay = new Button { Text = "Create", DialogResult = DialogResult.OK,
                    Left = 206, Top = 112, Width = 75 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel,
                    Left = 287, Top = 112, Width = 75 };
                dialog.Controls.AddRange(new Control[] { name, reverse, hint, okay, cancel });
                dialog.AcceptButton = okay; dialog.CancelButton = cancel;
                if (dialog.ShowDialog() != DialogResult.OK) return;
                if (!project.DuplicateAnimation(name.Text.Trim(), reverse.Checked)) {
                    MessageBox.Show("Choose a unique animation name and finish playback first.",
                        "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            if (project.HasComponent) PlaybackController.GoToAnimationStart();
            RefreshView();
        }
        private void ShowMarkersMenu() {
            AnimationProject project = AnimationProject.Current;
            ContextMenuStrip menu = PrepareContextMenu();
            bool ready = project.CanEdit && project.CanUseCurrentAssembly;
            menu.Items.Add(MenuItem("Add marker at playhead...", ready,
                delegate { AddMarker(); }));
            TimelineMarker[] markers = project.TimelineMarkers;
            if (markers.Length > 0) menu.Items.Add(new ToolStripSeparator());
            foreach (TimelineMarker marker in markers) {
                double time = marker.Time;
                menu.Items.Add(MenuItem(marker.Name + "   " +
                    time.ToString("0.##", CultureInfo.InvariantCulture) + " s", ready,
                    delegate { PlaybackController.SeekTime(time); RefreshView(); }));
            }
            menu.Show(markersButton, new Point(0, markersButton.Height));
        }
        private void AddMarker() {
            AnimationProject project = AnimationProject.Current;
            string name = AskName("New timeline marker", "Marker " + (project.TimelineMarkers.Length + 1));
            if (name == null) return;
            if (!project.AddMarker(name, project.PlayheadSeconds))
                MessageBox.Show("Choose a unique name and a free time on the timeline.", "SC Animator");
            RefreshView();
        }
        internal void GoToMarker(int index) {
            TimelineMarker[] markers = AnimationProject.Current.TimelineMarkers;
            if (index < 0 || index >= markers.Length) return;
            PlaybackController.SeekTime(markers[index].Time);
            RefreshView();
        }
        internal void ShowMarkerMenu(Control surface, Point location, int index) {
            AnimationProject project = AnimationProject.Current;
            TimelineMarker[] markers = project.TimelineMarkers;
            if (index < 0 || index >= markers.Length) return;
            ContextMenuStrip menu = PrepareContextMenu();
            bool ready = project.CanEdit && project.CanUseCurrentAssembly;
            menu.Items.Add(MenuItem("Go to " + markers[index].Name, ready,
                delegate { GoToMarker(index); }));
            menu.Items.Add(MenuItem("Set In here", ready && markers[index].Time < project.EffectiveRangeEnd,
                delegate { project.SetPlaybackIn(markers[index].Time); RefreshView(); }));
            menu.Items.Add(MenuItem("Set Out here", ready && markers[index].Time > project.EffectiveRangeStart &&
                markers[index].Time <= project.DurationSeconds,
                delegate { project.SetPlaybackOut(markers[index].Time); RefreshView(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Rename...", ready, delegate {
                string name = AskName("Rename marker", markers[index].Name);
                if (name != null && !project.RenameMarker(index, name))
                    MessageBox.Show("Choose a unique marker name.", "SC Animator");
                RefreshView();
            }));
            menu.Items.Add(MenuItem("Move time...", ready, delegate {
                decimal? seconds = AskSeconds("Marker time", "Time in seconds (snaps to FPS)",
                    (decimal)markers[index].Time, 0, 3600, 4, 1m / project.FramesPerSecond);
                if (seconds.HasValue) {
                    double target = Math.Round((double)seconds.Value * project.FramesPerSecond) /
                        project.FramesPerSecond;
                    if (!project.MoveMarker(index, target))
                        MessageBox.Show("Another marker already uses that time.", "SC Animator");
                }
                RefreshView();
            }));
            menu.Items.Add(MenuItem("Delete marker", ready, delegate {
                project.DeleteMarker(index); RefreshView();
            }));
            menu.Show(surface, location);
        }
        private void RenameAnimation() {
            AnimationProject project = AnimationProject.Current;
            string name = AskName("Rename animation", project.AnimationNames[project.ActiveAnimationIndex]);
            if (name == null) return;
            if (!project.RenameAnimation(name))
                MessageBox.Show("Choose a unique name (up to 80 characters) and finish playback first.",
                    "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshView();
        }
        private void DeleteAnimation() {
            AnimationProject project = AnimationProject.Current;
            string name = project.AnimationNames[project.ActiveAnimationIndex];
            string consequence = project.AnimationCount == 1
                ? "This is the last animation. It will be replaced by an empty Animation 1."
                : "The other animations will remain in this model.";
            if (MessageBox.Show("Delete animation '" + name + "' and all of its keys?\n\n" + consequence,
                "SC Animator", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try {
                if (!project.DeleteActiveAnimation()) throw new InvalidOperationException("Finish playback before deleting an animation.");
                if (project.HasComponent) PlaybackController.GoToAnimationStart();
            } catch (Exception ex) {
                MessageBox.Show("Cannot delete animation: " + ex.Message, "SC Animator",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshView();
        }
        private void RemoveComponent() {
            AnimationProject project = AnimationProject.Current;
            int track = project.SelectedTrackIndex;
            if (track < 0 || track >= project.CameraTrackIndex) return;
            string name = project.GetTrackName(track);
            if (MessageBox.Show("Remove '" + name + "' and all its keys from the current animation?\n\n" +
                "The component remains in the SpaceClaim model and in any other animations.",
                "SC Animator", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (project.IsPlaneTrack(track) ? project.RemovePlaneTrack(track) : project.RemoveComponentTrack(track)) {
                if (project.HasComponent) PlaybackController.SeekTime(project.PlayheadSeconds);
            } else MessageBox.Show("Unlock this track and finish playback before removing it. Remove plane tracks before removing the last component.",
                "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshView();
        }
        private void SetSelectedVisibility(VisibilityMode mode) {
            TrackKeyRef? selected = SingleSelection();
            if (!selected.HasValue) return;
            AnimationProject project = AnimationProject.Current;
            if (!project.SetKeyVisibility(selected.Value.Track, selected.Value.Time, mode)) {
                MessageBox.Show(project.IsPlaneTrack(selected.Value.Track) &&
                    (mode == VisibilityMode.FadeIn || mode == VisibilityMode.FadeOut)
                    ? "Clipping planes can be shown or hidden, but cannot fade."
                    : "Visibility animation needs a component with bodies used by only one instance. Shared or nested parts cannot fade independently in the V261 API.",
                    "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } else PlaybackController.SeekTime(project.PlayheadSeconds);
            RefreshView();
        }
        private static decimal? AskSeconds(string title, string label, decimal value,
            decimal minimum, decimal maximum, int places, decimal increment) {
            using (var dialog = new Form { Text = title, Width = 315, Height = 145,
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false }) {
                var caption = new Label { Left = 12, Top = 13, Width = 275, Text = label };
                var field = new NumericUpDown { Left = 12, Top = 38, Width = 275,
                    DecimalPlaces = places, Minimum = minimum, Maximum = maximum,
                    Increment = increment, Value = Math.Max(minimum, Math.Min(maximum, value)) };
                var okay = new Button { Text = "OK", DialogResult = DialogResult.OK,
                    Left = 131, Top = 74, Width = 75 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel,
                    Left = 212, Top = 74, Width = 75 };
                dialog.Controls.AddRange(new Control[] { caption, field, okay, cancel });
                dialog.AcceptButton = okay; dialog.CancelButton = cancel;
                return dialog.ShowDialog() == DialogResult.OK ? (decimal?)field.Value : null;
            }
        }
        private void ChangeSelectedTime() {
            TrackKeyRef? selected = SingleSelection();
            if (!selected.HasValue) return;
            AnimationProject project = AnimationProject.Current;
            decimal? seconds = AskSeconds("Key time", "Time in seconds (snaps to FPS)",
                (decimal)selected.Value.Time, 0, 3600, 4, 1m / project.FramesPerSecond);
            if (!seconds.HasValue) return;
            double target = Math.Round((double)seconds.Value * project.FramesPerSecond) / project.FramesPerSecond;
            if (Math.Abs(target - selected.Value.Time) < 1e-8) return;
            MoveSelected(selected.Value.Track, selected.Value.Time, target);
        }
        private void ChangeFadeDuration() {
            AnimationProject project = AnimationProject.Current;
            decimal? seconds = AskSeconds("Fade duration", "Duration for this animation (seconds)",
                (decimal)project.FadeDurationSeconds, .1m, 5m, 2, .05m);
            if (!seconds.HasValue) return;
            project.FadeDurationSeconds = (double)seconds.Value;
            PlaybackController.SeekTime(project.PlayheadSeconds);
            RefreshView();
        }
        private void InsertPauseAtPlayhead() {
            AnimationProject project = AnimationProject.Current;
            decimal? seconds = AskSeconds("Insert pause", "Pause duration (seconds)",
                1m, .001m, 3600m, 3, 1m / project.FramesPerSecond);
            if (!seconds.HasValue) return;
            if (!project.InsertPause(project.PlayheadSeconds, (double)seconds.Value))
                MessageBox.Show("Cannot insert pause: unlock all tracks or choose a shorter duration. No keys changed.",
                    "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else { selectedKeys.Clear(); PlaybackController.SeekTime(project.PlayheadSeconds); }
            RefreshView();
        }
        private static ToolStripMenuItem MenuItem(string text, bool enabled, EventHandler click) {
            var item = new ToolStripMenuItem(text) { Enabled = enabled };
            if (click != null) item.Click += click;
            return item;
        }
        internal void ShowKeyMenu(Control surface, Point location, int track, double time) {
            SelectMarker(track, time, false);
            AnimationProject project = AnimationProject.Current;
            bool ready = project.CanEdit && project.CanUseCurrentAssembly;
            TrackKeyRef? single = SingleSelection();
            bool editable = ready && single.HasValue && !project.IsTrackLocked(track);
            bool visibilityAllowed = editable && track < project.CameraTrackIndex;
            bool plane = project.IsPlaneTrack(track);
            ContextMenuStrip menu = PrepareContextMenu();
            menu.Items.Add(MenuItem("Time: " + time.ToString("0.####", CultureInfo.InvariantCulture) + " s...",
                editable, delegate { ChangeSelectedTime(); }));
            var visibility = new ToolStripMenuItem(project.ReversedVisibility ?
                "Visibility (reverse-time copy)" : "Visibility");
            visibility.Enabled = visibilityAllowed;
            if (project.ReversedVisibility)
                visibility.DropDownItems.Add(MenuItem("Fade acts before this key", false, null));
            string[] labels = { "No change", "Show", "Hide", "Fade in", "Fade out" };
            for (int i = 0; i < labels.Length; i++) {
                VisibilityMode mode = (VisibilityMode)i;
                var item = MenuItem(labels[i], visibilityAllowed &&
                    (!plane || mode != VisibilityMode.FadeIn && mode != VisibilityMode.FadeOut),
                    delegate { SetSelectedVisibility(mode); });
                item.Checked = single.HasValue && project.GetKeyVisibility(track, time) == mode;
                visibility.DropDownItems.Add(item);
            }
            menu.Items.Add(visibility);
            menu.Items.Add(MenuItem("Fade duration (animation): " +
                project.FadeDurationSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s...",
                ready, delegate { ChangeFadeDuration(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Copy selected keys", ready && selectedKeys.Count > 0,
                delegate { CopySelected(); }));
            menu.Items.Add(MenuItem("Paste at playhead", ready && clipboard != null,
                delegate { PasteSelected(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Update key from current pose", editable,
                delegate { Edit(1); }));
            menu.Items.Add(MenuItem("Delete selected keys", ready && selectedKeys.Count > 0,
                delegate { DeleteSelected(); }));
            menu.Show(surface, location);
        }
        internal void ShowTimeMenu(Control surface, Point location, int track, double time) {
            Select(track, time, false);
            AnimationProject project = AnimationProject.Current;
            bool ready = project.CanEdit && project.CanUseCurrentAssembly;
            ContextMenuStrip menu = PrepareContextMenu();
            menu.Items.Add(MenuItem("Add key here", ready,
                delegate { Edit(0); }));
            menu.Items.Add(MenuItem("Paste keys here", ready && clipboard != null,
                delegate { PasteSelected(); }));
            menu.Show(surface, location);
        }
        private ContextMenuStrip PrepareContextMenu() {
            keyContextMenu.Hide();
            while (keyContextMenu.Items.Count > 0) {
                ToolStripItem item = keyContextMenu.Items[0];
                keyContextMenu.Items.RemoveAt(0);
                item.Dispose();
            }
            return keyContextMenu;
        }
        internal void Select(int track, double time, bool marker) {
            AnimationProject project = AnimationProject.Current;
            project.SelectedTrackIndex = track;
            selectedKeys.Clear();
            if (marker) selectedKeys.Add(new TrackKeyRef(track, time));
            PlaybackController.SeekTime(time);
            RefreshView();
        }
        internal bool IsSelected(int track, double time) { return selectedKeys.Contains(new TrackKeyRef(track, time)); }
        internal TrackKeyRef[] Selection() { var result = new TrackKeyRef[selectedKeys.Count]; selectedKeys.CopyTo(result); return result; }
        internal void SelectMarker(int track, double time, bool toggle) {
            var key = new TrackKeyRef(track, time);
            if (toggle) { if (!selectedKeys.Add(key)) selectedKeys.Remove(key); }
            else if (!selectedKeys.Contains(key)) { selectedKeys.Clear(); selectedKeys.Add(key); }
            AnimationProject.Current.SelectedTrackIndex = track;
            PlaybackController.SeekTime(time);
            RefreshView();
        }
        internal void SelectRectangle(Rectangle world, bool append) {
            AnimationProject project = AnimationProject.Current;
            if (!append) selectedKeys.Clear();
            for (int row = 0; row < visibleTracks.Count; row++) {
                int track = visibleTracks[row];
                int y = 30 + row * 29 + 14;
                if (y < world.Top || y > world.Bottom) continue;
                foreach (double time in project.GetTrackTimes(track)) {
                    int x = TrackLabelWidth + (int)Math.Round(time * canvas.TimePixels);
                    if (x >= world.Left && x <= world.Right) selectedKeys.Add(new TrackKeyRef(track, time));
                }
            }
            RefreshView();
        }
        internal int VisibleTrackCount { get { return visibleTracks.Count; } }
        internal int VisibleTrackAtRow(int row) {
            return row >= 0 && row < visibleTracks.Count ? visibleTracks[row] : -1;
        }
        internal int VisibleRowOfTrack(int track) { return visibleTracks.IndexOf(track); }
        private void RebuildVisibleTracks(AnimationProject project) {
            visibleTracks.Clear();
            if (!project.HasComponent) return;
            string query = trackSearch.Text.Trim();
            for (int track = 0; track < project.TrackCount; track++) {
                if (query.Length > 0 && project.GetTrackName(track).IndexOf(query,
                    StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (onlyFavorites.Checked && !project.IsFavoriteTrack(track)) continue;
                if (onlySelected.Checked && !selectedFilterIds.Contains(project.GetTrackIdentifier(track))) continue;
                visibleTracks.Add(track);
            }
        }
        private void CaptureSelectedTrackFilter() {
            selectedFilterIds.Clear();
            if (!onlySelected.Checked) return;
            AnimationProject project = AnimationProject.Current;
            var selectedComponents = new HashSet<Component>();
            var selectedPlanes = new HashSet<DatumPlane>();
            Window window = Window.ActiveWindow;
            if (window != null && window.ActiveContext != null) {
                foreach (IComponent component in window.ActiveContext.GetSelection<IComponent>())
                    selectedComponents.Add(component.Master);
                foreach (IDatumPlane plane in window.ActiveContext.GetSelection<IDatumPlane>())
                    selectedPlanes.Add(plane.Master);
            }
            for (int track = 0; track < project.TrackCount; track++) {
                IDocObject master = project.GetTrackMaster(track);
                if (master is Component && selectedComponents.Contains((Component)master) ||
                    master is DatumPlane && selectedPlanes.Contains((DatumPlane)master))
                    selectedFilterIds.Add(project.GetTrackIdentifier(track));
            }
        }
        internal void ToggleFavorite(int track) {
            AnimationProject.Current.ToggleFavoriteTrack(track);
            RefreshView();
        }
        internal void FocusTrackInModel(int track) {
            AnimationProject project = AnimationProject.Current;
            IDocObject master = project.GetTrackMaster(track);
            Window window = Window.ActiveWindow;
            if (master == null || window == null || window.Scene == null) return;
            if (master is Component) {
                foreach (IComponent occurrence in window.Scene.GetDescendants<IComponent>())
                    if (occurrence.Master == master) {
                        window.ActiveContext.SingleSelection = occurrence;
                        return;
                    }
            } else if (master is DatumPlane) {
                foreach (IDatumPlane occurrence in window.Scene.GetDescendants<IDatumPlane>())
                    if (occurrence.Master == master) {
                        window.ActiveContext.SingleSelection = occurrence;
                        return;
                    }
            }
        }
        private TrackKeyRef? SingleSelection() {
            if (selectedKeys.Count != 1) return null;
            foreach (TrackKeyRef key in selectedKeys) return key;
            return null;
        }
        private void Edit(int operation) {
            AnimationProject project = AnimationProject.Current;
            TrackKeyRef? single = SingleSelection();
            int track = operation == 0 ? project.SelectedTrackIndex : single.HasValue ? single.Value.Track : -1;
            double time = operation == 0 ? project.PlayheadSeconds : single.HasValue ? single.Value.Time : -1;
            bool okay = operation == 0 ? project.AddTrackKey(track, time)
                : operation == 1 ? project.UpdateTrackKey(track, time) : project.DeleteTrackKey(track, time);
            if (okay) { selectedKeys.Clear(); if (operation != 2) selectedKeys.Add(new TrackKeyRef(track, time)); }
            else MessageBox.Show(operation == 2 && track < project.ComponentCount && project.GetTrackTimes(track).Length == 1
                ? "Each component must keep at least one key. Add another key before deleting this one."
                : "Select a component track and a key. Use Move or adjust the camera, then choose Add key or Update key.",
                "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshView();
        }
        internal void DeleteSelected() {
            if (selectedKeys.Count == 0) return;
            if (AnimationProject.Current.DeleteTrackKeys(Selection())) {
                selectedKeys.Clear();
                PlaybackController.SeekTime(AnimationProject.Current.PlayheadSeconds);
                RefreshView();
            }
            else MessageBox.Show("Unlock selected tracks and leave at least one key for each component. No keys were changed.",
                "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        internal void MoveSelected(int track, double from, double to) {
            AnimationProject project = AnimationProject.Current;
            TrackKeyRef[] before = Selection();
            double delta = to - from;
            if (project.MoveTrackKeys(before, delta)) {
                selectedKeys.Clear();
                foreach (TrackKeyRef key in before) selectedKeys.Add(new TrackKeyRef(key.Track, key.Time + delta));
                project.SelectedTrackIndex = track;
                PlaybackController.SeekTime(to);
                RefreshView();
            } else {
                MessageBox.Show("The selected keys cannot move: unlock the tracks and choose free positions within the timeline. No keys were changed.",
                    "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
                canvas.Invalidate();
            }
        }
        private void UndoEdit() { if (AnimationProject.Current.UndoTrackEdit()) { selectedKeys.Clear(); PlaybackController.SeekTime(AnimationProject.Current.PlayheadSeconds); RefreshView(); } }
        private void RedoEdit() { if (AnimationProject.Current.RedoTrackEdit()) { selectedKeys.Clear(); PlaybackController.SeekTime(AnimationProject.Current.PlayheadSeconds); RefreshView(); } }
        private void CopySelected() { clipboard = AnimationProject.Current.CopyTrackKeys(Selection()); RefreshView(); }
        private void PasteSelected() {
            TrackKeyRef[] pasted;
            AnimationProject project = AnimationProject.Current;
            if (project.PasteTrackKeys(clipboard, project.PlayheadSeconds, out pasted)) {
                selectedKeys.Clear(); foreach (TrackKeyRef key in pasted) selectedKeys.Add(key);
                PlaybackController.SeekTime(project.PlayheadSeconds); RefreshView();
            } else MessageBox.Show("Cannot paste: a destination is occupied or outside the timeline. No keys were changed.",
                "SC Animator", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        internal void StepSelected(int direction) {
            AnimationProject project = AnimationProject.Current;
            double[] times = project.GetTrackTimes(project.SelectedTrackIndex);
            if (direction < 0) {
                for (int i = times.Length - 1; i >= 0; i--)
                    if (times[i] < project.PlayheadSeconds - 1e-6) { Select(project.SelectedTrackIndex, times[i], true); return; }
            } else {
                foreach (double time in times)
                    if (time > project.PlayheadSeconds + 1e-6) { Select(project.SelectedTrackIndex, time, true); return; }
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData == (Keys.Control | Keys.Z)) { UndoEdit(); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { RedoEdit(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { CopySelected(); return true; }
            if (keyData == (Keys.Control | Keys.V)) { PasteSelected(); return true; }
            if (keyData == Keys.Delete) { DeleteSelected(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void RefreshView() {
            if (IsDisposed) return;
            AnimationProject project = AnimationProject.Current;
            string[] names = project.AnimationNames;
            bool changed = animationPicker.Items.Count != names.Length;
            if (!changed) for (int i = 0; i < names.Length; i++)
                if (!String.Equals(animationPicker.Items[i] as string, names[i], StringComparison.Ordinal)) { changed = true; break; }
            if (!animationPicker.DroppedDown && (changed || animationPicker.SelectedIndex != project.ActiveAnimationIndex)) {
                updatingAnimations = true;
                if (changed) { animationPicker.Items.Clear(); animationPicker.Items.AddRange(names); }
                animationPicker.SelectedIndex = project.ActiveAnimationIndex;
                updatingAnimations = false;
            }
            if (!Object.ReferenceEquals(selectionProject, project) || selectionGeneration != project.EditGeneration) {
                selectedKeys.Clear(); clipboard = null; selectionProject = project;
                selectionGeneration = project.EditGeneration;
                if (onlySelected.Checked) CaptureSelectedTrackFilter();
            }
            selectedKeys.RemoveWhere(key => !project.HasTrackKey(key.Track, key.Time));
            RebuildVisibleTracks(project);
            selectedKeys.RemoveWhere(key => !visibleTracks.Contains(key.Track));
            bool ready = project.CanEdit && project.CanUseCurrentAssembly;
            updatingCamera = true;
            animateCamera.Checked = project.CameraEnabled;
            updatingCamera = false;
            TrackKeyRef? selected = SingleSelection();
            animateCamera.Enabled = ready;
            rangeIn.Enabled = rangeOut.Enabled = rangeFull.Enabled = ready && project.DurationSeconds > 0;
            saveStart.Enabled = ready && project.HasComponent;
            zoomIn.Enabled = zoomOut.Enabled = project.HasComponent;
            animationPicker.Enabled = !Animation.IsAnimating && project.AnimationCount > 0;
            newAnimation.Enabled = !Animation.IsAnimating && Window.ActiveWindow != null;
            duplicateAnimation.Enabled = !Animation.IsAnimating && Window.ActiveWindow != null;
            renameAnimation.Enabled = !Animation.IsAnimating && Window.ActiveWindow != null;
            deleteAnimation.Enabled = !Animation.IsAnimating && Window.ActiveWindow != null;
            scenarioButton.Enabled = !Animation.IsAnimating && Window.ActiveWindow != null &&
                Window.ActiveWindow.Document != null && Window.ActiveWindow.Document.IsComplete;
            addComponents.Enabled = !Animation.IsAnimating && Window.ActiveWindow != null &&
                Window.ActiveWindow.Document != null && Window.ActiveWindow.Document.IsComplete &&
                (!project.HasComponent || ready);
            removeComponent.Enabled = ready && visibleTracks.Contains(project.SelectedTrackIndex) &&
                project.SelectedTrackIndex >= 0 &&
                project.SelectedTrackIndex < project.CameraTrackIndex;
            add.Enabled = ready && visibleTracks.Contains(project.SelectedTrackIndex);
            update.Enabled = ready && selectedKeys.Count == 1;
            delete.Enabled = ready && selectedKeys.Count > 0;
            undo.Enabled = project.CanUndoTrackEdit;
            redo.Enabled = project.CanRedoTrackEdit;
            copy.Enabled = ready && selectedKeys.Count > 0;
            paste.Enabled = ready && clipboard != null;
            markersButton.Enabled = ready;
            insertPause.Enabled = ready && project.DurationSeconds > 0;
            status.Text = project.HasComponent && visibleTracks.Count == 0
                ? "No tracks match the current search or filters. Clear Find / Selected / ★ to show all tracks."
                : project.HasComponent
                ? String.Format(CultureInfo.InvariantCulture,
                    "{0}  •  {1:0.00}s  •  {2} selected  •  range {3:0.00}–{4:0.00}s  •  Ctrl+wheel: zoom  •  Shift+wheel: time",
                    project.GetTrackName(project.SelectedTrackIndex), project.PlayheadSeconds, selectedKeys.Count,
                    project.EffectiveRangeStart, project.EffectiveRangeEnd)
                : "Select components and click Add track, or clear selection to add all assembly components.";
            if (!String.IsNullOrEmpty(PlaybackController.ScenarioStatus))
                status.Text = PlaybackController.ScenarioStatus + "  •  Esc: stop";
            else if (project.ReversedVisibility)
                status.Text += "  •  Visibility is time-reversed; Fade acts before its key";
            canvas.RefreshSize();
            canvas.Invalidate();
        }
        protected override void Dispose(bool disposing) {
            if (disposing) {
                ThemeSettings.Changed -= OnThemeChanged;
                timer.Stop(); timer.Dispose(); keyContextMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class TrackCanvas : ScrollableControl {
            private const int LabelWidth = TrackLabelWidth, Header = 30, Row = 29;
            private int PixelsPerSecond = 100;
            internal int TimePixels { get { return PixelsPerSecond; } }
            private readonly TimelinePanel owner;
            private readonly Font smallFont = new Font("Segoe UI", 8.5f);
            private readonly ToolTip trackTip = new ToolTip();
            private string lastTip;
            private bool dragPending, dragging, boxPending, boxing, boxAppend, rulerScrubbing;
            private Point? pendingContextPoint;
            private int dragStartX, dragTrack;
            private Point boxStart, boxEnd;
            private double dragFrom, dragPreview;
            public TrackCanvas(TimelinePanel owner) {
                this.owner = owner;
                AutoScroll = true;
                DoubleBuffered = true;
                TabStop = true;
                BackColor = Color.FromArgb(43, 46, 51);
            }
            public void RefreshSize() {
                AnimationProject project = AnimationProject.Current;
                int width = LabelWidth + (int)(Math.Max(12,
                    Math.Max(Math.Max(project.DurationSeconds, project.PlayheadSeconds),
                        project.LastMarkerTime) + 10) * PixelsPerSecond);
                int height = Header + Math.Max(1, owner.VisibleTrackCount) * Row + 12;
                if (AutoScrollMinSize.Width != width || AutoScrollMinSize.Height != height)
                    AutoScrollMinSize = new Size(width, height);
            }
            public void Zoom(int direction) {
                int next = direction > 0 ? (int)Math.Round(PixelsPerSecond * 1.25) :
                    (int)Math.Round(PixelsPerSecond / 1.25);
                PixelsPerSecond = Math.Max(35, Math.Min(400, next));
                RefreshSize();
                KeepPlayheadVisible(AnimationProject.Current.PlayheadSeconds);
                Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e) {
                base.OnPaint(e);
                AnimationProject project = AnimationProject.Current;
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                GraphicsState state = g.Save();
                g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
                int width = AutoScrollMinSize.Width, count = owner.VisibleTrackCount;
                bool light = ThemeSettings.Current == ThemeChoice.Light;
                using (var grid = new Pen(light ? Color.FromArgb(218, 223, 229) : Color.FromArgb(65, 70, 76)))
                using (var line = new Pen(light ? Color.FromArgb(138, 157, 176) : Color.FromArgb(95, 111, 126)))
                using (var playhead = new Pen(Color.FromArgb(255, 127, 82), 2))
                using (var labelBrush = new SolidBrush(light ? Color.FromArgb(32, 40, 48) : Color.Gainsboro))
                using (var blue = new SolidBrush(light ? Color.FromArgb(33, 112, 171) : Color.FromArgb(95, 176, 230)))
                using (var selected = new SolidBrush(light ? Color.FromArgb(214, 119, 23) : Color.FromArgb(255, 190, 90)))
                using (var selectedRow = new SolidBrush(light ? Color.FromArgb(221, 235, 248) : Color.FromArgb(61, 71, 80))) {
                    if (project.HasPlaybackRange) {
                        float inX = LabelWidth + (float)project.EffectiveRangeStart * PixelsPerSecond;
                        float outX = LabelWidth + (float)project.EffectiveRangeEnd * PixelsPerSecond;
                        using (var fill = new SolidBrush(Color.FromArgb(30, 70, 180, 110)))
                        using (var boundary = new Pen(Color.FromArgb(60, 170, 100), 2)) {
                            g.FillRectangle(fill, inX, 0, outX - inX, Header + count * Row);
                            g.DrawLine(boundary, inX, 0, inX, Header + count * Row);
                            g.DrawLine(boundary, outX, 0, outX, Header + count * Row);
                        }
                    }
                    for (int s = 0; LabelWidth + s * PixelsPerSecond < width; s++) {
                        int x = LabelWidth + s * PixelsPerSecond;
                        g.DrawLine(grid, x, 0, x, Header + count * Row);
                        g.DrawString(s.ToString(CultureInfo.InvariantCulture) + "s", smallFont, labelBrush, x + 3, 5);
                    }
                    for (int row = 0; row < count; row++) {
                        int track = owner.VisibleTrackAtRow(row);
                        int y = Header + row * Row;
                        if (track == project.SelectedTrackIndex)
                            g.FillRectangle(selectedRow, 0, y, width, Row);
                        g.DrawLine(grid, 0, y + Row, width, y + Row);
                        g.DrawLine(line, LabelWidth, y + Row / 2, width, y + Row / 2);
                        foreach (double t in project.GetTrackTimes(track)) {
                            float x = LabelWidth + (float)t * PixelsPerSecond;
                            float cy = y + Row / 2f;
                            Brush brush = owner.IsSelected(track, t) ? selected : blue;
                            g.FillPolygon(brush, new[] { new PointF(x, cy - 7), new PointF(x + 7, cy),
                                new PointF(x, cy + 7), new PointF(x - 7, cy) });
                            VisibilityMode visibility = project.GetKeyVisibility(track, t);
                            if (visibility != VisibilityMode.Default) {
                                using (var stateBrush = new SolidBrush(visibility == VisibilityMode.Hide
                                    ? Color.FromArgb(220, 80, 75) : visibility == VisibilityMode.FadeIn
                                        ? Color.FromArgb(244, 184, 60) : visibility == VisibilityMode.FadeOut
                                            ? Color.FromArgb(174, 115, 220) : Color.FromArgb(80, 185, 110)))
                                    g.FillEllipse(stateBrush, x + 7, cy - 11, 6, 6);
                            }
                        }
                    }
                    using (var markerPen = new Pen(light ? Color.FromArgb(110, 140, 185) : Color.FromArgb(110, 180, 230)))
                    using (var markerBrush = new SolidBrush(light ? Color.FromArgb(35, 100, 165) : Color.FromArgb(115, 195, 245))) {
                        markerPen.DashStyle = DashStyle.Dot;
                        foreach (TimelineMarker marker in project.TimelineMarkers) {
                            float x = LabelWidth + (float)marker.Time * PixelsPerSecond;
                            g.DrawLine(markerPen, x, Header, x, Header + count * Row);
                            g.FillPolygon(markerBrush, new[] { new PointF(x - 5, 15), new PointF(x + 5, 15), new PointF(x, 25) });
                            g.DrawString(marker.Name, smallFont, markerBrush, x + 7, 13);
                        }
                    }
                    float px = LabelWidth + (float)project.PlayheadSeconds * PixelsPerSecond;
                    g.DrawLine(playhead, px, 0, px, Header + count * Row);
                    if (dragging) {
                        foreach (TrackKeyRef key in owner.Selection()) {
                            int visibleRow = owner.VisibleRowOfTrack(key.Track);
                            if (visibleRow < 0) continue;
                            float markerX = LabelWidth + (float)(key.Time + dragPreview - dragFrom) * PixelsPerSecond;
                            float markerY = Header + visibleRow * Row + Row / 2f;
                            g.FillPolygon(selected, new[] { new PointF(markerX, markerY - 8), new PointF(markerX + 8, markerY),
                                new PointF(markerX, markerY + 8), new PointF(markerX - 8, markerY) });
                        }
                        float x = LabelWidth + (float)dragPreview * PixelsPerSecond;
                        float cy = Header + Math.Max(0, owner.VisibleRowOfTrack(project.SelectedTrackIndex)) * Row + Row / 2f;
                        g.DrawString(dragPreview.ToString("0.00", CultureInfo.InvariantCulture) + "s",
                            smallFont, labelBrush, x + 10, cy - 9);
                    }
                    if (boxing) {
                        Rectangle rect = Rectangle.FromLTRB(Math.Min(boxStart.X, boxEnd.X), Math.Min(boxStart.Y, boxEnd.Y),
                            Math.Max(boxStart.X, boxEnd.X), Math.Max(boxStart.Y, boxEnd.Y));
                        using (var fill = new SolidBrush(Color.FromArgb(55, 95, 176, 230))) g.FillRectangle(fill, rect);
                        g.DrawRectangle(line, rect);
                    }
                }
                g.Restore(state);
                // Keep component names visible while the timeline moves sideways.
                using (var background = new SolidBrush(light ? Color.FromArgb(249, 250, 252) : Color.FromArgb(43, 46, 51)))
                using (var highlighted = new SolidBrush(light ? Color.FromArgb(221, 235, 248) : Color.FromArgb(61, 71, 80)))
                using (var label = new SolidBrush(light ? Color.FromArgb(32, 40, 48) : Color.Gainsboro))
                using (var edge = new Pen(light ? Color.FromArgb(180, 190, 200) : Color.FromArgb(80, 86, 93)))
                using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap }) {
                    g.FillRectangle(background, 0, 0, LabelWidth, ClientSize.Height);
                    g.DrawString("Tracks", smallFont, label, 8, 5);
                    for (int row = 0; row < count; row++) {
                        int track = owner.VisibleTrackAtRow(row);
                        int y = Header + row * Row + AutoScrollPosition.Y;
                        if (y + Row < Header || y > ClientSize.Height) continue;
                        if (track == project.SelectedTrackIndex) g.FillRectangle(highlighted, 0, y, LabelWidth, Row);
                        string name = project.GetTrackName(track);
                        if (track == project.CameraTrackIndex && !project.CameraEnabled) name += " (off)";
                        g.DrawString(project.IsFavoriteTrack(track) ? "★" : "☆", smallFont, label, 7, y + 6);
                        g.DrawString(name, smallFont, label,
                            new RectangleF(27, y + 6, LabelWidth - 61, Row - 8), format);
                        DrawLock(g, LabelWidth - 27, y + 5, project.IsTrackLocked(track),
                            light ? Color.FromArgb(55, 67, 80) : Color.Gainsboro);
                        g.DrawLine(edge, 0, y + Row, LabelWidth, y + Row);
                    }
                    g.DrawLine(edge, LabelWidth - 1, 0, LabelWidth - 1, ClientSize.Height);
                }
                PaintPinnedRuler(g, project, light);
            }
            private void PaintPinnedRuler(Graphics g, AnimationProject project, bool light) {
                Color background = light ? Color.FromArgb(249, 250, 252) : Color.FromArgb(43, 46, 51);
                Color foreground = light ? Color.FromArgb(32, 40, 48) : Color.Gainsboro;
                Color markerColor = light ? Color.FromArgb(35, 100, 165) : Color.FromArgb(115, 195, 245);
                using (var fill = new SolidBrush(background))
                using (var label = new SolidBrush(foreground))
                using (var markerBrush = new SolidBrush(markerColor))
                using (var grid = new Pen(light ? Color.FromArgb(218, 223, 229) : Color.FromArgb(65, 70, 76)))
                using (var edge = new Pen(light ? Color.FromArgb(180, 190, 200) : Color.FromArgb(80, 86, 93)))
                using (var playhead = new Pen(Color.FromArgb(255, 127, 82), 2)) {
                    g.FillRectangle(fill, 0, 0, ClientSize.Width, Header);
                    GraphicsState state = g.Save();
                    g.SetClip(new Rectangle(LabelWidth, 0, Math.Max(0, ClientSize.Width - LabelWidth), Header));
                    if (project.HasPlaybackRange) {
                        float inX = LabelWidth + (float)project.EffectiveRangeStart * PixelsPerSecond + AutoScrollPosition.X;
                        float outX = LabelWidth + (float)project.EffectiveRangeEnd * PixelsPerSecond + AutoScrollPosition.X;
                        using (var rangeFill = new SolidBrush(Color.FromArgb(30, 70, 180, 110)))
                        using (var boundary = new Pen(Color.FromArgb(60, 170, 100), 2)) {
                            g.FillRectangle(rangeFill, inX, 0, outX - inX, Header);
                            g.DrawLine(boundary, inX, 0, inX, Header);
                            g.DrawLine(boundary, outX, 0, outX, Header);
                        }
                    }
                    int firstSecond = Math.Max(0, (int)Math.Floor(-AutoScrollPosition.X / (double)PixelsPerSecond) - 1);
                    int lastSecond = Math.Min((AutoScrollMinSize.Width - LabelWidth) / PixelsPerSecond,
                        (int)Math.Ceiling((ClientSize.Width - LabelWidth - AutoScrollPosition.X) / (double)PixelsPerSecond) + 1);
                    for (int second = firstSecond; second <= lastSecond; second++) {
                        int x = LabelWidth + second * PixelsPerSecond + AutoScrollPosition.X;
                        g.DrawLine(grid, x, 0, x, Header);
                        g.DrawString(second.ToString(CultureInfo.InvariantCulture) + "s", smallFont, label, x + 3, 5);
                    }
                    foreach (TimelineMarker marker in project.TimelineMarkers) {
                        float x = LabelWidth + (float)marker.Time * PixelsPerSecond + AutoScrollPosition.X;
                        g.FillPolygon(markerBrush, new[] { new PointF(x - 5, 15), new PointF(x + 5, 15), new PointF(x, 25) });
                        g.DrawString(marker.Name, smallFont, markerBrush, x + 7, 13);
                    }
                    float playheadX = LabelWidth + (float)project.PlayheadSeconds * PixelsPerSecond + AutoScrollPosition.X;
                    g.DrawLine(playhead, playheadX, 0, playheadX, Header);
                    g.Restore(state);
                    g.DrawString("Tracks", smallFont, label, 8, 5);
                    g.DrawLine(edge, 0, Header - 1, ClientSize.Width, Header - 1);
                    g.DrawLine(edge, LabelWidth - 1, 0, LabelWidth - 1, Header);
                }
            }
            private static void DrawLock(Graphics g, int x, int y, bool locked, Color color) {
                using (var pen = new Pen(color, 1.7f)) {
                    g.DrawRectangle(pen, x + 2, y + 10, 16, 11);
                    if (locked) g.DrawArc(pen, x + 5, y + 2, 10, 15, 180, 180);
                    else g.DrawArc(pen, x + 9, y + 2, 10, 15, 180, 145);
                }
            }
            protected override void OnMouseWheel(MouseEventArgs e) {
                if ((ModifierKeys & Keys.Control) != 0) { Zoom(e.Delta > 0 ? 1 : -1); return; }
                if ((ModifierKeys & Keys.Shift) != 0) {
                    ScrubTime(-e.Delta);
                    return;
                }
                // Always scroll rows with the ordinary wheel, independent of cursor position.
                int current = -AutoScrollPosition.Y;
                int maximum = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height);
                int next = Math.Max(0, Math.Min(maximum,
                    current - (int)Math.Round(e.Delta * Row * 3.0 / 120.0)));
                AutoScrollPosition = new Point(-AutoScrollPosition.X, next);
                Invalidate();
            }
            protected override void OnScroll(ScrollEventArgs e) {
                base.OnScroll(e);
                Invalidate();
            }
            protected override void WndProc(ref Message message) {
                const int WM_MOUSEHWHEEL = 0x020E;
                if (message.Msg == WM_MOUSEHWHEEL) {
                    short delta = unchecked((short)((message.WParam.ToInt64() >> 16) & 0xffff));
                    ScrubTime(delta);
                    message.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref message);
            }
            private void ScrubTime(int horizontalDelta) {
                AnimationProject project = AnimationProject.Current;
                if (!project.CanEdit || !project.HasComponent || horizontalDelta == 0) return;
                int fps = project.FramesPerSecond;
                double next = Math.Max(0, Math.Min(3600,
                    Math.Round((project.PlayheadSeconds + horizontalDelta * 0.25 / 120.0) * fps) / fps));
                if (Math.Abs(next - project.PlayheadSeconds) < 1e-8) return;
                int track = project.SelectedTrackIndex;
                owner.Select(track, next, project.HasTrackKey(track, next));
                KeepPlayheadVisible(next);
            }
            private void SeekRulerAt(int clientX) {
                AnimationProject project = AnimationProject.Current;
                int fps = project.FramesPerSecond;
                double seconds = Math.Max(0, Math.Min(3600,
                    (clientX - AutoScrollPosition.X - LabelWidth) / (double)PixelsPerSecond));
                double next = Math.Round(seconds * fps) / fps;
                if (Math.Abs(next - project.PlayheadSeconds) < 1e-8) return;
                owner.Select(project.SelectedTrackIndex, next, false);
            }
            private void KeepPlayheadVisible(double seconds) {
                int worldX = LabelWidth + (int)Math.Round(seconds * PixelsPerSecond);
                int screenX = worldX + AutoScrollPosition.X;
                if (screenX >= LabelWidth + 15 && screenX <= ClientSize.Width - 30) return;
                int maximum = Math.Max(0, AutoScrollMinSize.Width - ClientSize.Width);
                int desired = Math.Max(0, Math.Min(maximum,
                    worldX - Math.Max(LabelWidth + 20, ClientSize.Width / 2)));
                AutoScrollPosition = new Point(desired, -AutoScrollPosition.Y);
                Invalidate();
            }
            protected override void OnMouseDown(MouseEventArgs e) {
                base.OnMouseDown(e);
                Focus();
                AnimationProject project = AnimationProject.Current;
                if (!project.CanEdit || !project.HasComponent) return;
                if (e.Button == MouseButtons.Right) { pendingContextPoint = e.Location; return; }
                if (e.Button != MouseButtons.Left) return;
                pendingContextPoint = null;
                int x = e.X - AutoScrollPosition.X, y = e.Y - AutoScrollPosition.Y;
                if (e.X < LabelWidth) {
                    if (e.Y < Header) return;
                    int row = (y - Header) / Row;
                    int labelTrack = owner.VisibleTrackAtRow(row);
                    if (y >= Header && labelTrack >= 0) {
                        project.SelectedTrackIndex = labelTrack;
                        if (e.X >= LabelWidth - 31 && e.X < LabelWidth - 4)
                            project.SetTrackLocked(labelTrack, !project.IsTrackLocked(labelTrack));
                        else if (e.X < 25) owner.ToggleFavorite(labelTrack);
                        owner.RefreshView();
                    }
                    return;
                }
                if (e.Y < Header) {
                    rulerScrubbing = true;
                    Capture = true;
                    int marker = FindMarkerAt(x);
                    if (marker >= 0) owner.GoToMarker(marker);
                    else SeekRulerAt(e.X);
                    return;
                }
                int track = owner.VisibleTrackAtRow((y - Header) / Row);
                if (track < 0) return;
                double clicked = Math.Max(0, (x - LabelWidth) / (double)PixelsPerSecond);
                double nearest = -1, distance = 8.0 / PixelsPerSecond;
                foreach (double time in project.GetTrackTimes(track)) {
                    double d = Math.Abs(clicked - time);
                    if (d <= distance) { nearest = time; distance = d; }
                }
                bool toggle = (ModifierKeys & Keys.Control) != 0;
                if (nearest >= 0) {
                    owner.SelectMarker(track, nearest, toggle);
                    if (toggle) return;
                    dragPending = true;
                    dragging = false;
                    dragTrack = track;
                    dragFrom = dragPreview = nearest;
                    dragStartX = e.X;
                    Capture = true;
                } else {
                    project.SelectedTrackIndex = track;
                    boxPending = true; boxing = false; boxAppend = toggle;
                    boxStart = boxEnd = new Point(x, y);
                    dragStartX = e.X;
                    Capture = true;
                }
            }
            protected override void OnMouseMove(MouseEventArgs e) {
                base.OnMouseMove(e);
                if (rulerScrubbing) {
                    if ((e.Button & MouseButtons.Left) != 0) SeekRulerAt(e.X);
                    else { rulerScrubbing = false; Capture = false; }
                    return;
                }
                string tip = null;
                if (e.X < LabelWidth) {
                    AnimationProject project = AnimationProject.Current;
                    int row = (e.Y - AutoScrollPosition.Y - Header) / Row;
                    int track = owner.VisibleTrackAtRow(row);
                    if (e.Y >= Header && track >= 0)
                        tip = e.X >= LabelWidth - 31 ?
                            (project.IsTrackLocked(track) ? "Unlock track" : "Lock track") :
                            e.X < 25 ? (project.IsFavoriteTrack(track) ? "Remove favorite" : "Add favorite") :
                            project.GetTrackName(track) + " — double-click to select in SpaceClaim";
                } else if (e.Y < Header) {
                    int marker = FindMarkerAt(e.X - AutoScrollPosition.X);
                    if (marker >= 0) tip = AnimationProject.Current.TimelineMarkers[marker].Name;
                }
                if (tip != lastTip) { lastTip = tip; trackTip.SetToolTip(this, tip); }
                if (boxPending) {
                    boxEnd = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
                    if (Math.Abs(boxEnd.X - boxStart.X) >= 4 || Math.Abs(boxEnd.Y - boxStart.Y) >= 4) boxing = true;
                    Invalidate(); return;
                }
                if (!dragPending || (e.Button & MouseButtons.Left) == 0) return;
                if (!dragging && Math.Abs(e.X - dragStartX) < 10) return;
                dragging = true;
                double seconds = Math.Max(0, (e.X - AutoScrollPosition.X - LabelWidth) / (double)PixelsPerSecond);
                int fps = AnimationProject.Current.FramesPerSecond;
                dragPreview = Math.Min(3600, Math.Round(seconds * fps) / fps);
                Invalidate();
            }
            protected override void OnMouseUp(MouseEventArgs e) {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Right) {
                    Point? point = pendingContextPoint;
                    pendingContextPoint = null;
                    if (point.HasValue) ShowContextAt(point.Value);
                    return;
                }
                if (rulerScrubbing && e.Button == MouseButtons.Left) {
                    SeekRulerAt(e.X);
                    rulerScrubbing = false;
                    Capture = false;
                    return;
                }
                if (boxPending) {
                    boxPending = false; Capture = false;
                    if (boxing) {
                        boxing = false;
                        owner.SelectRectangle(Rectangle.FromLTRB(Math.Min(boxStart.X, boxEnd.X), Math.Min(boxStart.Y, boxEnd.Y),
                            Math.Max(boxStart.X, boxEnd.X), Math.Max(boxStart.Y, boxEnd.Y)), boxAppend);
                    } else owner.Select(AnimationProject.Current.SelectedTrackIndex,
                        Math.Max(0, (boxStart.X - LabelWidth) / (double)PixelsPerSecond), false);
                    Invalidate(); return;
                }
                if (!dragPending) return;
                dragPending = false;
                Capture = false;
                bool moved = dragging && Math.Abs(e.X - dragStartX) >= 10;
                dragging = false;
                if (moved && Math.Abs(dragPreview - dragFrom) > 1e-6)
                    owner.MoveSelected(dragTrack, dragFrom, dragPreview);
                Invalidate();
            }
            protected override void OnMouseCaptureChanged(EventArgs e) {
                base.OnMouseCaptureChanged(e);
                if (!Capture) rulerScrubbing = false;
            }
            private void ShowContextAt(Point point) {
                AnimationProject project = AnimationProject.Current;
                if (!project.CanEdit || !project.HasComponent || point.X < LabelWidth) return;
                int worldX = point.X - AutoScrollPosition.X;
                int worldY = point.Y - AutoScrollPosition.Y;
                if (point.Y < Header) {
                    int marker = FindMarkerAt(worldX);
                    if (marker >= 0) { owner.ShowMarkerMenu(this, point, marker); return; }
                }
                int track = point.Y < Header ? project.SelectedTrackIndex :
                    owner.VisibleTrackAtRow((worldY - Header) / Row);
                if (track < 0 || track >= project.TrackCount) return;
                double clickedTime = Math.Max(0, (worldX - LabelWidth) / (double)PixelsPerSecond);
                double nearestTime = -1, nearestDistance = 8.0 / PixelsPerSecond;
                if (point.Y >= Header) foreach (double candidate in project.GetTrackTimes(track)) {
                    double keyDistance = Math.Abs(clickedTime - candidate);
                    if (keyDistance <= nearestDistance) { nearestTime = candidate; nearestDistance = keyDistance; }
                }
                if (nearestTime >= 0) owner.ShowKeyMenu(this, point, track, nearestTime);
                else owner.ShowTimeMenu(this, point, track, clickedTime);
            }
            private int FindMarkerAt(int worldX) {
                TimelineMarker[] markers = AnimationProject.Current.TimelineMarkers;
                int nearest = -1;
                double distance = 8;
                for (int i = 0; i < markers.Length; i++) {
                    double d = Math.Abs(worldX - (LabelWidth + markers[i].Time * PixelsPerSecond));
                    if (d <= distance) { nearest = i; distance = d; }
                }
                return nearest;
            }
            protected override void OnMouseDoubleClick(MouseEventArgs e) {
                base.OnMouseDoubleClick(e);
                if (e.Button != MouseButtons.Left || e.X >= LabelWidth) return;
                int y = e.Y - AutoScrollPosition.Y;
                if (e.Y < Header) return;
                int track = owner.VisibleTrackAtRow((y - Header) / Row);
                if (track >= 0) owner.FocusTrackInModel(track);
            }
            protected override void OnKeyDown(KeyEventArgs e) {
                if (e.KeyCode == Keys.Delete) { owner.DeleteSelected(); e.Handled = true; }
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) {
                    owner.StepSelected(e.KeyCode == Keys.Left ? -1 : 1);
                    e.Handled = true;
                }
                base.OnKeyDown(e);
            }
            protected override bool IsInputKey(Keys keyData) {
                Keys key = keyData & Keys.KeyCode;
                return key == Keys.Left || key == Keys.Right || key == Keys.Delete || base.IsInputKey(keyData);
            }
            protected override void Dispose(bool disposing) {
                if (disposing) { smallFont.Dispose(); trackTip.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}

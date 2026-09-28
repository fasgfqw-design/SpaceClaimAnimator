using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpaceClaim.Api.V261;
using SCAnimator.V261.Engine;
using GeoPoint = SpaceClaim.Api.V261.Geometry.Point;

namespace SCAnimator.V261.UI {
    internal sealed class MechanismsDialog : Form {
        private readonly AnimationProject project;
        private readonly int animationIndex;
        private readonly Window window;
        private readonly HingeSuggestionOverlay overlay;
        private readonly MechanismIsolation isolation;
        private readonly ICollection<IDocObject> previousSecondary;
        private readonly List<AlignSuggestionMarker> markers = new List<AlignSuggestionMarker>();
        private readonly Timer markerLayoutTimer;
        private readonly MarkerClickFilter markerClickFilter;
        private readonly ListBox candidates, joints;
        private readonly TextBox name;
        private readonly TextBox suggestionFilter;
        private readonly NumericUpDown axisOffsetMm;
        private readonly NumericUpDown diameterMinMm, diameterMaxMm;
        private readonly CheckBox isolatePair;
        private readonly Label status;
        private readonly List<HingeCandidate> found = new List<HingeCandidate>();
        private readonly List<HingeCandidate> visible = new List<HingeCandidate>();
        private HingeCandidate hoveredCandidate;
        private HingeCandidate isolatedCandidate;
        private bool coloredPreviewMeshBuilt;
        private bool applying;
        private bool hasMarkerLayout;
        private SpaceClaim.Api.V261.Geometry.Direction lastRight, lastUp;
        private double lastZoom;
        private SpaceClaim.Api.V261.Geometry.Point lastCameraPosition;
        private Size lastViewSize;
        private AlignSuggestionMarker pressedMarker;
        private System.Drawing.Point pressedAt;

        private sealed class MarkerClickFilter : IMessageFilter {
            private readonly MechanismsDialog owner;
            internal MarkerClickFilter(MechanismsDialog owner) { this.owner = owner; }
            public bool PreFilterMessage(ref Message message) {
                if (message.Msg == 0x201) owner.MarkerMouseDown(message.HWnd);
                else if (message.Msg == 0x202) owner.MarkerMouseUp(message.HWnd);
                return false;
            }
        }

        internal MechanismsDialog(AnimationProject project) {
            this.project = project;
            animationIndex = project.ActiveAnimationIndex;
            window = Window.ActiveWindow;
            overlay = window == null ? null : HingeSuggestionOverlay.Create(window);
            isolation = new MechanismIsolation(window);
            previousSecondary = window == null || window.ActiveContext.SecondarySelection == null ?
                new List<IDocObject>() : window.ActiveContext.SecondarySelection.ToList();
            Text = "SC Animator — Mechanisms";
            ClientSize = new Size(760, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; TopMost = true;
            Font = new Font("Segoe UI", 9);
            bool light = ThemeSettings.Current == ThemeChoice.Light;
            BackColor = light ? Color.FromArgb(249, 250, 252) : Color.FromArgb(43, 46, 51);
            ForeColor = light ? Color.FromArgb(32, 40, 48) : Color.Gainsboro;
            AddLabel("Green = proposed Align. Hover: blue = fixed face, orange = moving face. Click a marker to create Align and its animation track.", 14, 10, 730, 35);
            AddLabel("Possible Align pairs", 14, 51, 185, 24);
            AddLabel("Find", 205, 51, 34, 24);
            suggestionFilter = new TextBox { Left = 242, Top = 49, Width = 124 };
            suggestionFilter.TextChanged += delegate { RefreshSuggestions(); };
            Controls.Add(suggestionFilter);
            AddLabel("Created Align joints", 391, 51, 180, 24);
            AddLabel("Max offset mm", 575, 51, 96, 24);
            axisOffsetMm = new NumericUpDown { Left = 672, Top = 49, Width = 72,
                Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = .1M, Value = .2M };
            axisOffsetMm.ValueChanged += delegate {
                if (IsHandleCreated && IsForCurrentProject) FindAxes();
            };
            Controls.Add(axisOffsetMm);
            AddLabel("Ø mm", 14, 80, 42, 22);
            AddLabel("from", 58, 80, 33, 22);
            diameterMinMm = new NumericUpDown { Left = 94, Top = 77, Width = 76,
                Minimum = 0, Maximum = 10000, DecimalPlaces = 2, Increment = 1, Value = 0 };
            Controls.Add(diameterMinMm);
            AddLabel("to", 176, 80, 18, 22);
            diameterMaxMm = new NumericUpDown { Left = 198, Top = 77, Width = 84,
                Minimum = 0, Maximum = 10000, DecimalPlaces = 2, Increment = 1, Value = 10000 };
            Controls.Add(diameterMaxMm);
            diameterMinMm.ValueChanged += delegate { RefreshSuggestions(); };
            diameterMaxMm.ValueChanged += delegate { RefreshSuggestions(); };
            candidates = new ListBox { Left = 14, Top = 105, Width = 352, Height = 185,
                IntegralHeight = false, HorizontalScrollbar = true };
            joints = new ListBox { Left = 391, Top = 78, Width = 353, Height = 212,
                IntegralHeight = false, HorizontalScrollbar = true };
            Controls.Add(candidates); Controls.Add(joints);
            candidates.SelectedIndexChanged += delegate {
                if (candidates.SelectedIndex >= 0) {
                    name.Text = NextName("Hinge");
                }
                hoveredCandidate = null;
                ShowCurrentPreview();
            };
            candidates.DoubleClick += delegate { AddHinge(); };
            AddButton("Find axes", 14, 297, 105, FindAxes);
            AddButton("Swap fixed/moving", 126, 297, 147, Swap);
            AddButton("✓ Add all", 279, 297, 87, AutoAddAll);
            AddLabel("Name", 14, 338, 52, 24);
            name = new TextBox { Left = 67, Top = 335, Width = 202, Text = "Hinge 1" };
            Controls.Add(name);
            AddButton("Create Align", 275, 334, 91, AddHinge);
            AddButton("Remove joint", 391, 297, 111, RemoveHinge);
            isolatePair = new CheckBox { Text = "Isolate selected pair", Left = 391, Top = 337,
                Width = 215, Height = 25, Checked = true, ForeColor = ForeColor };
            isolatePair.CheckedChanged += delegate {
                isolatedCandidate = null;
                ShowCurrentPreview();
            };
            Controls.Add(isolatePair);
            status = AddLabel("", 14, 373, 730, 40);
            AddButton("Close", 650, 335, 94, delegate { Close(); });
            RefreshJoints();
            Shown += delegate { FindAxes(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (isolation.IsActive && window != null && window.Document != null) {
                    string error;
                    if (!isolation.Restore(out error)) { e.Cancel = true; SetStatus(error); }
                }
            };
            if (window != null) {
                window.PreselectionChanged += OnPreselectionChanged;
                Document.DocumentDeepSaveProposed += OnDeepSaveProposed;
                markerClickFilter = new MarkerClickFilter(this);
                System.Windows.Forms.Application.AddMessageFilter(markerClickFilter);
                markerLayoutTimer = new Timer { Interval = 250 };
                markerLayoutTimer.Tick += delegate {
                    if (!applying && IsForCurrentProject) {
                        if (Control.MouseButtons == MouseButtons.None && markers.Count > 1)
                            LayoutMarkers();
                        UpdateHoverFromCursor();
                    }
                };
                markerLayoutTimer.Start();
            }
            FormClosed += delegate {
                if (window != null) {
                    window.PreselectionChanged -= OnPreselectionChanged;
                    Document.DocumentDeepSaveProposed -= OnDeepSaveProposed;
                    System.Windows.Forms.Application.RemoveMessageFilter(markerClickFilter);
                    markerLayoutTimer.Stop(); markerLayoutTimer.Dispose();
                    try {
                        window.ActiveContext.SecondarySelection = previousSecondary;
                        foreach (AlignSuggestionMarker marker in markers)
                            if (!marker.IsDeleted) marker.Delete();
                        if (overlay != null && !overlay.IsDeleted) overlay.Delete();
                    } catch (InvalidOperationException) { /* The original window was closed. */ }
                }
            };
        }
        private Label AddLabel(string value, int x, int y, int width, int height) {
            var label = new Label { Text = value, Left = x, Top = y, Width = width, Height = height,
                ForeColor = ForeColor };
            Controls.Add(label); return label;
        }
        private void AddButton(string value, int x, int y, int width, Action action) {
            var button = new Button { Text = value, Left = x, Top = y, Width = width, Height = 27 };
            button.Click += delegate { action(); }; Controls.Add(button);
        }
        private void SetStatus(string value) { status.Text = value; }
        private void OnDeepSaveProposed(object sender, SaveDocumentEventArgs e, CancelStatus cancellation) {
            if (e.Document != isolation.Document || !isolation.IsActive) return;
            string error;
            if (!isolation.Restore(out error)) {
                cancellation.Cancel();
                SetStatus(error + " Save was canceled so hidden preview bodies are not stored.");
            }
        }
        internal bool IsForCurrentProject { get {
            return window != null && Window.ActiveWindow == window &&
                AnimationProject.Current == project && project.ActiveAnimationIndex == animationIndex;
        } }
        private string NextName(string prefix) {
            for (int number = 1; number <= 100; number++) {
                string proposed = prefix + " " + number;
                bool used = false;
                for (int i = 0; i < project.HingeCount; i++)
                    if (String.Equals(project.GetHinge(i).Name, proposed,
                        StringComparison.OrdinalIgnoreCase)) { used = true; break; }
                if (!used) return proposed;
            }
            return null;
        }
        private void Preview(HingeCandidate candidate) {
            if (window == null || window.Document == null || overlay == null) return;
            HingeCandidate shown = hoveredCandidate ?? candidate;
            bool coloredFaces = overlay.Show(visible, shown);
            coloredPreviewMeshBuilt = coloredFaces;
            window.ActiveContext.SecondarySelection = shown == null || coloredFaces ? previousSecondary :
                new IDocObject[] { shown.FixedFace, shown.MovingFace };
            foreach (AlignSuggestionMarker marker in markers)
                marker.SetFocused(marker.Candidate == shown);
            window.RefreshRendering();
        }
        private void PreviewAll() {
            if (window == null || window.Document == null || overlay == null) return;
            hoveredCandidate = null;
            isolatedCandidate = null;
            coloredPreviewMeshBuilt = false;
            window.ActiveContext.SecondarySelection = previousSecondary;
            overlay.Show(visible, null);
            foreach (AlignSuggestionMarker marker in markers) marker.SetFocused(false);
            window.RefreshRendering();
        }
        private void RefreshMarkers() {
            bool wasApplying = applying;
            applying = true;
            try {
                foreach (AlignSuggestionMarker marker in markers)
                    if (!marker.IsDeleted) marker.Delete();
                markers.Clear();
                foreach (HingeCandidate candidate in visible)
                    markers.Add(AlignSuggestionMarker.Create(window, candidate));
                hasMarkerLayout = false;
                LayoutMarkers();
            } finally { applying = wasApplying; }
        }
        private void LayoutMarkers() {
            if (window == null || window.Document == null || markers.Count == 0) return;
            var frame = window.GetCameraFrame();
            double zoom = window.Camera.Zoom;
            var cameraPosition = window.Camera.Position;
            Size viewSize = window.Size;
            if (hasMarkerLayout && frame.DirX == lastRight && frame.DirY == lastUp &&
                cameraPosition == lastCameraPosition && viewSize == lastViewSize &&
                Math.Abs(zoom - lastZoom) < 1e-9) return;
            lastRight = frame.DirX; lastUp = frame.DirY; lastZoom = zoom;
            lastCameraPosition = cameraPosition; lastViewSize = viewSize;
            hasMarkerLayout = true;
            var groups = new List<List<AlignSuggestionMarker>>();
            foreach (AlignSuggestionMarker marker in markers) {
                List<AlignSuggestionMarker> group = groups.Find(items => {
                    System.Drawing.Point a = window.ActiveContext.ProjectToScreen(items[0].BaseCenter),
                        b = window.ActiveContext.ProjectToScreen(marker.BaseCenter);
                    int dx = a.X - b.X, dy = a.Y - b.Y;
                    return dx * dx + dy * dy <= 36 * 36;
                });
                if (group == null) { group = new List<AlignSuggestionMarker>(); groups.Add(group); }
                group.Add(marker);
            }
            foreach (List<AlignSuggestionMarker> group in groups) {
                if (group.Count == 1) { group[0].SetAnchor(group[0].BaseCenter); continue; }
                GeoPoint center = group[0].BaseCenter;
                System.Drawing.Point screen = window.ActiveContext.ProjectToScreen(center);
                // Keep a compact regular polygon; adjacent hit disks must remain distinct.
                double spacing = Math.Max(20, group.Count * 35 / (2 * Math.PI));
                for (int i = 0; i < group.Count; i++) {
                    double angle = 2 * Math.PI * i / group.Count - Math.PI / 2;
                    var pixel = new System.Drawing.Point(
                        screen.X + (int)Math.Round(spacing * Math.Cos(angle)),
                        screen.Y + (int)Math.Round(spacing * Math.Sin(angle)));
                    group[i].SetAnchor(AlignSuggestionMarker.PointAtScreenPixel(window, pixel, center));
                }
            }
            window.RefreshRendering();
        }
        private string SuggestionCount() {
            return visible.Count + " shown of " + found.Count + " possible Align pairs";
        }
        private void RemoveLinkedSuggestions() {
            found.RemoveAll(candidate => project.HasHingeForCandidate(candidate));
            RefreshSuggestions();
        }
        private void RefreshSuggestions() {
            if (candidates == null || suggestionFilter == null ||
                diameterMinMm == null || diameterMaxMm == null) return;
            bool wasApplying = applying;
            applying = true;
            HingeCandidate selected = candidates.SelectedItem as HingeCandidate;
            string query = suggestionFilter.Text.Trim();
            double minimumMm = (double)diameterMinMm.Value;
            double maximumMm = (double)diameterMaxMm.Value;
            try {
                visible.Clear();
                visible.AddRange(found.Where(candidate =>
                    candidate.IsWithinDiameterRange(minimumMm, maximumMm) &&
                    (query.Length == 0 || candidate.Label.IndexOf(query,
                        StringComparison.CurrentCultureIgnoreCase) >= 0)));
                candidates.BeginUpdate();
                try {
                    candidates.Items.Clear();
                    foreach (HingeCandidate candidate in visible) candidates.Items.Add(candidate);
                    if (selected != null && visible.Contains(selected)) candidates.SelectedItem = selected;
                } finally { candidates.EndUpdate(); }
            } finally { applying = wasApplying; }
            RefreshMarkers();
            PreviewAll();
            if (!wasApplying) ShowCurrentPreview();
        }
        private bool IsDialogMessage(IntPtr handle) {
            for (Control control = Control.FromHandle(handle); control != null; control = control.Parent)
                if (control == this) return true;
            return false;
        }
        private void MarkerMouseDown(IntPtr handle) {
            pressedMarker = null;
            if (applying || !IsForCurrentProject || IsDialogMessage(handle)) return;
            pressedMarker = MarkerAtCursor();
            pressedAt = window.CursorPosition;
        }
        private void MarkerMouseUp(IntPtr handle) {
            AlignSuggestionMarker marker = pressedMarker;
            pressedMarker = null;
            if (marker == null || applying || !IsForCurrentProject || IsDialogMessage(handle)) return;
            System.Drawing.Point at = window.CursorPosition;
            int dx = at.X - pressedAt.X, dy = at.Y - pressedAt.Y;
            if (dx * dx + dy * dy > 36 || MarkerAtCursor() != marker) return;
            BeginInvoke(new Action(delegate {
                if (!IsDisposed && !applying && IsForCurrentProject &&
                    markers.Contains(marker)) SelectAndAdd(marker.Candidate);
            }));
        }
        private void OnPreselectionChanged(object sender, EventArgs e) {
            if (applying || !IsForCurrentProject || window.Document == null) return;
            UpdateHoverFromCursor();
        }
        private AlignSuggestionMarker MarkerAtCursor() {
            if (Bounds.Contains(Control.MousePosition)) return null;
            System.Drawing.Point cursor = window.CursorPosition;
            Size size = window.Size;
            if (cursor.X < 0 || cursor.Y < 0 || cursor.X >= size.Width || cursor.Y >= size.Height)
                return null;
            AlignSuggestionMarker nearest = null;
            int closest = AlignSuggestionMarker.HitRadiusPixels * AlignSuggestionMarker.HitRadiusPixels;
            foreach (AlignSuggestionMarker marker in markers) {
                System.Drawing.Point point = window.ActiveContext.ProjectToScreen(marker.Anchor);
                int dx = point.X - cursor.X, dy = point.Y - cursor.Y;
                int distance = dx * dx + dy * dy;
                if (distance <= closest) { closest = distance; nearest = marker; }
            }
            return nearest;
        }
        private void UpdateHoverFromCursor() {
            if (applying || Control.MouseButtons != MouseButtons.None ||
                !IsForCurrentProject || window.Document == null) return;
            AlignSuggestionMarker marker = MarkerAtCursor();
            HingeCandidate candidate = marker == null ? null : marker.Candidate;
            if (candidate == hoveredCandidate) return;
            hoveredCandidate = candidate;
            ShowCurrentPreview();
        }
        private void ShowCurrentPreview() {
            if (applying || window == null || window.Document == null || overlay == null) return;
            HingeCandidate shown = hoveredCandidate ?? candidates.SelectedItem as HingeCandidate;
            string error;
            // Visibility changes fire host selection events. Guard the whole transition so
            // hovering a marker cannot recursively enter another isolation write block.
            applying = true;
            try {
                if (isolatePair != null && isolatePair.Checked && shown != null) {
                    if (isolatedCandidate != shown) {
                        if (!isolation.ShowPair(shown, out error)) { SetStatus(error); return; }
                        isolatedCandidate = shown;
                    }
                } else if (isolation.IsActive) {
                    if (!isolation.Restore(out error)) { SetStatus(error); return; }
                    isolatedCandidate = null;
                }
                Preview(shown);
                if (shown != null)
                    SetStatus("Blue fixed: " + AnimationProject.MechanismName(shown.Fixed) +
                        "   |   Orange moving: " + AnimationProject.MechanismName(shown.Moving) +
                        (coloredPreviewMeshBuilt ? "" : "   |   Native face highlight fallback.") +
                        (hoveredCandidate == null ? "" : "   |   Click marker to create Align."));
                else if (diameterMinMm.Value > diameterMaxMm.Value)
                    SetStatus("Diameter range is empty: Ø from must not exceed Ø to.");
                else if (found.Count > 0) SetStatus(SuggestionCount() +
                    ". Hover a marker or select a row to inspect the pair.");
            } finally { applying = false; }
        }
        private void SelectAndAdd(HingeCandidate candidate) {
            candidates.SelectedItem = candidate;
            AddHinge();
        }
        private static string CandidateLabel(HingeCandidate candidate) {
            return AnimationProject.MechanismName(candidate.Fixed) + " → " +
                AnimationProject.MechanismName(candidate.Moving) + "   Ø" +
                (candidate.Radius * 2000).ToString("0.##") + " mm   offset " +
                (candidate.AxisDistance * 1000).ToString("0.###") + " mm";
        }
        private void FindAxes() {
            if (window == null || window.Document == null || !IsForCurrentProject) return;
            if (isolation.IsActive) {
                string restoreError;
                if (!isolation.Restore(out restoreError)) { SetStatus(restoreError); return; }
            }
            var selected = new List<IComponent>();
            foreach (IComponent component in window.ActiveContext.GetSelection<IComponent>())
                if (!component.Master.IsDeleted && !selected.Any(item => item.Master == component.Master))
                    selected.Add(component);
            var groups = new List<IList<IComponent>>();
            if (selected.Count == 2) groups.Add(selected);
            else if (selected.Count == 1) groups.Add(selected[0].Parent.Components.ToList());
            else {
                // The document's main part may contain just one assembly. Search
                // every sibling group below it, not only its direct components.
                groups.AddRange(window.Document.MainPart.GetDescendants<IComponent>()
                    .GroupBy(component => component.Parent.Moniker.ToString(), StringComparer.Ordinal)
                    .Select(group => (IList<IComponent>)group.ToList())
                    .Where(group => group.Count >= 2));
            }
            if (groups.Count == 0 || groups.All(group => group.Count < 2)) {
                SetStatus("No component pairs found. Select two components in the Structure tree."); return;
            }
            if (groups.Any(group => group.Count > 40)) {
                SetStatus("An assembly contains more than 40 siblings. Select two components in that assembly first.");
                return;
            }
            try {
                found.Clear();
                foreach (IList<IComponent> group in groups)
                    if (group.Count >= 2)
                        found.AddRange(HingeDetector.Detect(group, (double)axisOffsetMm.Value / 1000));
                foreach (HingeCandidate candidate in found) {
                    candidate.Label = CandidateLabel(candidate);
                }
                RemoveLinkedSuggestions();
                SetStatus(found.Count == 0 ?
                    "No near-parallel axes within the selected offset. Select another pair or increase Max offset." :
                    diameterMinMm.Value > diameterMaxMm.Value ?
                    "Diameter range is empty: Ø from must not exceed Ø to." :
                    SuggestionCount() + ". Add all chooses one Align per moving component.");
            } catch (Exception ex) { SetStatus("Axis search failed: " + ex.Message); }
        }
        private void Swap() {
            HingeCandidate candidate = candidates.SelectedItem as HingeCandidate;
            if (candidate == null) return;
            IComponent previous = candidate.Fixed; candidate.Fixed = candidate.Moving; candidate.Moving = previous;
            IDesignFace previousFace = candidate.FixedFace;
            candidate.FixedFace = candidate.MovingFace; candidate.MovingFace = previousFace;
            double previousRadius = candidate.Radius;
            candidate.Radius = candidate.OtherRadius; candidate.OtherRadius = previousRadius;
            var fixedShape = candidate.FixedFace as IHasShape;
            var fixedSurface = fixedShape == null ? null :
                fixedShape.Shape as SpaceClaim.Api.V261.Geometry.ISurfaceShape;
            var fixedCylinder = fixedSurface == null ? null :
                fixedSurface.Geometry as SpaceClaim.Api.V261.Geometry.Cylinder;
            if (fixedCylinder != null) {
                var axis = fixedCylinder.Axis;
                var toMarker = candidate.MarkerCenter - axis.Origin;
                double axialPosition = toMarker.X * axis.Direction.X +
                    toMarker.Y * axis.Direction.Y + toMarker.Z * axis.Direction.Z;
                candidate.Origin = axis.Origin;
                candidate.Direction = axis.Direction;
                candidate.MarkerCenter = axis.Origin + axis.Direction * axialPosition;
            }
            isolatedCandidate = null;
            candidate.Label = CandidateLabel(candidate);
            int index = candidates.SelectedIndex;
            candidates.Items[index] = candidate; candidates.SelectedIndex = index;
            RefreshSuggestions();
        }
        private void AddHinge() {
            HingeCandidate candidate = candidates.SelectedItem as HingeCandidate;
            if (candidate == null) { SetStatus("Select a detected candidate first."); return; }
            if (!IsForCurrentProject) {
                SetStatus("Reopen Mechanisms after changing the model or animation."); return;
            }
            try {
                applying = true;
                string result;
                if (!isolation.Restore(out result)) { SetStatus(result); return; }
                if (!project.AddAlignedHinge(name.Text.Trim(), candidate, out result)) {
                    SetStatus("Could not create Align: " + result); return;
                }
                found.Remove(candidate);
                RefreshJoints(); joints.SelectedIndex = joints.Items.Count - 1;
                RemoveLinkedSuggestions();
                SetStatus(result + " Use Move, then add a key at the desired time in Tracks.");
            } catch (Exception ex) { SetStatus("Could not add hinge: " + ex.Message); }
            finally { applying = false; }
        }
        private void AutoAddAll() {
            if (!IsForCurrentProject) {
                SetStatus("Reopen Mechanisms after changing the model or animation."); return;
            }
            // Pairwise coaxial detections are alternatives, not six independent
            // joints for four components. Pick one best-fitting axis per moving
            // component, matching the same one-hinge rule as manual creation.
            var options = visible.Select((candidate, index) => new AutoHingeOption {
                Index = index, FixedId = candidate.Fixed.Moniker.ToString(),
                MovingId = candidate.Moving.Moniker.ToString(),
                RadiusMismatch = candidate.RadiusMismatch,
                AxisDistance = candidate.AxisDistance,
                AxialGap = candidate.AxialGap,
                AxialCenterDistance = candidate.AxialCenterDistance
            });
            var chosen = AutoHingeSelection.Choose(options).Select(index => visible[index])
                .Where(candidate => !project.HasHingeForMoving(candidate.Moving)).ToList();
            int added = 0;
            var accepted = new List<HingeCandidate>();
            var failures = new List<string>();
            try {
                applying = true;
                string restoreError;
                if (!isolation.Restore(out restoreError)) { SetStatus(restoreError); return; }
                foreach (HingeCandidate candidate in chosen) {
                    string proposed = NextName("Auto hinge");
                    if (proposed == null) break;
                    string result;
                    if (project.AddAlignedHinge(proposed, candidate, out result)) {
                        added++; accepted.Add(candidate);
                    }
                    else failures.Add(AnimationProject.MechanismName(candidate.Moving) + ": " + result);
                }
                found.RemoveAll(candidate => accepted.Contains(candidate));
                RefreshJoints();
                if (added > 0) joints.SelectedIndex = joints.Items.Count - 1;
                RemoveLinkedSuggestions();
                SetStatus(chosen.Count == 0 ?
                    (visible.Count == 0 ? "No proposals match the current filters." :
                    "No unlinked close-fitting component is available for Add all. Select a row to add another joint manually.") :
                    added + " of " + chosen.Count + " independent Align/animation hinge(s) added" +
                    (failures.Count == 0 ? ". Other face pairings are alternatives." :
                        ". Failed: " + String.Join("; ", failures.ToArray())));
            } catch (Exception ex) { SetStatus(added + " hinge(s) added; then stopped: " + ex.Message); }
            finally { applying = false; }
        }
        private void RefreshJoints() {
            joints.Items.Clear();
            for (int i = 0; i < project.HingeCount; i++) {
                HingeSnapshot hinge = project.GetHinge(i);
                joints.Items.Add(hinge.Name);
            }
        }
        private void RemoveHinge() {
            if (!IsForCurrentProject) {
                SetStatus("Reopen Mechanisms after changing the model or animation."); return;
            }
            if (joints.SelectedIndex < 0) return;
            int index = joints.SelectedIndex;
            string result;
            if (!project.RemoveHinge(index, out result)) { SetStatus(result ?? "Could not remove hinge."); return; }
            RefreshJoints(); SetStatus(result);
        }
    }
}

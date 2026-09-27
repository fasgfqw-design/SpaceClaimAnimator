using System;
using System.Collections.Generic;
using System.Drawing;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Scripting.Selection;
using SpaceClaim.UserInterface;

namespace SCAnimator.V261.Engine {
    internal enum VisibilityMode { Default, Show, Hide, FadeIn, FadeOut }

    internal sealed partial class AnimationProject {
        private sealed class FadeColorState {
            internal Color? OriginalColor;
            internal Color? PublicOriginalColor;
            internal Color EffectiveColor;
            internal Color? LastAppliedColor;
            internal SpaceClaim.IHasColor NativeColor;
            internal int TargetAlpha;
        }
        private readonly Dictionary<IDesignBody, bool?> originalBodyVisibility = new Dictionary<IDesignBody, bool?>();
        private readonly Dictionary<DesignBody, BodyStyle> originalBodyStyles = new Dictionary<DesignBody, BodyStyle>();
        private readonly Dictionary<DesignBody, FadeColorState> originalBodyColors =
            new Dictionary<DesignBody, FadeColorState>();
        private readonly Dictionary<int, List<IDesignBody>> visibleBodyOccurrences = new Dictionary<int, List<IDesignBody>>();
        private readonly Dictionary<DatumPlane, bool?> originalPlaneVisibility = new Dictionary<DatumPlane, bool?>();
        private readonly Dictionary<TrackKeyRef, VisibilityMode> visibilityKeys = new Dictionary<TrackKeyRef, VisibilityMode>();
        private bool reversedVisibility;
        internal bool ReversedVisibility { get { return reversedVisibility; } }

        internal VisibilityMode GetKeyVisibility(int track, double time) {
            VisibilityMode mode;
            return visibilityKeys.TryGetValue(new TrackKeyRef(track, time), out mode) ? mode : VisibilityMode.Default;
        }
        internal bool SetKeyVisibility(int track, double time, VisibilityMode mode) {
            if (!CanEdit || !CanUseCurrentAssembly ||
                track < components.Count && mode != VisibilityMode.Default && !CanAnimateComponentVisibility(track)) return false;
            return SetKeyVisibilityCore(track, time, mode);
        }
        internal bool CanAnimateComponentVisibility(int track) {
            if (track < 0 || track >= components.Count || document == null) return false;
            Component component = components[track];
            Part part = component.Template;
            if (part == null || part.Bodies.Count == 0) return false;
            int occurrences = 0;
            foreach (IComponent occurrence in document.MainPart.GetDescendants<IComponent>())
                if (occurrence.Master.Template == part && ++occurrences > 1) return false;
            return occurrences == 1;
        }
        internal bool SetKeyVisibilityCore(int track, double time, VisibilityMode mode) {
            if (track < 0 || track >= CameraTrackIndex || !HasTrackKey(track, time) || IsTrackLocked(track) ||
                !Enum.IsDefined(typeof(VisibilityMode), mode) || IsPlaneTrack(track) &&
                (mode == VisibilityMode.FadeIn || mode == VisibilityMode.FadeOut)) return false;
            SaveUndoState();
            var key = new TrackKeyRef(track, time);
            if (mode == VisibilityMode.Default) visibilityKeys.Remove(key);
            else visibilityKeys[key] = mode;
            MarkDirty(); return true;
        }
        internal VisibilityMode VisibilityAt(int track, double time, out double changeTime) {
            changeTime = 0;
            VisibilityMode result = VisibilityMode.Default;
            double[] times = GetTrackTimes(track);
            if (reversedVisibility) {
                for (int i = times.Length - 1; i >= 0; i--) {
                    double keyTime = times[i];
                    if (keyTime < time) break;
                    VisibilityMode mode;
                    if (visibilityKeys.TryGetValue(new TrackKeyRef(track, keyTime), out mode)) {
                        result = mode; changeTime = keyTime;
                    }
                }
            } else {
                foreach (double keyTime in times) {
                    if (keyTime > time) break;
                    VisibilityMode mode;
                    if (visibilityKeys.TryGetValue(new TrackKeyRef(track, keyTime), out mode)) {
                        result = mode; changeTime = keyTime;
                    }
                }
            }
            return result;
        }
        internal static double FadeOpacity(VisibilityMode mode, double time, double changeTime, double fadeSeconds) {
            if (mode == VisibilityMode.Hide) return 0;
            if (mode == VisibilityMode.FadeIn || mode == VisibilityMode.FadeOut) {
                double progress = Math.Max(0, Math.Min(1, (time - changeTime) / Math.Max(.001, fadeSeconds)));
                return mode == VisibilityMode.FadeIn ? progress : 1 - progress;
            }
            return 1;
        }
        internal static double ReversedFadeOpacity(VisibilityMode mode, double time, double changeTime, double fadeSeconds) {
            if (mode == VisibilityMode.Hide) return 0;
            if (mode == VisibilityMode.FadeIn || mode == VisibilityMode.FadeOut) {
                double progress = Math.Max(0, Math.Min(1, (changeTime - time) / Math.Max(.001, fadeSeconds)));
                return mode == VisibilityMode.FadeIn ? progress : 1 - progress;
            }
            return 1;
        }
        internal static int FadeAlpha(int targetAlpha, double progress) {
            double t = Math.Max(0, Math.Min(1, progress));
            t = t * t * (3 - 2 * t);
            return (int)Math.Round(Math.Max(0, Math.Min(255, targetAlpha)) * t);
        }
        internal static Color FadeColor(Color background, Color target, double progress) {
            double t = Math.Max(0, Math.Min(1, progress));
            t = t * t * (3 - 2 * t);
            return Color.FromArgb(
                (int)Math.Round(background.R + (target.R - background.R) * t),
                (int)Math.Round(background.G + (target.G - background.G) * t),
                (int)Math.Round(background.B + (target.B - background.B) * t));
        }
        private bool ApplyFadeColor(DesignBody body, double progress, Color background) {
            FadeColorState state;
            if (!originalBodyColors.TryGetValue(body, out state)) {
                Color? original = body.GetColor(null);
                Color? effective = original;
                if (!effective.HasValue && body.Layer != null)
                    effective = body.Layer.GetColor(null);
                state = new FadeColorState { OriginalColor = original,
                    PublicOriginalColor = original,
                    EffectiveColor = effective ?? Color.LightGray,
                    TargetAlpha = body.Style == BodyStyle.Transparent ? 128 : 255 };
                try {
                    foreach (SpaceClaim.IEvaluation evaluation in
                        SelectionMethods.GetEvaluations(Selection.Create(body))) {
                        var native = evaluation as SpaceClaim.IHasColor;
                        if (native == null) continue;
                        state.NativeColor = native;
                        state.OriginalColor = AppearanceHelper.GetColorOverride(native, null, null);
                        state.EffectiveColor = AppearanceHelper.GetColor(native);
                        state.TargetAlpha = body.Style == BodyStyle.Opaque ? 255 :
                            body.Style == BodyStyle.Transparent ? Math.Min(128, (int)state.EffectiveColor.A) :
                            state.EffectiveColor.A;
                        break;
                    }
                } catch { state.NativeColor = null; }
                originalBodyColors.Add(body, state);
            }
            if (state.NativeColor != null) {
                Color color = Color.FromArgb(FadeAlpha(state.TargetAlpha, progress),
                    state.EffectiveColor.R, state.EffectiveColor.G, state.EffectiveColor.B);
                try {
                    AppearanceHelper.SetColor(state.NativeColor, color, null);
                    state.LastAppliedColor = color;
                    return true;
                } catch { state.NativeColor = null; }
            }
            Color blended = FadeColor(background, state.EffectiveColor, progress);
            body.SetColor(null, blended);
            state.LastAppliedColor = blended;
            return false;
        }
        private void RestoreBodyColor(DesignBody body) {
            FadeColorState state;
            if (!originalBodyColors.TryGetValue(body, out state)) return;
            if (!body.IsDeleted && state.LastAppliedColor.HasValue) {
                if (state.NativeColor != null) {
                    try {
                        Color? current = AppearanceHelper.GetColorOverride(state.NativeColor, null, null);
                        if (current.HasValue && current.Value.ToArgb() == state.LastAppliedColor.Value.ToArgb())
                            AppearanceHelper.SetColor(state.NativeColor, state.OriginalColor, null);
                    } catch { }
                } else {
                    Color? current = body.GetColor(null);
                    if (current.HasValue && current.Value.ToArgb() == state.LastAppliedColor.Value.ToArgb())
                        body.SetColor(null, state.PublicOriginalColor);
                }
            }
            originalBodyColors.Remove(body);
        }
        private IList<IDesignBody> GetBodyOccurrences(int track) {
            List<IDesignBody> result;
            if (visibleBodyOccurrences.TryGetValue(track, out result)) return result;
            result = new List<IDesignBody>();
            Window window = Window.ActiveWindow;
            if (window != null && window.Document == document && window.Scene != null) {
                var masters = new HashSet<DesignBody>();
                foreach (DesignBody body in components[track].Template.Bodies) masters.Add(body);
                // The visible objects are occurrences. A master-only change can be
                // overridden by an occurrence's appearance in SpaceClaim.
                foreach (IDesignBody body in window.Scene.GetDescendants<IDesignBody>())
                    if (masters.Contains(body.Master)) result.Add(body);
            }
            if (result.Count > 0) visibleBodyOccurrences[track] = result;
            return result;
        }
        internal void ApplyVisibility(double time, bool fade) {
            if (visibilityKeys.Count == 0 && originalBodyVisibility.Count == 0 &&
                originalBodyStyles.Count == 0 && originalBodyColors.Count == 0 &&
                originalPlaneVisibility.Count == 0) return;
            Color background = SpaceClaim.Api.V261.Application.UserOptions.BackgroundColor;
            for (int track = 0; track < components.Count; track++) {
                double changeTime;
                VisibilityMode mode = VisibilityAt(track, time, out changeTime);
                if (mode == VisibilityMode.Default && originalBodyVisibility.Count == 0 &&
                    originalBodyStyles.Count == 0 && originalBodyColors.Count == 0) continue;
                if (!CanAnimateComponentVisibility(track)) continue;
                double opacity = reversedVisibility ? ReversedFadeOpacity(mode, time, changeTime, FadeDurationSeconds) :
                    FadeOpacity(mode, time, changeTime, FadeDurationSeconds);
                if (!fade && mode == VisibilityMode.FadeIn) opacity = 1;
                if (!fade && mode == VisibilityMode.FadeOut) opacity = 0;
                foreach (IDesignBody body in GetBodyOccurrences(track)) {
                    bool? originalVisibility;
                    if (mode != VisibilityMode.Default && !originalBodyVisibility.TryGetValue(body, out originalVisibility)) {
                        originalVisibility = body.GetVisibility(null);
                        originalBodyVisibility.Add(body, originalVisibility);
                    }
                    DesignBody master = body.Master;
                    if (mode == VisibilityMode.Default) {
                        if (originalBodyVisibility.TryGetValue(body, out originalVisibility)) {
                            body.SetVisibility(null, originalVisibility);
                            originalBodyVisibility.Remove(body);
                        }
                        RestoreBodyColor(master);
                        RestoreBodyStyle(master);
                    } else if (mode == VisibilityMode.Hide ||
                        (mode == VisibilityMode.FadeIn || mode == VisibilityMode.FadeOut) && opacity <= 0) {
                        body.SetVisibility(null, false);
                        RestoreBodyColor(master);
                        RestoreBodyStyle(master);
                    } else {
                        body.SetVisibility(null, true);
                        if ((mode == VisibilityMode.FadeIn || mode == VisibilityMode.FadeOut) && opacity < 1) {
                            bool nativeOpacity = ApplyFadeColor(master, opacity, background);
                            BodyStyle fadeStyle = nativeOpacity ? BodyStyle.Default : BodyStyle.Transparent;
                            if (master.Style != fadeStyle) {
                                if (!originalBodyStyles.ContainsKey(master)) originalBodyStyles.Add(master, master.Style);
                                master.Style = fadeStyle;
                            }
                        } else {
                            RestoreBodyColor(master);
                            RestoreBodyStyle(master);
                        }
                    }
                }
            }
            for (int i = 0; i < planeTracks.Count; i++) {
                double changeTime;
                VisibilityMode mode = VisibilityAt(components.Count + i, time, out changeTime);
                DatumPlane plane = planeTracks[i].Plane;
                bool? original;
                if (mode == VisibilityMode.Default) {
                    if (originalPlaneVisibility.TryGetValue(plane, out original)) plane.SetVisibility(null, original);
                    continue;
                }
                if (!originalPlaneVisibility.ContainsKey(plane)) originalPlaneVisibility.Add(plane, plane.GetVisibility(null));
                plane.SetVisibility(null, mode == VisibilityMode.Show);
            }
        }
        private void RestoreBodyStyle(DesignBody body) {
            BodyStyle style;
            if (!originalBodyStyles.TryGetValue(body, out style)) return;
            // A user may edit the style while the timeline is idle. Never replace
            // a newer explicit choice with a stale snapshot.
            if (!body.IsDeleted && (body.Style == BodyStyle.Transparent || body.Style == BodyStyle.Default))
                body.Style = style;
            originalBodyStyles.Remove(body);
        }
        internal void RestoreAppearanceStyles() {
            foreach (var body in new List<DesignBody>(originalBodyStyles.Keys)) RestoreBodyStyle(body);
        }
        internal void RestoreAppearanceStylesInWriteBlock() {
            if (originalBodyStyles.Count == 0 && originalBodyColors.Count == 0) return;
            Action restore = delegate {
                foreach (var body in new List<DesignBody>(originalBodyColors.Keys)) RestoreBodyColor(body);
                RestoreAppearanceStyles();
            };
            if (WriteBlock.IsActive) restore();
            else WriteBlock.ExecuteTask("SC Animator - Finish fade", delegate { restore(); });
        }
        private void RestoreOriginalAppearances() {
            foreach (var body in originalBodyVisibility)
                if (!body.Key.IsDeleted) body.Key.SetVisibility(null, body.Value);
            originalBodyVisibility.Clear();
            foreach (var body in new List<DesignBody>(originalBodyColors.Keys)) RestoreBodyColor(body);
            RestoreAppearanceStyles();
            visibleBodyOccurrences.Clear();
            foreach (var plane in originalPlaneVisibility)
                if (!plane.Key.IsDeleted) plane.Key.SetVisibility(null, plane.Value);
            originalPlaneVisibility.Clear();
        }
        private void RestoreAppearancesInWriteBlock() {
            if (originalBodyVisibility.Count == 0 && originalBodyStyles.Count == 0 &&
                originalBodyColors.Count == 0 &&
                originalPlaneVisibility.Count == 0) return;
            if (WriteBlock.IsActive) RestoreOriginalAppearances();
            else WriteBlock.ExecuteTask("SC Animator - Restore visibility", RestoreOriginalAppearances);
        }
    }
}

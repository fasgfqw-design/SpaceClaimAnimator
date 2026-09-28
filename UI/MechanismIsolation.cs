using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261;
using SCAnimator.V261.Engine;

namespace SCAnimator.V261.UI {
    // A reversible preview. Preserve each body's explicit visibility value,
    // including null (inherit), before the first change.
    internal sealed class MechanismIsolation {
        private readonly Window window;
        private readonly Document document;
        private readonly Dictionary<IDesignBody, bool?> original = new Dictionary<IDesignBody, bool?>();
        internal bool IsActive { get { return original.Count != 0; } }
        internal Document Document { get { return document; } }

        internal MechanismIsolation(Window window) {
            this.window = window;
            document = window == null ? null : window.Document;
        }
        private bool CanWrite(out string error) {
            error = null;
            if (document == null || window == null || window.Document != document) {
                error = "The original model is no longer open."; return false;
            }
            if (!WriteBlock.IsAvailable && !WriteBlock.IsActive) {
                error = "Finish the current SpaceClaim command before isolating the pair.";
                return false;
            }
            return true;
        }
        private static void Run(string title, SpaceClaim.Api.V261.Task action) {
            if (WriteBlock.IsActive) action();
            else WriteBlock.ExecuteTask(title, action);
        }
        internal bool ShowPair(HingeCandidate candidate, out string error) {
            if (candidate == null) { error = "Select an Align proposal first."; return false; }
            if (!CanWrite(out error)) return false;
            try {
                if (original.Count == 0)
                    foreach (IDesignBody body in window.Scene.GetDescendants<IDesignBody>())
                        if (!body.IsDeleted) original[body] = body.GetVisibility(null);
                var kept = new HashSet<DesignBody>();
                foreach (IDesignBody body in candidate.Fixed.Content.GetDescendants<IDesignBody>())
                    kept.Add(body.Master);
                foreach (IDesignBody body in candidate.Moving.Content.GetDescendants<IDesignBody>())
                    kept.Add(body.Master);
                Run("SC Animator - Isolate mechanism pair", delegate {
                    foreach (var entry in original)
                        if (!entry.Key.IsDeleted)
                            entry.Key.SetVisibility(null,
                                kept.Contains(entry.Key.Master) ? entry.Value : (bool?)false);
                });
                window.RefreshRendering();
                error = null;
                return true;
            } catch (Exception ex) { error = "Could not isolate pair: " + ex.Message; return false; }
        }
        internal bool Restore(out string error) {
            error = null;
            if (!IsActive) return true;
            if (!CanWrite(out error)) return false;
            try {
                Run("SC Animator - Restore mechanism visibility", delegate {
                    foreach (var entry in original)
                        if (!entry.Key.IsDeleted) entry.Key.SetVisibility(null, entry.Value);
                });
                original.Clear();
                window.RefreshRendering();
                return true;
            } catch (Exception ex) { error = "Could not restore model visibility: " + ex.Message; return false; }
        }
    }
}

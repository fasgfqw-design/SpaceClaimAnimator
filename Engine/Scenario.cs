using System;
using System.Collections.Generic;
using SpaceClaim.Api.V261;

namespace SCAnimator.V261.Engine {
    internal sealed class ScenarioDefinition {
        internal readonly List<string> AnimationNames = new List<string>();
        internal int Repeats = 1;
        internal bool PingPong;
        internal ScenarioDefinition Clone() {
            var copy = new ScenarioDefinition { Repeats = Repeats, PingPong = PingPong };
            copy.AnimationNames.AddRange(AnimationNames);
            return copy;
        }
        internal List<ScenarioEntry> BuildOrder() {
            var order = new List<ScenarioEntry>();
            for (int repeat = 0; repeat < Repeats; repeat++) {
                foreach (string name in AnimationNames) order.Add(new ScenarioEntry(name, false));
                if (PingPong) for (int i = AnimationNames.Count - 1; i >= 0; i--)
                    order.Add(new ScenarioEntry(AnimationNames[i], true));
            }
            return order;
        }
    }
    internal struct ScenarioEntry {
        internal readonly string Name;
        internal readonly bool Reverse;
        internal ScenarioEntry(string name, bool reverse) { Name = name; Reverse = reverse; }
    }

    internal sealed partial class AnimationProject {
        private ScenarioDefinition scenario = new ScenarioDefinition();
        internal ScenarioDefinition Scenario { get { return scenario.Clone(); } }
        internal bool SetScenario(ScenarioDefinition definition) {
            if (Animation.IsAnimating || document == null || Window.ActiveWindow == null ||
                Window.ActiveWindow.Document != document || definition == null ||
                definition.AnimationNames.Count > 100 || definition.Repeats < 1 || definition.Repeats > 20)
                return false;
            foreach (string name in definition.AnimationNames)
                if (FindAnimation(name) < 0) return false;
            bool unchanged = scenario.Repeats == definition.Repeats &&
                scenario.PingPong == definition.PingPong &&
                scenario.AnimationNames.Count == definition.AnimationNames.Count;
            if (unchanged) for (int i = 0; i < scenario.AnimationNames.Count; i++)
                if (!String.Equals(scenario.AnimationNames[i], definition.AnimationNames[i], StringComparison.Ordinal)) {
                    unchanged = false; break;
                }
            if (unchanged) return true;
            SaveStructuralUndoState();
            scenario = definition.Clone();
            MarkDirty();
            return true;
        }
        private int FindAnimation(string name) {
            for (int i = 0; i < animations.Count; i++)
                if (String.Equals(animations[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
        internal AnimationProject[] CreateScenarioProjects(ScenarioDefinition definition) {
            if (definition == null || definition.AnimationNames.Count == 0 ||
                document == null || Window.ActiveWindow == null || Window.ActiveWindow.Document != document ||
                Animation.IsAnimating) throw new InvalidOperationException("The scenario cannot run on this model.");
            var projects = new AnimationProject[definition.AnimationNames.Count];
            for (int i = 0; i < projects.Length; i++) {
                int index = FindAnimation(definition.AnimationNames[i]);
                if (index < 0) throw new InvalidOperationException("A scenario animation was deleted: " + definition.AnimationNames[i]);
                var clip = new AnimationProject { document = document };
                clip.Restore(index == activeAnimationIndex ? CreateSnapshot() : animations[index].Project);
                if (!clip.CanPlay || !clip.CanUseCurrentAssembly || clip.EffectiveRangeEnd <= clip.EffectiveRangeStart)
                    throw new InvalidOperationException("Animation has no playable range: " + definition.AnimationNames[i]);
                projects[i] = clip;
            }
            return projects;
        }
    }
}

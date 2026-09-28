using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Extensibility;
using SCAnimator.V261.Commands;
using SCAnimator.V261.Engine;
using SCAnimator.V261.UI;

namespace SCAnimator.V261 {
    public class SCAnimatorAddIn : AddIn, IExtensibility, ICommandExtensibility, IRibbonExtensibility {
        private readonly HashSet<Document> persistedDuringSave = new HashSet<Document>();
        private readonly CommandCapsule[] capsules = new CommandCapsule[] {
            new ShowTimelineCapsule(),
            new MechanismsCapsule(),
            new PlayPauseCapsule(),
            new CancelAnimationCapsule(),
            new ResetPoseCapsule(),
            new LoopCapsule(), new FpsCapsule(), new SpeedCapsule(), new EasingCapsule(), new ThemeCapsule(), new ExportVideoCapsule(),
        };

        public bool Connect() {
            Document.DocumentSaving += OnDocumentSaving;
            Document.DocumentSaved += OnDocumentSaved;
            Document.DocumentClosed += OnDocumentClosed;
            return true;
        }

        public void Disconnect() {
            Document.DocumentSaving -= OnDocumentSaving;
            Document.DocumentSaved -= OnDocumentSaved;
            Document.DocumentClosed -= OnDocumentClosed;
            PlaybackController.CancelAnimation();
            TimelinePanelHost.Dispose();
            AnimationProject.ReleaseAll();
            persistedDuringSave.Clear();
        }

        private void OnDocumentSaving(object sender, SaveDocumentEventArgs args) {
            AnimationProject project;
            if (!AnimationProject.TryGetLoaded(args.Document, out project) || !project.IsDirty) return;
            if (project.IsEmbedded) {
                persistedDuringSave.Add(args.Document);
                return;
            }
            MessageBox.Show("SC Animator could not embed the latest animation in this model. The animation remains in memory.\n\n" +
                (project.PersistenceError ?? "The document could not be changed inside a write block."),
                "SC Animator save error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void OnDocumentSaved(object sender, SaveDocumentEventArgs args) {
            if (!persistedDuringSave.Remove(args.Document)) return;
            AnimationProject project;
            if (AnimationProject.TryGetLoaded(args.Document, out project)) project.MarkSaved();
        }

        private void OnDocumentClosed(object sender, SubjectEventArgs<Document> args) {
            if (args.Subject == null) return;
            persistedDuringSave.Remove(args.Subject);
            AnimationProject.Release(args.Subject);
        }

        public void Initialize() {
            foreach (CommandCapsule capsule in capsules)
                capsule.Initialize();
        }

        public string GetCustomUI() {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<customUI
  xmlns=""http://schemas.spaceclaim.com/customui""
  xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
  xsi:schemaLocation=""http://schemas.spaceclaim.com/customui http://schemas.spaceclaim.com/customui/SpaceClaimCustomUI.V261.xsd"">
  <ribbon>
    <tabs>
      <tab id=""SCAnimator.V261.Tab"" label=""SC Animator"">
        <group id=""SCAnimator.V261.ToolsGroup"" label=""Tools"">
          <button id=""SCAnimator.V261.Select"" size=""large"" command=""Select""/>
          <button id=""SCAnimator.V261.Move"" size=""large"" command=""Transform""/>
        </group>
        <group id=""SCAnimator.V261.KeyframesGroup"" label=""Keyframes"">
          <button id=""SCAnimator.V261.ShowTimeline"" size=""large"" command=""SCAnimator.V261.ShowTimeline""/>
        </group>
        <group id=""SCAnimator.V261.MechanismsGroup"" label=""Mechanisms"">
          <button id=""SCAnimator.V261.Mechanisms"" size=""large"" command=""SCAnimator.V261.Mechanisms""/>
        </group>
        <group id=""SCAnimator.V261.PlaybackGroup"" label=""Playback"">
          <button id=""SCAnimator.V261.PlayPause"" size=""large"" command=""SCAnimator.V261.PlayPause""/>
          <button id=""SCAnimator.V261.CancelAnimation"" size=""large"" command=""SCAnimator.V261.CancelAnimation""/>
          <button id=""SCAnimator.V261.ResetPose"" size=""large"" command=""SCAnimator.V261.ResetPose""/>
          <checkBox id=""SCAnimator.V261.Loop"" command=""SCAnimator.V261.Loop""/>
        </group>
        <group id=""SCAnimator.V261.ExportGroup"" label=""Export"">
          <button id=""SCAnimator.V261.ExportVideo"" size=""large"" command=""SCAnimator.V261.ExportVideo""/>
        </group>
        <group id=""SCAnimator.V261.TimingGroup"" label=""Timing"" layoutOrientation=""vertical"">
          <container id=""SCAnimator.V261.FpsRow"" layoutOrientation=""horizontal"">
            <label id=""SCAnimator.V261.FpsLabel"" text=""FPS""/>
            <spinBox id=""SCAnimator.V261.Fps"" command=""SCAnimator.V261.Fps"" width=""48"" minimumValue=""1"" maximumValue=""120"" increment=""1"" decimalPlaces=""0""/>
          </container>
          <container id=""SCAnimator.V261.SpeedRow"" layoutOrientation=""horizontal"">
            <label id=""SCAnimator.V261.SpeedLabel"" text=""Speed""/>
            <comboBox id=""SCAnimator.V261.Speed"" command=""SCAnimator.V261.Speed"" width=""80"" textEditable=""false""/>
          </container>
          <container id=""SCAnimator.V261.EasingRow"" layoutOrientation=""horizontal"">
            <label id=""SCAnimator.V261.EasingLabel"" text=""Smooth""/>
            <comboBox id=""SCAnimator.V261.Easing"" command=""SCAnimator.V261.Easing"" width=""110"" textEditable=""false""/>
          </container>
        </group>
        <group id=""SCAnimator.V261.SettingsGroup"" label=""Settings"" layoutOrientation=""vertical"">
          <container id=""SCAnimator.V261.ThemeRow"" layoutOrientation=""horizontal"">
            <label id=""SCAnimator.V261.ThemeLabel"" text=""Theme""/>
            <comboBox id=""SCAnimator.V261.Theme"" command=""SCAnimator.V261.Theme"" width=""88"" textEditable=""false""/>
          </container>
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>";
        }
    }
}


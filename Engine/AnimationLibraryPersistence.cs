using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using SpaceClaim.Api.V261;

namespace SCAnimator.V261.Engine {
    internal sealed class NamedAnimationSnapshot {
        internal string Name;
        internal ProjectSnapshot Project;
    }

    internal sealed class AnimationLibrarySnapshot {
        internal int ActiveIndex;
        internal readonly List<NamedAnimationSnapshot> Animations = new List<NamedAnimationSnapshot>();
        internal ScenarioDefinition Scenario = new ScenarioDefinition();
    }

    internal static partial class ProjectPersistence {
        internal static void WriteLibrary(Document doc, AnimationLibrarySnapshot library) {
            WriteEncoded(doc, EncodeLibrary(library));
        }

        internal static AnimationLibrarySnapshot ReadLibrary(Document doc) {
            string encoded = ReadEncoded(doc);
            return encoded == null ? null : DecodeLibrary(encoded);
        }

        internal static string EncodeLibrary(AnimationLibrarySnapshot library) {
            if (library == null || library.Animations.Count < 1 || library.Animations.Count > 100 ||
                library.ActiveIndex < 0 || library.ActiveIndex >= library.Animations.Count)
                throw new InvalidDataException("Invalid animation list.");
            var raw = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (XmlWriter writer = XmlWriter.Create(raw, new XmlWriterSettings { OmitXmlDeclaration = true })) {
                writer.WriteStartElement("SCAnimatorLibrary");
                writer.WriteAttributeString("version", "4");
                writer.WriteAttributeString("active", library.ActiveIndex.ToString(CultureInfo.InvariantCulture));
                foreach (NamedAnimationSnapshot animation in library.Animations) {
                    if (animation == null || animation.Project == null || !ValidAnimationName(animation.Name) ||
                        !seen.Add(animation.Name)) throw new InvalidDataException("Invalid or duplicate animation name.");
                    writer.WriteStartElement("animation");
                    writer.WriteAttributeString("name", animation.Name);
                    writer.WriteString(Encode(animation.Project));
                    writer.WriteEndElement();
                }
                ScenarioDefinition scenario = library.Scenario ?? new ScenarioDefinition();
                if (scenario.Repeats < 1 || scenario.Repeats > 20 || scenario.AnimationNames.Count > 100)
                    throw new InvalidDataException("Invalid scenario settings.");
                writer.WriteStartElement("scenario");
                writer.WriteAttributeString("repeats", scenario.Repeats.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("pingpong", scenario.PingPong ? "true" : "false");
                foreach (string name in scenario.AnimationNames) {
                    if (!seen.Contains(name)) throw new InvalidDataException("Scenario references a missing animation.");
                    writer.WriteStartElement("step");
                    writer.WriteAttributeString("animation", name);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            byte[] bytes = Encoding.UTF8.GetBytes(raw.ToString());
            using (var output = new MemoryStream()) {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(bytes, 0, bytes.Length);
                return Convert.ToBase64String(output.ToArray());
            }
        }

        internal static AnimationLibrarySnapshot DecodeLibrary(string encoded) {
            byte[] compressed = Convert.FromBase64String(encoded);
            byte[] plain;
            using (var input = new MemoryStream(compressed))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream()) {
                var buffer = new byte[8192];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) != 0) {
                    if (output.Length + read > 32 * 1024 * 1024)
                        throw new InvalidDataException("SC Animator library is too large.");
                    output.Write(buffer, 0, read);
                }
                plain = output.ToArray();
            }
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 32 * 1024 * 1024 };
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(Encoding.UTF8.GetString(plain)), settings))
                document.Load(reader);
            XmlElement root = document.DocumentElement;
            if (root != null && root.Name == "SCAnimator") {
                var previous = new AnimationLibrarySnapshot();
                previous.Animations.Add(new NamedAnimationSnapshot { Name = "Animation 1", Project = Decode(encoded) });
                return previous;
            }
            if (root == null || root.Name != "SCAnimatorLibrary" ||
                root.GetAttribute("version") != "3" && root.GetAttribute("version") != "4")
                throw new InvalidDataException("Unsupported SC Animator library format.");
            bool hasScenario = root.GetAttribute("version") == "4";
            var result = new AnimationLibrarySnapshot();
            if (!Int32.TryParse(root.GetAttribute("active"), NumberStyles.None, CultureInfo.InvariantCulture,
                out result.ActiveIndex)) throw new InvalidDataException("Invalid active animation index.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            XmlElement scenarioElement = null;
            foreach (XmlNode node in root.ChildNodes) {
                XmlElement element = node as XmlElement;
                if (element != null && element.Name == "scenario" && hasScenario && scenarioElement == null) {
                    scenarioElement = element;
                    continue;
                }
                if (element == null || element.Name != "animation" || scenarioElement != null)
                    throw new InvalidDataException("Invalid animation entry.");
                string name = element.GetAttribute("name");
                if (!ValidAnimationName(name) || !seen.Add(name))
                    throw new InvalidDataException("Invalid or duplicate animation name.");
                result.Animations.Add(new NamedAnimationSnapshot { Name = name, Project = Decode(element.InnerText) });
                if (result.Animations.Count > 100) throw new InvalidDataException("Too many animations.");
            }
            if (result.Animations.Count == 0 || result.ActiveIndex < 0 || result.ActiveIndex >= result.Animations.Count)
                throw new InvalidDataException("Invalid animation list.");
            if (hasScenario) {
                if (scenarioElement == null || !Int32.TryParse(scenarioElement.GetAttribute("repeats"),
                    NumberStyles.None, CultureInfo.InvariantCulture, out result.Scenario.Repeats) ||
                    result.Scenario.Repeats < 1 || result.Scenario.Repeats > 20 ||
                    !Boolean.TryParse(scenarioElement.GetAttribute("pingpong"), out result.Scenario.PingPong))
                    throw new InvalidDataException("Invalid scenario settings.");
                foreach (XmlNode node in scenarioElement.ChildNodes) {
                    XmlElement step = node as XmlElement;
                    if (step == null || step.Name != "step" || !seen.Contains(step.GetAttribute("animation")))
                        throw new InvalidDataException("Scenario references a missing animation.");
                    result.Scenario.AnimationNames.Add(step.GetAttribute("animation"));
                    if (result.Scenario.AnimationNames.Count > 100)
                        throw new InvalidDataException("Too many scenario steps.");
                }
            }
            return result;
        }

        internal static bool ValidAnimationName(string name) {
            return !String.IsNullOrWhiteSpace(name) && name.Length <= 80 && name.Trim() == name;
        }
    }
}

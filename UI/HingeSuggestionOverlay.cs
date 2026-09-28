using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Display;
using SpaceClaim.Api.V261.Geometry;
using SpaceClaim.Api.V261.Modeler;
using SCAnimator.V261.Engine;

namespace SCAnimator.V261.UI {
    // Window-scoped transient face rendering; no model color is changed.
    public class HingeSuggestionOverlay : LightweightCustomWrapper<HingeSuggestionOverlay> {
        [NonSerialized]
        private Window viewWindow;
        protected HingeSuggestionOverlay(LightweightCustomObject subject) : base(subject) { }
        protected HingeSuggestionOverlay(Window window) : base(window) { }

        internal static HingeSuggestionOverlay Create(Window window) {
            var overlay = new HingeSuggestionOverlay(window) { viewWindow = window };
            overlay.Initialize();
            return overlay;
        }

        internal bool Show(ICollection<HingeCandidate> candidates, HingeCandidate selected) {
            var allEdges = new List<Primitive>();
            var fixedEdges = new List<Primitive>();
            var movingEdges = new List<Primitive>();
            var seenAll = new HashSet<string>(StringComparer.Ordinal);
            var seenFixed = new HashSet<string>(StringComparer.Ordinal);
            var seenMoving = new HashSet<string>(StringComparer.Ordinal);
            foreach (HingeCandidate candidate in candidates) {
                AddFace(candidate.FixedFace, allEdges, seenAll);
                AddFace(candidate.MovingFace, allEdges, seenAll);
            }
            if (selected != null) {
                AddFace(selected.FixedFace, fixedEdges, seenFixed);
                AddFace(selected.MovingFace, movingEdges, seenMoving);
            }
            var graphics = new List<Graphic>();
            bool bothFacesRendered = false;
            if (selected != null) {
                bool fixedRendered = AddFaceFill(selected.FixedFace,
                    Color.FromArgb(135, 36, 155, 255), graphics);
                bool movingRendered = AddFaceFill(selected.MovingFace,
                    Color.FromArgb(190, 255, 162, 26), graphics);
                bothFacesRendered = fixedRendered && movingRendered;
            }
            if (allEdges.Count > 0)
                graphics.Add(Graphic.Create(new GraphicStyle {
                    LineColor = Color.FromArgb(90, 0, 190, 215),
                    LineWidth = 1, IsSelectable = false, EnableDepthBuffer = true
                }, allEdges));
            if (fixedEdges.Count > 0)
                graphics.Add(Graphic.Create(new GraphicStyle {
                    LineColor = Color.FromArgb(255, 36, 155, 255),
                    LineWidth = 5, IsSelectable = false, EnableDepthBuffer = false
                }, fixedEdges));
            if (movingEdges.Count > 0)
                graphics.Add(Graphic.Create(new GraphicStyle {
                    LineColor = Color.FromArgb(255, 255, 162, 26),
                    LineWidth = 5, IsSelectable = false, EnableDepthBuffer = false
                }, movingEdges));
            Rendering = Graphic.Create(null, new Primitive[0], graphics);
            return bothFacesRendered;
        }

        private bool AddFaceFill(IDesignFace face, Color color, List<Graphic> graphics) {
            var designFace = face as DesignFace;
            var body = face == null ? null : face.Parent as DesignBody;
            if (designFace == null || body == null || designFace.Shape == null || viewWindow == null) return false;
            try {
                FaceTessellation tessellation;
                var tessellations = body.GetTessellation(new[] { designFace.Shape });
                if (!tessellations.TryGetValue(designFace.Shape, out tessellation) ||
                    tessellation == null) {
                    tessellation = null;
                    foreach (var entry in body.GetTessellation(null)) {
                        DesignFace mapped = body.GetDesignFace(entry.Key);
                        if (mapped != null && mapped.Moniker.ToString() == designFace.Moniker.ToString()) {
                            tessellation = entry.Value; break;
                        }
                    }
                    if (tessellation == null) return false;
                }
                double offset = Math.Max(.000001,
                    designFace.Shape.GetBoundingBox(Matrix.Identity).Size.Magnitude * .0002);
                var vertices = tessellation.Vertices.Select(vertex =>
                    new PositionNormal(vertex.PositionNormal.Position -
                        viewWindow.Camera.LookDirection * offset, vertex.PositionNormal.Normal)).ToList();
                var mesh = MeshPrimitive.CreateFacets(vertices, tessellation.Facets);
                graphics.Add(Graphic.Create(mesh, null, fillColor: color,
                    edgeDisplay: MeshEdgeDisplay.None, depthBuffer: true, selectable: false));
                return true;
            } catch (InvalidOperationException) { return false; }
              catch (ArgumentException) { return false; }
        }

        private static void AddFace(IDesignFace face, List<Primitive> primitives,
            HashSet<string> seen) {
            if (face == null) return;
            foreach (IDesignEdge edge in face.Edges) {
                if (!seen.Add(edge.Moniker.ToString())) continue;
                var shaped = edge as IHasShape;
                var curve = shaped == null ? null : shaped.Shape as ITrimmedCurve;
                if (curve != null) primitives.Add(CurvePrimitive.Create(curve));
            }
        }
    }
}

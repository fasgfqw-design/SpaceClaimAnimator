using System;
using System.Drawing;
using SpaceClaim.Api.V261;
using SpaceClaim.Api.V261.Display;
using SpaceClaim.Api.V261.Geometry;
using SCAnimator.V261.Engine;
using Point = SpaceClaim.Api.V261.Geometry.Point;

namespace SCAnimator.V261.UI {
    // A window-only marker for one proposed native Align condition. A native
    // point primitive stays circular in screen pixels while the camera moves.
    public class AlignSuggestionMarker : LightweightCustomWrapper<AlignSuggestionMarker> {
        protected AlignSuggestionMarker(LightweightCustomObject subject) : base(subject) { }
        protected AlignSuggestionMarker(Window window) : base(window) { }

        [field: NonSerialized]
        internal HingeCandidate Candidate { get; private set; }
        private Point anchor;
        private bool focused;
        internal Point Anchor { get { return anchor; } }
        internal const int HitRadiusPixels = 16;
        internal string SelectionId { get { return Subject.Moniker.ToString(); } }

        internal static AlignSuggestionMarker Create(Window window, HingeCandidate candidate) {
            var marker = new AlignSuggestionMarker(window) { Candidate = candidate };
            marker.Initialize();
            marker.anchor = marker.BaseCenter;
            // SetFocused(false) would be skipped because false is the default.
            // The first graphic must be assigned even when this marker is not
            // moved by LayoutMarkers (a single proposal at its base center).
            marker.UpdateRendering();
            return marker;
        }

        internal Point BaseCenter { get {
            return Candidate.MarkerCenter;
        } }

        internal void SetAnchor(Point value) {
            if (anchor == value) return;
            anchor = value; UpdateRendering();
        }
        internal void SetFocused(bool value) {
            if (focused == value) return;
            focused = value; UpdateRendering();
        }
        private static double Dot(Vector a, Direction b) {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }
        private static double Dot(Direction a, Direction b) {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }
        internal static Point PointAtScreenPixel(Window window, System.Drawing.Point pixel, Point depthAnchor) {
            Direction normal = window.Camera.LookDirection;
            Line ray = window.ActiveContext.GetCursorRay(pixel);
            if (ray == null) return depthAnchor;
            double denominator = Dot(ray.Direction, normal);
            if (Math.Abs(denominator) < 1e-8) return depthAnchor;
            double distance = Dot(depthAnchor - ray.Origin, normal) / denominator;
            return ray.Origin + ray.Direction * distance;
        }
        private void UpdateRendering() {
            Color color = focused ? Color.FromArgb(255, 255, 175, 35) :
                Color.FromArgb(255, 25, 210, 75);
            Rendering = Graphic.Create(new GraphicStyle {
                FillColor = Color.FromArgb(220, color),
                LineColor = Color.FromArgb(255, 15, 65, 30),
                IsSelectable = true, EnableDepthBuffer = false
            }, PointPrimitive.Create(anchor, HitRadiusPixels));
            SelectionOrder = SelectionOrder.ForceTop;
        }
    }
}

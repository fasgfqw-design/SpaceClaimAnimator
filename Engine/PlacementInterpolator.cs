using System;
using SpaceClaim.Api.V261.Geometry;

namespace SCAnimator.V261.Engine {
    internal static class PlacementInterpolator {
        private static readonly Frame LocalFrame = Frame.Create(Point.Origin, Direction.DirX, Direction.DirY);

        public static Matrix Interpolate(Matrix a, Matrix b, double t) {
            Frame fa = a * LocalFrame;
            Frame fb = b * LocalFrame;

            Vec3 pa = Vec3.FromPoint(fa.Origin);
            Vec3 pb = Vec3.FromPoint(fb.Origin);
            Vec3 p = Vec3.Lerp(pa, pb, t);

            QuaternionD qa = QuaternionD.FromFrame(fa);
            QuaternionD qb = QuaternionD.FromFrame(fb);
            QuaternionD q = QuaternionD.Slerp(qa, qb, t);

            Vec3 x;
            Vec3 y;
            q.ToXYAxes(out x, out y);

            Frame resultFrame = Frame.Create(
                Point.Create(p.X, p.Y, p.Z),
                Direction.Create(x.X, x.Y, x.Z),
                Direction.Create(y.X, y.Y, y.Z));

            return Matrix.CreateMapping(resultFrame);
        }

        private struct Vec3 {
            public Vec3(double x, double y, double z) {
                X = x;
                Y = y;
                Z = z;
            }

            public double X;
            public double Y;
            public double Z;

            public static Vec3 FromPoint(Point p) {
                return FromVector(p.Vector);
            }

            public static Vec3 FromDirection(Direction d) {
                return FromVector(d.UnitVector);
            }

            public static Vec3 FromVector(Vector v) {
                return new Vec3(
                    Vector.Dot(v, Direction.DirX.UnitVector),
                    Vector.Dot(v, Direction.DirY.UnitVector),
                    Vector.Dot(v, Direction.DirZ.UnitVector));
            }

            public static Vec3 Lerp(Vec3 a, Vec3 b, double t) {
                return new Vec3(
                    a.X + (b.X - a.X) * t,
                    a.Y + (b.Y - a.Y) * t,
                    a.Z + (b.Z - a.Z) * t);
            }
        }

        private struct QuaternionD {
            public double X;
            public double Y;
            public double Z;
            public double W;

            public QuaternionD(double x, double y, double z, double w) {
                X = x;
                Y = y;
                Z = z;
                W = w;
            }

            public static QuaternionD FromFrame(Frame frame) {
                Vec3 x = Vec3.FromDirection(frame.DirX);
                Vec3 y = Vec3.FromDirection(frame.DirY);
                Vec3 z = Vec3.FromDirection(frame.DirZ);

                double m00 = x.X; double m01 = y.X; double m02 = z.X;
                double m10 = x.Y; double m11 = y.Y; double m12 = z.Y;
                double m20 = x.Z; double m21 = y.Z; double m22 = z.Z;

                double trace = m00 + m11 + m22;
                QuaternionD q;

                if (trace > 0.0) {
                    double s = Math.Sqrt(trace + 1.0) * 2.0;
                    q = new QuaternionD(
                        (m21 - m12) / s,
                        (m02 - m20) / s,
                        (m10 - m01) / s,
                        0.25 * s);
                }
                else if (m00 > m11 && m00 > m22) {
                    double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
                    q = new QuaternionD(
                        0.25 * s,
                        (m01 + m10) / s,
                        (m02 + m20) / s,
                        (m21 - m12) / s);
                }
                else if (m11 > m22) {
                    double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
                    q = new QuaternionD(
                        (m01 + m10) / s,
                        0.25 * s,
                        (m12 + m21) / s,
                        (m02 - m20) / s);
                }
                else {
                    double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
                    q = new QuaternionD(
                        (m02 + m20) / s,
                        (m12 + m21) / s,
                        0.25 * s,
                        (m10 - m01) / s);
                }

                return Normalize(q);
            }

            public static QuaternionD Slerp(QuaternionD a, QuaternionD b, double t) {
                a = Normalize(a);
                b = Normalize(b);

                double dot = Dot(a, b);
                if (dot < 0.0) {
                    b = new QuaternionD(-b.X, -b.Y, -b.Z, -b.W);
                    dot = -dot;
                }

                if (dot > 0.9995) {
                    QuaternionD linear = new QuaternionD(
                        a.X + (b.X - a.X) * t,
                        a.Y + (b.Y - a.Y) * t,
                        a.Z + (b.Z - a.Z) * t,
                        a.W + (b.W - a.W) * t);
                    return Normalize(linear);
                }

                dot = Math.Max(-1.0, Math.Min(1.0, dot));
                double theta0 = Math.Acos(dot);
                double theta = theta0 * t;
                double sinTheta = Math.Sin(theta);
                double sinTheta0 = Math.Sin(theta0);

                double s0 = Math.Cos(theta) - dot * sinTheta / sinTheta0;
                double s1 = sinTheta / sinTheta0;

                return Normalize(new QuaternionD(
                    s0 * a.X + s1 * b.X,
                    s0 * a.Y + s1 * b.Y,
                    s0 * a.Z + s1 * b.Z,
                    s0 * a.W + s1 * b.W));
            }

            public void ToXYAxes(out Vec3 xAxis, out Vec3 yAxis) {
                QuaternionD q = Normalize(this);
                double xx = q.X * q.X;
                double yy = q.Y * q.Y;
                double zz = q.Z * q.Z;
                double xy = q.X * q.Y;
                double xz = q.X * q.Z;
                double yz = q.Y * q.Z;
                double wx = q.W * q.X;
                double wy = q.W * q.Y;
                double wz = q.W * q.Z;

                // First and second columns of the rotation matrix.
                xAxis = new Vec3(
                    1.0 - 2.0 * (yy + zz),
                    2.0 * (xy + wz),
                    2.0 * (xz - wy));

                yAxis = new Vec3(
                    2.0 * (xy - wz),
                    1.0 - 2.0 * (xx + zz),
                    2.0 * (yz + wx));
            }

            private static double Dot(QuaternionD a, QuaternionD b) {
                return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            }

            private static QuaternionD Normalize(QuaternionD q) {
                double n = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
                if (n < 1e-15)
                    return new QuaternionD(0, 0, 0, 1);
                return new QuaternionD(q.X / n, q.Y / n, q.Z / n, q.W / n);
            }
        }
    }
}

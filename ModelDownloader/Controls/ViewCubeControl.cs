using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace ModelDownloader.Controls;

public class ViewCubeControl : Control
{
    private int _hoveredFaceIndex = -1;
    private readonly List<ProjectedFace> _projectedFaces = new();

    public static readonly StyledProperty<ModelViewer?> TargetViewerProperty =
        AvaloniaProperty.Register<ViewCubeControl, ModelViewer?>(nameof(TargetViewer));

    public ModelViewer? TargetViewer
    {
        get => GetValue(TargetViewerProperty);
        set => SetValue(TargetViewerProperty, value);
    }

    private struct ProjectedFace
    {
        public int FaceIndex;
        public Point[] Polygon;
        public float ZDepth;
    }

    private static readonly Vector3[] CubeVertices =
    [
        new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
        new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)
    ];

    private struct CubeFace
    {
        public string Name;
        public Vector3 Normal;
        public Color BaseColor;
        public int[] Inds;
        public Quaternion TargetRotation;
    }

    private static readonly CubeFace[] CubeFaces =
    [
        new CubeFace { Name = "F", Normal = new Vector3(0, 0, 1), BaseColor = Color.FromRgb(41, 128, 185), Inds = [4, 5, 6, 7], TargetRotation = Quaternion.Identity }, // +Z Blue
        new CubeFace { Name = "B", Normal = new Vector3(0, 0, -1), BaseColor = Color.FromRgb(31, 97, 141), Inds = [1, 0, 3, 2], TargetRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI) }, // -Z Dark Blue
        new CubeFace { Name = "R", Normal = new Vector3(1, 0, 0), BaseColor = Color.FromRgb(231, 76, 60), Inds = [5, 1, 2, 6], TargetRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI / 2) }, // +X Red
        new CubeFace { Name = "L", Normal = new Vector3(-1, 0, 0), BaseColor = Color.FromRgb(176, 58, 46), Inds = [0, 4, 7, 3], TargetRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -(float)Math.PI / 2) }, // -X Dark Red
        new CubeFace { Name = "U", Normal = new Vector3(0, 1, 0), BaseColor = Color.FromRgb(39, 174, 96), Inds = [3, 7, 6, 2], TargetRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -(float)Math.PI / 2) }, // +Y Green
        new CubeFace { Name = "D", Normal = new Vector3(0, -1, 0), BaseColor = Color.FromRgb(30, 132, 73), Inds = [4, 0, 1, 5], TargetRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI / 2) } // -Y Dark Green
    ];

    private DispatcherTimer? _timer;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (s, args) => InvalidateVisual();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private static Point ProjectPoint(Vector3 pView, Point center, double cubeSize, float fov)
    {
        var perspectiveScale = fov / (fov - pView.Z);
        return new Point(
            center.X + pView.X * perspectiveScale * cubeSize,
            center.Y - pView.Y * perspectiveScale * cubeSize
        );
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (TargetViewer == null) return;

        _projectedFaces.Clear();
        
        var cubeSize = Math.Min(Bounds.Width, Bounds.Height) / 2.0 * 0.6; // Base Radius
        var center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);

        var invCamRot = Quaternion.Inverse(TargetViewer.CameraRotation);
        var fov = 10.0f; // Perspective distance from camera

        var projectedVerts = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            var rotated = Vector3.Transform(CubeVertices[i], invCamRot);
            var p2d = ProjectPoint(rotated, center, cubeSize, fov);
            projectedVerts[i] = new Vector3((float)p2d.X, (float)p2d.Y, rotated.Z);
        }

        var facesToDraw = new List<(CubeFace face, int index, Point[] polygon, float zDepth)>();

        for (int i = 0; i < CubeFaces.Length; i++)
        {
            var face = CubeFaces[i];
            var rotatedNormal = Vector3.Transform(face.Normal, invCamRot);
            
            // Perspective back-face culling: Dot(normal, cameraPos - faceCenter) > 0
            if (rotatedNormal.Z * fov - 1.0f > 0)
            {
                var pts = new Point[4];
                float avgZ = 0;
                for (int j = 0; j < 4; j++)
                {
                    var v = projectedVerts[face.Inds[j]];
                    pts[j] = new Point(v.X, v.Y);
                    avgZ += v.Z;
                }
                facesToDraw.Add((face, i, pts, avgZ / 4f));
            }
        }

        facesToDraw.Sort((a, b) => a.zDepth.CompareTo(b.zDepth));

        var typeface = new Typeface("Inter", FontStyle.Normal, FontWeight.Bold);
        
        foreach (var (face, index, polygon, zDepth) in facesToDraw)
        {
            _projectedFaces.Add(new ProjectedFace { FaceIndex = index, Polygon = polygon, ZDepth = zDepth });

            var color = index == _hoveredFaceIndex ? HighlightColor(face.BaseColor) : face.BaseColor;
            var brush = new SolidColorBrush(color);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1, lineJoin: PenLineJoin.Round);

            var path = new StreamGeometry();
            using (var ctx = path.Open())
            {
                ctx.BeginFigure(polygon[0], true);
                ctx.LineTo(polygon[1]);
                ctx.LineTo(polygon[2]);
                ctx.LineTo(polygon[3]);
                ctx.EndFigure(true);
            }
            context.DrawGeometry(brush, pen, path);

            var viewCenter = Vector3.Transform(face.Normal, invCamRot);
            
            var faceUpWorld = Vector3.Transform(Vector3.UnitY, face.TargetRotation);
            var faceRightWorld = Vector3.Transform(Vector3.UnitX, face.TargetRotation);
            
            var viewUp = Vector3.Transform(faceUpWorld, invCamRot);
            var viewRight = Vector3.Transform(faceRightWorld, invCamRot);

            var c2d = ProjectPoint(viewCenter, center, cubeSize, fov);
            var r2d = ProjectPoint(viewCenter + viewRight, center, cubeSize, fov);
            var d2d = ProjectPoint(viewCenter - viewUp, center, cubeSize, fov);

            var vR = new Avalonia.Vector(r2d.X - c2d.X, r2d.Y - c2d.Y);
            var vD = new Avalonia.Vector(d2d.X - c2d.X, d2d.Y - c2d.Y);

            var fontSize = 60.0;
            var text = new FormattedText(face.Name, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.White);
            
            // Text size in logical units: we want the text to be 1.2 units high (where face height is 2.0)
            double textScale = 1.2 / text.Height;

            var matrix = new Matrix(
                vR.X, vR.Y,
                vD.X, vD.Y,
                c2d.X, c2d.Y
            );

            using (context.PushTransform(Matrix.CreateScale(textScale, textScale) * matrix))
            {
                context.DrawText(text, new Point(-text.Width / 2, -text.Height / 2));
            }
        }
    }

    private static Color HighlightColor(Color c)
    {
        return Color.FromArgb(255, (byte)Math.Min(255, c.R + 40), (byte)Math.Min(255, c.G + 40), (byte)Math.Min(255, c.B + 40));
    }

    private static bool IsPointInPolygon(Point p, Point[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if ((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y) &&
                p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetCurrentPoint(this).Position;

        int newHoveredFace = -1;
        for (int i = _projectedFaces.Count - 1; i >= 0; i--)
        {
            if (IsPointInPolygon(pos, _projectedFaces[i].Polygon))
            {
                newHoveredFace = _projectedFaces[i].FaceIndex;
                break;
            }
        }

        if (newHoveredFace != _hoveredFaceIndex)
        {
            _hoveredFaceIndex = newHoveredFace;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoveredFaceIndex = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _hoveredFaceIndex != -1 && TargetViewer != null)
        {
            TargetViewer.SetTargetCameraRotation(CubeFaces[_hoveredFaceIndex].TargetRotation);
            e.Handled = true;
        }
    }
}

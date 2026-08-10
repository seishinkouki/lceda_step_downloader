using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace ModelDownloader;

public sealed class ObjParser
{
    public sealed class Result
    {
        public List<VertexData> Vertices { get; } = new();
        public List<uint> Indices { get; } = new();
    }

    public struct VertexData
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoords;
        public Vector3 Color;
    }

    private readonly List<Vector3> _positions = new();
    private readonly List<Vector3> _normals = new();
    private readonly List<Vector2> _texCoords = new();

    public Result Parse(Stream objStream, Dictionary<string, Vector3>? materials = null)
    {
        objStream.Position = 0;
        using var reader = new StreamReader(objStream, leaveOpen: true);

        _positions.Clear();
        _normals.Clear();
        _texCoords.Clear();

        var result = new Result();
        var faceGroups = new List<FaceGroup>();
        Vector3 currentColor = new(0.8f, 0.8f, 0.8f);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    _positions.Add(new Vector3(
                        ParseF(parts[1]), ParseF(parts[2]), ParseF(parts[3])));
                    break;

                case "vn" when parts.Length >= 4:
                    _normals.Add(new Vector3(
                        ParseF(parts[1]), ParseF(parts[2]), ParseF(parts[3])));
                    break;

                case "vt" when parts.Length >= 3:
                    _texCoords.Add(new Vector2(ParseF(parts[1]), ParseF(parts[2])));
                    break;

                case "usemtl" when parts.Length >= 2:
                    if (materials != null && materials.TryGetValue(parts[1], out var c))
                        currentColor = c;
                    break;

                case "f":
                    ParseFace(parts, faceGroups, currentColor);
                    break;
            }
        }

        FlattenFaces(faceGroups, result);
        return result;
    }

    private void ParseFace(string[] parts, List<FaceGroup> groups, Vector3 color)
    {
        // OBJ faces: f v1/vt1/vn1 v2/vt2/vn2 v3/vt3/vn3 [v4...]
        var group = new FaceGroup { Color = color };
        for (int i = 1; i < parts.Length; i++)
        {
            group.Indices.Add(ParseFaceVertex(parts[i]));
        }
        groups.Add(group);
    }

    private static FaceVertex ParseFaceVertex(string token)
    {
        // Format: v, v/vt, v//vn, v/vt/vn
        var segs = token.Split('/');
        var fv = new FaceVertex();

        if (segs.Length >= 1 && int.TryParse(segs[0], out int v))
            fv.PositionIndex = ResolveIndex(v);

        if (segs.Length >= 2 && int.TryParse(segs[1], out int vt))
            fv.TexCoordIndex = ResolveIndex(vt);

        if (segs.Length >= 3 && int.TryParse(segs[2], out int vn))
            fv.NormalIndex = ResolveIndex(vn);

        return fv;
    }

    private void FlattenFaces(List<FaceGroup> groups, Result result)
    {
        if (_normals.Count == 0)
            ComputeFaceNormals(groups);

        var vertexMap = new Dictionary<FaceVertex, uint>();
        var vertices = new List<VertexData>();
        var indices = new List<uint>();

        Vector2 defaultTex = Vector2.Zero;
        uint vertexCounter = 0;

        foreach (var group in groups)
        {
            var faceIndices = group.Indices;
            // Triangulate: fan triangulation for polygons with 3+ vertices
            for (int i = 0; i + 2 < faceIndices.Count; i++)
            {
                AddFaceVertex(faceIndices[0], group.Color);
                AddFaceVertex(faceIndices[i + 1], group.Color);
                AddFaceVertex(faceIndices[i + 2], group.Color);
            }
        }

        // Copy deduplicated vertices + indices to result
        result.Vertices.AddRange(vertices);
        result.Indices.AddRange(indices);
        return;

        void AddFaceVertex(FaceVertex fv, Vector3 color)
        {
            fv.Color = color;
            if (!vertexMap.TryGetValue(fv, out uint idx))
            {
                idx = vertexCounter++;
                vertexMap[fv] = idx;

                var vd = new VertexData { Color = color };

                if (fv.PositionIndex >= 0 && fv.PositionIndex < _positions.Count)
                    vd.Position = _positions[fv.PositionIndex];

                if (fv.NormalIndex >= 0 && fv.NormalIndex < _normals.Count)
                    vd.Normal = _normals[fv.NormalIndex];

                if (fv.TexCoordIndex >= 0 && fv.TexCoordIndex < _texCoords.Count)
                    vd.TexCoords = _texCoords[fv.TexCoordIndex];
                else
                    vd.TexCoords = defaultTex;

                vertices.Add(vd);
            }
            indices.Add(idx);
        }
    }

    private static int ResolveIndex(int idx)
    {
        // OBJ uses 1-based indices; negative = relative from end
        if (idx > 0) return idx - 1;
        if (idx < 0) return idx;  // relative indices handled upstream
        return -1;
    }

    private void ComputeFaceNormals(List<FaceGroup> groups)
    {
        foreach (var group in groups)
        {
            var fv = group.Indices;
            for (int i = 0; i + 2 < fv.Count; i++)
            {
                var p0 = _positions[fv[0].PositionIndex];
                var p1 = _positions[fv[i + 1].PositionIndex];
                var p2 = _positions[fv[i + 2].PositionIndex];
                var edge1 = p1 - p0;
                var edge2 = p2 - p0;
                var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));

                int ni = _normals.Count;
                _normals.Add(normal);

                var a = fv[0]; a.NormalIndex = ni; group.Indices[0] = a;
                var b = fv[i + 1]; b.NormalIndex = ni; group.Indices[i + 1] = b;
                var c = fv[i + 2]; c.NormalIndex = ni; group.Indices[i + 2] = c;
            }
        }
    }

    private static float ParseF(string s)
    {
        return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private struct FaceVertex : IEquatable<FaceVertex>
    {
        public int PositionIndex;
        public int NormalIndex;
        public int TexCoordIndex;
        public Vector3 Color; // not part of equality

        public bool Equals(FaceVertex other)
        {
            return PositionIndex == other.PositionIndex
                && NormalIndex == other.NormalIndex
                && TexCoordIndex == other.TexCoordIndex;
        }

        public override bool Equals(object? obj) => obj is FaceVertex fv && Equals(fv);

        public override int GetHashCode()
        {
            return HashCode.Combine(PositionIndex, NormalIndex, TexCoordIndex);
        }
    }

    private sealed class FaceGroup
    {
        public List<FaceVertex> Indices { get; } = new();
        public Vector3 Color;
    }
}

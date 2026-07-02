using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Silk.NET.Assimp;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

namespace ModelDownloader.Controls;

public class AvaloniaNativeContext(GlInterface glInterface) : INativeContext
{
    private readonly GlInterface _glInterface = glInterface;

    public IntPtr GetProcAddress(string proc, int? slot = null) => _glInterface.GetProcAddress(proc);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public bool TryGetProcAddress(string proc, out IntPtr addr, int? slot = null)
    {
        addr = _glInterface.GetProcAddress(proc);
        return addr != IntPtr.Zero;
    }
}

public class ModelViewer : OpenGlControlBase
{
    private float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
    private float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

    private float _cameraDistance = 5.0f;
    private Quaternion _cameraRotation = GetDefaultRotation();
    private Vector3 _cameraTarget = Vector3.Zero;
    private bool _isDraggingLeft = false;
    private bool _isDraggingMiddle = false;
    private Point _lastMousePos;

    private GL? _gl;

    private Quaternion? _targetCameraRotation = null;
    private float _animationProgress = 0f;
    private Quaternion _startCameraRotation;

    public static readonly StyledProperty<Quaternion> CameraRotationProperty =
        AvaloniaProperty.Register<ModelViewer, Quaternion>(nameof(CameraRotation), GetDefaultRotation());

    private static Quaternion GetDefaultRotation()
    {
        // Pitch to see Up (+Y), Yaw to see Left (-X). Base is Front (+Z).
        var pitchQ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)-Math.Asin(1.0 / Math.Sqrt(3.0)));
        var yawQ = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(-Math.PI / 4));
        return Quaternion.Normalize(yawQ * pitchQ);
    }

    public Quaternion CameraRotation
    {
        get => GetValue(CameraRotationProperty);
        set { SetValue(CameraRotationProperty, value); _cameraRotation = value; }
    }

    public void SetTargetCameraRotation(Quaternion target)
    {
        _targetCameraRotation = target;
        _startCameraRotation = _cameraRotation;
        _animationProgress = 0f;
    }

    public ModelViewer()
    {
        Focusable = true;
    }
    private Silk.NET.Assimp.Assimp? _assimp;

    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private uint _shaderProgram;
    private int _indexCount;
    private string? _shaderError;

    private bool _needsLoad = false;

    public static readonly StyledProperty<ModelSource?> SourceProperty =
        AvaloniaProperty.Register<ModelViewer, ModelSource?>(nameof(Source));

    public ModelSource? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            _needsLoad = true;
            RequestNextFrameRendering();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VertexData
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoords;
        public Vector3 Color;
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);

        _gl = GL.GetApi(new AvaloniaNativeContext(gl));
        _assimp = Silk.NET.Assimp.Assimp.GetApi();

        Console.WriteLine($"OpenGL initialized: {GlVersion}");
        InitializeGraphics();
        if (Source != null)
        {
            LoadModel();
        }
    }

    protected override void OnOpenGlLost()
    {
        Console.WriteLine("OpenGL context lost");
        base.OnOpenGlLost();
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_gl == null) return;

        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteProgram(_shaderProgram);

        _assimp?.Dispose();
        _gl?.Dispose();
        base.OnOpenGlDeinit(gl);
    }

    private unsafe void InitializeGraphics()
    {
        if (_gl == null) return;

        bool isGles = GlVersion.Type == GlProfileType.OpenGLES;
        string glslVersion = isGles ? "#version 300 es" : "#version 150";
        string precision = isGles ? "precision mediump float;\n" : "";

        string vertexShaderCode = @"
            in vec3 aPos;
            in vec3 aNormal;
            in vec2 aTexCoords;
            in vec3 aColor;

            out vec3 FragPos;
            out vec3 Normal;
            out vec2 TexCoords;
            out vec3 VertexColor;

            uniform mat4 model;
            uniform mat4 view;
            uniform mat4 projection;

            void main()
            {
                FragPos = vec3(model * vec4(aPos, 1.0));
                Normal = mat3(transpose(inverse(model))) * aNormal;  
                TexCoords = aTexCoords;
                VertexColor = aColor;
                
                gl_Position = projection * view * vec4(FragPos, 1.0);
            }";

        string fragmentShaderCode = @"
            out vec4 FragColor;

            in vec3 FragPos;
            in vec3 Normal;
            in vec2 TexCoords;
            in vec3 VertexColor;

            uniform vec3 lightPos;
            uniform vec3 lightColor;

            void main()
            {
                float ambientStrength = 0.3;
                vec3 ambient = ambientStrength * lightColor;
  	
                vec3 norm = normalize(Normal);
                vec3 lightDir = normalize(lightPos - FragPos);
                float diff = max(dot(norm, lightDir), 0.0);
                vec3 diffuse = diff * lightColor;
            
                vec3 result = (ambient + diffuse) * VertexColor;
                FragColor = vec4(result, 1.0);
            }";

        string vertexShaderSource = $"{glslVersion}\n{precision}{vertexShaderCode}".Replace("\r", "");
        string fragmentShaderSource = $"{glslVersion}\n{precision}{fragmentShaderCode}".Replace("\r", "");

        uint vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexShaderSource);
        _gl.CompileShader(vertexShader);
        if (!CheckShaderCompileError(vertexShader, "vertex"))
        {
            _gl.DeleteShader(vertexShader);
            return;
        }

        var fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentShaderSource);
        _gl.CompileShader(fragmentShader);
        if (!CheckShaderCompileError(fragmentShader, "fragment"))
        {
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
            return;
        }

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vertexShader);
        _gl.AttachShader(_shaderProgram, fragmentShader);
        _gl.BindAttribLocation(_shaderProgram, 0, "aPos");
        _gl.BindAttribLocation(_shaderProgram, 1, "aNormal");
        _gl.BindAttribLocation(_shaderProgram, 2, "aTexCoords");
        _gl.BindAttribLocation(_shaderProgram, 3, "aColor");
        _gl.LinkProgram(_shaderProgram);
        if (!CheckProgramLinkError(_shaderProgram))
        {
            _gl.DeleteProgram(_shaderProgram);
            _shaderProgram = 0;
        }

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _gl.Enable(EnableCap.Blend);
    }

    private bool CheckShaderCompileError(uint shader, string stage)
    {
        _gl!.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string infoLog = _gl.GetShaderInfoLog(shader);
            _shaderError = $"{stage} shader compile failed for {GlVersion}: {infoLog}";
            Console.WriteLine(_shaderError);
            return false;
        }
        return true;
    }

    private bool CheckProgramLinkError(uint program)
    {
        _gl!.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(program);
            _shaderError = $"Shader link failed for {GlVersion}: {infoLog}";
            Console.WriteLine(_shaderError);
            return false;
        }
        _shaderError = null;
        return true;
    }

    private unsafe void LoadModel()
    {
        try
        {
            if (_gl == null || _assimp == null) return;
            if (Source == null || Source.ObjStream == null)
            {
                Console.WriteLine("Model source or ObjStream is null");
                return;
            }

            var parsedMaterials = ParseMtlStream(Source.MtlStream);

            byte[] objBytes;
            using (var memoryStream = new MemoryStream())
            {
                Source.ObjStream.Position = 0;
                Source.ObjStream.CopyTo(memoryStream);
                objBytes = memoryStream.ToArray();
            }

            Silk.NET.Assimp.Scene* scene;
            fixed (byte* pObj = objBytes)
            {
                byte[] hint = System.Text.Encoding.UTF8.GetBytes("obj\0");
                fixed (byte* pHint = hint)
                {
                    scene = _assimp.ImportFileFromMemory(pObj, (uint)objBytes.Length, (uint)(PostProcessSteps.Triangulate | PostProcessSteps.GenerateNormals | PostProcessSteps.FlipUVs), pHint);
                }
            }

            if (scene == null || scene->MFlags == Silk.NET.Assimp.Assimp.SceneFlagsIncomplete || scene->MRootNode == null)
            {
                Console.WriteLine($"Error loading model: {_assimp.GetErrorStringS()}");
                return;
            }

            if (_vao != 0) _gl.DeleteVertexArray(_vao);
            if (_vbo != 0) _gl.DeleteBuffer(_vbo);
            if (_ebo != 0) _gl.DeleteBuffer(_ebo);

            minX = float.MaxValue; minY = float.MaxValue; minZ = float.MaxValue;
            maxX = float.MinValue; maxY = float.MinValue; maxZ = float.MinValue;

            var vertices = new List<VertexData>();
            var indices = new List<ushort>();

            ProcessNode(scene->MRootNode, scene, vertices, indices, parsedMaterials);

            foreach (var v in vertices)
            {
                if (v.Position.X < minX) minX = v.Position.X;
                if (v.Position.Y < minY) minY = v.Position.Y;
                if (v.Position.Z < minZ) minZ = v.Position.Z;
                if (v.Position.X > maxX) maxX = v.Position.X;
                if (v.Position.Y > maxY) maxY = v.Position.Y;
                if (v.Position.Z > maxZ) maxZ = v.Position.Z;
            }

            _indexCount = indices.Count;
            Console.WriteLine($"Model loaded successfully. Vertices: {vertices.Count}, Indices: {_indexCount}");

            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();
            _ebo = _gl.GenBuffer();

            _gl.BindVertexArray(_vao);

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            var verticesArray = vertices.ToArray();
            fixed (VertexData* v = verticesArray)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verticesArray.Length * sizeof(VertexData)), v, BufferUsageARB.StaticDraw);
            }

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            var indicesArray = indices.ToArray();
            fixed (ushort* i = indicesArray)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indicesArray.Length * sizeof(ushort)), i, BufferUsageARB.StaticDraw);
            }

            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(VertexData), (void*)0);
            _gl.EnableVertexAttribArray(0);

            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(VertexData), (void*)Marshal.OffsetOf<VertexData>(nameof(VertexData.Normal)));
            _gl.EnableVertexAttribArray(1);

            _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)sizeof(VertexData), (void*)Marshal.OffsetOf<VertexData>(nameof(VertexData.TexCoords)));
            _gl.EnableVertexAttribArray(2);

            _gl.BindVertexArray(0);
            _assimp.ReleaseImport(scene);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception in LoadModel: {ex}");
        }
    }

    private Dictionary<string, Vector3> ParseMtlStream(Stream mtlStream)
    {
        var materials = new Dictionary<string, Vector3>();
        if (mtlStream == null) return materials;

        try
        {
            mtlStream.Position = 0;
            using var reader = new StreamReader(mtlStream, leaveOpen: true);
            string currentMaterial = "";
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts[0] == "newmtl" && parts.Length > 1)
                {
                    currentMaterial = parts[1];
                }
                else if (parts[0] == "Kd" && parts.Length >= 4 && !string.IsNullOrEmpty(currentMaterial))
                {
                    if (float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float r) &&
                        float.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float g) &&
                        float.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float b))
                    {
                        materials[currentMaterial] = new Vector3(r, g, b);
                    }
                }
            }
            mtlStream.Position = 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing MTL stream: {ex}");
        }
        return materials;
    }

    private unsafe void ProcessNode(Node* node, Scene* scene, List<VertexData> vertices, List<ushort> indices, Dictionary<string, Vector3> parsedMaterials)
    {
        for (uint i = 0; i < node->MNumMeshes; i++)
        {
            Mesh* mesh = scene->MMeshes[node->MMeshes[i]];
            ProcessMesh(mesh, scene, vertices, indices, parsedMaterials);
        }

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            ProcessNode(node->MChildren[i], scene, vertices, indices, parsedMaterials);
        }
    }

    private unsafe void ProcessMesh(Mesh* mesh, Scene* scene, List<VertexData> vertices, List<ushort> indices, Dictionary<string, Vector3> parsedMaterials)
    {
        var startIndex = (uint)vertices.Count;

        Vector3 diffuseColor = new Vector3(0.8f, 0.8f, 0.8f);
        if (mesh->MMaterialIndex >= 0)
        {
            var material = scene->MMaterials[mesh->MMaterialIndex];
            AssimpString name = new AssimpString();
            _assimp?.GetMaterialString(material, Silk.NET.Assimp.Assimp.MatkeyName, 0, 0, ref name);

            string matName = name.AsString;
            if (parsedMaterials.TryGetValue(matName, out Vector3 parsedColor))
            {
                diffuseColor = parsedColor;
            }
            else
            {
                Vector4 color = new Vector4(1, 1, 1, 1);
                if (_assimp?.GetMaterialColor(material, Silk.NET.Assimp.Assimp.MatkeyColorDiffuse, 0, 0, ref color) == Return.Success)
                {
                    diffuseColor = new Vector3(color.X, color.Y, color.Z);
                }
            }
        }

        for (uint i = 0; i < mesh->MNumVertices; i++)
        {
            var vertex = new VertexData();
            vertex.Position = new Vector3(mesh->MVertices[i].X, mesh->MVertices[i].Y, mesh->MVertices[i].Z);

            if (mesh->MNormals != null)
            {
                vertex.Normal = new Vector3(mesh->MNormals[i].X, mesh->MNormals[i].Y, mesh->MNormals[i].Z);
            }

            if (mesh->MTextureCoords[0] != null)
            {
                vertex.TexCoords = new Vector2(mesh->MTextureCoords[0][i].X, mesh->MTextureCoords[0][i].Y);
            }
            else
            {
                vertex.TexCoords = Vector2.Zero;
            }

            vertex.Color = diffuseColor;

            vertices.Add(vertex);
        }

        for (uint i = 0; i < mesh->MNumFaces; i++)
        {
            var face = mesh->MFaces[i];
            for (uint j = 0; j < face.MNumIndices; j++)
            {
                indices.Add((ushort)(startIndex + face.MIndices[j]));
            }
        }
    }

    private float _time = 0.0f;
    private DispatcherTimer? _timer;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (sender, args) =>
        {
            RequestNextFrameRendering();
        };
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl == null) return;

        if (_needsLoad)
        {
            LoadModel();
            _needsLoad = false;
        }

        if (_indexCount == 0) return;

        var scaling = Avalonia.Controls.TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int w = (int)(Bounds.Width * scaling);
        int h = (int)(Bounds.Height * scaling);

        if (w <= 0 || h <= 0) return;
        if (_shaderProgram == 0) return;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
        _gl.Enable(EnableCap.DepthTest);

        var isDark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        if (isDark)
        {
            //_gl.ClearColor(0.12f, 0.12f, 0.15f, 1.0f); // Dark theme background
            _gl.ClearColor(0.086f, 0.086f, 0.102f, 1.0f);
        }
        else
        {
            //_gl.ClearColor(0.9f, 0.9f, 0.92f, 1.0f); // Light theme background
            _gl.ClearColor(1.0f, 1.0f, 1.0f, 1.0f);
        }

        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        _gl.Viewport(0, 0, (uint)w, (uint)h);

        _gl.UseProgram(_shaderProgram);

        _time += 0.016f;

        var model = Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(0, -1, 0);

        if (_targetCameraRotation.HasValue)
        {
            _animationProgress += 0.05f;
            if (_animationProgress >= 1f)
            {
                _cameraRotation = _targetCameraRotation.Value;
                _targetCameraRotation = null;
            }
            else
            {
                float t = _animationProgress;
                t = t * t * (3f - 2f * t); // SmoothStep
                _cameraRotation = Quaternion.Slerp(_startCameraRotation, _targetCameraRotation.Value, t);
            }
            CameraRotation = _cameraRotation; // Sync property
            Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
        }

        var cameraRotationMatrix = Matrix4x4.CreateFromQuaternion(_cameraRotation);
        var cameraDirection = Vector3.Transform(Vector3.UnitZ, cameraRotationMatrix);
        var cameraPos = _cameraTarget + cameraDirection * _cameraDistance;
        var cameraUp = Vector3.Transform(Vector3.UnitY, cameraRotationMatrix);

        var view = Matrix4x4.CreateLookAt(cameraPos, _cameraTarget, cameraUp);
        var aspect = h == 0 ? 1.0f : (float)w / h;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView((float)(Math.PI / 4.0), aspect, 0.1f, 100.0f);

        var modelLoc = _gl.GetUniformLocation(_shaderProgram, "model");
        _gl.UniformMatrix4(modelLoc, 1, false, (float*)&model);

        var viewLoc = _gl.GetUniformLocation(_shaderProgram, "view");
        _gl.UniformMatrix4(viewLoc, 1, false, (float*)&view);

        var projLoc = _gl.GetUniformLocation(_shaderProgram, "projection");
        _gl.UniformMatrix4(projLoc, 1, false, (float*)&projection);

        var lightColor = new Vector3(0.75f, 0.75f, 0.75f); // Reduced brightness
        var lightPos = cameraPos; // Light moves with the camera (Headlamp effect)

        var lightColorLoc = _gl.GetUniformLocation(_shaderProgram, "lightColor");
        _gl.Uniform3(lightColorLoc, lightColor.X, lightColor.Y, lightColor.Z);

        var lightPosLoc = _gl.GetUniformLocation(_shaderProgram, "lightPos");
        _gl.Uniform3(lightPosLoc, lightPos.X, lightPos.Y, lightPos.Z);

        _gl.BindVertexArray(_vao); // Fallback to VAO

        // Explicitly bind buffers and set attribute pointers in case VAO state is lost or context differs
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(VertexData), (void*)0);
        _gl.EnableVertexAttribArray(0);

        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)sizeof(VertexData), (void*)Marshal.OffsetOf<VertexData>(nameof(VertexData.Normal)));
        _gl.EnableVertexAttribArray(1);

        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, (uint)sizeof(VertexData), (void*)Marshal.OffsetOf<VertexData>(nameof(VertexData.TexCoords)));
        _gl.EnableVertexAttribArray(2);

        _gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, (uint)sizeof(VertexData), (void*)Marshal.OffsetOf<VertexData>(nameof(VertexData.Color)));
        _gl.EnableVertexAttribArray(3);

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

        _gl.DrawElements(Silk.NET.OpenGL.PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedShort, (void*)0);

        _gl.BindVertexArray(0);

        // Clean up GL state so Skia can render 2D UI properly
        _gl.Disable(EnableCap.DepthTest);
        _gl.UseProgram(0);

        var err = _gl.GetError();
        if (err != GLEnum.NoError)
        {
            Console.WriteLine($"GL Error during render: {err}");
        }

        RequestNextFrameRendering();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Fill a transparent rectangle to ensure Avalonia hit-testing registers mouse events on this control
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus(); // Ensure it gets focus for scroll events
        var point = e.GetCurrentPoint(this);

        _targetCameraRotation = null; // Stop animation if user clicks elsewhere

        if (point.Properties.IsLeftButtonPressed) _isDraggingLeft = true;
        if (point.Properties.IsMiddleButtonPressed) _isDraggingMiddle = true;
        _lastMousePos = point.Position;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left) _isDraggingLeft = false;
        if (e.InitialPressMouseButton == MouseButton.Middle) _isDraggingMiddle = false;
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetCurrentPoint(this);
        var pos = point.Position;
        var deltaX = (float)(pos.X - _lastMousePos.X);
        var deltaY = (float)(pos.Y - _lastMousePos.Y);

        if (_isDraggingLeft)
        {
            var right = Vector3.Transform(Vector3.UnitX, _cameraRotation);
            var up = Vector3.Transform(Vector3.UnitY, _cameraRotation);

            var pitchQ = Quaternion.CreateFromAxisAngle(right, -deltaY * 0.01f);
            var yawQ = Quaternion.CreateFromAxisAngle(up, -deltaX * 0.01f);

            _cameraRotation = Quaternion.Normalize(yawQ * pitchQ * _cameraRotation);
            CameraRotation = _cameraRotation; // Sync property
            e.Handled = true;
            RequestNextFrameRendering();
        }
        else if (_isDraggingMiddle)
        {
            var panSpeed = _cameraDistance * 0.001f;
            var rotation = Matrix4x4.CreateFromQuaternion(_cameraRotation);
            var cameraDirection = Vector3.Transform(Vector3.UnitZ, rotation);
            var cameraUp = Vector3.Transform(Vector3.UnitY, rotation);
            var cameraRight = Vector3.Normalize(Vector3.Cross(cameraUp, cameraDirection));

            _cameraTarget += cameraRight * (-deltaX * panSpeed);
            _cameraTarget += cameraUp * (deltaY * panSpeed);
            e.Handled = true;
        }

        _lastMousePos = pos;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        _cameraDistance -= (float)e.Delta.Y * 0.5f;
        if (_cameraDistance < 0.1f) _cameraDistance = 0.1f;
        e.Handled = true;
    }
}

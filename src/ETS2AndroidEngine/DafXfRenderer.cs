
using System;
using System.Collections.Generic;
using Android.Opengl;
using Java.Nio;
using Javax.Microedition.Khronos.Opengles;
using EGLConfig = Javax.Microedition.Khronos.Egl.EGLConfig;

namespace ETS2AndroidEngine;

public sealed class DafXfRenderer :
    Java.Lang.Object, GLSurfaceView.IRenderer
{
    private const string VertexShader = @"
        attribute vec3 aPosition;
        uniform mat4 uMvp;

        void main()
        {
            gl_Position = uMvp * vec4(aPosition, 1.0);
        }";

    private const string FragmentShader = @"
        precision mediump float;

        void main()
        {
            gl_FragColor = vec4(0.72, 0.76, 0.82, 1.0);
        }";

    private readonly PmgV20Model _model;
    private readonly List<PieceBuffer> _buffers = new();

    private int _program;
    private int _positionHandle;
    private int _mvpHandle;

    private readonly float[] _projection = new float[16];
    private readonly float[] _view = new float[16];
    private readonly float[] _modelMatrix = new float[16];
    private readonly float[] _mv = new float[16];
    private readonly float[] _mvp = new float[16];

    private float _centerX, _centerY, _centerZ;
    private float _scale = 1f;
    private float _rotation;

    private sealed class PieceBuffer
    {
        public FloatBuffer Vertices = null!;
        public ShortBuffer Indices = null!;
        public int IndexCount;
    }

    public DafXfRenderer(
        PmgV20Model model,
        DecodedDdsTexture color,
        DecodedDdsTexture glass,
        DecodedDdsTexture lightmask,
        DecodedDdsTexture environment)
    {
        _model = model ??
            throw new ArgumentNullException(nameof(model));

        CalculateBounds();
    }

    private void CalculateBounds()
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float minZ = float.MaxValue;

        float maxX = float.MinValue;
        float maxY = float.MinValue;
        float maxZ = float.MinValue;

        foreach (var piece in _model.Pieces)
        {
            foreach (var vertex in piece.Vertices)
            {
                if (!vertex.HasPosition)
                    continue;

                var p = vertex.Position;

                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                minZ = Math.Min(minZ, p.Z);

                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
                maxZ = Math.Max(maxZ, p.Z);
            }
        }

        if (minX == float.MaxValue)
            throw new InvalidOperationException(
                "Model contains no positions.");

        _centerX = (minX + maxX) * 0.5f;
        _centerY = (minY + maxY) * 0.5f;
        _centerZ = (minZ + maxZ) * 0.5f;

        float largest = Math.Max(
            maxX - minX,
            Math.Max(maxY - minY, maxZ - minZ));

        _scale = largest > 0 ? 2f / largest : 1f;
    }

    public void OnSurfaceCreated(
        IGL10? gl,
        EGLConfig? config)
    {
        GLES20.GlClearColor(
            0.08f, 0.09f, 0.12f, 1f);

        GLES20.GlEnable(GLES20.GlDepthTest);

        _program = CreateProgram();

        _positionHandle =
            GLES20.GlGetAttribLocation(
                _program, "aPosition");

        _mvpHandle =
            GLES20.GlGetUniformLocation(
                _program, "uMvp");

        BuildBuffers();
    }

    private void BuildBuffers()
    {
        _buffers.Clear();

        foreach (var piece in _model.Pieces)
        {
            if (piece.Vertices.Length == 0 ||
                piece.Indices.Length == 0)
                continue;

            // Ignore shadow-only materials for now.
            if (piece.MaterialIndex == 1 ||
                piece.MaterialIndex == 5)
                continue;

            var positions =
                new float[piece.Vertices.Length * 3];

            for (int i = 0; i < piece.Vertices.Length; i++)
            {
                var p = piece.Vertices[i].Position;

                positions[i * 3] = p.X;
                positions[i * 3 + 1] = p.Y;
                positions[i * 3 + 2] = p.Z;
            }

            var indices =
                new short[piece.Indices.Length];

            for (int i = 0; i < indices.Length; i++)
                indices[i] =
                    unchecked((short)piece.Indices[i]);

            var vertexBuffer = ByteBuffer
                .AllocateDirect(positions.Length * 4)
                .Order(ByteOrder.NativeOrder()!)
                .AsFloatBuffer();

            vertexBuffer.Put(positions);
            vertexBuffer.Position(0);

            var indexBuffer = ByteBuffer
                .AllocateDirect(indices.Length * 2)
                .Order(ByteOrder.NativeOrder()!)
                .AsShortBuffer();

            indexBuffer.Put(indices);
            indexBuffer.Position(0);

            _buffers.Add(new PieceBuffer
            {
                Vertices = vertexBuffer,
                Indices = indexBuffer,
                IndexCount = indices.Length
            });
        }
    }

    public void OnSurfaceChanged(
        IGL10? gl,
        int width,
        int height)
    {
        if (height <= 0)
            height = 1;

        GLES20.GlViewport(0, 0, width, height);

        Matrix.PerspectiveM(
            _projection, 0,
            45f,
            (float)width / height,
            0.1f, 100f);

        Matrix.SetLookAtM(
            _view, 0,
            0f, 0f, 4.2f,
            0f, -0.15f, 0f,
            0f, 1f, 0f);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        GLES20.GlClear(
            GLES20.GlColorBufferBit |
            GLES20.GlDepthBufferBit);

        if (_program == 0 || _buffers.Count == 0)
            return;

        _rotation += 0.35f;

        Matrix.SetIdentityM(_modelMatrix, 0);

        Matrix.RotateM(
            _modelMatrix, 0,
            _rotation, 0f, 1f, 0f);

        Matrix.ScaleM(
            _modelMatrix, 0,
            _scale, _scale, _scale);

        Matrix.TranslateM(
            _modelMatrix, 0,
            -_centerX, -_centerY, -_centerZ);

        Matrix.MultiplyMM(
            _mv, 0,
            _view, 0,
            _modelMatrix, 0);

        Matrix.MultiplyMM(
            _mvp, 0,
            _projection, 0,
            _mv, 0);

        GLES20.GlUseProgram(_program);

        GLES20.GlUniformMatrix4fv(
            _mvpHandle, 1,
            false, _mvp, 0);

        GLES20.GlEnableVertexAttribArray(
            _positionHandle);

        foreach (var piece in _buffers)
        {
            piece.Vertices.Position(0);
            piece.Indices.Position(0);

            GLES20.GlVertexAttribPointer(
                _positionHandle,
                3,
                GLES20.GlFloat,
                false,
                0,
                piece.Vertices);

            GLES20.GlDrawElements(
                GLES20.GlTriangles,
                piece.IndexCount,
                GLES20.GlUnsignedShort,
                piece.Indices);
        }

        GLES20.GlDisableVertexAttribArray(
            _positionHandle);

        GLES20.GlUseProgram(0);
    }

    private static int CreateProgram()
    {
        int vertex = CompileShader(
            GLES20.GlVertexShader,
            VertexShader);

        int fragment = CompileShader(
            GLES20.GlFragmentShader,
            FragmentShader);

        int program = GLES20.GlCreateProgram();

        GLES20.GlAttachShader(program, vertex);
        GLES20.GlAttachShader(program, fragment);
        GLES20.GlLinkProgram(program);

        var status = new int[1];

        GLES20.GlGetProgramiv(
            program,
            GLES20.GlLinkStatus,
            status,
            0);

        if (status[0] == 0)
            throw new InvalidOperationException(
                GLES20.GlGetProgramInfoLog(program));

        GLES20.GlDeleteShader(vertex);
        GLES20.GlDeleteShader(fragment);

        return program;
    }

    private static int CompileShader(
        int type,
        string source)
    {
        int shader = GLES20.GlCreateShader(type);

        GLES20.GlShaderSource(shader, source);
        GLES20.GlCompileShader(shader);

        var status = new int[1];

        GLES20.GlGetShaderiv(
            shader,
            GLES20.GlCompileStatus,
            status,
            0);

        if (status[0] == 0)
            throw new InvalidOperationException(
                GLES20.GlGetShaderInfoLog(shader));

        return shader;
    }
}

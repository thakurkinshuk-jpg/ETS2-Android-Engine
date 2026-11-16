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
        attribute vec2 aTexCoord;

        uniform mat4 uMvp;

        varying vec2 vTexCoord;

        void main()
        {
            gl_Position = uMvp * vec4(aPosition, 1.0);
            vTexCoord = aTexCoord;
        }";

    private const string FragmentShader = @"
        precision mediump float;

        uniform vec4 uColor;
        uniform sampler2D uTexture;
        uniform float uUseTexture;

        varying vec2 vTexCoord;

        void main()
        {
            vec4 texColor = texture2D(uTexture, vTexCoord);
            vec4 finalColor = mix(uColor, texColor, uUseTexture);
            gl_FragColor = finalColor;
        }";

    private readonly PmgV20Model _model;
    private readonly DecodedDdsTexture _texture;
    private readonly List<PieceBuffer> _buffers = new();

    private int _program;
    private int _positionHandle;
    private int _texCoordHandle;
    private int _mvpHandle;
    private int _colorHandle;
    private int _textureHandle;
    private int _useTextureHandle;
    private int _glTexture;

    private readonly float[] _projection = new float[16];
    private readonly float[] _view = new float[16];
    private readonly float[] _modelMatrix = new float[16];
    private readonly float[] _mv = new float[16];
    private readonly float[] _mvp = new float[16];

    private float _centerX;
    private float _centerY;
    private float _centerZ;
    private float _scale = 1f;
    private float _rotation;

    private sealed class PieceBuffer
    {
        public FloatBuffer Vertices = null!;
        public FloatBuffer TexCoords = null!;
        public ShortBuffer Indices = null!;

        public int IndexCount;
        public int MaterialIndex;
        public bool HasTextureCoordinates;
    }

    public DafXfRenderer(
        PmgV20Model model,
        DecodedDdsTexture texture)
    {
        _model = model ??
            throw new ArgumentNullException(nameof(model));

        _texture = texture ??
            throw new ArgumentNullException(nameof(texture));

        CalculateModelBounds();
    }

    private void CalculateModelBounds()
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
                "Model has no positions.");

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

        _program = CreateProgram(
            VertexShader,
            FragmentShader);

        _positionHandle =
            GLES20.GlGetAttribLocation(
                _program, "aPosition");

        _texCoordHandle =
            GLES20.GlGetAttribLocation(
                _program, "aTexCoord");

        _mvpHandle =
            GLES20.GlGetUniformLocation(
                _program, "uMvp");

        _colorHandle =
            GLES20.GlGetUniformLocation(
                _program, "uColor");

        _textureHandle =
            GLES20.GlGetUniformLocation(
                _program, "uTexture");

        _useTextureHandle =
            GLES20.GlGetUniformLocation(
                _program, "uUseTexture");

        BuildBuffers();
        UploadTexture();
    }

    private void BuildBuffers()
    {
        _buffers.Clear();

        foreach (var piece in _model.Pieces)
        {
            if (piece.Vertices.Length == 0 ||
                piece.Indices.Length == 0)
                continue;

            // Skip shadow-only and fake-shadow materials.
            if (piece.MaterialIndex == 1 ||
                piece.MaterialIndex == 5)
                continue;

            var positions =
                new float[piece.Vertices.Length * 3];

            var texCoords =
                new float[piece.Vertices.Length * 2];

            bool hasUv = piece.TexCoordWidth > 0;

            for (int i = 0; i < piece.Vertices.Length; i++)
            {
                var vertex = piece.Vertices[i];
                var p = vertex.Position;

                positions[i * 3] = p.X;
                positions[i * 3 + 1] = p.Y;
                positions[i * 3 + 2] = p.Z;

                // Use UV0 for the base texture.
                // UV1 and UV2 remain available for future
                // material-specific shader support.
                if (vertex.TextureCoordinates.Length > 0)
                {
                    var uv = vertex.TextureCoordinates[0];

                    texCoords[i * 2] = uv.X;
                    texCoords[i * 2 + 1] = 1f - uv.Y;
                }
                else
                {
                    hasUv = false;
                }
            }

            var indexData =
                new short[piece.Indices.Length];

            for (int i = 0; i < piece.Indices.Length; i++)
            {
                indexData[i] =
                    unchecked((short)piece.Indices[i]);
            }

            var vertexBuffer = ByteBuffer
                .AllocateDirect(positions.Length * 4)
                .Order(ByteOrder.NativeOrder()!)
                .AsFloatBuffer();

            vertexBuffer.Put(positions);
            vertexBuffer.Position(0);

            var uvBuffer = ByteBuffer
                .AllocateDirect(texCoords.Length * 4)
                .Order(ByteOrder.NativeOrder()!)
                .AsFloatBuffer();

            uvBuffer.Put(texCoords);
            uvBuffer.Position(0);

            var indexBuffer = ByteBuffer
                .AllocateDirect(indexData.Length * 2)
                .Order(ByteOrder.NativeOrder()!)
                .AsShortBuffer();

            indexBuffer.Put(indexData);
            indexBuffer.Position(0);

            _buffers.Add(new PieceBuffer
            {
                Vertices = vertexBuffer,
                TexCoords = uvBuffer,
                Indices = indexBuffer,
                IndexCount = indexData.Length,
                MaterialIndex = piece.MaterialIndex,
                HasTextureCoordinates = hasUv
            });
        }
    }

    private void UploadTexture()
    {
        var pixels = ByteBuffer
            .AllocateDirect(_texture.Rgba.Length);

        pixels.Put(_texture.Rgba);
        pixels.Position(0);

        var textureIds = new int[1];

        GLES20.GlGenTextures(1, textureIds, 0);

        _glTexture = textureIds[0];

        GLES20.GlBindTexture(
            GLES20.GlTexture2d,
            _glTexture);

        GLES20.GlPixelStorei(
            GLES20.GlUnpackAlignment,
            1);

        GLES20.GlTexParameteri(
            GLES20.GlTexture2d,
            GLES20.GlTextureMinFilter,
            GLES20.GlLinear);

        GLES20.GlTexParameteri(
            GLES20.GlTexture2d,
            GLES20.GlTextureMagFilter,
            GLES20.GlLinear);

        // Repeat texture coordinates outside the 0-1 range.
        GLES20.GlTexParameteri(
            GLES20.GlTexture2d,
            GLES20.GlTextureWrapS,
            GLES20.GlRepeat);

        GLES20.GlTexParameteri(
            GLES20.GlTexture2d,
            GLES20.GlTextureWrapT,
            GLES20.GlRepeat);

        GLES20.GlTexImage2D(
            GLES20.GlTexture2d,
            0,
            GLES20.GlRgba,
            _texture.Width,
            _texture.Height,
            0,
            GLES20.GlRgba,
            GLES20.GlUnsignedByte,
            pixels);

        GLES20.GlBindTexture(
            GLES20.GlTexture2d,
            0);
    }

    public void OnSurfaceChanged(
        IGL10? gl,
        int width,
        int height)
    {
        if (height <= 0)
            height = 1;

        GLES20.GlViewport(
            0, 0, width, height);

        Matrix.PerspectiveM(
            _projection,
            0,
            45f,
            (float)width / height,
            0.1f,
            100f);

        Matrix.SetLookAtM(
            _view,
            0,
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

        Matrix.SetIdentityM(
            _modelMatrix, 0);

        Matrix.RotateM(
            _modelMatrix,
            0,
            _rotation,
            0f, 1f, 0f);

        Matrix.ScaleM(
            _modelMatrix,
            0,
            _scale, _scale, _scale);

        Matrix.TranslateM(
            _modelMatrix,
            0,
            -_centerX,
            -_centerY,
            -_centerZ);

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
            _mvpHandle,
            1,
            false,
            _mvp,
            0);

        GLES20.GlActiveTexture(
            GLES20.GlTexture0);

        GLES20.GlBindTexture(
            GLES20.GlTexture2d,
            _glTexture);

        GLES20.GlUniform1i(
            _textureHandle,
            0);

        GLES20.GlEnableVertexAttribArray(
            _positionHandle);

        GLES20.GlEnableVertexAttribArray(
            _texCoordHandle);

        foreach (var piece in _buffers)
        {
            piece.Vertices.Position(0);
            piece.TexCoords.Position(0);
            piece.Indices.Position(0);

            GLES20.GlVertexAttribPointer(
                _positionHandle,
                3,
                GLES20.GlFloat,
                false,
                0,
                piece.Vertices);

            GLES20.GlVertexAttribPointer(
                _texCoordHandle,
                2,
                GLES20.GlFloat,
                false,
                0,
                piece.TexCoords);

            bool useTexture =
                piece.HasTextureCoordinates &&
                piece.MaterialIndex != 8;

            if (piece.MaterialIndex == 8)
            {
                // Temporary glass tint.
                GLES20.GlUniform4f(
                    _colorHandle,
                    0.55f, 0.78f, 0.72f, 1f);
            }
            else
            {
                GLES20.GlUniform4f(
                    _colorHandle,
                    0.72f, 0.74f, 0.77f, 1f);
            }

            GLES20.GlUniform1f(
                _useTextureHandle,
                useTexture ? 1f : 0f);

            GLES20.GlDrawElements(
                GLES20.GlTriangles,
                piece.IndexCount,
                GLES20.GlUnsignedShort,
                piece.Indices);
        }

        GLES20.GlDisableVertexAttribArray(
            _positionHandle);

        GLES20.GlDisableVertexAttribArray(
            _texCoordHandle);

        GLES20.GlBindTexture(
            GLES20.GlTexture2d,
            0);
    }

    private static int CreateProgram(
        string vertexSource,
        string fragmentSource)
    {
        int vertex = CompileShader(
            GLES20.GlVertexShader,
            vertexSource);

        int fragment = CompileShader(
            GLES20.GlFragmentShader,
            fragmentSource);

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
        {
            string error =
                GLES20.GlGetProgramInfoLog(program);

            throw new InvalidOperationException(
                "OpenGL program link failed: " + error);
        }

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
        {
            string error =
                GLES20.GlGetShaderInfoLog(shader);

            throw new InvalidOperationException(
                "OpenGL shader compilation failed: " + error);
        }

        return shader;
    }
}

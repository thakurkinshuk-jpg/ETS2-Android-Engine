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
        attribute vec3 aNormal;
        attribute vec2 aTexCoord;
        attribute vec2 aLightUv;

        uniform mat4 uMvp;
        uniform mat4 uModelView;

        varying vec2 vTexCoord;
        varying vec2 vLightUv;
        varying vec3 vEyePosition;
        varying vec3 vEyeNormal;

        void main()
        {
            vec4 eyePosition = uModelView * vec4(aPosition, 1.0);

            gl_Position = uMvp * vec4(aPosition, 1.0);

            vTexCoord = aTexCoord;
            vLightUv = aLightUv;
            vEyePosition = eyePosition.xyz;
            vEyeNormal = normalize(mat3(uModelView) * aNormal);
        }";

    private const string FragmentShader = @"
        precision mediump float;

        uniform sampler2D uTexture;
        uniform sampler2D uEnvironment;
        uniform sampler2D uLightmask;

        uniform float uIsGlass;
        uniform float uUseEnvironment;
        uniform float uUseLamp;
        uniform float uEnvironmentStrength;

        varying vec2 vTexCoord;
        varying vec2 vLightUv;
        varying vec3 vEyePosition;
        varying vec3 vEyeNormal;

        vec2 EnvironmentUv(vec3 direction)
        {
            float denominator =
                2.0 * sqrt(
                    direction.x * direction.x +
                    direction.y * direction.y +
                    (direction.z + 1.0) * (direction.z + 1.0)
                );

            if (denominator < 0.0001)
                return vec2(0.5, 0.5);

            return direction.xy / denominator + 0.5;
        }

        void main()
        {
            if (uIsGlass > 0.5)
            {
                gl_FragColor =
                    vec4(0.62, 0.78, 0.84, 0.22);
                return;
            }

            vec4 base =
                texture2D(uTexture, vTexCoord);

            vec3 normal =
                normalize(vEyeNormal);

            vec3 viewDirection =
                normalize(-vEyePosition);

            if (uUseLamp > 0.5)
            {
                vec4 mask =
                    texture2D(uLightmask, vLightUv);

                // Approximate lamp-mask contribution.
                base.rgb += base.rgb * mask.r * 0.65;
            }

            if (uUseEnvironment > 0.5)
            {
                vec3 reflected =
                    reflect(-viewDirection, normal);

                vec2 reflectionUv =
                    EnvironmentUv(reflected);

                vec3 environmentColor =
                    texture2D(
                        uEnvironment,
                        reflectionUv
                    ).rgb;

                float fresnel =
                    pow(
                        1.0 - max(
                            dot(normal, viewDirection),
                            0.0
                        ),
                        2.0
                    );

                float reflectionAmount =
                    uEnvironmentStrength *
                    (0.25 + 0.75 * fresnel);

                base.rgb = mix(
                    base.rgb,
                    environmentColor,
                    clamp(reflectionAmount, 0.0, 0.45)
                );
            }

            gl_FragColor = base;
        }";

    private readonly PmgV20Model _model;

    private readonly DecodedDdsTexture _colorTexture;
    private readonly DecodedDdsTexture _glassTexture;
    private readonly DecodedDdsTexture _lightmaskTexture;
    private readonly DecodedDdsTexture _environmentTexture;

    private readonly List<PieceBuffer> _buffers = new();

    private int _program;
    private int _positionHandle;
    private int _normalHandle;
    private int _texCoordHandle;
    private int _lightUvHandle;

    private int _mvpHandle;
    private int _modelViewHandle;

    private int _textureHandle;
    private int _environmentHandle;
    private int _lightmaskHandle;

    private int _glassHandle;
    private int _useEnvironmentHandle;
    private int _useLampHandle;
    private int _environmentStrengthHandle;

    private int _colorGlTexture;
    private int _glassGlTexture;
    private int _lightmaskGlTexture;
    private int _environmentGlTexture;

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
        public FloatBuffer Normals = null!;
        public FloatBuffer TexCoords = null!;
        public FloatBuffer LightUvs = null!;
        public ShortBuffer Indices = null!;

        public int IndexCount;
        public int MaterialIndex;
        public bool HasTextureCoordinates;
    }

    public DafXfRenderer(
        PmgV20Model model,
        DecodedDdsTexture colorTexture)
        : this(
            model,
            colorTexture,
            CreateFallbackTexture(),
            CreateFallbackTexture(),
            CreateFallbackTexture())
    {
    }

    public DafXfRenderer(
        PmgV20Model model,
        DecodedDdsTexture colorTexture,
        DecodedDdsTexture glassTexture)
        : this(
            model,
            colorTexture,
            glassTexture,
            CreateFallbackTexture(),
            CreateFallbackTexture())
    {
    }

    public DafXfRenderer(
        PmgV20Model model,
        DecodedDdsTexture colorTexture,
        DecodedDdsTexture glassTexture,
        DecodedDdsTexture lightmaskTexture,
        DecodedDdsTexture environmentTexture)
    {
        _model = model ??
            throw new ArgumentNullException(nameof(model));

        _colorTexture = colorTexture ??
            throw new ArgumentNullException(nameof(colorTexture));

        _glassTexture = glassTexture ??
            throw new ArgumentNullException(nameof(glassTexture));

        _lightmaskTexture = lightmaskTexture ??
            throw new ArgumentNullException(nameof(lightmaskTexture));

        _environmentTexture = environmentTexture ??
            throw new ArgumentNullException(nameof(environmentTexture));

        CalculateModelBounds();
    }

    private static DecodedDdsTexture CreateFallbackTexture()
    {
        return new DecodedDdsTexture
        {
            Width = 1,
            Height = 1,
            Rgba = new byte[] { 190, 225, 215, 255 }
        };
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
            GLES20.GlGetAttribLocation(_program, "aPosition");

        _normalHandle =
            GLES20.GlGetAttribLocation(_program, "aNormal");

        _texCoordHandle =
            GLES20.GlGetAttribLocation(_program, "aTexCoord");

        _lightUvHandle =
            GLES20.GlGetAttribLocation(_program, "aLightUv");

        _mvpHandle =
            GLES20.GlGetUniformLocation(_program, "uMvp");

        _modelViewHandle =
            GLES20.GlGetUniformLocation(_program, "uModelView");

        _textureHandle =
            GLES20.GlGetUniformLocation(_program, "uTexture");

        _environmentHandle =
            GLES20.GlGetUniformLocation(_program, "uEnvironment");

        _lightmaskHandle =
            GLES20.GlGetUniformLocation(_program, "uLightmask");

        _glassHandle =
            GLES20.GlGetUniformLocation(_program, "uIsGlass");

        _useEnvironmentHandle =
            GLES20.GlGetUniformLocation(_program, "uUseEnvironment");

        _useLampHandle =
            GLES20.GlGetUniformLocation(_program, "uUseLamp");

        _environmentStrengthHandle =
            GLES20.GlGetUniformLocation(
                _program, "uEnvironmentStrength");

        BuildBuffers();

        _colorGlTexture = UploadTexture(_colorTexture);
        _glassGlTexture = UploadTexture(_glassTexture);
        _lightmaskGlTexture = UploadTexture(_lightmaskTexture);
        _environmentGlTexture = UploadTexture(_environmentTexture);
    }

    private void BuildBuffers()
    {
        _buffers.Clear();

        foreach (var piece in _model.Pieces)
        {
            if (piece.Vertices.Length == 0 ||
                piece.Indices.Length == 0)
                continue;

            // Shadow-only and fake-shadow materials.
            if (piece.MaterialIndex == 1 ||
                piece.MaterialIndex == 5)
                continue;

            int count = piece.Vertices.Length;

            var positions = new float[count * 3];
            var normals = new float[count * 3];
            var texCoords = new float[count * 2];
            var lightUvs = new float[count * 2];

            bool hasUv = piece.TexCoordWidth > 0;

            for (int i = 0; i < count; i++)
            {
                var vertex = piece.Vertices[i];
                var p = vertex.Position;

                positions[i * 3] = p.X;
                positions[i * 3 + 1] = p.Y;
                positions[i * 3 + 2] = p.Z;

                if (vertex.HasNormal)
                {
                    normals[i * 3] = vertex.Normal.X;
                    normals[i * 3 + 1] = vertex.Normal.Y;
                    normals[i * 3 + 2] = vertex.Normal.Z;
                }
                else
                {
                    normals[i * 3 + 2] = 1f;
                }

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

                // The lamp shader's second UV channel is used
                // for its lightmask texture.
                if (vertex.TextureCoordinates.Length > 1)
                {
                    var uv1 = vertex.TextureCoordinates[1];

                    lightUvs[i * 2] = uv1.X;
                    lightUvs[i * 2 + 1] = 1f - uv1.Y;
                }
                else
                {
                    lightUvs[i * 2] = texCoords[i * 2];
                    lightUvs[i * 2 + 1] = texCoords[i * 2 + 1];
                }
            }

            var indexData = new short[piece.Indices.Length];

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

            var normalBuffer = ByteBuffer
                .AllocateDirect(normals.Length * 4)
                .Order(ByteOrder.NativeOrder()!)
                .AsFloatBuffer();

            normalBuffer.Put(normals);
            normalBuffer.Position(0);

            var uvBuffer = ByteBuffer
                .AllocateDirect(texCoords.Length * 4)
                .Order(ByteOrder.NativeOrder()!)
                .AsFloatBuffer();

            uvBuffer.Put(texCoords);
            uvBuffer.Position(0);

            var lightUvBuffer = ByteBuffer
                .AllocateDirect(lightUvs.Length * 4)
                .Order(ByteOrder.NativeOrder()!)
                .AsFloatBuffer();

            lightUvBuffer.Put(lightUvs);
            lightUvBuffer.Position(0);

            var indexBuffer = ByteBuffer
                .AllocateDirect(indexData.Length * 2)
                .Order(ByteOrder.NativeOrder()!)
                .AsShortBuffer();

            indexBuffer.Put(indexData);
            indexBuffer.Position(0);

            _buffers.Add(new PieceBuffer
            {
                Vertices = vertexBuffer,
                Normals = normalBuffer,
                TexCoords = uvBuffer,
                LightUvs = lightUvBuffer,
                Indices = indexBuffer,
                IndexCount = indexData.Length,
                MaterialIndex = piece.MaterialIndex,
                HasTextureCoordinates = hasUv
            });
        }
    }

    private static int UploadTexture(
        DecodedDdsTexture texture)
    {
        var pixels = ByteBuffer
            .AllocateDirect(texture.Rgba.Length);

        pixels.Put(texture.Rgba);
        pixels.Position(0);

        var textureIds = new int[1];

        GLES20.GlGenTextures(1, textureIds, 0);

        int textureId = textureIds[0];

        GLES20.GlBindTexture(
            GLES20.GlTexture2d,
            textureId);

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
            texture.Width,
            texture.Height,
            0,
            GLES20.GlRgba,
            GLES20.GlUnsignedByte,
            pixels);

        GLES20.GlBindTexture(
            GLES20.GlTexture2d,
            0);

        return textureId;
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

        Matrix.SetIdentityM(_modelMatrix, 0);

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
            _mvpHandle, 1, false, _mvp, 0);

        GLES20.GlUniformMatrix4fv(
            _modelViewHandle, 1, false, _mv, 0);

        GLES20.GlUniform1i(_textureHandle, 0);
        GLES20.GlUniform1i(_environmentHandle, 1);
        GLES20.GlUniform1i(_lightmaskHandle, 2);

        GLES20.GlActiveTexture(GLES20.GlTexture1);
        GLES20.GlBindTexture(
            GLES20.GlTexture2d,
            _environmentGlTexture);

        GLES20.GlActiveTexture(GLES20.GlTexture2);
        GLES20.GlBindTexture(
            GLES20.GlTexture2d,
            _lightmaskGlTexture);

        GLES20.GlActiveTexture(GLES20.GlTexture0);

        GLES20.GlEnableVertexAttribArray(_positionHandle);
        GLES20.GlEnableVertexAttribArray(_normalHandle);
        GLES20.GlEnableVertexAttribArray(_texCoordHandle);
        GLES20.GlEnableVertexAttribArray(_lightUvHandle);

        foreach (var piece in _buffers)
        {
            piece.Vertices.Position(0);
            piece.Normals.Position(0);
            piece.TexCoords.Position(0);
            piece.LightUvs.Position(0);
            piece.Indices.Position(0);

            GLES20.GlVertexAttribPointer(
                _positionHandle, 3,
                GLES20.GlFloat, false, 0,
                piece.Vertices);

            GLES20.GlVertexAttribPointer(
                _normalHandle, 3,
                GLES20.GlFloat, false, 0,
                piece.Normals);

            GLES20.GlVertexAttribPointer(
                _texCoordHandle, 2,
                GLES20.GlFloat, false, 0,
                piece.TexCoords);

            GLES20.GlVertexAttribPointer(
                _lightUvHandle, 2,
                GLES20.GlFloat, false, 0,
                piece.LightUvs);

            int material = piece.MaterialIndex;

            bool isGlass = material == 8;
            bool isLamp = material == 6;

            bool usesEnvironment =
                material == 3 ||
                material == 4 ||
                material == 6 ||
                material == 7;

            float environmentStrength = 0f;

            if (material == 3)
                environmentStrength = 0.336f;
            else if (material == 4)
                environmentStrength = 0.141f;
            else if (material == 6)
                environmentStrength = 0.12f;
            else if (material == 7)
                environmentStrength = 0.087f;

            int selectedTexture = isGlass
                ? _glassGlTexture
                : _colorGlTexture;

            GLES20.GlActiveTexture(GLES20.GlTexture0);

            GLES20.GlBindTexture(
                GLES20.GlTexture2d,
                selectedTexture);

            GLES20.GlUniform1f(
                _glassHandle,
                isGlass ? 1f : 0f);

            GLES20.GlUniform1f(
                _useEnvironmentHandle,
                usesEnvironment ? 1f : 0f);

            GLES20.GlUniform1f(
                _useLampHandle,
                isLamp ? 1f : 0f);

            GLES20.GlU

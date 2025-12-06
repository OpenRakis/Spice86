namespace Spice86.Views;

using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform.Interop;
using Avalonia.Threading;

using Spice86.ViewModels;

using System;
using System.Runtime.InteropServices;

using static Avalonia.OpenGL.GlConsts;

/// <summary>
/// OpenGL control for rendering VGA output with optional CRT shader effects
/// </summary>
public class OpenGlVideoControl : OpenGlControlBase {
    private int _vertexShader;
    private int _fragmentShader;
    private int _shaderProgram;
    private int _vbo;
    private int _texture;
    private int _width;
    private int _height;
    private uint[]? _frameBuffer;
    private bool _initialized;
    private CrtShaderType _currentShaderType = CrtShaderType.None;
    private string? _currentShaderSource;

    /// <summary>
    /// Property for the CRT shader type to use
    /// </summary>
    public static readonly StyledProperty<CrtShaderType> ShaderTypeProperty =
        AvaloniaProperty.Register<OpenGlVideoControl, CrtShaderType>(nameof(ShaderType), CrtShaderType.None);

    /// <summary>
    /// Gets or sets the CRT shader type
    /// </summary>
    public CrtShaderType ShaderType {
        get => GetValue(ShaderTypeProperty);
        set => SetValue(ShaderTypeProperty, value);
    }

    static OpenGlVideoControl() {
        AffectsRender<OpenGlVideoControl>(ShaderTypeProperty);
    }

    /// <summary>
    /// Updates the video buffer with new frame data
    /// </summary>
    /// <param name="buffer">The frame buffer to render</param>
    /// <param name="width">Width of the frame</param>
    /// <param name="height">Height of the frame</param>
    public void UpdateFrame(Span<uint> buffer, int width, int height) {
        if (width != _width || height != _height) {
            _width = width;
            _height = height;
            _frameBuffer = new uint[width * height];
        }

        if (_frameBuffer is not null && buffer.Length <= _frameBuffer.Length) {
            buffer.CopyTo(_frameBuffer);
            Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
        }
    }

    /// <summary>
    /// Render the frame with the selected shader
    /// </summary>
    /// <param name="gl">The OpenGL interface</param>
    /// <param name="fb">The framebuffer to render to</param>
    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb) {
        if (_frameBuffer is null || _width == 0 || _height == 0) {
            return;
        }

        if (!_initialized || _currentShaderType != ShaderType) {
            InitializeResources(gl);
            _currentShaderType = ShaderType;
        }

        gl.ClearColor(0, 0, 0, 1);
        gl.Clear(GL_COLOR_BUFFER_BIT);

        if (_shaderProgram == 0 || _texture == 0) {
            return;
        }

        // Update texture with new frame data
        gl.BindTexture(GL_TEXTURE_2D, _texture);
        fixed (void* pdata = _frameBuffer) {
            gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, _width, _height, 0, GL_RGBA, GL_UNSIGNED_BYTE, new IntPtr(pdata));
        }

        // Use shader program
        gl.UseProgram(_shaderProgram);

        // Set uniforms
        int rubyTextureLoc = gl.GetUniformLocationString(_shaderProgram, "rubyTexture");
        int rubyInputSizeLoc = gl.GetUniformLocationString(_shaderProgram, "rubyInputSize");
        int rubyTextureSizeLoc = gl.GetUniformLocationString(_shaderProgram, "rubyTextureSize");
        int rubyOutputSizeLoc = gl.GetUniformLocationString(_shaderProgram, "rubyOutputSize");

        gl.Uniform1i(rubyTextureLoc, 0);
        gl.Uniform2f(rubyInputSizeLoc, _width, _height);
        gl.Uniform2f(rubyTextureSizeLoc, _width, _height);
        
        float pixelWidth = (float)Bounds.Width;
        float pixelHeight = (float)Bounds.Height;
        gl.Uniform2f(rubyOutputSizeLoc, pixelWidth, pixelHeight);

        // Setup vertex attributes
        gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
        int posLoc = gl.GetAttribLocationString(_shaderProgram, "a_position");
        gl.EnableVertexAttribArray(posLoc);
        gl.VertexAttribPointer(posLoc, 2, GL_FLOAT, 0, 0, IntPtr.Zero);

        // Draw quad
        gl.DrawArrays(GL_TRIANGLE_STRIP, 0, 4);
        
        gl.DisableVertexAttribArray(posLoc);
    }

    /// <summary>
    /// Cleanup OpenGL resources
    /// </summary>
    /// <param name="gl">The OpenGL interface</param>
    protected override void OnOpenGlDeinit(GlInterface gl) {
        if (_vbo != 0) {
            gl.DeleteBuffer(_vbo);
        }
        if (_texture != 0) {
            gl.DeleteTexture(_texture);
        }
        if (_shaderProgram != 0) {
            gl.DeleteProgram(_shaderProgram);
        }
        if (_vertexShader != 0) {
            gl.DeleteShader(_vertexShader);
        }
        if (_fragmentShader != 0) {
            gl.DeleteShader(_fragmentShader);
        }

        base.OnOpenGlDeinit(gl);
    }

    private unsafe void InitializeResources(GlInterface gl) {
        // Clean up previous resources
        if (_initialized) {
            if (_vbo != 0) {
                gl.DeleteBuffer(_vbo);
                _vbo = 0;
            }
            if (_texture != 0) {
                gl.DeleteTexture(_texture);
                _texture = 0;
            }
            if (_shaderProgram != 0) {
                gl.DeleteProgram(_shaderProgram);
                _shaderProgram = 0;
            }
            if (_vertexShader != 0) {
                gl.DeleteShader(_vertexShader);
                _vertexShader = 0;
            }
            if (_fragmentShader != 0) {
                gl.DeleteShader(_fragmentShader);
                _fragmentShader = 0;
            }
        }

        // Load and compile shader
        string shaderSource = LoadShaderSource(ShaderType);
        if (!CompileShaders(gl, shaderSource)) {
            return;
        }

        // Create vertex buffer for a fullscreen quad
        float[] vertices = {
            -1.0f, -1.0f,
             1.0f, -1.0f,
            -1.0f,  1.0f,
             1.0f,  1.0f
        };

        _vbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);

        fixed (void* pdata = vertices) {
            gl.BufferData(GL_ARRAY_BUFFER, new IntPtr(vertices.Length * sizeof(float)),
                new IntPtr(pdata), GL_STATIC_DRAW);
        }

        // Create texture
        _texture = gl.GenTexture();
        gl.BindTexture(GL_TEXTURE_2D, _texture);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

        _initialized = true;
    }

    private bool CompileShaders(GlInterface gl, string shaderSource) {
        // Separate vertex and fragment shaders
        string vertexSource = ExtractShaderPart(shaderSource, true);
        string fragmentSource = ExtractShaderPart(shaderSource, false);

        // Compile vertex shader
        _vertexShader = gl.CreateShader(GL_VERTEX_SHADER);
        string? vertexError = gl.CompileShaderAndGetError(_vertexShader, vertexSource);
        if (!string.IsNullOrEmpty(vertexError)) {
            System.Diagnostics.Debug.WriteLine($"Vertex shader compilation failed: {vertexError}");
            return false;
        }

        // Compile fragment shader
        _fragmentShader = gl.CreateShader(GL_FRAGMENT_SHADER);
        string? fragmentError = gl.CompileShaderAndGetError(_fragmentShader, fragmentSource);
        if (!string.IsNullOrEmpty(fragmentError)) {
            System.Diagnostics.Debug.WriteLine($"Fragment shader compilation failed: {fragmentError}");
            return false;
        }

        // Link program
        _shaderProgram = gl.CreateProgram();
        gl.AttachShader(_shaderProgram, _vertexShader);
        gl.AttachShader(_shaderProgram, _fragmentShader);
        gl.BindAttribLocationString(_shaderProgram, 0, "a_position");
        string? linkError = gl.LinkProgramAndGetError(_shaderProgram);
        if (!string.IsNullOrEmpty(linkError)) {
            System.Diagnostics.Debug.WriteLine($"Program linking failed: {linkError}");
            return false;
        }

        return true;
    }

    private string ExtractShaderPart(string shaderSource, bool vertex) {
        string marker = vertex ? "#if defined(VERTEX)" : "#elif defined(FRAGMENT)";
        string endMarker = vertex ? "#elif defined(FRAGMENT)" : "#endif";
        
        int startIndex = shaderSource.IndexOf(marker, StringComparison.Ordinal);
        if (startIndex == -1) {
            return string.Empty;
        }

        startIndex += marker.Length;
        int endIndex = shaderSource.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        if (endIndex == -1) {
            endIndex = shaderSource.Length;
        }

        string extracted = shaderSource.Substring(startIndex, endIndex - startIndex);
        
        // Add the appropriate define
        return (vertex ? "#define VERTEX\n" : "#define FRAGMENT\n") + extracted;
    }

    private string LoadShaderSource(CrtShaderType shaderType) {
        if (_currentShaderSource is not null && _currentShaderType == shaderType) {
            return _currentShaderSource;
        }

        string resourceName = shaderType switch {
            CrtShaderType.FakeLottes => "fakelottes.glsl",
            CrtShaderType.EasyMode => "crt-easymode.glsl",
            CrtShaderType.CrtGeom => "crt-geom.glsl",
            _ => "passthrough.glsl"
        };

        string resourcePath = $"avares://Spice86/Views/Shaders/Crt/{resourceName}";
        
        try {
            using System.IO.Stream? stream = Avalonia.Platform.AssetLoader.Open(new Uri(resourcePath));
            if (stream is null) {
                return LoadPassthroughShader();
            }

            using System.IO.StreamReader reader = new(stream);
            _currentShaderSource = reader.ReadToEnd();
            return _currentShaderSource;
        } catch {
            return LoadPassthroughShader();
        }
    }

    private string LoadPassthroughShader() {
        // Fallback inline passthrough shader
        return @"#version 120
#if defined(VERTEX)
attribute vec4 a_position;
varying vec2 v_texCoord;
uniform vec2 rubyInputSize;
uniform vec2 rubyTextureSize;
void main() {
    gl_Position = a_position;
    v_texCoord = vec2(a_position.x + 1.0, 1.0 - a_position.y) / 2.0 * rubyInputSize / rubyTextureSize;
}
#elif defined(FRAGMENT)
varying vec2 v_texCoord;
uniform sampler2D rubyTexture;
void main() {
    gl_FragColor = texture2D(rubyTexture, v_texCoord);
}
#endif";
    }

}

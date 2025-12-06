namespace Spice86.Views;

using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

using Spice86.ViewModels;

using System;
using System.Runtime.InteropServices;

using static Avalonia.OpenGL.GlConsts;

/// <summary>
/// OpenGL control for rendering VGA output with CRT shader effects
/// </summary>
public class OpenGlVideoControl : OpenGlControlBase {
    // Additional GL constants not always available in GlConsts
    private const int GL_TRIANGLE_STRIP = 0x0005;
    private const int GL_CLAMP_TO_EDGE = 0x812F;
    private const int GL_TEXTURE_WRAP_S = 0x2802;
    private const int GL_TEXTURE_WRAP_T = 0x2803;
    
    private GlExtensions? _glExt;
    private int _vertexShader;
    private int _fragmentShader;
    private int _shaderProgram;
    private int _vao;
    private int _vbo;
    private int _texture;
    private int _width;
    private int _height;
    private uint[]? _frameBuffer;
    private bool _initialized;
    private CrtShaderType _currentShaderType = CrtShaderType.None;

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
    public void UpdateFrame(uint[] buffer, int width, int height) {
        if (width != _width || height != _height) {
            _width = width;
            _height = height;
            _frameBuffer = new uint[width * height];
        }

        if (_frameBuffer is not null && buffer.Length <= _frameBuffer.Length) {
            Array.Copy(buffer, _frameBuffer, buffer.Length);
            Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
        }
    }

    /// <summary>
    /// Initialize OpenGL resources
    /// </summary>
    /// <param name="gl">The OpenGL interface</param>
    protected override unsafe void OnOpenGlInit(GlInterface gl) {
        base.OnOpenGlInit(gl);
        _glExt = new GlExtensions(gl.GetProcAddress);
        _initialized = false;
    }

    /// <summary>
    /// Render the frame with the selected shader
    /// </summary>
    /// <param name="gl">The OpenGL interface</param>
    /// <param name="fb">The framebuffer to render to</param>
    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb) {
        if (_frameBuffer is null || _width == 0 || _height == 0) {
            gl.ClearColor(0, 0, 0, 1);
            gl.Clear(GL_COLOR_BUFFER_BIT);
            return;
        }

        if (!_initialized || _currentShaderType != ShaderType) {
            InitializeResources(gl);
            _currentShaderType = ShaderType;
        }

        gl.Viewport(0, 0, (int)Bounds.Width, (int)Bounds.Height);
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

        // Set uniforms - these are standard uniforms used by DOSBox Staging shaders
        if (_glExt is null) {
            return;
        }

        int rubyTextureLoc = gl.GetUniformLocationString(_shaderProgram, "rubyTexture");
        int rubyInputSizeLoc = gl.GetUniformLocationString(_shaderProgram, "rubyInputSize");
        int rubyTextureSizeLoc = gl.GetUniformLocationString(_shaderProgram, "rubyTextureSize");
        int rubyOutputSizeLoc = gl.GetUniformLocationString(_shaderProgram, "rubyOutputSize");

        // Set texture unit (sampler2D uniform - use integer for texture unit)
        if (rubyTextureLoc >= 0) {
            _glExt.Uniform1i(rubyTextureLoc, 0);
        }
        
        // Set size uniforms (vec2)
        if (rubyInputSizeLoc >= 0) {
            _glExt.Uniform2f(rubyInputSizeLoc, _width, _height);
        }
        if (rubyTextureSizeLoc >= 0) {
            _glExt.Uniform2f(rubyTextureSizeLoc, _width, _height);
        }
        if (rubyOutputSizeLoc >= 0) {
            float outputWidth = (float)Bounds.Width;
            float outputHeight = (float)Bounds.Height;
            _glExt.Uniform2f(rubyOutputSizeLoc, outputWidth, outputHeight);
        }

        // Setup vertex attributes and draw
        gl.BindVertexArray(_vao);
        gl.DrawArrays(GL_TRIANGLE_STRIP, 0, 4);
        gl.BindVertexArray(0);
    }

    /// <summary>
    /// Cleanup OpenGL resources
    /// </summary>
    /// <param name="gl">The OpenGL interface</param>
    protected override void OnOpenGlDeinit(GlInterface gl) {
        if (_vao != 0) {
            gl.DeleteVertexArray(_vao);
        }
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
            if (_vao != 0) {
                gl.DeleteVertexArray(_vao);
                _vao = 0;
            }
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
            System.Diagnostics.Debug.WriteLine("Failed to compile shaders, using passthrough");
            // Try fallback to passthrough shader
            if (!CompileShaders(gl, LoadPassthroughShader())) {
                return;
            }
        }

        // Create vertex array and buffer for a fullscreen quad
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        // Fullscreen quad vertices (position only, 2D)
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

        // Setup vertex attribute pointer for position
        int posLoc = gl.GetAttribLocationString(_shaderProgram, "a_position");
        if (posLoc >= 0) {
            gl.EnableVertexAttribArray(posLoc);
            gl.VertexAttribPointer(posLoc, 2, GL_FLOAT, 0, 0, IntPtr.Zero);
        }

        gl.BindVertexArray(0);

        // Create texture for the VGA framebuffer
        _texture = gl.GenTexture();
        gl.BindTexture(GL_TEXTURE_2D, _texture);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

        _initialized = true;
    }

    private bool CompileShaders(GlInterface gl, string shaderSource) {
        // Separate vertex and fragment shaders from the combined GLSL file
        string vertexSource = ExtractShaderPart(shaderSource, true);
        string fragmentSource = ExtractShaderPart(shaderSource, false);

        if (string.IsNullOrEmpty(vertexSource) || string.IsNullOrEmpty(fragmentSource)) {
            System.Diagnostics.Debug.WriteLine("Failed to extract shader parts");
            return false;
        }

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
        // DOSBox Staging shaders use conditional compilation with #if defined(VERTEX) and #if defined(FRAGMENT)
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
        
        // Add the appropriate define at the beginning
        return (vertex ? "#define VERTEX\n" : "#define FRAGMENT\n") + extracted;
    }

    private string LoadShaderSource(CrtShaderType shaderType) {
        string resourceName = shaderType switch {
            CrtShaderType.FakeLottes => "fakelottes.glsl",
            CrtShaderType.EasyMode => "crt-easymode.glsl",
            CrtShaderType.CrtGeom => "crt-geom.glsl",
            _ => "passthrough.glsl"
        };

        string resourcePath = $"avares://Spice86/Views/Shaders/Crt/{resourceName}";
        
        try {
            using System.IO.Stream? stream = AssetLoader.Open(new Uri(resourcePath));
            if (stream is null) {
                return LoadPassthroughShader();
            }

            using System.IO.StreamReader reader = new(stream);
            return reader.ReadToEnd();
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"Failed to load shader {resourceName}: {ex.Message}");
            return LoadPassthroughShader();
        }
    }

    private string LoadPassthroughShader() {
        // Inline passthrough shader that just renders the texture without effects
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

    /// <summary>
    /// Helper class to access additional OpenGL functions not wrapped by GlInterface
    /// </summary>
    private class GlExtensions {
        private delegate void Uniform1iDelegate(int location, int v0);
        private delegate void Uniform2fDelegate(int location, float v0, float v1);
        
        private readonly Uniform1iDelegate? _uniform1i;
        private readonly Uniform2fDelegate? _uniform2f;

        /// <summary>
        /// Initializes OpenGL extension functions
        /// </summary>
        /// <param name="getProcAddress">Function to get OpenGL procedure addresses</param>
        public GlExtensions(Func<string, IntPtr> getProcAddress) {
            IntPtr uniform1iPtr = getProcAddress("glUniform1i");
            if (uniform1iPtr != IntPtr.Zero) {
                _uniform1i = Marshal.GetDelegateForFunctionPointer<Uniform1iDelegate>(uniform1iPtr);
            }

            IntPtr uniform2fPtr = getProcAddress("glUniform2f");
            if (uniform2fPtr != IntPtr.Zero) {
                _uniform2f = Marshal.GetDelegateForFunctionPointer<Uniform2fDelegate>(uniform2fPtr);
            }
        }

        /// <summary>
        /// Sets a uniform integer value
        /// </summary>
        /// <param name="location">Uniform location</param>
        /// <param name="v0">Value</param>
        public void Uniform1i(int location, int v0) {
            _uniform1i?.Invoke(location, v0);
        }

        /// <summary>
        /// Sets a uniform vec2 value
        /// </summary>
        /// <param name="location">Uniform location</param>
        /// <param name="v0">X component</param>
        /// <param name="v1">Y component</param>
        public void Uniform2f(int location, float v0, float v1) {
            _uniform2f?.Invoke(location, v0, v1);
        }
    }
}

namespace Spice86.Views;

using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

using Spice86.ViewModels;

using System;
using System.Diagnostics;
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

    public OpenGlVideoControl() {
        Console.WriteLine("[WARN] OpenGL: OpenGlVideoControl constructor called");
        AttachedToVisualTree += (s, e) => {
            Console.WriteLine("[WARN] OpenGL: Control attached to visual tree, requesting render");
            Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
        };
    }

    private readonly object _frameBufferLock = new();

    /// <summary>
    /// Updates the video buffer with new frame data
    /// </summary>
    /// <param name="buffer">The frame buffer to render</param>
    /// <param name="width">Width of the frame</param>
    /// <param name="height">Height of the frame</param>
    public void UpdateFrame(uint[] buffer, int width, int height) {
        lock (_frameBufferLock) {
            Console.WriteLine($"[WARN] OpenGL: UpdateFrame called - width={width}, height={height}, buffer.Length={buffer.Length}");
            
            if (width != _width || height != _height) {
                Console.WriteLine($"[WARN] OpenGL: Resolution changed from {_width}x{_height} to {width}x{height}");
                _width = width;
                _height = height;
                _frameBuffer = new uint[width * height];
            }

            if (_frameBuffer is not null && buffer.Length <= _frameBuffer.Length) {
                Array.Copy(buffer, _frameBuffer, buffer.Length);
                Console.WriteLine($"[WARN] OpenGL: Frame buffer copied, requesting render");
                Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
            } else {
                Console.WriteLine($"[WARN] OpenGL: Frame buffer copy skipped - _frameBuffer null or buffer too large");
            }
        }
    }

    /// <summary>
    /// Initialize OpenGL resources
    /// </summary>
    /// <param name="gl">The OpenGL interface</param>
    protected override void OnOpenGlInit(GlInterface gl) {
        base.OnOpenGlInit(gl);
        Console.WriteLine("[WARN] OpenGL: OnOpenGlInit called");
        _glExt = new GlExtensions(gl.GetProcAddress);
        Console.WriteLine("[WARN] OpenGL: GlExtensions initialized");
        _initialized = false;
    }

    /// <summary>
    /// Render the frame with the selected shader
    /// </summary>
    /// <param name="gl">The OpenGL interface</param>
    /// <param name="fb">The framebuffer to render to</param>
    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb) {
        Console.WriteLine($"[WARN] OpenGL: OnOpenGlRender called - _glExt={(_glExt != null)}, _frameBuffer={(_frameBuffer != null)}, _width={_width}, _height={_height}");
        
        if (_glExt is null) {
            Console.WriteLine("[WARN] OpenGL: _glExt was null, initializing now");
            _glExt = new GlExtensions(gl.GetProcAddress);
        }

        if (_frameBuffer is null || _width == 0 || _height == 0) {
            Console.WriteLine($"[WARN] OpenGL: No frame buffer or invalid dimensions, clearing screen");
            gl.ClearColor(0, 0, 0, 1);
            gl.Clear(GL_COLOR_BUFFER_BIT);
            return;
        }

        if (!_initialized || _currentShaderType != ShaderType) {
            Console.WriteLine($"[WARN] OpenGL: Initializing resources - _initialized={_initialized}, ShaderType={ShaderType}");
            InitializeResources(gl);
            _currentShaderType = ShaderType;
            Console.WriteLine($"[WARN] OpenGL: Resources initialized - _shaderProgram={_shaderProgram}, _texture={_texture}, _vao={_vao}");
        }

        gl.Viewport(0, 0, (int)Bounds.Width, (int)Bounds.Height);
        Console.WriteLine($"[WARN] OpenGL: Viewport set to {(int)Bounds.Width}x{(int)Bounds.Height}");
        gl.ClearColor(0, 0, 0, 1);
        gl.Clear(GL_COLOR_BUFFER_BIT);

        if (_shaderProgram == 0 || _texture == 0 || _glExt is null) {
            Console.WriteLine($"[WARN] OpenGL: Missing required resources - _shaderProgram={_shaderProgram}, _texture={_texture}, _glExt={(_glExt != null)}");
            return;
        }

        // Update texture with new frame data
        gl.BindTexture(GL_TEXTURE_2D, _texture);
        lock (_frameBufferLock) {
            fixed (void* pdata = _frameBuffer) {
                Console.WriteLine($"[WARN] OpenGL: Uploading texture data {_width}x{_height}");
                gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, _width, _height, 0, GL_RGBA, GL_UNSIGNED_BYTE, new IntPtr(pdata));
            }
        }

        // Use shader program
        gl.UseProgram(_shaderProgram);
        Console.WriteLine($"[WARN] OpenGL: Using shader program {_shaderProgram}");

        // Set uniforms
        SetUniforms(gl, _glExt);

        // Draw quad
        gl.BindVertexArray(_vao);
        Console.WriteLine($"[WARN] OpenGL: Drawing quad with VAO {_vao}");
        gl.DrawArrays(GL_TRIANGLE_STRIP, 0, 4);
        gl.BindVertexArray(0);
        Console.WriteLine("[WARN] OpenGL: Frame rendered");
    }

    private void SetUniforms(GlInterface gl, GlExtensions glExt) {
        int rubyTextureLoc = gl.GetUniformLocationString(_shaderProgram, "rubyTexture");
        int rubyInputSizeLoc = gl.GetUniformLocationString(_shaderProgram, "rubyInputSize");
        int rubyTextureSizeLoc = gl.GetUniformLocationString(_shaderProgram, "rubyTextureSize");
        int rubyOutputSizeLoc = gl.GetUniformLocationString(_shaderProgram, "rubyOutputSize");

        Console.WriteLine($"[WARN] OpenGL: Uniform locations - rubyTexture={rubyTextureLoc}, rubyInputSize={rubyInputSizeLoc}, rubyTextureSize={rubyTextureSizeLoc}, rubyOutputSize={rubyOutputSizeLoc}");

        if (rubyTextureLoc >= 0) {
            glExt.Uniform1i(rubyTextureLoc, 0);
            Console.WriteLine("[WARN] OpenGL: Set rubyTexture uniform to 0");
        }
        if (rubyInputSizeLoc >= 0) {
            glExt.Uniform2f(rubyInputSizeLoc, _width, _height);
            Console.WriteLine($"[WARN] OpenGL: Set rubyInputSize uniform to {_width}x{_height}");
        }
        if (rubyTextureSizeLoc >= 0) {
            glExt.Uniform2f(rubyTextureSizeLoc, _width, _height);
            Console.WriteLine($"[WARN] OpenGL: Set rubyTextureSize uniform to {_width}x{_height}");
        }
        if (rubyOutputSizeLoc >= 0) {
            glExt.Uniform2f(rubyOutputSizeLoc, (float)Bounds.Width, (float)Bounds.Height);
            Console.WriteLine($"[WARN] OpenGL: Set rubyOutputSize uniform to {Bounds.Width}x{Bounds.Height}");
        }
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
        Console.WriteLine($"[WARN] OpenGL: InitializeResources starting - ShaderType={ShaderType}");
        
        // Clean up previous resources
        if (_initialized) {
            Console.WriteLine("[WARN] OpenGL: Cleaning up previous resources");
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
        Console.WriteLine($"[WARN] OpenGL: Loading shader source for {ShaderType}");
        string shaderSource = LoadShaderSource(ShaderType);
        Console.WriteLine($"[WARN] OpenGL: Shader source loaded, length={shaderSource.Length}");
        
        if (!CompileShaders(gl, shaderSource)) {
            Console.WriteLine("[WARN] OpenGL: Failed to compile shaders, using passthrough");
            // Try fallback to passthrough shader
            if (!CompileShaders(gl, LoadPassthroughShader())) {
                Console.WriteLine("[WARN] OpenGL: FATAL - Failed to compile passthrough shader");
                _initialized = false;
                return;
            }
        }
        
        Console.WriteLine($"[WARN] OpenGL: Shaders compiled successfully - program={_shaderProgram}");

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
        Console.WriteLine("[WARN] OpenGL: Creating texture");
        _texture = gl.GenTexture();
        gl.BindTexture(GL_TEXTURE_2D, _texture);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        Console.WriteLine($"[WARN] OpenGL: Texture created - id={_texture}");

        _initialized = true;
        Console.WriteLine($"[WARN] OpenGL: InitializeResources completed successfully - VAO={_vao}, VBO={_vbo}, Texture={_texture}, Program={_shaderProgram}");
    }

    private bool CompileShaders(GlInterface gl, string shaderSource) {
        Console.WriteLine($"[WARN] OpenGL: CompileShaders starting - source length={shaderSource.Length}");
        
        // DOSBox Staging shaders use conditional compilation with #if defined(VERTEX) and #elif defined(FRAGMENT)
        // We need to compile the entire source twice - once for vertex, once for fragment
        // Insert #define after #version directive (must be first non-comment line)
        int versionEnd = shaderSource.IndexOf('\n');
        if (versionEnd < 0) versionEnd = 0;
        string versionLine = versionEnd > 0 ? shaderSource.Substring(0, versionEnd + 1) : "";
        string restOfShader = versionEnd > 0 ? shaderSource.Substring(versionEnd + 1) : shaderSource;
        
        string vertexSource = versionLine + "#define VERTEX\n" + restOfShader;
        string fragmentSource = versionLine + "#define FRAGMENT\n" + restOfShader;
        
        Console.WriteLine($"[WARN] OpenGL: Prepared vertex shader length={vertexSource.Length}, fragment shader length={fragmentSource.Length}");

        // Compile vertex shader
        Console.WriteLine("[WARN] OpenGL: Compiling vertex shader");
        _vertexShader = gl.CreateShader(GL_VERTEX_SHADER);
        string? vertexError = gl.CompileShaderAndGetError(_vertexShader, vertexSource);
        if (!string.IsNullOrEmpty(vertexError)) {
            Console.WriteLine($"[WARN] OpenGL: Vertex shader compilation FAILED: {vertexError}");
            gl.DeleteShader(_vertexShader);
            _vertexShader = 0;
            return false;
        }
        Console.WriteLine($"[WARN] OpenGL: Vertex shader compiled successfully - id={_vertexShader}");

        // Compile fragment shader
        Console.WriteLine("[WARN] OpenGL: Compiling fragment shader");
        _fragmentShader = gl.CreateShader(GL_FRAGMENT_SHADER);
        string? fragmentError = gl.CompileShaderAndGetError(_fragmentShader, fragmentSource);
        if (!string.IsNullOrEmpty(fragmentError)) {
            Console.WriteLine($"[WARN] OpenGL: Fragment shader compilation FAILED: {fragmentError}");
            gl.DeleteShader(_vertexShader);
            gl.DeleteShader(_fragmentShader);
            _vertexShader = 0;
            _fragmentShader = 0;
            return false;
        }
        Console.WriteLine($"[WARN] OpenGL: Fragment shader compiled successfully - id={_fragmentShader}");

        // Link program
        Console.WriteLine("[WARN] OpenGL: Linking shader program");
        _shaderProgram = gl.CreateProgram();
        gl.AttachShader(_shaderProgram, _vertexShader);
        gl.AttachShader(_shaderProgram, _fragmentShader);
        gl.BindAttribLocationString(_shaderProgram, 0, "a_position");
        
        string? linkError = gl.LinkProgramAndGetError(_shaderProgram);
        if (!string.IsNullOrEmpty(linkError)) {
            Console.WriteLine($"[WARN] OpenGL: Program linking FAILED: {linkError}");
            gl.DeleteShader(_vertexShader);
            gl.DeleteShader(_fragmentShader);
            gl.DeleteProgram(_shaderProgram);
            _vertexShader = 0;
            _fragmentShader = 0;
            _shaderProgram = 0;
            return false;
        }
        Console.WriteLine($"[WARN] OpenGL: Program linked successfully - id={_shaderProgram}");

        return true;
    }

    private string LoadShaderSource(CrtShaderType shaderType) {
        string resourceName = shaderType switch {
            CrtShaderType.FakeLottes => "fakelottes.glsl",
            CrtShaderType.EasyMode => "crt-easymode.glsl",
            CrtShaderType.CrtGeom => "crt-geom.glsl",
            _ => "passthrough.glsl"
        };

        string resourcePath = $"avares://Spice86/Views/Shaders/Crt/{resourceName}";
        Console.WriteLine($"[WARN] OpenGL: Loading shader from {resourcePath}");
        
        try {
            using System.IO.Stream? stream = AssetLoader.Open(new Uri(resourcePath));
            if (stream is null) {
                Console.WriteLine($"[WARN] OpenGL: Stream is null for {resourceName}, loading passthrough");
                return LoadPassthroughShader();
            }

            using System.IO.StreamReader reader = new(stream);
            string content = reader.ReadToEnd();
            Console.WriteLine($"[WARN] OpenGL: Successfully loaded {resourceName} - length={content.Length}");
            return content;
        } catch (System.IO.IOException ex) {
            Console.WriteLine($"[WARN] OpenGL: IOException loading shader {resourceName}: {ex.Message}");
            return LoadPassthroughShader();
        } catch (UnauthorizedAccessException ex) {
            Console.WriteLine($"[WARN] OpenGL: UnauthorizedAccessException loading shader {resourceName}: {ex.Message}");
            return LoadPassthroughShader();
        } catch (ArgumentException ex) {
            Console.WriteLine($"[WARN] OpenGL: ArgumentException loading shader {resourceName}: {ex.Message}");
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

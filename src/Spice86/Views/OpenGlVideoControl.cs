namespace Spice86.Views;

using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

using Spice86.ViewModels;

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
    private const int GL_FRAMEBUFFER = 0x8D40;
    private const int GL_COLOR_ATTACHMENT0 = 0x8CE0;
    private const int GL_RGBA8 = 0x8058;

    private GlExtensions? _glExt;

    // Pass 2 (User Shader)
    private int _vertexShader;
    private int _fragmentShader;
    private int _shaderProgram;

    // Pass 1 (Image Adjustment)
    private int _pass1VertexShader;
    private int _pass1FragmentShader;
    private int _pass1ShaderProgram;
    private int _fbo;
    private int _fboTexture;
    private int _fboWidth;
    private int _fboHeight;

    private int _vao;
    private int _vbo;
    private int _texture; // Input texture
    private int _width;
    private int _height;
    private uint[]? _frameBuffer;
    private bool _initialized;
    private bool _isOpenGLES;
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
           // Console.WriteLine($"[WARN] OpenGL: UpdateFrame called - width={width}, height={height}, buffer.Length={buffer.Length}");

            if (width != _width || height != _height) {
               Console.WriteLine($"[WARN] OpenGL: Resolution changed from {_width}x{_height} to {width}x{height}");
                _width = width;
                _height = height;
                _frameBuffer = new uint[width * height];
            }

            if (_frameBuffer is not null) {
                // Ensure we don't copy more than the destination can hold, and not more than the source has.
                // We use width*height as the target size because _frameBuffer is exactly that size.
                // If buffer is larger (e.g. reused buffer), we only copy the relevant part.
                int lengthToCopy = Math.Min(buffer.Length, _frameBuffer.Length);
                
                if (lengthToCopy > 0) {
                    Array.Copy(buffer, _frameBuffer, lengthToCopy);
                    Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
                }
            } else {
               Console.WriteLine($"[WARN] OpenGL: Frame buffer copy skipped - _frameBuffer is null");
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

        string? version = gl.GetString(GL_VERSION);
        string? renderer = gl.GetString(GL_RENDERER);
        _isOpenGLES = (version?.Contains("OpenGL ES") is true || version?.Contains("GLES") is true) is true;
       Console.WriteLine($"[WARN] OpenGL Context: Version='{version}', Renderer='{renderer}', IsGLES={_isOpenGLES}");

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
        // Console.WriteLine($"[WARN] OpenGL: OnOpenGlRender called - _glExt={(_glExt != null)}, _frameBuffer={(_frameBuffer != null)}, _width={_width}, _height={_height}");

        if (_glExt is null) {
            _glExt = new GlExtensions(gl.GetProcAddress);
        }

        if (_frameBuffer is null || _width == 0 || _height == 0) {
            Console.WriteLine($"[WARN] OpenGL: No frame buffer or invalid dimensions, clearing screen");
            gl.ClearColor(0, 0, 0, 1);
            gl.Clear(GL_COLOR_BUFFER_BIT);
            return;
        }

        // Check bounds
        if (Bounds.Width <= 0 || Bounds.Height <= 0) {
             // Console.WriteLine($"[WARN] OpenGL: Invalid Bounds {Bounds.Width}x{Bounds.Height}");
             return;
        }

        if (!_initialized || _currentShaderType != ShaderType) {
           Console.WriteLine($"[WARN] OpenGL: Initializing resources - _initialized={_initialized}, ShaderType={ShaderType}");
            InitializeResources(gl);
            _currentShaderType = ShaderType;
           Console.WriteLine($"[WARN] OpenGL: Resources initialized - _shaderProgram={_shaderProgram}, _texture={_texture}, _vao={_vao}");
        }

        if (_shaderProgram == 0 || _texture == 0 || _glExt is null) {
           Console.WriteLine($"[WARN] OpenGL: Missing required resources - _shaderProgram={_shaderProgram}, _texture={_texture}, _glExt={(_glExt != null)}");
            return;
        }

        // Check if FBO needs resizing
        if (_fboWidth != _width || _fboHeight != _height) {
            ResizeFbo(gl, _width, _height);
        }

        // --- PASS 1: Render to FBO (Image Adjustments) ---
        // Bind FBO
        _glExt.BindFramebuffer(GL_FRAMEBUFFER, _fbo);
        gl.Viewport(0, 0, _width, _height);

        // Use Pass 1 Shader
        gl.UseProgram(_pass1ShaderProgram);

        // Upload Input Data to _texture
        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, _texture);
        lock (_frameBufferLock) {
            fixed (void* pdata = _frameBuffer) {
                // Console.WriteLine($"[WARN] OpenGL: Uploading texture data {_width}x{_height}");
                // Use GL_RGBA8 for internal format for GLES 3.0 compliance
                gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, _width, _height, 0, GL_RGBA, GL_UNSIGNED_BYTE, new IntPtr(pdata));
            }
        }

        // Set Pass 1 Uniforms
        SetPass1Uniforms(gl, _glExt);

        // Draw Quad (Pass 1)
        gl.BindVertexArray(_vao);
        gl.DrawArrays(GL_TRIANGLE_STRIP, 0, 4);

        // --- PASS 2: Render to Screen (CRT) ---
        // Bind Output Buffer (Screen)
        _glExt.BindFramebuffer(GL_FRAMEBUFFER, fb);
        gl.Viewport(0, 0, (int)Bounds.Width, (int)Bounds.Height);
        gl.ClearColor(0, 0, 0, 1);
        gl.Clear(GL_COLOR_BUFFER_BIT);

        // Use Pass 2 Shader
        gl.UseProgram(_shaderProgram);

        // Bind FBO Texture as Input
        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, _fboTexture);

        // Set Pass 2 Uniforms
        SetPass2Uniforms(gl, _glExt);

        // Draw Quad (Pass 2)
        gl.BindVertexArray(_vao); // VAO is already bound but good practice
        gl.DrawArrays(GL_TRIANGLE_STRIP, 0, 4);
        gl.BindVertexArray(0);

        // Console.WriteLine("[WARN] OpenGL: Frame rendered");
    }

    private void ResizeFbo(GlInterface gl, int width, int height) {
        if (_glExt == null) return;
        gl.BindTexture(GL_TEXTURE_2D, _fboTexture);
        // Use GL_RGBA8 for internal format
        gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);
        // Do not unbind as it might interfere

        _fboWidth = width;
        _fboHeight = height;

        _glExt.BindFramebuffer(GL_FRAMEBUFFER, _fbo);
        _glExt.FramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _fboTexture, 0);

        // Check status
        // int status = _glExt.CheckFramebufferStatus(GL_FRAMEBUFFER);
        // if (status != GL_FRAMEBUFFER_COMPLETE) ...
    }

    private void SetPass1Uniforms(GlInterface gl, GlExtensions glExt) {
        SetUniform1i(gl, glExt, _pass1ShaderProgram, "inputTexture", 0);
        SetUniform1i(gl, glExt, _pass1ShaderProgram, "COLOR_SPACE", 0);
        // Disable adjustments temporarily to debug black screen
        SetUniform1i(gl, glExt, _pass1ShaderProgram, "ENABLE_ADJUSTMENTS", 0);
        SetUniform1i(gl, glExt, _pass1ShaderProgram, "COLOR_PROFILE", 0);

        SetUniform1f(gl, glExt, _pass1ShaderProgram, "BRIGHTNESS", 50.0f);
        SetUniform1f(gl, glExt, _pass1ShaderProgram, "CONTRAST", 50.0f);
        SetUniform1f(gl, glExt, _pass1ShaderProgram, "GAMMA", 0.0f);
        SetUniform1f(gl, glExt, _pass1ShaderProgram, "SATURATION", 0.0f);
        SetUniform1f(gl, glExt, _pass1ShaderProgram, "DIGITAL_CONTRAST", 0.0f);

        SetUniform1f(gl, glExt, _pass1ShaderProgram, "BLACK_LEVEL", 0.0f);
        SetUniform3f(gl, glExt, _pass1ShaderProgram, "BLACK_LEVEL_COLOR", 0.0f, 0.0f, 0.0f);

        SetUniform1f(gl, glExt, _pass1ShaderProgram, "COLOR_TEMPERATURE_KELVIN", 6500.0f);
        SetUniform1f(gl, glExt, _pass1ShaderProgram, "COLOR_TEMPERATURE_LUMA_PRESERVE", 1.0f);

        SetUniform1f(gl, glExt, _pass1ShaderProgram, "RED_GAIN", 1.0f);
        SetUniform1f(gl, glExt, _pass1ShaderProgram, "GREEN_GAIN", 1.0f);
        SetUniform1f(gl, glExt, _pass1ShaderProgram, "BLUE_GAIN", 1.0f);

        // Pass 1 inputSize is also needed by vertex shader
        SetUniform2f(gl, glExt, _pass1ShaderProgram, "inputSize", _width, _height);
    }

    private void SetPass2Uniforms(GlInterface gl, GlExtensions glExt) {
        // Shader uses 's_p' as the sampler, but we set 'rubyTexture' historically.
        // Explicitly set 's_p' (and 'source'/'Source' for compatibility) to unit 0.
        SetUniform1i(gl, glExt, _shaderProgram, "rubyTexture", 0);
        SetUniform1i(gl, glExt, _shaderProgram, "s_p", 0);
        SetUniform1i(gl, glExt, _shaderProgram, "source", 0);
        
        SetUniform2f(gl, glExt, _shaderProgram, "rubyInputSize", _width, _height);
        SetUniform2f(gl, glExt, _shaderProgram, "rubyTextureSize", _width, _height);
        SetUniform2f(gl, glExt, _shaderProgram, "rubyOutputSize", (float)Bounds.Width, (float)Bounds.Height);
        SetUniform1i(gl, glExt, _shaderProgram, "rubyFrameCount", 0);

        // Set Default Uniforms for crt-hyllian
        SetUniform1f(gl, glExt, _shaderProgram, "BEAM_PROFILE", 0.0f);
        SetUniform1f(gl, glExt, _shaderProgram, "HFILTER_PROFILE", 0.0f);
        SetUniform1f(gl, glExt, _shaderProgram, "BEAM_MIN_WIDTH", 0.95f);
        SetUniform1f(gl, glExt, _shaderProgram, "BEAM_MAX_WIDTH", 1.30f);
        SetUniform1f(gl, glExt, _shaderProgram, "SCANLINES_STRENGTH", 0.85f);
        SetUniform1f(gl, glExt, _shaderProgram, "COLOR_BOOST", 2.50f);
        SetUniform1f(gl, glExt, _shaderProgram, "SHARPNESS_HACK", 1.0f);
        SetUniform1f(gl, glExt, _shaderProgram, "PHOSPHOR_LAYOUT", 2.0f);
        SetUniform1f(gl, glExt, _shaderProgram, "MASK_INTENSITY", 0.55f);
        SetUniform1f(gl, glExt, _shaderProgram, "CRT_ANTI_RINGING", 1.0f);
        SetUniform1f(gl, glExt, _shaderProgram, "INPUT_GAMMA", 2.4f);
        SetUniform1f(gl, glExt, _shaderProgram, "OUTPUT_GAMMA", 2.0f);
        SetUniform1f(gl, glExt, _shaderProgram, "VSCANLINES", 0.0f);
    }

    private void SetUniform1i(GlInterface gl, GlExtensions glExt, int program, string name, int value) {
        int loc = gl.GetUniformLocationString(program, name);
        if (loc >= 0) glExt.Uniform1i(loc, value);
    }

    private void SetUniform1f(GlInterface gl, GlExtensions glExt, int program, string name, float value) {
        int loc = gl.GetUniformLocationString(program, name);
        if (loc >= 0) glExt.Uniform1f(loc, value);
    }

    private void SetUniform2f(GlInterface gl, GlExtensions glExt, int program, string name, float v1, float v2) {
        int loc = gl.GetUniformLocationString(program, name);
        if (loc >= 0) glExt.Uniform2f(loc, v1, v2);
    }

    private void SetUniform3f(GlInterface gl, GlExtensions glExt, int program, string name, float v1, float v2, float v3) {
        int loc = gl.GetUniformLocationString(program, name);
        if (loc >= 0) glExt.Uniform3f(loc, v1, v2, v3);
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

        // Delete Pass 2 resources
        if (_texture != 0) gl.DeleteTexture(_texture);
        if (_shaderProgram != 0) gl.DeleteProgram(_shaderProgram);
        if (_vertexShader != 0) gl.DeleteShader(_vertexShader);
        if (_fragmentShader != 0) gl.DeleteShader(_fragmentShader);

        // Delete Pass 1 resources
        if (_fboTexture != 0) gl.DeleteTexture(_fboTexture);
        if (_fbo != 0) _glExt?.DeleteFramebuffer(_fbo);
        if (_pass1ShaderProgram != 0) gl.DeleteProgram(_pass1ShaderProgram);
        if (_pass1VertexShader != 0) gl.DeleteShader(_pass1VertexShader);
        if (_pass1FragmentShader != 0) gl.DeleteShader(_pass1FragmentShader);

        base.OnOpenGlDeinit(gl);
    }

    private unsafe void InitializeResources(GlInterface gl) {
        // Console.WriteLine($"[WARN] OpenGL: InitializeResources starting - ShaderType={ShaderType}");

        // Clean up previous resources
        if (_initialized) {
            OnOpenGlDeinit(gl); // Reuse deinit to clean up
            _initialized = false;
        }

        // 1. Compile Pass 1 (Image Adjustments)
        string pass1Source = LoadInternalShader("image-adjustments-pass.glsl");
        if (string.IsNullOrEmpty(pass1Source)) {
           Console.WriteLine("[WARN] OpenGL: Failed to load image-adjustments-pass.glsl");
            return;
        }

        if (!CompileShaders(gl, pass1Source, out _pass1VertexShader, out _pass1FragmentShader, out _pass1ShaderProgram)) {
           Console.WriteLine("[WARN] OpenGL: Failed to compile Pass 1 shaders");
            // If Pass 1 fails, we are kind of stuck. Using passthrough for Pass 1?
            // For now, let's proceed and see.
        }

        // 2. Compile Pass 2 (User Shader)
       Console.WriteLine($"[WARN] OpenGL: Loading shader source for {ShaderType}");
        string pass2Source = LoadShaderSource(ShaderType);

        if (!CompileShaders(gl, pass2Source, out _vertexShader, out _fragmentShader, out _shaderProgram)) {
           Console.WriteLine("[WARN] OpenGL: Failed to compile Pass 2 shaders, using passthrough");
            if (!CompileShaders(gl, LoadPassthroughShader(), out _vertexShader, out _fragmentShader, out _shaderProgram)) {
               Console.WriteLine("[WARN] OpenGL: FATAL - Failed to compile passthrough shader");
                return;
            }
        }

       Console.WriteLine($"[WARN] OpenGL: Shaders compiled - P1:{_pass1ShaderProgram} P2:{_shaderProgram}");

        // 3. Setup VAO/VBO (Fullscreen Quad)
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

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

        // Setup vertex attribute pointer for position (Same for both shaders usually)
        // Pass 1
        int posLoc1 = gl.GetAttribLocationString(_pass1ShaderProgram, "a_position");
        if (posLoc1 >= 0) {
            gl.EnableVertexAttribArray(posLoc1);
            gl.VertexAttribPointer(posLoc1, 2, GL_FLOAT, 0, 0, IntPtr.Zero);
        }

        // Pass 2
        int posLoc2 = gl.GetAttribLocationString(_shaderProgram, "a_position");
        if (posLoc2 >= 0) {
            gl.EnableVertexAttribArray(posLoc2);
            gl.VertexAttribPointer(posLoc2, 2, GL_FLOAT, 0, 0, IntPtr.Zero);
        }

        gl.BindVertexArray(0);

        // 4. Create Input Texture
        _texture = gl.GenTexture();
        gl.BindTexture(GL_TEXTURE_2D, _texture);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

        // 5. Create FBO
        if (_glExt != null) {
            _fbo = _glExt.GenFramebuffer();
            _fboTexture = gl.GenTexture();
            gl.BindTexture(GL_TEXTURE_2D, _fboTexture);
            // Default initialization - will be resized later
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

            _fboWidth = 0;
            _fboHeight = 0;
        }

        _initialized = true;
    }

    private bool CompileShaders(GlInterface gl, string shaderSource, out int vertexShader, out int fragmentShader, out int shaderProgram) {
        fragmentShader = 0;
        shaderProgram = 0;

        // Strip #pragma parameter directives
        string cleanedSource = StripPragmaParameters(shaderSource);

        // DOSBox Staging shaders use conditional compilation with #if defined(VERTEX) and #elif defined(FRAGMENT)
        int versionEnd = cleanedSource.IndexOf('\n');
        if (versionEnd < 0) versionEnd = 0;

        string originalVersionLine = "";
        string restOfShader = cleanedSource;
        if (versionEnd > 0) {
            string firstLine = cleanedSource.Substring(0, versionEnd).Trim();
            if (firstLine.StartsWith("#version")) {
                originalVersionLine = firstLine; // e.g. "#version 330 core"
                restOfShader = cleanedSource.Substring(versionEnd + 1);
            }
        }

        // Determine correct version line
        string versionLine;
        bool addPrecision = false;

        if (_isOpenGLES) {
            // Force ES 3.0
            versionLine = "#version 300 es\n";
            addPrecision = true;
        } else {
            // Keep original if it exists and looks valid (330 core), otherwise fallback
            if (originalVersionLine.Contains("330 core") || originalVersionLine.Contains("4")) {
                versionLine = originalVersionLine + "\n";
            } else if (originalVersionLine.Contains("120") || originalVersionLine.Contains("130")) {
                versionLine = originalVersionLine + "\n";
            } else {
                // Default to 120 or 150 depending on support? 
                // Let's rely on shader source mostly, but strip 'core' if version is low?
                // For now, let's trust the source UNLESS it's the broken passthrough 120
                versionLine = originalVersionLine + "\n";
                if (string.IsNullOrWhiteSpace(originalVersionLine)) {
                    versionLine = "#version 120\n";
                }
            }
        }

        string precisionLine = addPrecision ? "precision highp float;\n" : "";

        string esCompat = "";
        string varyingDefVert = "";
        string varyingDefFrag = "";

        if (_isOpenGLES) {
            esCompat = "#define attribute in\n#define texture2D texture\n";
            varyingDefVert = "#define varying out\n";
            varyingDefFrag = "#define varying in\n";
        }

        string vertexSource = versionLine + "#define VERTEX\n" + precisionLine + esCompat + varyingDefVert + restOfShader;
        string fragmentSource = versionLine + "#define FRAGMENT\n" + precisionLine + esCompat + varyingDefFrag + restOfShader;

        Console.WriteLine($"[WARN] OpenGL: Compiling Shader. Version Line: '{versionLine.Trim()}'");

        // Compile vertex shader
        vertexShader = gl.CreateShader(GL_VERTEX_SHADER);
        string? vertexError = gl.CompileShaderAndGetError(vertexShader, vertexSource);
        if (!string.IsNullOrEmpty(vertexError)) {
           Console.WriteLine($"[WARN] OpenGL: Vertex shader compilation FAILED: {vertexError}");
            Console.WriteLine("Vertex Source First 5 lines:");
            // using (var reader = new System.IO.StringReader(vertexSource)) {
            //    for(int i=0; i<5; i++)Console.WriteLine(reader.ReadLine());
            // }
            gl.DeleteShader(vertexShader);
            vertexShader = 0;
            return false;
        }

        // Compile fragment shader
        fragmentShader = gl.CreateShader(GL_FRAGMENT_SHADER);
        string? fragmentError = gl.CompileShaderAndGetError(fragmentShader, fragmentSource);
        if (!string.IsNullOrEmpty(fragmentError)) {
           Console.WriteLine($"[WARN] OpenGL: Fragment shader compilation FAILED: {fragmentError}");
            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);
            vertexShader = 0;
            fragmentShader = 0;
            return false;
        }

        // Link program
        shaderProgram = gl.CreateProgram();
        gl.AttachShader(shaderProgram, vertexShader);
        gl.AttachShader(shaderProgram, fragmentShader);

        // We bind a_position generically for both programs if possible
        gl.BindAttribLocationString(shaderProgram, 0, "a_position");

        string? linkError = gl.LinkProgramAndGetError(shaderProgram);
        if (!string.IsNullOrEmpty(linkError)) {
           Console.WriteLine($"[WARN] OpenGL: Program linking FAILED: {linkError}");
            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);
            gl.DeleteProgram(shaderProgram);
            vertexShader = 0;
            fragmentShader = 0;
            shaderProgram = 0;
            return false;
        }

        return true;
    }

    private string StripPragmaParameters(string source) {
        StringBuilder sb = new StringBuilder();
        string[] lines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (string line in lines) {
            if (line.TrimStart().StartsWith("#pragma parameter")) {
                continue;
            }
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    private string LoadShaderSource(CrtShaderType shaderType) {
        string resourceName = shaderType switch {
            CrtShaderType.FakeLottes => "crt-hyllian.glsl",
            CrtShaderType.EasyMode => "vga-1080p.glsl",
            CrtShaderType.CrtGeom => "vga-1080p-fake-double-scan.glsl",
            _ => "passthrough"
        };

        if (resourceName == "passthrough") return LoadPassthroughShader();

        // Note: The folder is lower case 'crt' on disk based on file listing
        string resourcePath = $"avares://Spice86/Views/Shaders/crt/{resourceName}";
       Console.WriteLine($"[WARN] OpenGL: Loading shader from {resourcePath}");

        try {
            using Stream stream = Avalonia.Platform.AssetLoader.Open(new Uri(resourcePath));
            using var reader = new System.IO.StreamReader(stream);
            return reader.ReadToEnd();
        } catch (Exception ex) {
            Console.WriteLine($"[WARN] OpenGL: Error loading shader {resourceName}: {ex.Message}");
            return LoadPassthroughShader();
        }
    }

    private string LoadInternalShader(string name) {
        // Since we are in Spice86.Views assembly, and the file is in Views/Shaders/_internal
        // The resource path for Avalonia is likely "avares://Spice86/Views/Shaders/_internal/..."
        string resourcePath = $"avares://Spice86/Views/Shaders/_internal/{name}";
        try {
            using Stream stream = Avalonia.Platform.AssetLoader.Open(new Uri(resourcePath));
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        } catch (Exception ex) {
            Console.WriteLine($"[ERROR] OpenGL: Failed to load internal shader {name}: {ex}");
            return "";
        }
    }

    private string LoadPassthroughShader() {
        if (_isOpenGLES) {
            return @"#version 300 es
#if defined(VERTEX)
layout(location = 0) in vec2 a_position;
out vec2 v_texCoord;
uniform vec2 rubyInputSize;
uniform vec2 rubyTextureSize;
void main() {
    gl_Position = vec4(a_position, 0.0, 1.0);
    v_texCoord = vec2(a_position.x + 1.0, 1.0 - a_position.y) / 2.0 * rubyInputSize / rubyTextureSize;
}
#elif defined(FRAGMENT)
in vec2 v_texCoord;
out vec4 FragColor;
uniform sampler2D rubyTexture;
void main() {
    FragColor = texture(rubyTexture, v_texCoord);
}
#endif";
        }

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
        private delegate void Uniform1fDelegate(int location, float v0);
        private delegate void Uniform3fDelegate(int location, float v0, float v1, float v2);
        private delegate void GenFramebuffersDelegate(int n, [Out] int[] framebuffers);
        private delegate void BindFramebufferDelegate(int target, int framebuffer);
        private delegate void FramebufferTexture2DDelegate(int target, int attachment, int textarget, int texture, int level);
        private delegate void DeleteFramebuffersDelegate(int n, [In] int[] framebuffers);

        private readonly Uniform1iDelegate? _uniform1i;
        private readonly Uniform2fDelegate? _uniform2f;
        private readonly Uniform1fDelegate? _uniform1f;
        private readonly Uniform3fDelegate? _uniform3f;
        private readonly GenFramebuffersDelegate? _genFramebuffers;
        private readonly BindFramebufferDelegate? _bindFramebuffer;
        private readonly FramebufferTexture2DDelegate? _framebufferTexture2D;
        private readonly DeleteFramebuffersDelegate? _deleteFramebuffers;

        /// <summary>
        /// Initializes OpenGL extension functions
        /// </summary>
        /// <param name="getProcAddress">Function to get OpenGL procedure addresses</param>
        public GlExtensions(Func<string, IntPtr> getProcAddress) {
            void Load<T>(string name, ref T? del) where T : Delegate {
                IntPtr ptr = getProcAddress(name);
                if (ptr != IntPtr.Zero) {
                    del = Marshal.GetDelegateForFunctionPointer<T>(ptr);
                }
            }

            Load("glUniform1i", ref _uniform1i);
            Load("glUniform2f", ref _uniform2f);
            Load("glUniform1f", ref _uniform1f);
            Load("glUniform3f", ref _uniform3f);
            Load("glGenFramebuffers", ref _genFramebuffers);
            Load("glBindFramebuffer", ref _bindFramebuffer);
            Load("glFramebufferTexture2D", ref _framebufferTexture2D);
            Load("glDeleteFramebuffers", ref _deleteFramebuffers);
        }

        public void Uniform1i(int location, int v0) => _uniform1i?.Invoke(location, v0);
        public void Uniform2f(int location, float v0, float v1) => _uniform2f?.Invoke(location, v0, v1);
        public void Uniform1f(int location, float v0) => _uniform1f?.Invoke(location, v0);
        public void Uniform3f(int location, float v0, float v1, float v2) => _uniform3f?.Invoke(location, v0, v1, v2);

        public int GenFramebuffer() {
            if (_genFramebuffers == null) return 0;
            int[] fbs = new int[1];
            _genFramebuffers(1, fbs);
            return fbs[0];
        }

        public void BindFramebuffer(int target, int framebuffer) => _bindFramebuffer?.Invoke(target, framebuffer);

        public void FramebufferTexture2D(int target, int attachment, int textarget, int texture, int level)
            => _framebufferTexture2D?.Invoke(target, attachment, textarget, texture, level);

        public void DeleteFramebuffer(int framebuffer) {
            if (_deleteFramebuffers != null && framebuffer != 0) {
                _deleteFramebuffers(1, new int[] { framebuffer });
            }
        }
    }
}

using ShaderCompilation;
using Vortice.SpirvCross;

namespace Samples;

//todo, this sucks, solely for sample purposes (todo include builtin online compiler for magpievk)

public unsafe class ShaderCompiler : IDisposable {
    public CompilerCtx _compilerCtx;

    public ShaderCompiler() {
        _compilerCtx = new CompilerCtx();
        _compilerCtx._options.CustomIncludeHandler = DefaultIncludeHandler;
    }

    public void SetCustomIncludeHandler(CompilerCtx.Options.IncludeHandler handler) {
        _compilerCtx._options.CustomIncludeHandler = handler;
    }

    private CompilerCtx.IncludeResult DefaultIncludeHandler(string requestedSource, string requestingSource, IncludeType type) {
        string resolvedPath;
        string requestingSourceDir = Path.GetDirectoryName(requestingSource) ?? string.Empty;
        if (type == IncludeType.Relative && !string.IsNullOrEmpty(requestingSourceDir)) {
            resolvedPath = Path.Combine(requestingSourceDir, requestedSource);
            if (File.Exists(resolvedPath)) {
                return new CompilerCtx.IncludeResult(resolvedPath, File.ReadAllText(resolvedPath));
            }
        }

        resolvedPath = "resources";
        if (File.Exists(resolvedPath)) {
            return new CompilerCtx.IncludeResult(resolvedPath, File.ReadAllText(resolvedPath));
        }

        if (File.Exists(requestedSource)) {
            return new CompilerCtx.IncludeResult(requestedSource, File.ReadAllText(requestedSource));
        }
        
        string errorMessage = $"could not resolve include '{requestedSource}' requested by '{requestingSource}'.";
        Console.Error.WriteLine(errorMessage);
        return new CompilerCtx.IncludeResult(requestedSource, errorMessage, isError: true);
    }

    public ReadOnlySpan<byte> CompileShader(string path, ShaderKind kind, bool debug = false) {
        _compilerCtx._options.SetSourceLanguage(LangKind.Glsl);
        _compilerCtx._options.SetTargetEnv(TargetEnv.Vulkan, Vortice.Vulkan.VkVersion.Version_1_3);
        _compilerCtx._options.SetTargetSpirv(SpirvVersion.Spirv13);
        _compilerCtx._options.SetGenerateDebugInfo(debug);
        _compilerCtx._options.SetOptimizationLevel(debug ? OptimizationLevel.None : OptimizationLevel.Performance);

        string shaderCode = File.ReadAllText(path);

        using (var compileResult = _compilerCtx.Compile(shaderCode, kind, path, "main")) {
            if (compileResult.CompilationStatus != Status.Success) {
                if (compileResult.NumWarnings > 0) {
                    Console.WriteLine($"shader compilation warnings for {path}:\n{compileResult.ErrorMessage}");
                }
                throw new Exception($"failed to compile shader {path}: {compileResult.ErrorMessage}");
            }
            
            if (compileResult.NumWarnings > 0) {
                 Console.WriteLine($"shader compilation warnings for {path}:\n{compileResult.ErrorMessage}");
            }
            

            return compileResult.GetSPVBytes();
        }
    }
    
    public ReflectedShaderData ReflectShader(byte[] spirvBytes, Backend backend) {
        return _compilerCtx.AttemptSPVReflect(spirvBytes, backend);
    }

    public void Dispose() {
        _compilerCtx.Dispose();
    }
}
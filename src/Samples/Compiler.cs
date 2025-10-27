using ShaderCompilation;
using Vortice.SpirvCross;

namespace Samples;

//todo, this sucks, solely for sample purposes (todo include builtin online compiler for magpievk)

public unsafe class ShaderCompiler : IDisposable {
    public CompilerCtx CompilerCtx;

    public ShaderCompiler() {
        CompilerCtx = new CompilerCtx();
        CompilerCtx._options.CustomIncludeHandler = DefaultIncludeHandler;
    }

    public void SetCustomIncludeHandler(CompilerCtx.Options.IncludeHandler handler) {
        CompilerCtx._options.CustomIncludeHandler = handler;
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
        CompilerCtx._options.SetSourceLanguage(LangKind.Glsl);
        CompilerCtx._options.SetTargetEnv(TargetEnv.Vulkan, Vortice.Vulkan.VkVersion.Version_1_3);
        CompilerCtx._options.SetTargetSpirv(SpirvVersion.Spirv13);
        CompilerCtx._options.SetGenerateDebugInfo(debug);
        CompilerCtx._options.SetOptimizationLevel(debug ? OptimizationLevel.None : OptimizationLevel.Performance);

        string shaderCode = File.ReadAllText(path);

        using (var compileResult = CompilerCtx.Compile(shaderCode, kind, path, "main")) {
            if (compileResult.CompilationStatus != Status.Success) {
                if (compileResult.NumWarnings > 0) {
                    Console.WriteLine($"shader compilation warnings for {path}:\n{compileResult.ErrorMessage}");
                }
                throw new Exception($"failed to compile shader {path}: {compileResult.ErrorMessage}");
            }
            
            if (compileResult.NumWarnings > 0) {
                 Console.WriteLine($"shader compilation warnings for {path}:\n{compileResult.ErrorMessage}");
            }
            

            return compileResult.GetBytesCopy();
        }
    }
    
    public ReflectedShaderData ReflectShader(byte[] spirvBytes, Backend backend) {
        return CompilerCtx.AttemptSpvReflect(spirvBytes, backend);
    }

    public void Dispose() {
        CompilerCtx.Dispose();
    }
}
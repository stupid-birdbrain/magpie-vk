using ShaderCompilation;
using ShaderCompilation.Models;
using Vortice.SpirvCross;

namespace Samples;

//todo, this sucks, solely for sample purposes (todo include builtin online compiler for magpievk)

public unsafe class ShaderCompiler : IDisposable {
    public CompilerCtx CompilerCtx;

    public ShaderCompiler() {
        CompilerCtx = new CompilerCtx(Backend.GLSL);
        CompilerCtx.Options.CustomIncludeHandler = DefaultIncludeHandler;
    }

    public void SetCustomIncludeHandler(CompilerOptions.IncludeHandler handler) {
        CompilerCtx.Options.CustomIncludeHandler = handler;
    }

    private Include DefaultIncludeHandler(string requestedSource, string requestingSource, IncludeType type) {
        string resolvedPath;
        string requestingSourceDir = Path.GetDirectoryName(requestingSource) ?? string.Empty;
        if (type == IncludeType.Relative && !string.IsNullOrEmpty(requestingSourceDir)) {
            resolvedPath = Path.Combine(requestingSourceDir, requestedSource);
            if (File.Exists(resolvedPath)) {
                return new Include(resolvedPath, File.ReadAllText(resolvedPath));
            }
        }

        resolvedPath = "resources";
        if (File.Exists(resolvedPath)) {
            return new Include(resolvedPath, File.ReadAllText(resolvedPath));
        }

        if (File.Exists(requestedSource)) {
            return new Include(requestedSource, File.ReadAllText(requestedSource));
        }
        
        string errorMessage = $"could not resolve include '{requestedSource}' requested by '{requestingSource}'.";
        Console.Error.WriteLine(errorMessage);
        return new Include(requestedSource, "", errorMessage);
    }

    public ReadOnlySpan<byte> CompileShader(string path, ShaderKind kind, bool debug = false) {
        CompilerCtx.Options.SetSourceLanguage(LangKind.Glsl);
        CompilerCtx.Options.SetTargetEnv(TargetEnv.Vulkan, Vortice.Vulkan.VkVersion.Version_1_3);
        CompilerCtx.Options.SetTargetSpirv(SpirvVersion.Spirv13);
        CompilerCtx.Options.SetGenerateDebugInfo(debug);
        CompilerCtx.Options.SetOptimizationLevel(debug ? OptimizationLevel.None : OptimizationLevel.Performance);

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
    
    public ReflectedShaderData ReflectShader(byte[] spirvBytes) {
        return CompilerCtx.AttemptSpvReflect(spirvBytes);
    }

    public void Dispose() {
        CompilerCtx.Dispose();
    }
}
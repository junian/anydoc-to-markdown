using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.IO;

namespace AnyDocToMarkdown.Native
{
    /// <summary>
    /// DllImport surface, extended with an <see cref="AnydocNative"/> static
    /// constructor that registers a platform-specific loader mapping the logical
    /// library name to <c>runtimes/{platform}-{arch}/native/</c>, the layout both
    /// NuGet and this project's build output use.
    /// </summary>
    /// <remarks>
    /// <see cref="System.Runtime.InteropServices.NativeLibrary"/> (and the
    /// <c>DllImportResolver</c> delegate it takes) only exist on .NET Core 3.0
    /// and later, so a netstandard2.0 assembly cannot reference them at compile
    /// time. The registration below therefore happens through reflection with a
    /// dynamically constructed delegate, keeping the compiled surface clean for
    /// every consuming runtime while still attaching the real resolver on runtimes
    /// that provide it.
    /// </remarks>
    internal static unsafe partial class AnydocNative
    {
        static AnydocNative()
        {
            RegisterResolver();
        }

        private static void RegisterResolver()
        {
            try
            {
                Type nativeLibrary = Type.GetType(
                    "System.Runtime.InteropServices.NativeLibrary, System.Runtime.InteropServices",
                    throwOnError: true)!;
                if (nativeLibrary is null)
                {
                    return; // .NET Framework or another runtime without it: rely on the package's own probing.
                }
                MethodInfo setResolver = nativeLibrary.GetMethod("SetDllImportResolver");
                if (setResolver is null)
                {
                    return;
                }
                Type resolverDelegateType = setResolver.GetParameters()[1].ParameterType;

                // Build a delegate of the runtime's DllImportResolver signature:
                // IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath).
                // The search-path value is unused; the loader resolves the runtimes/{rid}/native path itself.
                var dynamicMethod = new DynamicMethod(
                    "AnyDocResolveLibrary",
                    typeof(IntPtr),
                    new[] { typeof(string), typeof(Assembly), resolverDelegateType.GetMethod("Invoke")!.GetParameters()[2].ParameterType },
                    typeof(AnydocNative).Module);
                ILGenerator il = dynamicMethod.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); // libraryName
                il.Emit(OpCodes.Call, typeof(AnydocNative).GetMethod(
                    "ResolveLibrary", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null));
                il.Emit(OpCodes.Ret);

                setResolver.Invoke(null, new object[] { typeof(AnydocNative).Assembly, dynamicMethod.CreateDelegate(resolverDelegateType) });
            }
            catch
            {
                // The default resolver still works for apps that place the native
                // library on the ordinary search paths; do not block loading.
            }
        }

        private static IntPtr ResolveLibrary(string libraryName)
        {
            if (libraryName != __DllName)
            {
                return IntPtr.Zero;
            }

            string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
                : throw new PlatformNotSupportedException($"anydoc has no native library for {RuntimeInformation.OSDescription}");

            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => throw new PlatformNotSupportedException($"anydoc has no native library for {RuntimeInformation.OSArchitecture}"),
            };

            string prefix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "" : "lib";
            string ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib"
                : ".so";

            string relative = $"runtimes/{platform}-{arch}/native/{prefix}{libraryName}{ext}";
            foreach (string baseDir in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(AnydocNative).Assembly.Location) ?? "" })
            {
                if (string.IsNullOrEmpty(baseDir))
                {
                    continue;
                }
                string candidate = Path.Combine(baseDir, relative);
                if (File.Exists(candidate))
                {
                    return LoadByPath(candidate);
                }
            }
            // Last chance: let the default resolution attempt it (NuGet hosts
            // native libs on the default search path too).
            return IntPtr.Zero;
        }

        private static IntPtr LoadByPath(string candidate)
        {
            try
            {
                Type nativeLibrary = Type.GetType(
                    "System.Runtime.InteropServices.NativeLibrary, System.Runtime.InteropServices",
                    throwOnError: true)!;
                MethodInfo load = nativeLibrary.GetMethod("Load", new[] { typeof(string) });
                return (IntPtr)(load!.Invoke(null, new[] { candidate })!);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }
}
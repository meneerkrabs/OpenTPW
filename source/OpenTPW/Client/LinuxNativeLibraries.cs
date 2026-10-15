using System.Reflection;
using System.Runtime.InteropServices;

namespace OpenTPW;

/// <summary>
/// Linux native-library fixes for dependencies that predate glibc 2.34. The Vulkan bindings
/// Veldrid uses (<c>vk</c> 1.0.25) import <c>dlopen</c>/<c>dlerror</c> from "libdl", which .NET
/// probes as <c>libdl.so</c>; current distributions ship only the versioned <c>libdl.so.2</c>
/// (the symbols live in libc), so creating the Vulkan device failed with DllNotFoundException.
/// </summary>
internal static class LinuxNativeLibraries
{
	private static bool registered;

	public static void Register()
	{
		if ( registered || !OperatingSystem.IsLinux() )
			return;
		registered = true;
		NativeLibrary.SetDllImportResolver( typeof( Vulkan.VulkanNative ).Assembly, Resolve );
	}

	internal static IntPtr Resolve( string libraryName, Assembly assembly, DllImportSearchPath? searchPath )
	{
		if ( libraryName != "libdl" )
			return IntPtr.Zero;
		foreach ( var candidate in new[] { "libdl.so.2", "libc.so.6" } )
			if ( NativeLibrary.TryLoad( candidate, out var handle ) )
				return handle;
		return IntPtr.Zero;
	}
}

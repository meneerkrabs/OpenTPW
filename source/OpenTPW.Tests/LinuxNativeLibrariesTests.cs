using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class LinuxNativeLibrariesTests
{
	[TestMethod]
	public void LibdlResolvesToTheVersionedLibraryOnLinux()
	{
		var assembly = typeof( Vulkan.VulkanNative ).Assembly;
		Assert.AreEqual( IntPtr.Zero, LinuxNativeLibraries.Resolve( "libvulkan.so.1", assembly, null ), "other libraries use the default probing" );
		if ( !OperatingSystem.IsLinux() )
			Assert.Inconclusive( "libdl is only redirected on Linux." );
		Assert.AreNotEqual( IntPtr.Zero, LinuxNativeLibraries.Resolve( "libdl", assembly, null ) );
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenTPW.Tests;

// Capture fixtures exercise real staff-owner cleanup without creating a GPU renderer.
// They also pin the asset file system to an empty directory: other tests point the global FileSystem at the original game
// data and leave it there, and then Level.SetupStaff would load real staff atlases and create textures without a device.
internal sealed class AutomaticCheckpointCpuRendererScope : IDisposable
{
	private readonly Renderer? previous = Global.Render;
	private readonly Renderer renderer = (Renderer)RuntimeHelpers.GetUninitializedObject( typeof( Renderer ) );
	private readonly BaseFileSystem? previousFiles = FileSystem;
	private readonly string assets = Directory.CreateTempSubdirectory( "opentpw-auto-empty-assets-" ).FullName;

	public AutomaticCheckpointCpuRendererScope()
	{
		typeof( Renderer ).GetField( "_deletionQueue", BindingFlags.Instance | BindingFlags.NonPublic )!
			.SetValue( renderer, new List<Action>() );
		Global.Render = renderer;
		FileSystem = new BaseFileSystem( assets );
	}

	public void Dispose()
	{
		try
		{
			// Run the actual queued cleanup. These synthetic fixtures create no GPU resources.
			renderer.FlushDeletions();
		}
		finally
		{
			Global.Render = previous!;
			FileSystem = previousFiles!;
			Directory.Delete( assets, true );
		}
	}
}

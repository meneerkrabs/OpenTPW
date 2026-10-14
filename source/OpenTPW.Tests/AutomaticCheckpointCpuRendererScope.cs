using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OpenTPW.Tests;

// Capture fixtures exercise real staff-owner cleanup without creating a GPU renderer.
internal sealed class AutomaticCheckpointCpuRendererScope : IDisposable
{
	private readonly Renderer? previous = Global.Render;
	private readonly Renderer renderer = (Renderer)RuntimeHelpers.GetUninitializedObject( typeof( Renderer ) );

	public AutomaticCheckpointCpuRendererScope()
	{
		typeof( Renderer ).GetField( "_deletionQueue", BindingFlags.Instance | BindingFlags.NonPublic )!
			.SetValue( renderer, new List<Action>() );
		Global.Render = renderer;
	}

	public void Dispose()
	{
		try
		{
			// Run the actual queued cleanup. These synthetic fixtures create no GPU resources.
			typeof( Renderer ).GetMethod( "ProcessDeletionQueue", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( renderer, null );
		}
		finally { Global.Render = previous!; }
	}
}

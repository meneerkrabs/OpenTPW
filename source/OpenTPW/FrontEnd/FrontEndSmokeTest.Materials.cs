using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace OpenTPW;

internal sealed partial class FrontEndSmokeTest
{
	/// <summary>Checks draw-order uniforms and cached bindings on the running renderer's actual GPU.</summary>
	private void PlanMaterialRendering()
	{
		Material<ObjectUniformBuffer>? material = null;
		Model? model = null, secondModel = null;
		Texture? texture = null;
		Texture? replacement = null;
		Framebuffer? target = null;
		Veldrid.Texture? color = null, depth = null, resolved = null;
		ResourceSet[]? stable = null, replaced = null, recompiled = null;
		var outsideUpload = true;
		var firstRetired = false;
		const uint Width = 64, Height = 32;

		ObjectUniformBuffer Uniform( float x ) => new()
		{
			g_mModel = Matrix4x4.CreateTranslation( x, 0, 0 ),
			g_mView = Matrix4x4.Identity,
			g_mProj = Matrix4x4.Identity
		};

		void Draw()
		{
			Require( global::Global.Render.IsFrameRecording, "material uniforms are recorded inside the frame" );
			var commands = global::Global.Render.CommandList;
			commands.SetFramebuffer( target! );
			commands.SetFullViewports();
			commands.SetFullScissorRects();
			commands.ClearColorTarget( 0, RgbaFloat.Black );
			if ( !outsideUpload )
				material!.Set( "ObjectUniformBuffer", Uniform( -0.5f ) );
			(firstRetired ? secondModel : model)!.Draw();
			var left = material!.GetResourceSets();
			material.Set( "ObjectUniformBuffer", Uniform( 0.5f ) );
			secondModel!.Draw();
			Require( ReferenceEquals( left, material.GetResourceSets() ), "uniform values retain the same binding cache" );
			commands.ResolveTexture( color!, resolved! );
			commands.SetFramebuffer( global::Global.Render.MultisampledFramebuffer );
			commands.SetFullViewports();
			commands.SetFullScissorRects();
		}

		void VerifyPixels( byte red, byte green )
		{
			using var staging = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
				Width, Height, 1, 1, resolved!.Format, TextureUsage.Staging ) );
			using var commands = Device.ResourceFactory.CreateCommandList();
			commands.Begin();
			commands.CopyTexture( resolved, staging );
			commands.End();
			Device.SubmitCommands( commands );
			Device.WaitForIdle();
			var mapped = Device.Map( staging, MapMode.Read );
			try
			{
				var pixel = new byte[4];
				foreach ( var x in new[] { 16, 48 } )
				{
					Marshal.Copy( IntPtr.Add( mapped.Data, checked((int)(Height / 2 * mapped.RowPitch + x * 4)) ), pixel, 0, 4 );
					var redChannel = resolved.Format == PixelFormat.B8_G8_R8_A8_UNorm ? 2 : 0;
					Require( pixel[redChannel] == red && pixel[1] == green && pixel[3] == 255,
						$"shared-material draw at x={x} retains its transform and texture pixels" );
				}
				Marshal.Copy( IntPtr.Add( mapped.Data, checked((int)(Height / 2 * mapped.RowPitch + Width / 2 * 4)) ), pixel, 0, 4 );
				Require( pixel[0] == 0 && pixel[1] == 0 && pixel[2] == 0, "the two differently transformed draws leave a black gap" );
			}
			finally { Device.Unmap( staging ); }
		}

		Do( "material draw fixture starts", () =>
		{
			var world = global::Global.Render.MultisampledFramebuffer;
			var source = world.ColorTargets[0].Target;
			color = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
				Width, Height, 1, 1, source.Format, TextureUsage.RenderTarget, source.SampleCount ) );
			depth = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
				Width, Height, 1, 1, world.DepthTarget!.Value.Target.Format, TextureUsage.DepthStencil, source.SampleCount ) );
			resolved = Device.ResourceFactory.CreateTexture( TextureDescription.Texture2D(
				Width, Height, 1, 1, source.Format, TextureUsage.RenderTarget | TextureUsage.Sampled ) );
			target = Device.ResourceFactory.CreateFramebuffer( new FramebufferDescription( depth, color ) );
			texture = new Texture( [220, 0, 0, 255], 1, 1, TextureFlags.PointFilter );
			replacement = new Texture( [0, 200, 0, 255], 1, 1, TextureFlags.PointFilter );
			material = new Material<ObjectUniformBuffer>( "content/shaders/unlit.shader", MaterialFlags.DisableDepth );
			material.Set( "Color", texture );
			Vertex V( float x, float y ) => new( new OpenTPW.Vector3( x, y, 0.5f ), OpenTPW.Vector3.Up, new OpenTPW.Vector2( 0.5f, 0.5f ) );
			var a = V( -0.2f, -0.4f ); var b = V( 0.2f, -0.4f ); var c = V( -0.2f, 0.4f ); var d = V( 0.2f, 0.4f );
			Vertex[] vertices = [a, b, c, b, d, c, a, c, b, b, c, d];
			model = new Model( vertices, material );
			secondModel = new Model( vertices, material );
			Require( !global::Global.Render.IsFrameRecording, "the outside-frame uniform upload runs after frame submission" );
			material.Set( "ObjectUniformBuffer", Uniform( -0.5f ) );
			stable = material.GetResourceSets();
			global::Global.Render.OnRender += Draw;
		} );
		Wait( "material outside-frame draw", 1 );
		Do( "material outside-frame pixels", () => { VerifyPixels( 220, 0 ); outsideUpload = false; } );
		Wait( "material cache stable frames", 3 );
		Do( "material cache stable", () =>
		{
			VerifyPixels( 220, 0 );
			Require( ReferenceEquals( stable, material!.GetResourceSets() ), "stable draws reuse the original resource sets across frames" );
			// Texture.Reload swaps these native identities in place; the binding must follow without another Set call.
			(texture!.NativeTexture, replacement!.NativeTexture) = (replacement.NativeTexture, texture.NativeTexture);
			(texture.NativeTextureView, replacement.NativeTextureView) = (replacement.NativeTextureView, texture.NativeTextureView);
		} );
		Wait( "material texture replacement frames", 2 );
		Do( "material texture replacement", () =>
		{
			VerifyPixels( 0, 200 );
			replaced = material!.GetResourceSets();
			Require( !ReferenceEquals( stable, replaced ) && stable!.All( set => set.IsDisposed ),
				"a native texture replacement rebuilds and releases the old binding cache" );
			material.Shader.Recompile();
		} );
		Wait( "material shader replacement frames", 2 );
		Do( "material shader replacement", () =>
		{
			VerifyPixels( 0, 200 );
			recompiled = material!.GetResourceSets();
			Require( !ReferenceEquals( replaced, recompiled ) && replaced!.All( set => set.IsDisposed ),
				"recompiling the shader rebuilds and releases the old binding cache" );
			model!.ReleaseBuffers();
			model.ReleaseBuffers();
			firstRetired = true;
			Require( recompiled.All( set => !set.IsDisposed ), "model disposal keeps bindings alive until the submitted frame is retired" );
		} );
		Wait( "shared material survives model retirement", 2 );
		Do( "shared material retains its surviving model", () =>
		{
			VerifyPixels( 0, 200 );
			Require( recompiled!.All( set => set.IsDisposed ), "retiring the first model disposes its cached sets" );
			recompiled = material!.GetResourceSets();
			global::Global.Render.OnRender -= Draw;
			secondModel!.ReleaseBuffers();
			material.ReleaseOwnedResources();
			Asset.All.Remove( texture! );
			Asset.All.Remove( replacement! );
			global::Global.Render.ScheduleDelete( () =>
			{
				target!.Dispose(); color!.Dispose(); depth!.Dispose(); resolved!.Dispose();
				texture!.NativeTextureView.Dispose(); texture.NativeTexture.Dispose();
				replacement!.NativeTextureView.Dispose(); replacement.NativeTexture.Dispose();
			} );
		} );
		Wait( "material disposal frames", 2 );
		Do( "material bindings released", () =>
		{
			Require( recompiled!.All( set => set.IsDisposed ), "model cleanup disposes the cached GPU resource sets" );
			Log.Trace( "Material rendering: outside-frame upload, two shared-material transforms, stable cache, native texture/shader invalidation and cache disposal passed GPU readback." );
		} );
	}
}

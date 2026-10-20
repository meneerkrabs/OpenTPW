using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using OpenTPW.Tests;
using static OpenTPW.Tests.ParticleSystemTests;

namespace OpenTPW;

/// <summary>Asset-free, opt-in framebuffer diagnostic; excluded from ordinary browser builds.</summary>
public static partial class Program
{
	private static Renderer? particleOracleRenderer;
	private static Matrix4x4 particleOracleView, particleOracleProjection;
	private static Veldrid.GraphicsDevice? particleOraclePreviousDevice;

	[JSExport]
	public static void ParticleOracleStart()
	{
		if ( particleOracleRenderer != null || Global.Render != null || Entity.All.Count != 0 || Level.Current != null )
			throw new InvalidOperationException( "The particle oracle requires a fresh page without a running game." );
		WriteContent();
		particleOracleView = Camera.ViewMatrix;
		particleOracleProjection = Camera.ProjMatrix;
		particleOraclePreviousDevice = Global.Device;
		Global.Render = particleOracleRenderer = new Renderer( new DisplaySettings { Width = 320, Height = 200, FrameCap = 0 }, null );
		particleOracleRenderer.Start();
	}

	private readonly record struct OracleParticle( uint Colour, int Flags = 0, int Z = 16 );

	[JSExport]
	public static string ParticleOracleDraw( string name )
	{
		var renderer = particleOracleRenderer ?? throw new InvalidOperationException( "Start the particle oracle first." );
		OracleParticle[] spec = name switch
		{
			"red0" => [new( 0x00FF0000 )],
			"redEF" => [new( 0xEFFF0000 )],
			"redF0" => [new( 0xF0FF0000 )],
			"redFF" => [new( 0xFFFF0000 )],
			"blueFF" => [new( 0xFF0000FF )],
			"additive0" => [new( 0x00FF0000, 0x2004 )],
			"oneOne0" => [new( 0x00FF0000, 4 )],
			"nearZeroThenFar" => [new( 0x00FF0000 ), new( 0xFF0000FF, 0, 48 )],
			"nearF0ThenFar" => [new( 0xF0FF0000 ), new( 0xFF0000FF, 0, 48 )],
			"noWriteZeroThenFar" => [new( 0x00FF0000, 0x4000 ), new( 0xFF0000FF, 0, 48 )],
			"nearThenFar" => [new( 0xFFFF0000 ), new( 0xFF0000FF, 0, 48 )],
			"farThenNear" => [new( 0xFF0000FF, 0, 48 ), new( 0xFFFF0000 )],
			"noWriteNearThenFar" => [new( 0xFFFF0000, 0x4000 ), new( 0xFF0000FF, 0, 48 )],
			// A later, nearer opaque blue must replace the earlier red and additive green. Reordering by material makes green leak through.
			"interleaved" => [new( 0xFFFF0000, 0, 48 ), new( 0xFF00FF00, 0x2004, 32 ), new( 0xFF0000FF )],
			"screen0" => [new( 0x00FF0000, 0x40000 )],
			"screenFF" => [new( 0xFFFF0000, 0x40000 )],
			"screenThenScreen" => [new( 0xFFFF0000, 0x40000 ), new( 0xFF0000FF, 0x40000 )],
			_ => throw new ArgumentException( $"Unknown particle oracle case: {name}" )
		};
		var templates = spec.Select( particle =>
		{
			var screen = (particle.Flags & 0x40000) != 0;
			var size = screen ? 16384 : 512;
			var fields = new List<(int, int, int)> { Life( 1000 ), Burst( 1 ), Cap( 1 ), ParticleLife( 1000 ),
				(0x74, 2, size), (0xA6, 2, size), (0x70, 4, particle.Flags), (5, 1, screen ? 1 : 0) };
			for ( var index = 0; index < 16; index++ )
				fields.Add( (0xD8 + index * 4, 4, unchecked( (int)particle.Colour )) );
			return Template( fields.ToArray() );
		} ).ToArray();
		var system = New( templates );
		for ( var index = 0; index < spec.Length; index++ )
			if ( (system.Create( index, (spec[index].Flags & 0x40000) != 0 ? (50000, 0, 37500) : (0, 0, spec[index].Z * 16) ) & 0xFFFF) != index )
				throw new InvalidOperationException( "Unexpected emitter slot order." );
		// Burst creation leaves emitter bounds empty; the normal simulation update computes them.
		// Constant colour/size and zero velocity keep the requested fixture invariant.
		system.Tick(); // The first pool tick is the normal startup skip.
		system.Tick(); // The second tick updates particles and their bounds.
		var bounds = new List<ParticleEmitterBounds>();
		if ( system.CollectBounds( bounds ) != spec.Length )
			throw new InvalidOperationException( "Synthetic emitter bounds were not populated by the simulation tick." );
		var snapshot = new List<ParticleSprite>();
		if ( system.Snapshot( snapshot ) != spec.Length || snapshot.Where( (sprite, index) =>
			sprite.Emitter != index || sprite.Colour != spec[index].Colour || sprite.Size != ((spec[index].Flags & 0x40000) != 0 ? 16384 : 512) || sprite.FrameCount != 0
			|| sprite.RenderFlags != spec[index].Flags || sprite.ScreenSpace != ((spec[index].Flags & 0x40000) != 0)
			|| (!sprite.ScreenSpace && (sprite.X != 0 || sprite.Y != 0 || sprite.Z != spec[index].Z)) ).Any() )
			throw new InvalidOperationException( "Synthetic snapshot differs from the requested particles." );
		ParticleRenderer? particles = null;
		var previousRender = renderer.OnRender;
		var view = Camera.ViewMatrix;
		var projection = Camera.ProjMatrix;
		try
		{
			particles = new ParticleRenderer( system, new ParticleSpriteAtlas( [] ), position => position, () => true, 1f );
			renderer.OnRender = () =>
			{
				Camera.ViewMatrix = Matrix4x4.Identity;
				Camera.ProjMatrix = Matrix4x4.Identity;
				particles.Render();
			};
			var before = renderer.FramesDrawn;
			var frameOpen = renderer.Frame();
			if ( !frameOpen || renderer.FramesDrawn != before + 1 || particles.DrawnParticles != spec.Length
				|| particles.DrawnScreenParticles != spec.Count( particle => (particle.Flags & 0x40000) != 0 ) )
				throw new InvalidOperationException( $"Particle frame {name}: open={frameOpen}, frames={before}->{renderer.FramesDrawn}, drawn={particles.DrawnParticles}/{spec.Length}, screen={particles.DrawnScreenParticles}/{spec.Count( particle => (particle.Flags & 0x40000) != 0 )}." );
			return JsonSerializer.Serialize( new { name, frame = renderer.FramesDrawn, drawn = particles.DrawnParticles,
				screen = particles.DrawnScreenParticles, width = renderer.MultisampledFramebuffer.Width,
				height = renderer.MultisampledFramebuffer.Height, colours = snapshot.Select( sprite => sprite.Colour ).ToArray() } );
		}
		finally
		{
			renderer.OnRender = previousRender;
			Camera.ViewMatrix = view;
			Camera.ProjMatrix = projection;
			particles?.Delete();
		}
	}

	[JSExport]
	public static void ParticleOracleStop()
	{
		var renderer = particleOracleRenderer;
		if ( renderer == null ) return;
		try
		{
			Global.Device.WaitForIdle();
			typeof( Renderer ).GetMethod( "ProcessDeletionQueue", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( renderer, null );
			renderer.Stop();
			renderer.Window.SdlWindow.Close();
		}
		finally
		{
			Camera.ViewMatrix = particleOracleView;
			Camera.ProjMatrix = particleOracleProjection;
			Global.Render = null!;
			Global.Device = particleOraclePreviousDevice!;
			particleOracleRenderer = null;
		}
		// The isolated browser target is closed by the harness, releasing its renderer/context resources.
	}
}

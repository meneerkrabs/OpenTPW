using System.Runtime.CompilerServices;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Connects the object scripts' EVENT 1/2 and ADDOBJ 1/2 requests to the level's particle renderer, created by the first request (or by a
/// resumed park that held live emitters). Hosts without a renderer (plain simulation tests) and levels without the particle files stay
/// particle-free.
/// </summary>
internal static class ParticleRendererModule
{
	private static WeakReference<Level>? unavailableFor;

	[ModuleInitializer]
	internal static void Attach()
	{
		OriginalObjectRuntime.ParticleRequested += request => Start( request );
		OriginalObjectRuntime.ParticleStarter = Start;
		OriginalObjectRuntime.ParticleKiller = Kill;
		OriginalObjectRuntime.ParticleAlive = IsAlive;
		Level.ParticlesCaptured = Capture;
		Level.ParticlesResumed = Resume;
	}

	private static ParticleRenderer? RendererOf( Level level ) => ParticleRenderer.For( level );

	/// <summary>True when the handle names a live emitter, or when there is no pool to ask.</summary>
	private static bool IsAlive( int handle ) =>
		Level.Current is not { } level || RendererOf( level ) is not { } renderer || renderer.Particles.IsAlive( handle );

	/// <summary>Starts a prepared request and returns the pool's handle (-1 when nothing started).</summary>
	private static int Start( ParticleEventRequest request )
	{
		if ( global::Global.Render == null || Level.Current is not { } level )
			return -1;
		var renderer = RendererOf( level ) ?? Create( level );
		if ( renderer == null )
			return -1;
		// [BIN:TPW-EXE:0x5573D0 dispatcher] the key's world position * 1024 truncated (a key without a matrix is the world origin); a control other than 1000 is set after creation
		var position = request.AtWorldOrigin ? (0, 0, 0) : ToNative( level, request.EnginePosition );
		if ( request.Type == 2 && (request.AtWorldOrigin || request.EngineUnitDirection != null) )
		{
			var direction = request.AtWorldOrigin ? (0, 0, (int)ParticleKeyResolver.NativeDirectionMultiplier) : ToNativeDirection( request.EngineUnitDirection!.Value );
			return renderer.Particles.CreateDirected( request.EffectId, position, direction, request.Control );
		}
		return renderer.Particles.Create( request.EffectId, position, request.Control );
	}

	/// <summary>An emitter the caller keeps: the handle belongs to its own <see cref="ParticleSystem"/> (slot and generation only mean something there), so a handle left over from another level can never touch a newer system.</summary>
	internal readonly record struct EmitterRef( ParticleSystem System, int Handle, Level Level );

	/// <summary>Starts an emitter that the caller keeps (the bump rides' break smoke), or null when there is no renderer or the pool refuses.</summary>
	internal static EmitterRef? StartEmitter( int effectId, SVector3 engine )
	{
		if ( global::Global.Render == null || Level.Current is not { } level )
			return null;
		var renderer = RendererOf( level ) ?? Create( level );
		if ( renderer == null )
			return null;
		var handle = renderer.Particles.Create( effectId, ToNative( level, engine ) );
		return handle is 0 or -1 ? null : new EmitterRef( renderer.Particles, handle, level );
	}

	internal static void MoveEmitter( EmitterRef emitter, SVector3 engine ) =>
		emitter.System.Move( emitter.Handle, ToNative( emitter.Level, engine ) );

	internal static void KillEmitter( EmitterRef emitter ) => emitter.System.Kill( emitter.Handle );

	private static void Kill( int handle )
	{
		if ( Level.Current is { } level && RendererOf( level ) is { } renderer )
			renderer.Particles.Kill( handle );
	}

	/// <summary>The live emitters and effectors of the level's pool for a park-resume save, or null when there are none.</summary>
	private static ParticleSystemState? Capture( Level level ) => RendererOf( level )?.Particles.Capture();

	/// <summary>Rebuilds the saved pool for a resumed park (no particles, as in the original's load): creates the renderer and restores it.</summary>
	private static void Resume( Level level, ParticleSystemState state )
	{
		if ( global::Global.Render == null )
			return;
		var renderer = RendererOf( level ) ?? Create( level );
		try
		{
			renderer?.Particles.Restore( state );
		}
		catch ( InvalidDataException exception )
		{
			// presentation only: a saved pool the template file cannot rebuild is dropped, the park still resumes
			Log.Warning( $"The saved particles were dropped ({exception.Message})." );
			renderer?.Particles.Clear();
		}
	}

	private static ParticleRenderer? Create( Level level )
	{
		if ( unavailableFor != null && unavailableFor.TryGetTarget( out var failed ) && ReferenceEquals( failed, level ) )
			return null;
		try
		{
			var templates = new ParticleTemplateFile( "/Particle/Tp2.plb" );
			var atlas = ParticleSpriteAtlas.Load();
			if ( atlas != null )
				// the entity takes Level.Current at construction; a resumed park's candidate is not necessarily current yet
				return new ParticleRenderer( new ParticleSystem( templates.Templates, effectors: templates.EffectorTemplates ), atlas,
					position => ToEngine( level, position ), () => !(level.SimulationTimeScale > 0f), level: level );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidDataException or NullReferenceException )
		{
			Log.Warning( $"The particle templates are not available ({exception.Message}); particles are not drawn." );
		}
		unavailableFor = new WeakReference<Level>( level );
		return null;
	}

	/// <summary>An original position (Y up) in engine space: through the heightfield's centring when the level has one, else scaled only.</summary>
	internal static SVector3 ToEngine( Level level, SVector3 position ) =>
		level.OriginalPark?.Heightfield is { } field
			? OriginalParkPlacement.ToEngine( field, position )
			: new SVector3( position.X, position.Z, position.Y ) * OriginalParkPlacement.ModelScale;

	/// <summary>The inverse of <see cref="ToEngine"/>: an engine position (Z up) as an original position (Y up).</summary>
	internal static SVector3 ToOriginal( Level level, SVector3 engine )
	{
		const float scale = OriginalParkPlacement.ModelScale;
		if ( level.OriginalPark?.Heightfield is { } field )
		{
			var origin = OriginalParkPlacement.GetOrigin( field );
			return new SVector3( (engine.X + origin.X) / scale, engine.Z / scale, (engine.Y + origin.Y) / scale );
		}
		return new SVector3( engine.X, engine.Z, engine.Y ) / scale;
	}

	/// <summary>The native position of an engine position: original units times 1024, Y up, truncated toward zero (the dispatcher's unit before the constructor's >> 4).</summary>
	// [BIN:TPW-EXE:0x5573D0 EVENT dispatcher] world position * 1024.0 (constant 0x700FDC) through _ftol, which truncates
	// [APPROX:PTCL-906] the conversion inverts OpenTPW's placement; the pool runs with the options' default density (PARTICLEDENSITY 500), 2048 particles and the original's flat-zero ground stub, and the density option is not exposed
	internal static (int X, int Y, int Z) ToNative( Level level, SVector3 engine )
	{
		var original = ToOriginal( level, engine );
		return (Fixed( original.X ), Fixed( original.Y ), Fixed( original.Z ));
	}

	/// <summary>The native direction of an engine unit direction (Z up): original axes (Y up), times 1024.</summary>
	// [BIN:TPW-EXE:0x556B90 particle key resolver] the direction is normalised and multiplied by 1024
	internal static (int X, int Y, int Z) ToNativeDirection( SVector3 engineUnit ) =>
		(Fixed( engineUnit.X ), Fixed( engineUnit.Z ), Fixed( engineUnit.Y ));

	// [BIN:TPW-EXE:0x67A830 _ftol] the control word is set to chop: the conversion truncates toward zero
	internal static int Fixed( float value ) => (int)(Math.Clamp( value, -1e6f, 1e6f ) * (double)ParticleKeyResolver.NativeDirectionMultiplier);
}

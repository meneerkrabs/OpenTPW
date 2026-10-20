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
		OriginalObjectRuntime.ParticleFollower = Follow;
		Level.ParticlesCaptured = Capture;
		Level.ParticlesResumed = Resume;
		Level.ParticlesImported = ImportOriginal;
		Level.PlacedParticlesStarted = StartPlaced;
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
		// [BIN:TPW-EXE:0x557308 WALKOFF position] the walk effect takes each coordinate through ftol and shifts it left by 10: whole units, as a kept ADDOBJ effect follows
		var position = request.AtWorldOrigin ? (0, 0, 0) : request.WholeUnits ? WholeNative( level, request.EnginePosition ) : ToNative( level, request.EnginePosition );
		if ( request.Type == 2 && (request.AtWorldOrigin || request.EngineUnitDirection != null) )
		{
			var direction = request.AtWorldOrigin ? (0, 0, (int)ParticleKeyResolver.NativeDirectionMultiplier) : ToNativeDirection( request.EngineUnitDirection!.Value );
			return renderer.Particles.CreateDirected( request.EffectId, position, direction, request.Control );
		}
		var handle = renderer.Particles.Create( request.EffectId, position, request.Control );
		// [BIN:TPW-EXE:0x520030 volume extent] the object effects size the emitter's spawn volume right after creating it (the burst of the creation itself ran with the template's)
		if ( request.VolumeExtent is { } extent && handle is not (0 or -1) )
			renderer.Particles.SetVolumeExtent( handle, extent.X, extent.Y, extent.Z );
		return handle;
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

	/// <summary>The emitters live in the current level's pool (0 without one), for callers that leave room for the park's own effects.</summary>
	internal static int LiveEmitters() => Level.Current is { } level && RendererOf( level ) is { } renderer ? renderer.Particles.LiveEmitters : 0;

	/// <summary>The advisor's screen effects start in the current level's pool (at native positions, which screen-space templates read as screen points).</summary>
	internal static IAdvisorParticlePool AdvisorPool { get; } = new LevelAdvisorPool();

	private sealed class LevelAdvisorPool : IAdvisorParticlePool
	{
		public object? Create( int effectId, (int X, int Y, int Z) native )
		{
			if ( global::Global.Render == null || Level.Current is not { } level )
				return null;
			var renderer = RendererOf( level ) ?? ParticleRendererModule.Create( level );
			if ( renderer == null )
				return null;
			var handle = renderer.Particles.Create( effectId, native );
			return handle is 0 or -1 ? null : new EmitterRef( renderer.Particles, handle, level );
		}

		public void Move( object handle, (int X, int Y, int Z) native )
		{
			if ( handle is EmitterRef emitter )
				emitter.System.Move( emitter.Handle, native );
		}

		public void Kill( object handle )
		{
			if ( handle is EmitterRef emitter )
				emitter.System.Kill( emitter.Handle );
		}
	}

	internal static void MoveEmitter( EmitterRef emitter, SVector3 engine ) =>
		emitter.System.Move( emitter.Handle, ToNative( emitter.Level, engine ) );

	internal static void KillEmitter( EmitterRef emitter ) => emitter.System.Kill( emitter.Handle );

	/// <summary>Moves a kept ADDOBJ effect to its key: the position truncated to whole original units, times 1024; a type-2 effect also turns.</summary>
	// [BIN:TPW-EXE:0x55190F ADDOBJ follow position] ftol of each matrix float (chop), shifted left by 10: whole units only; [BIN:TPW-EXE:0x5519D8] the type-2 direction times 1024 through ftol
	private static void Follow( int handle, ParticleFollow follow )
	{
		if ( Level.Current is not { } level || RendererOf( level ) is not { } renderer )
			return;
		var position = (0, 0, 0);
		if ( !follow.AtWorldOrigin )
		{
			var original = ToOriginal( level, follow.EnginePosition );
			position = (WholeUnits( original.X ) << 10, WholeUnits( original.Y ) << 10, WholeUnits( original.Z ) << 10);
		}
		renderer.Particles.Move( handle, position );
		if ( follow.EngineUnitDirection is { } unit )
			renderer.Particles.SetDirection( handle, ToNativeDirection( unit ) );
		else if ( follow.Directed )
			renderer.Particles.SetDirection( handle, (0, 0, (int)ParticleKeyResolver.NativeDirectionMultiplier) );
	}

	private static (int X, int Y, int Z) WholeNative( Level level, SVector3 engine )
	{
		var original = ToOriginal( level, engine );
		return (WholeUnits( original.X ) << 10, WholeUnits( original.Y ) << 10, WholeUnits( original.Z ) << 10);
	}

	/// <summary>A coordinate truncated to whole units; a value within 1/1024 of a whole unit counts as that unit, as OpenTPW's engine round trip leaves whole values a few ulps short.</summary>
	// [EXT:sound-unit-round-trip] the same tolerance as the EVENT sounds' whole units (OriginalObjectRuntime.WholeUnits)
	internal static int WholeUnits( float value )
	{
		var clamped = Math.Clamp( value, -1e6f, 1e6f );
		var nearest = MathF.Round( clamped );
		return (int)(MathF.Abs( clamped - nearest ) < 1f / 1024f ? nearest : MathF.Truncate( clamped ));
	}

	private static void Kill( int handle )
	{
		if ( Level.Current is { } level && RendererOf( level ) is { } renderer )
			renderer.Particles.Kill( handle );
	}

	/// <summary>The initialized level pool for a park-resume save, including empty pools; null when no renderer is available.</summary>
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

	/// <summary>
	/// The pool of an original save (tp.exe loader 0x51F7A0): the saved emitters and effectors come back with the generator and the counters, the post-load pass 0x5200B0 then hides the
	/// screen-space emitters (the saved button sparkles), so what a loaded park shows is its world emitters (Jungle's waterfall).
	/// </summary>
	private static void ImportOriginal( Level level, SaveParticlesBlock block )
	{
		if ( global::Global.Render == null )
			return;
		try
		{
			var state = block.ToSystemState( new ParticleTemplateFile( "/Particle/Tp2.plb" ).Templates );
			if ( state != null )
				Resume( level, state );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"The saved particles were not imported ({exception.Message})." );
		}
	}

	/// <summary>Starts the land effects of a level's <c>scape.omp</c> in its pool: each record's effect at its place, taken as native units (1/1024 of an original unit), the handle dropped.</summary>
	// [BIN:TPW-EXE:0x550e00 scape.omp type 2] 0x521E60(effect, x, y, z) with the record's words, no conversion
	private static void StartPlaced( Level level, IReadOnlyList<PlacedObjectsFile.Record> records )
	{
		if ( global::Global.Render == null )
			return;
		var renderer = RendererOf( level ) ?? Create( level );
		if ( renderer == null )
			return;
		var started = StartPlacedIn( renderer.Particles, records );
		Log.Trace( $"Particles: {started} land effect(s) of scape.omp started in {level.LevelName}; the first at engine {ToEngine( level, new SVector3( records[0].Place.X, records[0].Place.Height, records[0].Place.Z ) / ParticleKeyResolver.NativeDirectionMultiplier )}." );
	}

	/// <summary>Starts every record's effect in <paramref name="system"/> (the pool's refusals, an unknown effect id or a full pool, start nothing) and returns how many emitters started.</summary>
	internal static int StartPlacedIn( ParticleSystem system, IReadOnlyList<PlacedObjectsFile.Record> records )
	{
		var started = 0;
		foreach ( var record in records )
		{
			if ( record.Type == PlacedObjectsFile.ParticleType && system.Create( record.Effect, record.Place ) is not (0 or -1) )
				started++;
		}
		return started;
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
				return new ParticleRenderer( new ParticleSystem( templates.Templates, ParticleSystem.OptionsDensity( IGraphicsSettings.Instance ), effectors: templates.EffectorTemplates ), atlas,
					position => ToEngine( level, position ), () => !(level.SimulationTimeScale > 0f), level: level );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidDataException or NullReferenceException )
		{
			Log.Warning( $"The particle templates are not available ({exception.Message}); particles are not drawn." );
		}
		unavailableFor = new WeakReference<Level>( level );
		return null;
	}

	private static bool screenHostUnavailable;

	/// <summary>
	/// A particle host for the front end's screen-space effects: its own pool (the pool is global in the original, drawn by whichever scene is up),
	/// advanced from the frame time and deleted with the other entities when the lobby is torn down. Null when the template file or the sprites
	/// are missing (the effect is presentation only).
	/// </summary>
	// [BIN:TPW-EXE:0x5200B0 post-load pass] a park load kills the pool's screen-space emitters, so a front-end screen effect ends with the lobby
	internal static ParticleRenderer? CreateScreenRenderer()
	{
		if ( global::Global.Render == null || screenHostUnavailable )
			return null;
		try
		{
			var templates = new ParticleTemplateFile( "/Particle/Tp2.plb" );
			var atlas = ParticleSpriteAtlas.Load();
			if ( atlas != null )
				return new ParticleRenderer( new ParticleSystem( templates.Templates, ParticleSystem.OptionsDensity( IGraphicsSettings.Instance ), effectors: templates.EffectorTemplates ), atlas,
					position => position, () => false );
		}
		catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidDataException or NullReferenceException )
		{
			Log.Warning( $"The particle templates are not available ({exception.Message}); the lobby's screen effect is not drawn." );
		}
		screenHostUnavailable = true;
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
	// [BIN:TPW-EXE:0x556B90 particle key resolver] the pose is the key node's world position in original units (Y up), which ToOriginal recovers by undoing, constant for constant, the placement that made the engine pose (ObjectPlacement.ModelToEngine: 0.2 per unit, the grid origin, Z up); the constants cancel, so the native value does not depend on them, and it differs from the original's only by the float32 rounding of the engine pose (at most one native unit, 1/1024 of an original unit)
	// the pool runs with the detail options' PARTICLEDENSITY, 2048 particles and the original's flat-zero ground stub, as the original does
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

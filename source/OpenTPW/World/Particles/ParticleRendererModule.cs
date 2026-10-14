using System.Runtime.CompilerServices;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Connects the object scripts' EVENT 1/2 requests to the level's particle renderer, created by the first request. Hosts
/// without a renderer (plain simulation tests) and levels without the particle files stay particle-free.
/// </summary>
internal static class ParticleRendererModule
{
	private static WeakReference<Level>? unavailableFor;

	[ModuleInitializer]
	internal static void Attach() => OriginalObjectRuntime.ParticleRequested += Start;

	private static ParticleRenderer? RendererFor( out Level level )
	{
		level = null!;
		if ( global::Global.Render == null || Level.Current is not { } current )
			return null;
		level = current;
		return ParticleRenderer.Current is { } existing && ReferenceEquals( existing.Level, current ) ? existing : Create( current );
	}

	private static void Start( ParticleEventRequest request )
	{
		if ( RendererFor( out var level ) is not { } renderer )
			return;
		var position = ToNative( level, request.EnginePosition );
		// the resolver's control (1000) is the template's own draw scale, which the constructor already copies
		if ( request.Type == 2 && request.EngineUnitDirection is { } direction )
			renderer.Particles.CreateDirected( request.EffectId, position, ToNativeDirection( direction ) );
		else
			renderer.Particles.Create( request.EffectId, position );
	}

	/// <summary>An emitter the caller keeps: the handle belongs to its own <see cref="ParticleSystem"/> (slot and generation only mean something there), so a handle left over from another level can never touch a newer system.</summary>
	internal readonly record struct EmitterRef( ParticleSystem System, int Handle, Level Level );

	/// <summary>Starts an emitter that the caller keeps (the bump rides' break smoke), or null when there is no renderer or the pool refuses.</summary>
	internal static EmitterRef? StartEmitter( int effectId, SVector3 engine )
	{
		if ( RendererFor( out var level ) is not { } renderer )
			return null;
		var handle = renderer.Particles.Create( effectId, ToNative( level, engine ) );
		return handle >= 0 ? new EmitterRef( renderer.Particles, handle, level ) : null;
	}

	internal static void MoveEmitter( EmitterRef emitter, SVector3 engine ) =>
		emitter.System.Move( emitter.Handle, ToNative( emitter.Level, engine ) );

	internal static void KillEmitter( EmitterRef emitter ) => emitter.System.Kill( emitter.Handle );

	private static ParticleRenderer? Create( Level level )
	{
		if ( unavailableFor != null && unavailableFor.TryGetTarget( out var failed ) && ReferenceEquals( failed, level ) )
			return null;
		try
		{
			var templates = new ParticleTemplateFile( "/Particle/Tp2.plb" );
			var atlas = ParticleSpriteAtlas.Load();
			if ( atlas != null )
				return new ParticleRenderer( new ParticleSystem( templates.Templates ), atlas, position => ToEngine( level, position ), () => !(level.SimulationTimeScale > 0f) );
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

	/// <summary>The native position of an engine position: original units times 1024, Y up (the dispatcher's unit before the constructor's >> 4).</summary>
	// [BIN:STP-PPC:0x100AF9E8 EVENT dispatcher] position * 1024 into the constructor
	// [APPROX:PTCL-906] the conversion inverts OpenTPW's placement and the settings are not wired (density 1000, 2048 particles, no terrain callback)
	internal static (int X, int Y, int Z) ToNative( Level level, SVector3 engine )
	{
		var original = ToOriginal( level, engine );
		return (Fixed( original.X ), Fixed( original.Y ), Fixed( original.Z ));
	}

	/// <summary>The native direction of an engine unit direction (Z up): original axes (Y up), times 1024.</summary>
	// [BIN:STP-PPC:0x100AC7F0 particle key resolver] the direction is normalised and multiplied by 1024
	internal static (int X, int Y, int Z) ToNativeDirection( SVector3 engineUnit ) =>
		(Fixed( engineUnit.X ), Fixed( engineUnit.Z ), Fixed( engineUnit.Y ));

	private static int Fixed( float value ) => (int)MathF.Round( Math.Clamp( value, -1e6f, 1e6f ) * ParticleKeyResolver.NativeDirectionMultiplier );
}

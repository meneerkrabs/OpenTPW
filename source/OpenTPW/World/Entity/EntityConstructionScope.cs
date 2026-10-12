using System.Numerics;

namespace OpenTPW;

/// <summary>Stages a level's entities without retiring the current scene. Used synchronously between frames.</summary>
internal sealed class EntityConstructionScope : IDisposable
{
	private static EntityConstructionScope? active;
	private readonly SceneState previous;
	private readonly List<Entity> entities = new();
	private readonly HashSet<Asset> retainedAssets;
	private SceneState? candidate;
	private bool finished;

	internal static bool IsStaging => active != null && active.candidate == null;

	private readonly record struct SceneState( List<Entity> Entities, Level? Level, CameraMode Camera,
		float TargetExtent, Vector3 InitialTarget, Vector3 ListenerPosition, Vector3 ListenerForward )
	{
		public static SceneState Capture() => new( Entity.All, global::OpenTPW.Level.Current, global::OpenTPW.Camera.Mode,
			ParkCameraMode.TargetExtent, ParkCameraMode.InitialTarget, AudioListener.Position, AudioListener.Forward );

		public void Restore()
		{
			Entity.All = Entities;
			global::OpenTPW.Level.Current = Level!;
			global::OpenTPW.Camera.Mode = Camera;
			ParkCameraMode.TargetExtent = TargetExtent;
			ParkCameraMode.InitialTarget = InitialTarget;
			AudioListener.RestoreCaptured( ListenerPosition, ListenerForward );
		}
	}

	public EntityConstructionScope()
	{
		if ( active != null )
			throw new InvalidOperationException( "A scene construction transaction is already active." );
		previous = SceneState.Capture();
		// Assets registered before construction belong to the live scene and survive a rejected
		// candidate. Identity, not path equality: two materials from the same shader file are peers.
		retainedAssets = new HashSet<Asset>( Asset.All, ReferenceEqualityComparer.Instance );
		active = this;
		Entity.All = entities;
	}

	/// <summary>Retains the candidate and restores the live scene for its normal retirement, after all validation succeeds.</summary>
	public void Detach()
	{
		RequireActive();
		if ( candidate != null || !ReferenceEquals( Entity.All, entities ) )
			throw new InvalidOperationException( "The candidate scene is not active." );
		candidate = SceneState.Capture();
		previous.Restore();
	}

	/// <summary>Activates the detached candidate after the old scene has been retired. Reasserts its camera as well.</summary>
	public void Activate()
	{
		RequireActive();
		if ( candidate is not { } ready || !ReferenceEquals( Entity.All, previous.Entities ) )
			throw new InvalidOperationException( "Detach the validated candidate before activating it." );
		if ( previous.Entities.Count != 0 )
			throw new InvalidOperationException( "Retire the previous scene before activating the candidate." );
		ready.Restore();
		finished = true;
		active = null;
		if ( ready.Camera is ParkCameraMode park ) park.UpdateAudioListener();
	}

	private void RequireActive()
	{
		if ( finished || !ReferenceEquals( active, this ) )
			throw new ObjectDisposedException( nameof( EntityConstructionScope ) );
	}

	/// <summary>On failure deletes only candidate entities, restores the live scene and retires the
	/// candidate's GPU materials even if a cleanup callback throws.</summary>
	public void Dispose()
	{
		if ( finished ) return;
		RequireActive();
		var staged = candidate ?? SceneState.Capture();
		var failures = new List<Exception>();
		try
		{
			staged.Restore();
			if ( staged.Level != null && !ReferenceEquals( staged.Level, previous.Level ) )
			{
				try { staged.Level.Dispose(); }
				catch ( Exception error ) { failures.Add( error ); }
			}
			foreach ( var entity in entities.ToArray() )
			{
				try { entity.Delete(); }
				catch ( Exception error ) { failures.Add( error ); }
			}
		}
		finally
		{
			try { previous.Restore(); }
			finally
			{
				ReleaseCandidateAssets( failures );
				finished = true;
				active = null;
			}
		}
		if ( failures.Count != 0 )
			throw new AggregateException( "The candidate scene could not be fully cleaned up.", failures );
	}

	/// <summary>
	/// Retires the materials and shaders the rejected candidate registered. Entities only clear their
	/// bindings when deleted, so without this every failed candidate would leave its pipelines and
	/// native shader programs growing in <see cref="Asset.All"/> forever. Assets registered before the
	/// scope started, the shared texture cache and the process-lifetime UI materials stay untouched.
	/// </summary>
	private void ReleaseCandidateAssets( List<Exception> failures )
	{
		foreach ( var asset in Asset.All.ToArray() )
		{
			if ( retainedAssets.Contains( asset ) || asset is not Material and not Shader )
				continue;
			if ( IsProcessLifetimeMaterial( asset ) )
				continue;
			try
			{
				if ( asset is Material material )
					material.ReleaseOwnedResources();
				else if ( asset is Shader shader )
					shader.ReleaseOwnedResources();
			}
			catch ( Exception error ) { failures.Add( error ); }
		}
	}

	// Material.UI and Material.Default (cursor, images, terrain) are shared by every scene, but the
	// candidate can be the first thing to touch them, so the start snapshot may not contain them;
	// releasing them would break every later scene. Reading them can run their initializer, which
	// needs the graphics device — without one nothing has been shared yet, and that failure is fine.
	private static bool IsProcessLifetimeMaterial( Asset asset )
	{
		try
		{
			return ReferenceEquals( asset, Material.UI ) || ReferenceEquals( asset, Material.Default );
		}
		catch ( TypeInitializationException )
		{
			return false;
		}
	}
}

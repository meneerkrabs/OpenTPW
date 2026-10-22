using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Stages a level's entities without retiring the current scene. Used synchronously between frames, or across frames with
/// <see cref="Pause"/> and <see cref="Resume"/>: the live scene runs its frames while the candidate waits.
/// </summary>
internal sealed class EntityConstructionScope : IDisposable
{
	private static EntityConstructionScope? active;
	private SceneState previous;
	private readonly List<Entity> entities = new();
	private readonly HashSet<Asset> retainedAssets;
	private SceneState? candidate;
	private SceneState? paused;
	private HashSet<Asset>? assetsAtPause;
	private bool finished;

	internal static bool IsStaging => active != null && active.candidate == null && active.paused == null;

	/// <summary>The entities staged in the candidate scene so far, paused or not.</summary>
	internal int StagedEntities => entities.Count;

	private readonly record struct SceneState( List<Entity> Entities, Level? Level, CameraMode Camera,
		float TargetExtent, Vector3 InitialTarget, Vector3 ListenerPosition, Vector3 ListenerForward, Vector3 ListenerFront, Vector3 ListenerUp )
	{
		public static SceneState Capture() => new( Entity.All, global::OpenTPW.Level.Current, global::OpenTPW.Camera.Mode,
			ParkCameraMode.TargetExtent, ParkCameraMode.InitialTarget, AudioListener.Position, AudioListener.Forward, AudioListener.Front, AudioListener.Up );

		public void Restore()
		{
			Entity.All = Entities;
			global::OpenTPW.Level.Current = Level!;
			global::OpenTPW.Camera.Mode = Camera;
			ParkCameraMode.TargetExtent = TargetExtent;
			ParkCameraMode.InitialTarget = InitialTarget;
			AudioListener.RestoreCaptured( ListenerPosition, ListenerForward );
			AudioListener.RestoreCapturedAxes( ListenerFront, ListenerUp );
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

	/// <summary>Sets the candidate aside and brings the live scene back, so a frame of the live scene can run.</summary>
	public void Pause()
	{
		RequireActive();
		if ( candidate != null || paused != null || !ReferenceEquals( Entity.All, entities ) )
			throw new InvalidOperationException( "The candidate scene is not active." );
		paused = SceneState.Capture();
		previous.Restore();
		assetsAtPause = new HashSet<Asset>( Asset.All, ReferenceEqualityComparer.Instance );
	}

	/// <summary>Makes the paused candidate current again. What the live scene changed meanwhile is what is restored later.</summary>
	public void Resume()
	{
		RequireActive();
		if ( paused is not { } waiting || !ReferenceEquals( Entity.All, previous.Entities ) )
			throw new InvalidOperationException( "The candidate scene is not paused." );
		previous = SceneState.Capture();
		RetainLiveAssets();
		paused = null;
		waiting.Restore();
	}

	/// <summary>Assets the live scene registered during its frames are its own, not the candidate's: a rejected candidate keeps them.</summary>
	private void RetainLiveAssets()
	{
		foreach ( var asset in Asset.All )
			if ( !assetsAtPause!.Contains( asset ) )
				retainedAssets.Add( asset );
		assetsAtPause = null;
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
		// Paused, the live scene is current: that is the state to come back to, not the one from before its last frames.
		if ( paused != null )
		{
			previous = SceneState.Capture();
			RetainLiveAssets();
		}
		var staged = candidate ?? paused ?? SceneState.Capture();
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
				// Release must never leave the scope marked active, or a later construction would be
				// rejected because this rollback's own bookkeeping failed.
				try { ReleaseCandidateAssets( failures ); }
				catch ( Exception error ) { failures.Add( error ); }
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
	/// scope started, the shared texture cache and the process-lifetime shared materials stay untouched.
	/// </summary>
	private void ReleaseCandidateAssets( List<Exception> failures )
	{
		foreach ( var asset in Asset.All.ToArray() )
		{
			if ( retainedAssets.Contains( asset ) || asset is not Material and not Shader )
				continue;
			// Material.UI/Default record themselves and their owned shaders as process-lifetime when
			// created; the candidate can be the first to create them, and a kept shared material must
			// keep the shader that registers before it. Only the instance flag is read here: touching
			// those statics during cleanup could run their initializer (allocating assets mid-rollback,
			// or poisoning the type on a host without a graphics device).
			if ( asset is Material { IsProcessLifetime: true } or Shader { IsProcessLifetime: true } )
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
}

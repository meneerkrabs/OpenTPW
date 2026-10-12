using System.Numerics;

namespace OpenTPW;

/// <summary>Stages a level's entities without retiring the current scene. Used synchronously between frames.</summary>
internal sealed class EntityConstructionScope : IDisposable
{
	private static EntityConstructionScope? active;
	private readonly SceneState previous;
	private readonly List<Entity> entities = new();
	private SceneState? candidate;
	private bool finished;

	private readonly record struct SceneState( List<Entity> Entities, Level? Level, CameraMode Camera,
		float TargetExtent, Vector3 InitialTarget )
	{
		public static SceneState Capture() => new( Entity.All, global::OpenTPW.Level.Current, global::OpenTPW.Camera.Mode,
			ParkCameraMode.TargetExtent, ParkCameraMode.InitialTarget );

		public void Restore()
		{
			Entity.All = Entities;
			global::OpenTPW.Level.Current = Level!;
			global::OpenTPW.Camera.Mode = Camera;
			ParkCameraMode.TargetExtent = TargetExtent;
			ParkCameraMode.InitialTarget = InitialTarget;
		}
	}

	public EntityConstructionScope()
	{
		if ( active != null )
			throw new InvalidOperationException( "A scene construction transaction is already active." );
		previous = SceneState.Capture();
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
	}

	private void RequireActive()
	{
		if ( finished || !ReferenceEquals( active, this ) )
			throw new ObjectDisposedException( nameof( EntityConstructionScope ) );
	}

	/// <summary>On failure deletes only candidate entities and restores the live scene even if a cleanup callback throws.</summary>
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
			previous.Restore();
			finished = true;
			active = null;
		}
		if ( failures.Count != 0 )
			throw new AggregateException( "The candidate scene could not be fully cleaned up.", failures );
	}
}

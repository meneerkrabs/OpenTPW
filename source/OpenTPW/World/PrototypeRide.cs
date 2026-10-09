using System.Numerics;

namespace OpenTPW;

/// <summary>
/// The sandbox's original Inca Totem (Info.Id 1110), placed by its footprint centre. Since the generic object
/// slice this is a thin wrapper over <see cref="OriginalObject"/>: the same catalog entry, model hierarchy and
/// generic effects every original object uses (all of Totem.RSE's animations, including the TRIGANIM_CH
/// channel clips), plus the carriage-height probe the smoke test checks. It starts closed.
/// </summary>
public sealed class PrototypeRide : OriginalObject
{
	// [APPROX:RIDES-027] Sandbox Totem bounds check uses a 5-unit radius around its centre — evidence needed: none for gameplay (sandbox prototype)
	public const float FootprintRadius = 5f;
	public const float ModelScale = ObjectPlacement.ModelScale;
	public const string DisplayName = "Inca Totem (prototype)";
	public const int InfoId = 1110;
	internal const string ArchivePath = "/levels/jungle/rides/totem";
	internal const string CarriageMeshName = "tp_cart";
	internal const string ScriptPath = ArchivePath + "/Totem.RSE";

	/// <summary>
	/// <c>ANIM_Main</c> in ScriptDefs and the first operand of the docs' <c>TRIGANIM ANIM_Main 0 0</c> example.
	/// Totem.RSE triggers it once per ride cycle after setting VAR_RUNNING and waits for it with WAIT4ANIM.
	/// </summary>
	internal const int MainAnimation = 5;
	/// <summary>Original running cycle: cart lift/drop plus counter-rotating cogs (430 ticks), ANIM_Main variant 0.</summary>
	internal const string AnimationName = "totemm1.MD2";
	/// <summary>Sandbox choice; the original animation tick rate is not verified.</summary>
	public const float AnimationTicksPerSecond = ObjectAnimator.DefaultTicksPerSecond;

	private readonly int carriageNode;

	public PrototypeRide( Vector3 position, RideScriptWorld? world = null )
		: base( LoadEntry(), CreatePlacement( position ), world, seed: null, open: false )
	{
		var carriage = Runtime.Model.Meshes.FirstOrDefault( mesh => IsCarriage( mesh.Name ) )
			?? throw new InvalidDataException( "The original Totem model does not contain its tp_cart carriage mesh." );
		carriageNode = carriage.NodeIndex;
		Name = DisplayName;
		Position = position;
	}

	/// <summary>Animated carriage height above its rest pose, in engine units.</summary>
	public float MotionHeight => (NodeTransforms[carriageNode].M42 - Runtime.Animator.RestTransforms[carriageNode].M42) * ModelScale;
	/// <summary>True while ANIM_Main variant 0 (totemm1.MD2, the cart cycle) plays on the main channel.</summary>
	public bool IsPlayingMainAnimation => Runtime.Animator.IsChannelPlaying( OriginalObjectEffects.MainChannel )
		&& string.Equals( Runtime.Animator.GetClip( OriginalObjectEffects.MainChannel ), Path.GetFileNameWithoutExtension( AnimationName ), StringComparison.OrdinalIgnoreCase );
	/// <summary>Length of the create clip (ANIM_Create, totemc) that Totem.RSE waits for before its passenger loop.</summary>
	public double CreateAnimationMilliseconds => Entry.ResolveAnimation( 0, 0 ) is { } file && ObjectAssets.LoadModel( Entry.FileSystem, file.Path ).Clip is { } clip
		? clip.Duration / Runtime.Animator.TicksPerSecond * 1000.0 : 0;
	/// <summary>Current tick of the main animation channel (0 when nothing played yet).</summary>
	public float AnimationTick => Runtime.Animator.GetTick( OriginalObjectEffects.MainChannel );

	/// <summary>Opens the ride. The script decides when the carriage moves (Totem.RSE waits up to 10 s for passengers).</summary>
	public void Start() => Open();

	/// <summary>Closes the ride; the script finishes its current cycle and then idles.</summary>
	public void Stop() => Close();

	private static ObjectCatalogEntry LoadEntry()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Find( InfoId );
		if ( entry == null || !string.Equals( entry.ArchivePath, ArchivePath, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( "The ride settings do not identify the original Inca Totem." );
		return entry;
	}

	/// <summary>The Totem's 3×4-cell model (30×40 MD2 units) centred on <paramref name="position"/>.</summary>
	private static ObjectPlacement CreatePlacement( Vector3 position )
	{
		if ( !float.IsFinite( position.X ) || !float.IsFinite( position.Y ) || !float.IsFinite( position.Z ) )
			throw new ArgumentOutOfRangeException( nameof( position ) );
		// [DATA:Totem.sam:Info.Shape] 3×4 cells = 30×40 MD2 units, centred
		return new ObjectPlacement( 0, 0, 0, new Vector2( position.X - 15 * ModelScale, position.Y - 20 * ModelScale ), position.Z );
	}

	/// <summary>
	/// Maps a model-space MD2 node matrix to the renderer: swap Y/Z on both sides (vertices are
	/// converted with <see cref="ConvertAxes"/>), centre the ride footprint, scale and place it.
	/// </summary>
	internal static Matrix4x4 ToRenderTransform( Matrix4x4 node, Vector3 position ) =>
		ObjectPlacement.SwapAxes( node ) * CreatePlacement( position ).ModelToEngine;

	internal static bool IsCarriage( string meshName ) => string.Equals( meshName.TrimEnd( '\0' ), CarriageMeshName, StringComparison.OrdinalIgnoreCase );

	internal static Vector3 ConvertAxes( Vector3 position ) => ObjectAssets.ConvertAxes( position.GetSystemVector3() );

	internal static Vertex[] ConvertMesh( ModelFile.Mesh mesh ) => ObjectAssets.ConvertMesh( mesh );

	internal static string ResolveTexturePath( BaseFileSystem fileSystem, string textureName )
	{
		var name = textureName.TrimEnd( '\0' );
		foreach ( var directory in new[] { $"{ArchivePath}/textures", "/levels/jungle/sharetex" } )
		{
			var path = fileSystem.GetFiles( directory ).FirstOrDefault( entry => string.Equals( Path.GetFileNameWithoutExtension( entry ), name, StringComparison.OrdinalIgnoreCase ) );
			if ( path != null )
				return path;
		}
		throw new FileNotFoundException( $"Original Totem texture was not found: {name}" );
	}
}

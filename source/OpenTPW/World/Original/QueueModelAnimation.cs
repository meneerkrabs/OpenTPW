using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTPW;

/// <summary>
/// The animation clip of a queue model: its <c>m</c> member of the theme's queue archive (<c>queendm.md2</c>, group 5 of the original's letter table), with the frames of every texture slot its texture-frame
/// tracks drive decoded once. Presentation only.
/// </summary>
internal sealed class QueueClip
{
	/// <summary>The animation group the idle restart of an instance starts (<see cref="QueueAnimationGroup.IdleGroup"/>), the letter M.</summary>
	public const int Group = QueueAnimationGroup.IdleGroup;

	public required ModelAnimation Animation { get; init; }

	/// <summary>Per model texture slot a texture-frame track addresses: every frame's texture, null where no searched directory has the art.</summary>
	public required Dictionary<int, Texture?[]> Frames { get; init; }

	/// <summary>
	/// The clip of the model <paramref name="name"/> in an archive directory: its <c>m</c> member, or null when the archive has none or the member does not load (a model without one has no clip: the probe 0x44A220 finds nothing and +0xA8 stays 0, so the
	/// level builds without it).
	/// </summary>
	internal static ModelAnimation? FindAnimation( string name, string directory, Func<string, IEnumerable<string>> listFiles, Func<string, ModelAnimation?> load )
	{
		var member = $"{name}m.md2";
		if ( !listFiles( directory ).Any( file => string.Equals( System.IO.Path.GetFileName( file ), member, StringComparison.OrdinalIgnoreCase ) ) )
			return null;
		try
		{
			return load( $"{directory}/{member}" );
		}
		catch ( Exception exception ) when ( exception is System.IO.IOException or InvalidDataException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	/// <summary>The nodes whose meshes a track of the clip writes the texture coordinates of.</summary>
	public HashSet<int> UvNodes => uvNodes ??= Animation.Tracks.Where( track => track.UvAnimation != null ).Select( track => track.NodeIndex ).ToHashSet();
	private HashSet<int>? uvNodes;

	/// <summary>
	/// Whether this port plays the clip: its tracks only write texture coordinates and texture frames (every clip of the shipped queue archives: the torches' frame tracks, Space's scrolling coordinates). A clip with
	/// a node, vertex, path or node-flag track is not played (none ships).
	/// </summary>
	public static bool Supported( ModelAnimation clip ) => clip.NodeList.Count == 0 && clip.Tracks.All( track =>
		track.Position == null && track.Rotations.Count == 0 && track.Scales.Count == 0 && track.VertexAnimation == null && track.VertexPose == null
		&& track.PathParameters == null && track.NodeFlagToggles.Count == 0 );
}

/// <summary>The vertices of one mesh of one cell in a part's vertex array, with the atlas tile of the cell's composite (-1: none).</summary>
internal sealed record QueueUvRange( int MeshIndex, int First, int Count, bool[] Composite, ProceduralTerrainAtlas? Atlas, int Tile );

/// <summary>
/// The channel of one group of queue instances and its restart: no graphics, so the cycle can be tested on its own (<see cref="QueueAnimationGroup"/> draws what it samples).
/// </summary>
// [BIN:TPW-EXE:0x0047300E carry] the first start from the none animation 12 is fresh (0x472FBB); a clip that runs past its duration is restarted by the idle restart with the carry the overshoot leaves (0x47300E: frame minus duration, 0x472F60), which 0x472BC0 caps (0x472C12..0x472C25) and turns into the new start time, stamp minus ftol(carry * 33.33 / speed) (0x472C27..0x472C41), so the clip loops with its phase kept; the clock is the scaled clock 0x7B496C (channel flag 0x40 clear, 0x473440)
internal sealed class QueueClipPlayer
{
	private readonly ModelAnimation clip;
	private bool started;

	public QueueClipPlayer( ModelFile model, ModelAnimation clip )
	{
		this.clip = clip;
		Animator = new ObjectAnimator( model );
	}

	public ObjectAnimator Animator { get; }

	/// <summary>One frame: the clock moves on by <paramref name="scaledSeconds"/>; a channel that has not started (the none animation 12) starts the clip fresh, and a started one restarts when it ends with the carry of its overshoot (<see cref="ObjectAnimator.Advance"/>).</summary>
	public void Step( double scaledSeconds )
	{
		Animator.Advance( scaledSeconds );
		if ( started || Animator.GetAnimation( 0 ) != ObjectAnimator.NoAnimation )
			return;
		started = true;
		Animator.Play( 0, clip, "m", loop: true, animationId: QueueClip.Group );
	}
}

/// <summary>
/// One batch of an animated group (<see cref="QueueAnimationGroup"/>): the meshes of its cells that a track writes or whose slot a frame track drives, in one dynamic model with its own material. It rebinds the frames of
/// the slots the player shows and writes the sampled texture coordinates into the vertices of every cell, then hands the array to the GPU once.
/// </summary>
internal sealed class QueueAnimatedPart
{
	private readonly Model drawn;
	private readonly Vertex[] vertices;
	private readonly Material material;
	private readonly QueueUvRange[] ranges;
	private readonly (int Slot, Texture?[] Frames, int[] BatchSlots)[] bindings;
	private readonly int[] shown;
	private readonly int[] meshVersions;
	private int textureVersion = -1;
	private bool animating = true;

	public QueueAnimatedPart( Model drawn, Vertex[] vertices, Material material, QueueUvRange[] ranges, (int Slot, Texture?[] Frames, int[] BatchSlots)[] bindings, int meshCount )
	{
		this.drawn = drawn;
		this.vertices = vertices;
		this.material = material;
		this.ranges = ranges;
		this.bindings = bindings;
		shown = new int[bindings.Length];
		meshVersions = new int[meshCount];
		Array.Fill( meshVersions, -1 );
	}

	/// <summary>Brings the drawn frames and texture coordinates up to date with the animator.</summary>
	public void Update( ObjectAnimator animator )
	{
		UpdateTextures( animator );
		UpdateCoordinates( animator );
	}

	private void UpdateTextures( ObjectAnimator animator )
	{
		if ( bindings.Length == 0 )
			return;
		var enabled = OriginalObject.TextureAnimationEnabled;
		var version = animator.TextureFrameVersion;
		if ( version == textureVersion && enabled == animating )
			return;
		textureVersion = version;
		animating = enabled;
		for ( var index = 0; index < bindings.Length; index++ )
		{
			var (slot, frames, batchSlots) = bindings[index];
			var frame = OriginalObject.TextureFrameToShow( animator.GetTextureFrame( slot ), enabled );
			if ( frame == shown[index] )
				continue;
			shown[index] = frame;
			if ( frame >= frames.Length || frames[frame] is not { } texture )
				continue;
			foreach ( var batchSlot in batchSlots )
				material.SetTextureArrayElement( "Color", batchSlot, texture );
		}
	}

	private void UpdateCoordinates( ObjectAnimator animator )
	{
		if ( ranges.Length == 0 )
			return;
		var changed = false;
		for ( var mesh = 0; mesh < meshVersions.Length; mesh++ )
		{
			var version = animator.GetVertexVersion( mesh );
			if ( version == meshVersions[mesh] )
				continue;
			meshVersions[mesh] = version;
			var coordinates = animator.GetTexCoords( mesh );
			if ( coordinates == null )
				continue;
			foreach ( var range in ranges )
			{
				if ( range.MeshIndex != mesh || coordinates.Count < range.Count )
					continue;
				for ( var corner = 0; corner < range.Count; corner++ )
				{
					var value = new Vector2( coordinates[corner].X, coordinates[corner].Y );
					vertices[range.First + corner].TexCoords = range.Composite[corner] && range.Atlas != null ? range.Atlas.PageCoordinates( range.Tile, value ) : value;
				}
				changed = true;
			}
		}
		if ( changed )
			drawn.UpdateVertices( vertices );
	}
}

/// <summary>
/// The queue cells of one model that were made in the same frame: tp.exe gives every instance a channel of its own, and cells made together start together and loop for good in the same phase (the carry keeps it),
/// so one player and one dynamic batch per (model, start) draw them all. Cells a path edit made again start again, as a new group (<see cref="OriginalTerrain"/>).
/// </summary>
// [BIN:TPW-EXE:0x00473C8A idle restart] 0x473C70 asks 0x473490 whether every channel is idle (the none animation 12, or its frame past the clip's duration, +0x1C) and then starts group 5 variant 0 on channel 0 with 0x4732A0(instance, 5, 0, flags, 1.0, 0) (0x473C9A..0x473CAB, up to three tries); the instance flag 0x8004 (set by the creation flags 0x1 and 0x80, which 0x33A of the queue models has neither of) skips it; group 5 is the letter M (the table 0x6FE6BC), the clip file <model>m.md2 (the loader's twelve-letter pass, 0x4623A2, 0x4623D1)
// [BIN:TPW-EXE:0x004731D6 channel reset] the creator 0x463060 calls 0x473E30 (0x4631D6), which gives the instance's one channel the none animation 12 and, with its clips, sets +0xA8; an instance without any clip (+0xA8 = 0) never restarts
internal sealed class QueueAnimationGroup( QueueClipPlayer player )
{
	/// <summary>The animation group of the idle restart (<c>0x473CA8</c> pushes 5): the letter M of the table <c>0x6FE6BC</c>.</summary>
	public const int IdleGroup = 5;

	public QueueClipPlayer Player { get; } = player;
	public List<QueueAnimatedPart> Parts { get; } = new();

	/// <summary>One frame: the player moves on, every part shows what it sampled.</summary>
	public void Step( double scaledSeconds )
	{
		Player.Step( scaledSeconds );
		foreach ( var part in Parts )
			part.Update( Player.Animator );
	}
}

/// <summary>
/// The start of every queue cell by (model, cell, yaw): the generation of the frame it was made in. The first build (a level, a loaded park) makes them all in one frame. After a path edit, a cell that appears or goes
/// has its four orthogonal neighbours placed again by the link refresh (each takes a new channel at frame 0) and starts again with them as a group of their own; a cell that stays but whose model or yaw changed (a link bit
/// moved) restarts alone; the others keep their phase.
/// </summary>
// [BIN:TPW-EXE:0x00534A13 link refresh] the immediate link refresh of the path tool (0x534A13, 0x534A40, 0x534C51 and the calls around them: 0x5365D0 on the neighbour) re-places only the orthogonal neighbour whose link bit it sets or clears, and only when it has the same class ([edi+8] == ebp, or type 0xA), through 0x522A90 and 0x5229E0 (a new channel at frame 0): a cell that stays and changes model or yaw restarts alone
// [BIN:TPW-EXE:0x005311C0 fence walk] the 2 x 2 dirty-block walk 0x5311C0 (calls 0x5365D0 at 0x53132E..0x531362; called at 0x532557 and 0x5328B4 by the tool commit that draws lines of the type 0x19 cells, the park fence posts: 0x53ad60 tests type 0x19, ParkCellTypes.Hoarding) belongs to the fence line commit: it steps along the line by two cells and then scans the whole map for type 0x19 cells; it re-places fence cells, not queue cells, so no queue cell is restarted by it; the other writers of the dirty flag 0x40 (0x53a530 call sites such as 0x52C89F..0x52D0D5 and 0x532E9D..0x532F2C) are not traced here
internal sealed class QueueBirths
{
	private readonly Dictionary<(int Model, int X, int Y, int Yaw), int> birth = new();
	private int generation;

	/// <summary>The start of a cell of the last <see cref="Assign"/>.</summary>
	public int Of( (int Model, int X, int Y, int Yaw) key ) => birth[key];

	public void Assign( IEnumerable<(int Model, int X, int Y, int Yaw)> cells )
	{
		var now = cells.ToHashSet();
		var before = birth.Keys.Select( key => (key.X, key.Y) ).ToHashSet();
		var after = now.Select( key => (key.X, key.Y) ).ToHashSet();
		// the cells that appear or go set or clear the link bits of their orthogonal neighbours; a cell that stays and changed is a new key and restarts alone
		var dirty = new HashSet<(int X, int Y)>( after.Except( before ) );
		dirty.UnionWith( before.Except( after ) );
		var changed = now.Any( key => !birth.ContainsKey( key ) ) || birth.Keys.Any( key => !now.Contains( key ) );
		if ( !changed )
			return;
		var initial = birth.Count == 0;
		var born = ++generation;
		var next = new Dictionary<(int Model, int X, int Y, int Yaw), int>();
		foreach ( var key in now )
		{
			var restart = initial || !birth.ContainsKey( key ) || NextToDirty( dirty, key.X, key.Y );
			next[key] = restart ? born : birth[key];
		}
		birth.Clear();
		foreach ( var (key, start) in next )
			birth[key] = start;
	}

	private static bool NextToDirty( HashSet<(int X, int Y)> dirty, int x, int y ) =>
		dirty.Contains( (x + 1, y) ) || dirty.Contains( (x - 1, y) ) || dirty.Contains( (x, y + 1) ) || dirty.Contains( (x, y - 1) );
}

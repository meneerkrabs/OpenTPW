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
/// The start of every queue cell by cell: the generation of the frame its instance was made in. The first build (a level, a loaded park) makes them all in one frame. After that exactly the cells the tools re-create start
/// again, as a group of their own (<see cref="Assign"/>); every other cell keeps its phase, whatever its neighbours do.
/// </summary>
// [BIN:TPW-EXE:0x005365D0 tile refresh] the tile refresh of a class-2 cell (a queue cell; table 0x7630B0, the path table 0x763138 is class 1) removes its instance (0x522A90) and places it again (0x5229E0) with a new channel at frame 0, even when the model is unchanged; the callers that reach a queue cell are the line commit (0x5340A7 on every cell of the line from the anchor 0x81EDE4/0x81EDE8 to the end inclusive; the anchor becomes the end, 0x527650), the path link writer (0x534A8C, 0x534C9E, 0x534EB1, 0x5350BF: a queue neighbour whose +0xC link byte has the bit toward the new path cell), the clear 0x5367A0 (the path case re-creates each linked neighbour at 0x53687E over all eight bits, not gated by 0x81D7A8, which is what the type pass reaches when a queue is laid over a path, 0x5346D0; the queue and entrance case at 0x536B9E, from SetCell case 0x32 and Back 0x52FE50), the corner walk 0x530120 (0x530387, 0x5303A3), the fence fill 0x5311C0 (the scan blocks that carry 0x40 in each of its four directions, 0x53132E, 0x53146A, 0x5315A6, 0x5316E3, and every unmarked post, 0x531926..0x531965) and the cell-op step 0x5370E0 with op 0x81 on a fence post (0x5385F0, 0x53865D, 0x5386CA, 0x538708, 0x53874B; the line passes and the rewrite walk 0x52BA50 reach it); the link refresh sites 0x534A13, 0x534A40 and 0x534C51 re-create type-1 and type-10 neighbours only, and the dirty flag 0x40 is cleared by 0x53A530, set only by the cell reset 0x536490, the cell-op step 0x5370E0 (on a fence post, op 0x81 above) and 0x539670
internal sealed class QueueBirths
{
	private readonly Dictionary<(int X, int Y), int> birth = new();
	private int generation;

	/// <summary>The start of a cell of the last <see cref="Assign"/>.</summary>
	public int Of( (int X, int Y) cell ) => birth[cell];

	/// <summary>
	/// Records the queue cells of a build: a cell that is new (or every cell, for the first build) and a cell in <paramref name="recreated"/> take a new start; the others keep theirs.
	/// </summary>
	public void Assign( IEnumerable<(int X, int Y)> cells, IReadOnlySet<(int X, int Y)> recreated )
	{
		var now = cells.ToHashSet();
		var initial = birth.Count == 0;
		var added = now.Any( cell => !birth.ContainsKey( cell ) );
		var again = now.Any( cell => recreated.Contains( cell ) );
		var removed = birth.Count > 0 && birth.Keys.Any( cell => !now.Contains( cell ) );
		if ( !initial && !added && !again && !removed )
			return;
		var born = ++generation;
		foreach ( var cell in birth.Keys.Where( cell => !now.Contains( cell ) ).ToList() )
			birth.Remove( cell );
		foreach ( var cell in now )
			if ( initial || !birth.ContainsKey( cell ) || recreated.Contains( cell ) )
				birth[cell] = born;
	}
}

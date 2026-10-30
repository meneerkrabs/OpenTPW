using System.Numerics;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>One mesh part of a posed coaster model (a pylon or a car): the vertices (original units, engine axis order X, Z, Y; a pylon's are relative to its cell's corner, a car's to its pivot) and indices with the part's texture names.</summary>
public sealed record CoasterModelPart( string MeshName, string[] Textures, Vertex[] Vertices, uint[] Indices, int NodeIndex = -1, Matrix4x4? Bake = null, NVector3[]? Local = null );

/// <summary>
/// The node values the knot pass feeds a pylon's clips (<c>0x430ed0</c> → <c>0x430ca0</c>): the stored height parameter (node <c>+0xe8</c>), the yaw
/// (<c>+0xe0</c>), the pitch (<c>+0xdc</c>), the bank (<c>+0xd8</c>) and the loop rotate parameter (<c>+0xf8</c>).
/// </summary>
/// <param name="TurnedBack">Node flag <c>0x10000000</c>: the rotate clip takes the yaw half a turn on (a suspended coaster's pylon on the outside of a turn).</param>
public readonly record struct CoasterPylonPose( float Loft, float Yaw = 0, float Pitch = 0, float Bank = 0, float LoopRotate = 0, bool TurnedBack = false );

/// <summary>
/// Where a pylon stands in its cell's stack, which shows or hides the model's stack parts (<c>0x432df0</c>): at the bottom (on the ground), at the top (no pylon on it)
/// and whether the pylon on it has a model of its own (not the build cursor's node).
/// </summary>
public readonly record struct CoasterStackPlace( bool Bottom = true, bool Top = true, bool CarriesModel = true )
{
	public static readonly CoasterStackPlace Alone = new( Bottom: true, Top: true, CarriesModel: true );
}

/// <summary>
/// A pylon's track-tag table (pylon <c>+0x30</c> or <c>+0x44</c>): <see cref="Count"/> tags, the tag records whose value is <see cref="Base"/> + k
/// (<c>0x4344c0</c>), and the index of the tag on the track dummy (<see cref="Centre"/>), whose knot carries the node's sample index (node <c>+0xfc</c>).
/// </summary>
public readonly record struct CoasterTagTable( int Centre, int Count, int Base );

/// <summary>
/// What the knot pass needs of a pylon model (<c>0x430ed0</c>): its tag tables and, for a node's clip values, the posed matrices of a table's tags and of its track
/// dummy (MD2 axes, relative to the model's origin: the pylon's cell corner at its base height).
/// </summary>
public interface ICoasterTagModel
{
	CoasterTagTable SingleTable { get; }
	CoasterTagTable? StackTable { get; }
	Matrix4x4[] TagMatrices( CoasterPylonPose pose, bool stackTable );
	/// <summary>The posed matrix of the track dummy (the node of the first 0x800000 record), null when the model has none.</summary>
	Matrix4x4? TrackDummyMatrix( CoasterPylonPose pose );
}

/// <summary>
/// A pylon model (<c>StdPylon.MD2</c> or <c>LoopPylon.MD2</c>) posed for one node with its five clips (<c>0x430ca0</c>): the loft clip <c>L</c> stretches
/// the pylon to a height (it adds a vertex animation to the pylon mesh and a position key to the track tag and stack-height dummies), the rotate clip
/// <c>R</c> (<c>R1</c>) turns it about the cell centre, the incline clip <c>I</c> and the bank clip <c>B</c> tilt the track tag (and bend the pylon mesh
/// where they carry a vertex animation), and a loop pylon's second rotate clip <c>R2</c> turns its tag by the loop rotate parameter.
/// </summary>
// [BIN:TPW-EXE:0x42fad0 coaster model load] the TrackMaster loader opens the pylon models with the relative-animation flag 0x1013405 (the only caller to do so, with "GTexture" as the extra texture directory): a vertex key or position key is added to the stored mesh or node, not set
// [BIN:TPW-EXE:0x430590 pylon model tags] the loft, rotate, incline and bank clips and the track and stack dummies are looked up on the loaded model by node attribute (0x2, 0x3, 0x9, 0xa, 0x1000000, 0x800000, 0x2000000, 0x100000, 0x80000) when the control is set up (0x42eb70)
// [BIN:TPW-EXE:0x430ca0 pylon clip times] slot 0 (loft) = duration × map(clamp(+0xe8, 0, 1)), the node's +0xe8 written back clamped; slot 1 (rotate) = map(+0xec), +0xec = frac(yaw/2π) (+1 when negative, 0x43110c), frac(+0xec + ½) on node flag 0x10000000; slot 2 (incline) = map(+0xf0 · mult + add + 0.5), +0xf0 = pitch/2π; slot 3 (bank) = map(+0xf4 · mult + add + 0.5), +0xf4 = frac(bank/2π + ½) − ½ (0x431177..0x4311b7); slot 4 (loop tilt) = map(+0xf8), +0xf8 = frac(+0xf8); each map raises to its start cap, then lowers to its end cap
// [BIN:TPW-EXE:0x470d10 relative rotation] a model loaded with the relative-animation flag (header +0x30 & 0x4) multiplies a clip's rotation key into the node's matrix (key × node, 0x470d10) instead of setting it (0x46fbb0), and adds position and vertex keys (0x471860); the slots run in order loft, rotate, incline, bank, loop tilt (0x473c70 → 0x4735d0)
// [BIN:TPW-EXE:0x430590 pylon stack parts] the first four attribute records with 0x80000 name the stack parts by their value: 1 → pylon +0x1c (flag 0x400), 2 → +0x20 (0x800), 3 → +0x24 (0x1000), 4 → +0x28 (0x2000)
// [BIN:TPW-EXE:0x432df0 stack part visibility] for every node of a cell's stack the pose pass shows (clears node flag 0x10) or hides (sets 0x80000010, 0x433220) the parts: part 1 is hidden on the top node and shown on a lower one whose upper neighbour has a model (not 0x2000), part 2 shown on the top node and hidden on a lower one, part 3 hidden on the bottom node and shown on a stacked one, part 4 shown on the bottom node and hidden on a stacked one
// [BIN:TPW-EXE:0x4740d0 rotation blend] 0x7b4980 is written only by its setter 0x4740d0, which start-up (0x457a90) calls once with 0, so bit 2 is clear and the clip sampler slerps the rotation keys (0x474490 → 0x4742a0)
// [APPROX:COAST-033] the bottom node's model shadow (model +0x2c, placed at the cell's ground height by 0x469940) and its hiding on a stacked node unless pylon flag 0x10 is set are not modelled; a part's flag hides its own mesh here, its children are not hidden with it (no PC pylon hangs a mesh under a part); the clip sampler's rotation blend is the ported table-sine one (ModelRotationBlend, [BIN:TPW-EXE:0x4742A0 rotation blend]) — evidence needed: the renderer's use of node flag 0x10 for a dummy's children, the shadow object at model +0x2c and the source of pylon flag 0x10
public sealed class CoasterPylonRig : ICoasterTagModel
{
	/// <summary>The five clip slots in the original's order: loft, rotate, incline, bank, loop tilt (<c>0x74ca70</c>: types 3, 10, 2, 9, 10 index 1).</summary>
	public const int SlotCount = 5;
	private const int LoftSlot = 0, RotateSlot = 1, InclineSlot = 2, BankSlot = 3, LoopTiltSlot = 4;

	private readonly ModelFile model;
	private readonly ModelAnimation?[] clips;
	private readonly (NVector3 Scale, Quaternion Rotation, NVector3 Translation)[] rest;
	private readonly ModelAnimationTrack?[][] tracks;
	private readonly int[] order;
	private readonly Matrix4x4[] worlds;
	private readonly CoasterClipMap loftMap = CoasterClipMap.Identity, rotateMap = CoasterClipMap.Identity, inclineMap = CoasterClipMap.Identity, bankMap = CoasterClipMap.Identity, loopRotateMap = CoasterClipMap.Identity;

	/// <summary>The hand edits the pylon's animations allow; set by the asset loader from the models of the same family (<c>I</c>, <c>B</c>, <c>R2</c>).</summary>
	public CoasterPylonCapabilities Capabilities { get; internal set; }

	/// <param name="model">The pylon's geometry.</param>
	/// <param name="loft">The loft clip (<c>&lt;pylon&gt;L.MD2</c>), or null.</param>
	/// <param name="rotate">The rotate clip (<c>&lt;pylon&gt;R.MD2</c> or <c>R1</c>), or null.</param>
	/// <param name="control">The pylon control whose clip maps (<c>f…Anim*</c>) turn the node values into clip fractions; null takes the defaults.</param>
	/// <param name="incline">The incline clip (<c>&lt;pylon&gt;I.MD2</c>), or null.</param>
	/// <param name="bank">The bank clip (<c>&lt;pylon&gt;B.MD2</c>), or null.</param>
	/// <param name="loopTilt">The second rotate clip (<c>&lt;pylon&gt;R2.MD2</c>), or null.</param>
	public CoasterPylonRig( ModelFile model, ModelAnimation? loft, ModelAnimation? rotate, CoasterPylonControl? control = null, ModelAnimation? incline = null, ModelAnimation? bank = null, ModelAnimation? loopTilt = null )
	{
		ArgumentNullException.ThrowIfNull( model );
		if ( model.Kind != ModelFileKind.Geometry )
			throw new ArgumentException( "A pylon needs a geometry model.", nameof( model ) );
		this.model = model;
		clips = new[] { loft, rotate, incline, bank, loopTilt };
		var count = model.Nodes.Count;
		rest = new (NVector3, Quaternion, NVector3)[count];
		for ( var index = 0; index < count; index++ )
		{
			if ( !Matrix4x4.Decompose( model.Nodes[index].Transform, out var scale, out var rotation, out var translation ) )
			{
				scale = NVector3.One;
				rotation = Quaternion.Identity;
				translation = model.Nodes[index].Transform.Translation;
			}
			rest[index] = (scale, rotation, translation);
		}
		tracks = clips.Select( clip => TracksByNode( clip, count ) ).ToArray();
		order = ParentFirst( model );
		worlds = new Matrix4x4[count];
		// [BIN:TPW-EXE:0x430590 pylon track dummy] the track dummy is the node of the first attribute record with bit 0x800000 (0x4306be..0x4306e9: the record index at pylon +0x2c; +0x30 is the record's value − 1, the base of the knot tags), the stack dummy the node of the first record with 0x100000 (0x430780..0x4307a6); in the PC models it is the dummy the loft, incline and bank clips move (Skull Coaster's TrackCentreDummy, not its TrackHeightDummy); a model without such a record falls back to the first dummy named Track* or animated by the loft clip
		TagNode = DummyByAttribute( model, TrackDummyAttribute ) ?? FindTag( model, tracks[LoftSlot] );
		StackNode = DummyByAttribute( model, StackDummyAttribute ) ?? -1;
		ReadTagTables( model );
		ReadStackParts( model );
		Duration = loft?.Duration ?? 0;
		if ( control != null )
			(loftMap, rotateMap, inclineMap, bankMap, loopRotateMap) = (control.LoftMap, control.RotateMap, control.InclineMap, control.BankMap, control.LoopRotateMap);
		// [BIN:TPW-EXE:0x430590 pylon heights] h0 = the track dummy's Y with the loft clip at clamp(fLoftAnimAdd, StartCap, EndCap) (0x430923..0x430990), h1 = its Y at clamp(fLoftAnimMultiplier + fLoftAnimAdd, StartCap, EndCap) (0x430a0b..0x430a92), the stack offset the stack dummy's Y at the first pose minus h0, or 0 without a stack dummy (0x4309be..0x430a01)
		var first = LoftFraction( 0 );
		var low = TagHeightAtFraction( first );
		TagLow = low;
		TagHigh = TagHeightAtFraction( LoftFraction( 1 ) );
		StackOffset = StackNode >= 0 ? NodeHeightAtFraction( StackNode, first ) - low : 0;
	}

	/// <summary>Attribute bit of the records that name a pylon's track tags (the knots, <c>0x4344c0</c>).</summary>
	public const uint TagAttribute = 0x400000;
	/// <summary>Attribute bit of a tag record that may start a tag table (<c>0x430590</c>: two such records give a pylon two tag tables, pylon flag 8).</summary>
	public const uint TagTableAttribute = 0x1000000;

	/// <summary>The tag table of a pylon node (pylon <c>+0x30</c>): every node of this pylon but the lower nodes of a stack when <see cref="StackTable"/> exists.</summary>
	public CoasterTagTable SingleTable { get; private set; }
	/// <summary>The second tag table (pylon <c>+0x44</c>) of a model with two 0x1000000 tag records (pylon flag 8): the In, Centre and Out tags of a node a pylon stands on.</summary>
	public CoasterTagTable? StackTable { get; private set; }
	/// <summary>The model nodes of the tags of <see cref="SingleTable"/> and <see cref="StackTable"/> in table order (-1 where the model lacks a record).</summary>
	private int[] singleTags = Array.Empty<int>(), stackTags = Array.Empty<int>();
	/// <summary>Whether the model names its track tags (records with 0x400000), so the knot pass can take its knots from them.</summary>
	public bool HasTags => singleTags.Length > 0 && singleTags.All( node => node >= 0 ) && (StackTable == null || stackTags.All( node => node >= 0 ));

	/// <summary>The model nodes of a tag table's tags (<c>0x4344c0</c>: the record with 0x400000 and value <see cref="TagTable.Base"/> + k, 0x44b220).</summary>
	public IReadOnlyList<int> TagNodes( bool stackTable ) => stackTable && StackTable != null ? stackTags : singleTags;

	// [BIN:TPW-EXE:0x430590 pylon tag tables] with one record carrying 0x1000000 the pylon gets one table (pylon +0x30..+0x40): the count of 0x400000 records (+0x3c), base 1 (+0x40) and the track dummy's record value − 1 as its centre index (+0x30); with two such records it sets pylon flag 8 and gets two tables: +0x30 with centre 0, one tag and base 2 (+0x3c = 1), and +0x44 with centre 1, three tags and base 1 (+0x44 = 1, +0x50 = 3, +0x54 = 1)
	private void ReadTagTables( ModelFile model )
	{
		var tableRecords = model.DummyAttributes.Count( record => (record.Type & TagTableAttribute) != 0 );
		var trackRecord = model.DummyAttributes.FirstOrDefault( record => (record.Type & TrackDummyAttribute) != 0 );
		if ( tableRecords >= 2 )
		{
			SingleTable = new CoasterTagTable( 0, 1, 2 );
			StackTable = new CoasterTagTable( 1, 3, 1 );
		}
		else
		{
			var count = model.DummyAttributes.Count( record => (record.Type & TagAttribute) != 0 );
			// No PC pylon lacks the 0x1000000 record; without one the table's base keeps the control's zero.
			var centre = trackRecord != null ? (int)trackRecord.Value - 1 : 0;
			SingleTable = new CoasterTagTable( Math.Clamp( centre, 0, Math.Max( 0, count - 1 ) ), count, tableRecords == 1 ? 1 : 0 );
			StackTable = null;
		}
		singleTags = Tags( model, SingleTable );
		stackTags = StackTable is { } stack ? Tags( model, stack ) : Array.Empty<int>();
	}

	private static int[] Tags( ModelFile model, CoasterTagTable table )
	{
		var nodes = new int[table.Count];
		for ( var k = 0; k < table.Count; k++ )
		{
			var value = (uint)(table.Base + k);
			var record = -1;
			for ( var index = 0; index < model.DummyAttributes.Count && record < 0; index++ )
			{
				if ( model.DummyAttributes[index].Value == value && (model.DummyAttributes[index].Type & TagAttribute) != 0 )
					record = index;
			}
			nodes[k] = record >= 0 ? NodeOfRecord( model, record ) ?? -1 : -1;
		}
		return nodes;
	}

	/// <summary>The attribute bit of the records that name a pylon's stack parts (values 1..4).</summary>
	public const uint StackPartAttribute = 0x80000;
	/// <summary>The model node of each stack part, index value − 1 (-1 where the model names none).</summary>
	private readonly int[] stackParts = { -1, -1, -1, -1 };

	/// <summary>The node of stack part <paramref name="value"/> (1..4), -1 when the model names none.</summary>
	public int StackPart( int value ) => value is >= 1 and <= 4 ? stackParts[value - 1] : -1;

	private void ReadStackParts( ModelFile model )
	{
		var seen = 0;
		for ( var index = 0; index < model.DummyAttributes.Count && seen < 4; index++ )
		{
			var record = model.DummyAttributes[index];
			if ( (record.Type & StackPartAttribute) == 0 )
				continue;
			seen++;
			if ( record.Value is >= 1 and <= 4 )
				stackParts[record.Value - 1] = NodeOfRecord( model, index ) ?? -1;
		}
	}

	/// <summary>Whether a node of the model is hidden in a stack place (<c>0x432df0</c>).</summary>
	public bool IsHidden( int node, CoasterStackPlace place )
	{
		if ( node < 0 )
			return false;
		// Under a node without a model (the build cursor's) the original leaves part 1 as the last pass set it; it was the top then, so it stays hidden.
		if ( node == stackParts[0] && (place.Top || !place.CarriesModel) )
			return true;
		if ( node == stackParts[1] && !place.Top )
			return true;
		if ( node == stackParts[2] && place.Bottom )
			return true;
		return node == stackParts[3] && !place.Bottom;
	}

	/// <summary>The node of attribute record <paramref name="record"/>: the node stored as header +0x46 + record (<c>0x44ab90</c>).</summary>
	private static int? NodeOfRecord( ModelFile model, int record )
	{
		var stored = model.Counts.Unknown46 + record;
		return model.Nodes.FirstOrDefault( candidate => candidate.StoredIndex == stored )?.Index;
	}

	/// <summary>
	/// The posed matrices of a tag table's tags for a node's clip values (MD2 axes, relative to the pylon's cell corner and base), the matrices the knot pass reads
	/// the knots from (<c>0x430ed0</c>: the record table entry of each knot's tag record).
	/// </summary>
	public Matrix4x4[] TagMatrices( CoasterPylonPose pose, bool stackTable )
	{
		var tags = TagNodes( stackTable );
		var matrices = new Matrix4x4[model.Nodes.Count];
		Worlds( Ticks( pose ), matrices );
		var result = new Matrix4x4[tags.Count];
		for ( var k = 0; k < tags.Count; k++ )
			result[k] = tags[k] >= 0 && tags[k] < matrices.Length ? matrices[tags[k]] : Matrix4x4.Identity;
		return result;
	}

	public Matrix4x4? TrackDummyMatrix( CoasterPylonPose pose ) => TagNode >= 0 ? NodeMatrix( TagNode, pose ) : null;

	/// <summary>Attribute bit of the record that names a pylon's track dummy.</summary>
	public const uint TrackDummyAttribute = 0x800000;
	/// <summary>Attribute bit of the record that names a pylon's stack dummy.</summary>
	public const uint StackDummyAttribute = 0x100000;

	/// <summary>The loft clip's fraction for a height parameter t: t · fLoftAnimMultiplier + fLoftAnimAdd, raised to StartCap, then lowered to EndCap (<c>0x430ca0</c>).</summary>
	public float LoftFraction( float t ) => loftMap.Fraction( t );

	/// <summary>
	/// The fractions of the five clips for a node (<c>0x430ed0</c>, <c>0x430ca0</c>): the height parameter clamped to [0, 1] through the loft map, the yaw's
	/// turn fraction through the rotate map, the pitch's through the incline map and the bank's, wrapped into [−½, ½), through the bank map (both centred on
	/// ½), and the loop rotate parameter's fraction through the loop rotate map.
	/// </summary>
	public (float Loft, float Rotate, float Incline, float Bank, float LoopTilt) Fractions( CoasterPylonPose pose ) => Fractions( pose, loftMap, rotateMap, inclineMap, bankMap, loopRotateMap );

	/// <summary>The clip fractions of a pose with a pylon control's maps (the defaults without one).</summary>
	public static (float Loft, float Rotate, float Incline, float Bank, float LoopTilt) Fractions( CoasterPylonControl? control, CoasterPylonPose pose ) =>
		control == null ? Fractions( pose, CoasterClipMap.Identity, CoasterClipMap.Identity, CoasterClipMap.Identity, CoasterClipMap.Identity, CoasterClipMap.Identity )
			: Fractions( pose, control.LoftMap, control.RotateMap, control.InclineMap, control.BankMap, control.LoopRotateMap );

	private static (float Loft, float Rotate, float Incline, float Bank, float LoopTilt) Fractions( CoasterPylonPose pose, CoasterClipMap loftMap, CoasterClipMap rotateMap, CoasterClipMap inclineMap, CoasterClipMap bankMap, CoasterClipMap loopRotateMap )
	{
		const float InverseTwoPi = 0.15915494f;
		var loft = Math.Clamp( pose.Loft, 0f, 1f );
		var turn = Fraction( pose.Yaw * InverseTwoPi );
		if ( pose.TurnedBack )
			turn = Fraction( turn + 0.5f );
		var bank = Fraction( pose.Bank * InverseTwoPi + 0.5f ) - 0.5f;
		return (loftMap.Fraction( loft ), rotateMap.Fraction( turn ), inclineMap.Fraction( pose.Pitch * InverseTwoPi, 0.5f ), bankMap.Fraction( bank, 0.5f ), loopRotateMap.Fraction( Fraction( pose.LoopRotate ) ));
	}

	/// <summary>modf's fractional part, plus 1 when it is negative (<c>0x431480</c> and the callers' correction).</summary>
	private static float Fraction( float value )
	{
		var fraction = value - MathF.Truncate( value );
		return fraction < 0 ? fraction + 1f : fraction;
	}

	/// <summary>The node of the first attribute record that has any of <paramref name="mask"/>: record i belongs to the node stored as header +0x46 + i; null without one.</summary>
	// [BIN:TPW-EXE:0x44ab90 dummy record table] the world-matrix pass stores a dummy node's matrix in the record table entry (node stored index (+0x50) − header +0x46), and 0x430590 reads the track dummy's height from the entry of the record index 0x44b270 returned (0x430988)
	private static int? DummyByAttribute( ModelFile model, uint mask )
	{
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			if ( (model.DummyAttributes[index].Type & mask) == 0 )
				continue;
			var stored = model.Counts.Unknown46 + index;
			var node = model.Nodes.FirstOrDefault( candidate => candidate.StoredIndex == stored );
			return node?.Index;
		}
		return null;
	}

	/// <summary>The node the stack dummy names, -1 when the model has none (the next pylon of a stack then stands at the track dummy).</summary>
	public int StackNode { get; }
	/// <summary>How far above the track dummy the next pylon of a stack stands (pylon <c>+0xc4</c>): the stack dummy's height at the lowest loft pose minus h0.</summary>
	public float StackOffset { get; }

	private float TagHeightAtFraction( float fraction ) => NodeHeightAtFraction( TagNode, fraction );

	private float NodeHeightAtFraction( int node, float fraction )
	{
		if ( node < 0 || node >= worlds.Length )
			return 0;
		// The heights are the loft clip's alone (the other clips' keys at time 0 turn the dummies but do not raise them in the PC models).
		Worlds( new[] { fraction * Duration, 0, 0, 0, 0 }, worlds, loftOnly: true );
		return worlds[node].Translation.Y;
	}

	public ModelFile Model => model;
	/// <summary>The node the track attaches to (<c>TrackDummyCentre</c>, <c>TrackCentreDummy</c> or the first animated dummy), -1 when the model has none.</summary>
	public int TagNode { get; }
	public int Duration { get; }
	/// <summary>The height of the track tag above the pylon base at loft 0 (MD2 units).</summary>
	public float TagLow { get; }
	/// <summary>The height of the track tag above the pylon base at loft 1 (MD2 units).</summary>
	public float TagHigh { get; }
	public bool HasLoft => clips[LoftSlot] != null && TagHigh > TagLow;
	/// <summary>Whether the model has the incline, bank and loop tilt clips.</summary>
	public bool HasIncline => clips[InclineSlot] != null;
	public bool HasBank => clips[BankSlot] != null;
	public bool HasLoopTilt => clips[LoopTiltSlot] != null;

	/// <summary>The height above the pylon base of any node of the model at a loft clip fraction in [0, 1] (the node's world Y after the loft clip).</summary>
	public float NodeHeightAt( int node, float loftParameter ) => NodeHeightAtFraction( node, loftParameter );

	/// <summary>The tag height at a loft clip fraction in [0, 1].</summary>
	public float TagHeightAt( float loftParameter ) => TagHeightAtFraction( loftParameter );

	/// <summary>The posed world matrix of a node of the model for a node's clip values (MD2 axes, relative to the pylon's cell corner).</summary>
	public Matrix4x4 NodeMatrix( int node, CoasterPylonPose pose )
	{
		var matrices = new Matrix4x4[model.Nodes.Count];
		Worlds( Ticks( pose ), matrices );
		return node >= 0 && node < matrices.Length ? matrices[node] : Matrix4x4.Identity;
	}

	/// <summary>The clip times of the five slots: each clip's duration times its fraction (<c>0x430ca0</c>: slot +0x20 = slot +0x1c × fraction).</summary>
	private float[] Ticks( CoasterPylonPose pose )
	{
		var (loft, rotate, incline, bank, loopTilt) = Fractions( pose );
		var fractions = new[] { loft, rotate, incline, bank, loopTilt };
		var ticks = new float[SlotCount];
		for ( var slot = 0; slot < SlotCount; slot++ )
			ticks[slot] = clips[slot] is { } clip ? fractions[slot] * clip.Duration : 0;
		return ticks;
	}

	/// <summary>
	/// The pylon's mesh parts for a node's clip values; positions relative to the cell corner (engine axis order). The stack parts its place in the cell's stack
	/// hides are left out (<see cref="IsHidden"/>). A height parameter outside [0, 1] stands the pylon at the end of its loft (the original clamps the parameter, 0x430ca0).
	/// </summary>
	public IReadOnlyList<CoasterModelPart> Pose( CoasterPylonPose pose, CoasterStackPlace? place = null )
	{
		var stack = place ?? CoasterStackPlace.Alone;
		var ticks = Ticks( pose );
		var matrices = new Matrix4x4[model.Nodes.Count];
		Worlds( ticks, matrices );
		var parts = new List<CoasterModelPart>();
		foreach ( var mesh in model.Meshes )
		{
			if ( ObjectAssets.GetUnrenderableReason( mesh ) is { } reason && reason != "more than 16 texture slots" )
				continue;
			var node = mesh.NodeIndex >= 0 && mesh.NodeIndex < matrices.Length ? mesh.NodeIndex : model.RootNodeIndex;
			if ( IsHidden( mesh.NodeIndex, stack ) )
				continue;
			var source = mesh;
			if ( node >= 0 )
			{
				NVector3[]? positions = null;
				NVector2[]? texCoords = null;
				for ( var slot = 0; slot < SlotCount; slot++ )
				{
					if ( clips[slot] is not { } clip )
						continue;
					if ( tracks[slot][node]?.VertexAnimation is { } animation && animation.GetPoseLimitation( mesh.Positions.Length, clip.Duration ) == null )
					{
						positions ??= mesh.Positions.Select( position => position.GetSystemVector3() ).ToArray();
						// Add mode: every clip's vertex keys are added to the stored mesh (0x471860).
						if ( animation.HasStaticGroup )
							animation.ApplyStaticGroup( positions, add: true );
						animation.ApplyAnimatedGroups( ticks[slot], positions, add: true );
					}
					// Add mode: every clip's texture-coordinate keys are added to the stored coordinates as well (0x4745c0 through 0x471860): a lofted pylon's coordinates run past 1 along its height (wrap-flag slots repeat, the others clamp as tp.exe does, 0x575E9C)
					if ( tracks[slot][node]?.UvAnimation is { } uv && uv.CornerCount == mesh.TexCoords.Length )
					{
						texCoords ??= mesh.TexCoords.Select( coordinate => new NVector2( coordinate.X, coordinate.Y ) ).ToArray();
						uv.AddTo( ticks[slot], texCoords );
					}
				}
				if ( positions != null || texCoords != null )
					source = WithAnimated( mesh, positions, texCoords );
			}
			var bake = node >= 0 ? matrices[node] : Matrix4x4.Identity;
			foreach ( var (vertices, indices, textures, _) in ObjectAssets.ConvertMeshParts( source, bake ) )
				parts.Add( new CoasterModelPart( mesh.Name, textures, vertices, indices ) );
		}
		return parts;
	}

	private static ModelFile.Mesh WithAnimated( ModelFile.Mesh mesh, NVector3[]? positions, NVector2[]? texCoords )
	{
		var vertices = mesh.Vertices;
		if ( positions != null )
		{
			vertices = new ModelFile.Vertex[mesh.Vertices.Length];
			for ( var index = 0; index < vertices.Length; index++ )
				vertices[index] = new ModelFile.Vertex { Position = positions[mesh.CornerPositionIndices[index]], TextureIndex = mesh.Vertices[index].TextureIndex };
		}
		return new ModelFile.Mesh
		{
			Name = mesh.Name,
			NodeIndex = mesh.NodeIndex,
			Vertices = vertices,
			Normals = mesh.Normals,
			TexCoords = texCoords == null ? mesh.TexCoords : texCoords.Select( coordinate => new Vector2( coordinate.X, coordinate.Y ) ).ToArray(),
			Materials = mesh.Materials,
			Indices = mesh.Indices
		};
	}

	/// <summary>
	/// The world matrices of the model's nodes at the slots' clip times: each node starts from its stored matrix (the relative model is reset to it,
	/// 0x472310), then every slot in order adds its position key to the node's translation and multiplies its rotation key into the node's rotation
	/// (key × node, <c>0x470d10</c>).
	/// </summary>
	private void Worlds( float[] ticks, Matrix4x4[] output, bool loftOnly = false )
	{
		foreach ( var node in order )
		{
			var (scale, rotation, translation) = rest[node];
			var turned = Multiply( Matrix4x4.CreateScale( scale ), Matrix4x4.CreateFromQuaternion( rotation ) );
			for ( var slot = 0; slot < (loftOnly ? 1 : SlotCount); slot++ )
			{
				if ( tracks[slot][node] is not { } track )
					continue;
				if ( SampleTranslation( track, ticks[slot] ) is { } move )
					translation += move;
				if ( SampleRotation( track, ticks[slot] ) is { } key )
					turned = Multiply( Matrix4x4.CreateFromQuaternion( key ), turned );
			}
			var local = Multiply( turned, Matrix4x4.CreateTranslation( translation ) );
			var parent = model.Nodes[node].ParentIndex;
			output[node] = parent < 0 ? local : Multiply( local, output[parent] );
		}
	}

	/// <summary>
	/// Row-major 4x4 product with plain single-precision multiplies and adds (<see cref="DetMatrix.Multiply"/>). The pylon heights (h0, h1, the stack offset)
	/// feed the canonical state hash, and <see cref="Matrix4x4"/>'s operator may fuse the multiply-add on hardware that has it (a CPU-dependent last bit), so the
	/// heights are composed here instead.
	/// </summary>
	private static Matrix4x4 Multiply( in Matrix4x4 a, in Matrix4x4 b ) => DetMatrix.Multiply( a, b );

	/// <summary>A clip's position at a tick: Bézier keys as <see cref="ModelAnimationTrack.SampleTranslation"/> evaluates them (no fused operation), linear keys blended by <see cref="DetMatrix.Lerp"/>.</summary>
	private static NVector3? SampleTranslation( ModelAnimationTrack track, float tick )
	{
		if ( track.Position is { IsBezier: true } )
			return track.SampleTranslation( tick );
		if ( !track.TryLinearTranslationKeys( tick, out var from, out var to, out var fraction ) )
			return null;
		return fraction <= 0 ? from : DetMatrix.Lerp( from, to, fraction );
	}

	/// <summary>A clip's rotation at a tick, the keys blended by <see cref="DetMatrix.Slerp"/> (DetMath's acos and sin instead of the platform's).</summary>
	private static Quaternion? SampleRotation( ModelAnimationTrack track, float tick )
	{
		if ( !track.TryRotationKeys( tick, out var from, out var to, out var fraction ) )
			return null;
		return fraction <= 0 ? from : DetMatrix.Slerp( from, to, fraction );
	}

	private static ModelAnimationTrack?[] TracksByNode( ModelAnimation? clip, int count )
	{
		var tracks = new ModelAnimationTrack?[count];
		if ( clip == null )
			return tracks;
		foreach ( var track in clip.Tracks )
		{
			if ( track.NodeIndex >= 0 && track.NodeIndex < count )
				tracks[track.NodeIndex] = track;
		}
		return tracks;
	}

	private static int[] ParentFirst( ModelFile model )
	{
		var result = new List<int>( model.Nodes.Count );
		var seen = new bool[model.Nodes.Count];
		void Visit( int node )
		{
			if ( node < 0 || node >= seen.Length || seen[node] )
				return;
			Visit( model.Nodes[node].ParentIndex );
			seen[node] = true;
			result.Add( node );
		}
		for ( var node = 0; node < model.Nodes.Count; node++ )
			Visit( node );
		return result.ToArray();
	}

	private static int FindTag( ModelFile model, ModelAnimationTrack?[] loftTracks )
	{
		for ( var index = 0; index < model.Nodes.Count; index++ )
		{
			if ( model.Nodes[index].Name.StartsWith( "Track", StringComparison.OrdinalIgnoreCase ) && loftTracks[index]?.Position != null )
				return index;
		}
		for ( var index = 0; index < loftTracks.Length; index++ )
		{
			if ( loftTracks[index]?.Position != null && model.Nodes[index].MeshIndex < 0 )
				return index;
		}
		return -1;
	}
}

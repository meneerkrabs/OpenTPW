using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// The head space of one ride model and the slot table a ride script keeps for it (ADDHEAD / DELHEAD).
/// A head id is the <see cref="ModelDummyAttribute.Value"/> of a dummy attribute record whose type has flag 0x80; the
/// ids that run from 1 give the table length. Slot <c>n</c> holds the visitor on head id <c>n + 1</c>, 0 when free.
/// </summary>
// [BIN:STP-PPC:0x100B0E9C ADDHEAD] the table at script +48 (null: nothing), its length at +76, then a random slot drawn until one holds 0 (none free: nothing), the visitor stored there, and the head hung on head id slot + 1 (-1 from the lookup keeps the visitor without a head)
// [BIN:STP-PPC:0x100B0F90 DELHEAD] every slot holding the visitor is zeroed, and a head hung on it is taken down
// [BIN:STP-PPC:0x1019B298 head lookup] the 20-byte lookup records of the model's lookup state: a record matches head id k when its +4 word is k and its flags have 0x80
// [BIN:STP-PPC:0x1019B56C head hang] the matched record gets flag 0x2 and a sprite handle, and the lookup state counts one more attached head
// [APPROX:HEADS-003] the lookup records are the model's dummy attribute records (20 bytes: type, value, 12 reserved bytes); the table's length rule (the run from id 1) follows the PC executable's FUN_0044b220 (docs/exe/park.md, "The head table"), not a Mac trace
public sealed class RideHeadSpace
{
	/// <summary>Flag bit 0x80 of a dummy attribute type: a head record ([BIN:STP-PPC:0x1019B298]).</summary>
	public const uint HeadFlag = 0x80;

	/// <summary>Node names that carry a head id in the original models: <c>head07</c>, <c>Head7</c>, <c>Kid_07</c>, <c>spriteposition07</c>.</summary>
	private static readonly Regex NodeNameId = new( @"^(?:head|kid_|spriteposition)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );

	private readonly int[] nodes;
	private readonly int[] visitors;
	private readonly Random random;

	private RideHeadSpace( int[] nodes, int seed )
	{
		this.nodes = nodes;
		visitors = new int[nodes.Length];
		random = new Random( seed );
	}

	/// <summary>A head space with the given node for each slot (test seam; <see cref="Create"/> reads the model).</summary>
	internal static RideHeadSpace Of( int[] nodes, int seed ) => new( (int[])nodes.Clone(), seed );

	/// <summary>Length of the slot table (the run of head ids from 1).</summary>
	public int Count => visitors.Length;

	/// <summary>
	/// The head space of <paramref name="model"/>, or null when it has no head id 1 (the original's table is null then, so
	/// ADDHEAD does nothing). <paramref name="seed"/> drives the slot draw; the original draws from its global generator.
	/// </summary>
	// [APPROX:HEADS-001] the slot draw uses a generator seeded per ride, not the original's global rand (0x10105328 on the Mac)
	public static RideHeadSpace? Create( ModelFile model, int seed )
	{
		ArgumentNullException.ThrowIfNull( model );
		var ids = new HashSet<uint>( model.DummyAttributes.Where( attribute => (attribute.Type & HeadFlag) != 0 ).Select( attribute => attribute.Value ) );
		var count = 0;
		while ( ids.Contains( (uint)(count + 1) ) )
			count++;
		if ( count == 0 )
			return null;
		var nodes = new int[count];
		for ( var slot = 0; slot < count; slot++ )
			nodes[slot] = NodeForHead( model, slot + 1 );
		return new RideHeadSpace( nodes, seed );
	}

	/// <summary>
	/// The model node a head id hangs from, found by its name. The first node whose name carries the id wins; -1 when the
	/// model names none (the head then has no place to draw).
	/// </summary>
	// [APPROX:HEADS-002] the head's node is the node named after its id (headNN, HeadN, Kid_NN, spriteposition NN): the original's record-to-node link (the lookup record's +0 and +4 words, the attribute's 12 reserved bytes are zero in the Sun God) is not traced, and five jungle/fantasy/hallow/space models name no node for some ids (Flower Power, Phantom, Eruption, Hover Bot Havoc, Rock 'n Roll)
	public static int NodeForHead( ModelFile model, int id )
	{
		for ( var index = 0; index < model.Nodes.Count; index++ )
		{
			var match = NodeNameId.Match( model.Nodes[index].Name.Trim() );
			if ( match.Success && int.TryParse( match.Groups[1].Value, out var named ) && named == id )
				return index;
		}
		return -1;
	}

	/// <summary>The visitor on slot <paramref name="slot"/>, 0 when free.</summary>
	public int VisitorAt( int slot ) => visitors[slot];

	/// <summary>The model node of slot <paramref name="slot"/>, -1 when the model names none.</summary>
	public int NodeAt( int slot ) => nodes[slot];

	/// <summary>Visitors with a head up and the model node each hangs from (a head without a node is left out).</summary>
	public IEnumerable<(int Visitor, int Node)> Hung()
	{
		for ( var slot = 0; slot < visitors.Length; slot++ )
		{
			if ( visitors[slot] != 0 && nodes[slot] >= 0 )
				yield return (visitors[slot], nodes[slot]);
		}
	}

	/// <summary>ADDHEAD: a random free slot takes the visitor. Returns the slot, or -1 when the visitor is 0 or no slot is free.</summary>
	public int Add( int visitor )
	{
		if ( visitor == 0 || Array.IndexOf( visitors, 0 ) < 0 )
			return -1;
		int slot;
		do
		{
			slot = random.Next( visitors.Length );
		} while ( visitors[slot] != 0 );
		visitors[slot] = visitor;
		return slot;
	}

	/// <summary>DELHEAD: every slot holding the visitor is freed. Returns whether any was.</summary>
	public bool Remove( int visitor )
	{
		var removed = false;
		for ( var slot = 0; slot < visitors.Length; slot++ )
		{
			if ( visitors[slot] != visitor )
				continue;
			visitors[slot] = 0;
			removed = true;
		}
		return removed;
	}
}

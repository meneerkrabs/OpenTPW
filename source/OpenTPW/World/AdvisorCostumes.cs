using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>
/// The two accessory selectors of an advisor response (fields +0x14 and +0x18 of its 32-byte record in the Windows
/// executable's table at 0x768FB8). A selector is the dummy-attribute value of the mesh node to show: 5 to 14 on the
/// global advisor (hats, the spatula and the bow tie), 0 for none, −1 for the level's theme costume.
/// </summary>
internal readonly record struct AdvisorCostume( int Hat, int SecondHat )
{
	public static AdvisorCostume None => default;
}

/// <summary>
/// The shipped costume table (<c>content/data/advisor-costumes.toml</c>), copied for interoperability from the Windows
/// executable by <c>tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py</c>. A response that the table does not list
/// has no accessory.
/// </summary>
internal static class AdvisorCostumeTable
{
	public const string RelativePath = "content/data/advisor-costumes.toml";
	private static readonly Regex Row = new( @"^\s*\{ response = (\d+), hat = (-?\d+), hat2 = (-?\d+) \},?\s*$", RegexOptions.CultureInvariant );
	private static IReadOnlyDictionary<int, AdvisorCostume>? loaded;

	/// <summary>The table shipped with OpenTPW (loaded on first use; empty when the file is missing or damaged).</summary>
	public static IReadOnlyDictionary<int, AdvisorCostume> Table => loaded ??= LoadShipped();

	public static AdvisorCostume For( int responseId ) => Table.TryGetValue( responseId, out var costume ) ? costume : AdvisorCostume.None;

	private static IReadOnlyDictionary<int, AdvisorCostume> LoadShipped()
	{
		var path = Path.Combine( AppContext.BaseDirectory, RelativePath );
		try
		{
			return Parse( File.ReadAllText( path ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException )
		{
			Log.Warning( $"Advisor costumes unavailable ({path}): {exception.Message}" );
			return new Dictionary<int, AdvisorCostume>();
		}
	}

	public static IReadOnlyDictionary<int, AdvisorCostume> Parse( string text )
	{
		ArgumentNullException.ThrowIfNull( text );
		var table = new Dictionary<int, AdvisorCostume>();
		var inArray = false;
		var closed = false;
		var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.TrimEnd( '\r' );
			if ( line.Trim().Length == 0 || line.TrimStart().StartsWith( '#' ) )
				continue;
			if ( !inArray && !closed && line.Trim() == "costumes = [" )
			{
				inArray = true;
				continue;
			}
			if ( inArray && line.Trim() == "]" )
			{
				inArray = false;
				closed = true;
				continue;
			}
			var match = Row.Match( line );
			if ( !inArray || !match.Success )
				throw new InvalidDataException( $"Advisor costumes line {lineNumber} is not a costume row." );
			int Value( int group ) => int.Parse( match.Groups[group].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture );
			if ( !table.TryAdd( Value( 1 ), new AdvisorCostume( Value( 2 ), Value( 3 ) ) ) )
				throw new InvalidDataException( $"Advisor costume of response {Value( 1 )} appears twice." );
		}
		if ( !closed )
			throw new InvalidDataException( "Advisor costumes have no closed 'costumes' array." );
		return table;
	}
}

/// <summary>
/// The level's theme costume: the add-ons the selector −1 shows and the nodes it hides, from the level's
/// <c>Standard.sam</c> (<c>ThemeAdvisorCostumes[0..3].AddOns</c> and <c>.SubOffs</c>; the original copies them into the
/// advisor's slot tables at 0xF797A4/0xF797B4 in 0x4080E0). A value of 0 is an empty slot.
/// </summary>
internal sealed record AdvisorThemeCostume( IReadOnlyList<int> AddOns, IReadOnlyList<int> SubOffs )
{
	/// <summary>The four slots of each table.</summary>
	public const int Slots = 4;

	public static AdvisorThemeCostume None { get; } = new( new int[Slots], new int[Slots] );

	// [BIN:TPW-EXE:0x004080E0 level settings] 0x4080E0 copies the four AddOns and four SubOffs of the level's settings into 0xF797A4 and 0xF797B4
	public static AdvisorThemeCostume From( SamSettings settings )
	{
		ArgumentNullException.ThrowIfNull( settings );
		var addOns = new int[Slots];
		var subOffs = new int[Slots];
		for ( var slot = 0; slot < Slots; slot++ )
		{
			addOns[slot] = settings.GetInt( $"ThemeAdvisorCostumes[{slot}].AddOns", 0, optional: true );
			subOffs[slot] = settings.GetInt( $"ThemeAdvisorCostumes[{slot}].SubOffs", 0, optional: true );
		}
		return new AdvisorThemeCostume( addOns, subOffs );
	}

	/// <summary>The theme costume of <paramref name="level"/> (<c>/levels/&lt;level&gt;/Standard.sam</c>); none without that file.</summary>
	public static AdvisorThemeCostume Load( BaseFileSystem fileSystem, string? level )
	{
		ArgumentNullException.ThrowIfNull( fileSystem );
		if ( string.IsNullOrEmpty( level ) )
			return None;
		var path = $"/levels/{level}/Standard.sam";
		try
		{
			if ( !fileSystem.FileExists( path ) )
				return None;
			return From( new SamSettings( new[] { SamDocument.Parse( fileSystem.ReadAllText( path ), path ) } ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException or FormatException )
		{
			Log.Warning( $"Advisor theme costume of {level} could not be read: {exception.Message}" );
			return None;
		}
	}
}

/// <summary>
/// What the accessory selector needs of a model: its nodes, the meshes on them and the dummy-attribute records that name nodes.
/// Record <c>k</c> belongs to node <see cref="FirstAttributeNode"/> + <c>k</c>.
/// </summary>
/// <param name="FirstAttributeNode">The u16 at model header +0x46 (<see cref="ModelHeaderCounts.Unknown46"/>).</param>
internal sealed record AdvisorNodeMap( int NodeCount, int FirstAttributeNode, IReadOnlyList<(uint Type, uint Value)> Attributes, IReadOnlyList<(string Name, int Node)> Meshes )
{
	public static AdvisorNodeMap From( ModelFile model )
	{
		ArgumentNullException.ThrowIfNull( model );
		return new AdvisorNodeMap( model.Nodes.Count, model.Counts.Unknown46, model.DummyAttributes.Select( record => (record.Type, record.Value) ).ToArray(),
			model.Meshes.Select( mesh => (mesh.Name, mesh.NodeIndex) ).ToArray() );
	}

	/// <summary>
	/// Node of the first record (in order) whose type has a bit of <paramref name="typeMask"/> and whose value is <paramref name="value"/>; −1 without one.
	/// </summary>
	// [BIN:TPW-EXE:0x0044B220 attribute lookup] the first record (in order) whose value equals the argument and whose type has a bit of the mask; the record index plus the u16 at model header +0x46 is the node, a mesh node below the header's mesh count, a dummy node after
	private HashSet<int>? blinkNodes;

	/// <summary>Whether a clip's node-flag toggles apply to <paramref name="node"/>: a mesh node that is not a mouth (the mouths follow <see cref="AdvisorMouth"/>).</summary>
	public bool TogglesApplyTo( int node ) => (blinkNodes ??= Meshes.Where( mesh => !Advisor.Mouths.Contains( mesh.Name ) ).Select( mesh => mesh.Node ).ToHashSet()).Contains( node );

	public int FindNode( uint typeMask, int value )
	{
		for ( var index = 0; index < Attributes.Count; index++ )
		{
			if ( (Attributes[index].Type & typeMask) != 0 && Attributes[index].Value == unchecked((uint)value) )
			{
				var node = FirstAttributeNode + index;
				return node < NodeCount ? node : -1;
			}
		}
		return -1;
	}
}

/// <summary>
/// Which meshes of an advisor model are shown: the original's accessory selector (0x598CA0, run by 0x598DC0 right after a clip
/// starts), the nodes the clips hide at their start, and the clips' blink toggles. GPU-free: it works on the model's nodes and
/// dummy attributes only.
/// </summary>
internal static class AdvisorAccessories
{
	/// <summary>The type flag of a dummy-attribute record that marks a mesh node a selector can show or hide (0x429EE0 and 0x429F60 look up with 0x400).</summary>
	public const uint SelectableTypeFlag = 0x400;
	/// <summary>The two antennae of the global advisor (attribute values 0x13 and 0x14) that a hat replaces.</summary>
	public const int FirstAntenna = 0x13, SecondAntenna = 0x14;
	/// <summary>The right hand (0x15), which the spatula (attribute 14) replaces.</summary>
	public const int RightHand = 0x15;
	/// <summary>The accessory bit of a fixed sequence's clip that hides the costume (<c>0x10000</c>, bit 0 of <see cref="AdvisorSequenceEntry.Bits"/>).</summary>
	public const int SuppressCostumeBit = 1;

	/// <summary>
	/// The meshes drawn without any accessory: body, head, eyes, antennae and hands, and the five mouths (one of which is drawn
	/// at a time, see <see cref="AdvisorMouth"/>). Every other mesh (hats, spatula, bow tie, blink meshes, level props) starts hidden.
	/// </summary>
	// [BIN:TPW-EXE:0x00472E75 clip start] every clip lists the accessory, prop and blink nodes in its node list (clip 14 of the global advisor: spatula, seven hats and the bow tie, nodes 14 and 16-24; of a level advisor: tickets, map, costume pieces and the mutant eye) and sets node bit 0x10 on them (hidden) as it starts
	// [APPROX:ADVISOR-026] The level advisors' props (Ticket, MAP, Ticket2, Mutant Eye and the T_Particle/Rotate dummies) stay hidden: the ticket logic at 0x599B09-0x599BDE and the accessory bits 0x20000-0x200000 of 0x598DC0 that show them (and start particle effects) are not ported — evidence needed: the ticket schedule behind 0xF7978C/0xF79788 and the effects of 0x5ED5A0-0x5ED700
	public static bool IsBaseMesh( string name ) => Advisor.BodyMeshes.Contains( name ) || Advisor.Mouths.Contains( name );

	/// <summary>
	/// The nodes the accessory selector <paramref name="selector"/> hides and shows, applied to <paramref name="hidden"/> in the
	/// original's order: a hat hides the antennae (0x13, 0x14), no accessory shows them, the spatula hides the right hand (0x15), the
	/// theme costume (−1) shows its add-ons and hides its sub-offs; then a nonzero selector shows its own node unless the clip's accessory bit hides the costume.
	/// </summary>
	// [BIN:TPW-EXE:0x00598CA0 accessory selector] 0: show 0x13 and 0x14; 1, 2, 4, 5, 6, 7, 9, 10, 11 and 13: hide 0x13 and 0x14; 14: hide 0x15; −1: for each of the four slots show the nonzero add-on (unless accessory bit 0x10000 is set) and hide the nonzero sub-off; finally show the node of any nonzero selector (unless bit 0x10000)
	// [BIN:TPW-EXE:0x00429EE0 show node] clears node bit 0x10 of the node 0x44B220 finds for the attribute (type mask 0x400); 0x429F60 sets it
	public static void ApplySelector( AdvisorNodeMap model, int selector, AdvisorThemeCostume theme, bool costumeSuppressed, ISet<int> hidden )
	{
		ArgumentNullException.ThrowIfNull( model );
		ArgumentNullException.ThrowIfNull( theme );
		ArgumentNullException.ThrowIfNull( hidden );
		void Show( int value )
		{
			var node = model.FindNode( SelectableTypeFlag, value );
			if ( node >= 0 )
				hidden.Remove( node );
		}
		void Hide( int value )
		{
			var node = model.FindNode( SelectableTypeFlag, value );
			if ( node >= 0 )
				hidden.Add( node );
		}
		switch ( selector )
		{
			case 0:
				Show( FirstAntenna );
				Show( SecondAntenna );
				break;
			case 1 or 2 or 4 or 5 or 6 or 7 or 9 or 10 or 11 or 13:
				Hide( FirstAntenna );
				Hide( SecondAntenna );
				break;
			case 14:
				Hide( RightHand );
				break;
			case -1:
				for ( var slot = 0; slot < AdvisorThemeCostume.Slots; slot++ )
				{
					if ( theme.AddOns[slot] != 0 && !costumeSuppressed )
						Show( theme.AddOns[slot] );
					if ( theme.SubOffs[slot] != 0 )
						Hide( theme.SubOffs[slot] );
				}
				break;
		}
		if ( selector != 0 && !costumeSuppressed )
			Show( selector );
	}

	/// <summary>
	/// The mesh nodes that are hidden: everything but <see cref="IsBaseMesh"/> to start with, then the response's selectors
	/// (<see cref="ApplySelector"/>), then the blink toggles of <paramref name="clip"/> at <paramref name="tick"/> (a positive entry
	/// shows the node, zero or a negative one hides it; mouths follow <see cref="AdvisorMouth"/> instead). Without a costume the
	/// antennae stay shown and no accessory appears.
	/// </summary>
	/// <param name="accessoryBits">The accessory bits of the clip playing (<see cref="AdvisorSequence.AccessoryBits"/>); bit 0 hides the costume.</param>
	public static HashSet<int> HiddenMeshNodes( AdvisorNodeMap model, AdvisorCostume costume, AdvisorThemeCostume theme, int accessoryBits = 0, ModelAnimation? clip = null, float tick = 0 )
	{
		var hidden = CostumeHiddenNodes( model, costume, theme, accessoryBits );
		ApplyToggles( model, hidden, clip, tick );
		return hidden;
	}

	/// <summary>The part of <see cref="HiddenMeshNodes"/> that does not change while a clip plays: the accessories and props the selectors leave hidden. Compute it once per response and clip's accessory bits.</summary>
	public static HashSet<int> CostumeHiddenNodes( AdvisorNodeMap model, AdvisorCostume costume, AdvisorThemeCostume theme, int accessoryBits = 0 )
	{
		ArgumentNullException.ThrowIfNull( model );
		ArgumentNullException.ThrowIfNull( theme );
		var hidden = new HashSet<int>();
		foreach ( var (name, node) in model.Meshes )
			if ( !IsBaseMesh( name ) )
				hidden.Add( node );
		var suppressed = (accessoryBits & SuppressCostumeBit) != 0;
		// [BIN:TPW-EXE:0x00598DC0 accessory update] the first selector (response +0x14) is applied, then the second (+0x18) when it is nonzero
		ApplySelector( model, costume.Hat, theme, suppressed, hidden );
		if ( costume.SecondHat != 0 )
			ApplySelector( model, costume.SecondHat, theme, suppressed, hidden );
		return hidden;
	}

	/// <summary>Applies the blink toggles of <paramref name="clip"/> at <paramref name="tick"/> to <paramref name="hidden"/>; allocation-free.</summary>
	public static void ApplyToggles( AdvisorNodeMap model, ISet<int> hidden, ModelAnimation? clip, float tick )
	{
		if ( clip == null )
			return;
		var tracks = clip.Tracks;
		for ( var index = 0; index < tracks.Count; index++ )
		{
			var track = tracks[index];
			if ( track.NodeFlagToggles.Count == 0 || track.NodeIndex >= model.NodeCount || !model.TogglesApplyTo( track.NodeIndex ) )
				continue;
			// [BIN:STP-PPC:0x000A4F68 record sampler] scanning from the last entry back, the first whose magnitude is at most the tick decides: positive clears bit 0x10 (shown), zero or negative sets it (hidden)
			if ( track.SampleNodeFlag( tick ) is { } hide )
			{
				if ( hide )
					hidden.Add( track.NodeIndex );
				else
					hidden.Remove( track.NodeIndex );
			}
		}
	}
}

/// <summary>Which clip of a sequence plays at a moment after the request, and how far into it.</summary>
internal readonly record struct AdvisorClipPosition( int Clip, int Index, float Tick );

/// <summary>The advisor's clip timeline: the clips of a sequence play one after the other from the request, each for its own length.</summary>
internal static class AdvisorTimeline
{
	/// <summary>The animation clips run at 30 ticks a second (a clip's length is trailer word 2 × 1000 / 30 milliseconds).</summary>
	public const float TicksPerSecond = 30f;

	/// <summary>The clip playing <paramref name="elapsedMilliseconds"/> after the request; null once the last clip has ended.</summary>
	public static AdvisorClipPosition? At( AdvisorSequence sequence, AdvisorClipDurations durations, uint elapsedMilliseconds )
	{
		ArgumentNullException.ThrowIfNull( sequence );
		ArgumentNullException.ThrowIfNull( durations );
		var start = 0uL;
		for ( var index = 0; index < sequence.Clips.Count; index++ )
		{
			var length = durations.PlayOf( sequence.Clips[index] );
			if ( elapsedMilliseconds < start + length )
				return new AdvisorClipPosition( sequence.Clips[index], index, (elapsedMilliseconds - start) * TicksPerSecond / 1000f );
			start += length;
		}
		return null;
	}
}

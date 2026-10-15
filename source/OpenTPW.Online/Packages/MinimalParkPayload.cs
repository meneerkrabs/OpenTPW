using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OpenTPW.Online.Packages;

public sealed record CellRef( int X, int Y );

/// <summary>A placed object as imported from an original save's SYSG record (docs/TPWS-PAYLOAD.md).</summary>
/// <param name="Open">Version 2: the object's open state as published (null in version 1: unknown).</param>
/// <param name="Price">Version 2: the price the owner set (null in version 1 or without an economy).</param>
/// <param name="Repair">Version 2: the object's exact state of repair, 0–100 (null in version 1 or without an economy).</param>
public sealed record ParkObjectRecord( int InfoId, int X, int Y, int Width, int Height, int Rotation, bool Fixed,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] bool? Open = null,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] int? Price = null,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] double? Repair = null );

/// <summary>
/// One non-empty cell of the park cell map (version 2): the original type byte (1 path, 3 queue), the
/// cardinal link bits (1, 4, 16, 64), the queue link toward the queue's predecessor, the flag bits
/// (0x20 NoModify, 0x40 Unowned) and the same-type placement counter.
/// </summary>
public sealed record ParkCellRecord( int X, int Y, byte Type, byte Links, byte QueueLink, ushort Flags, short Placements );

/// <summary>One pylon of a published coaster track (version 2); heights are world units above the ground.</summary>
public sealed record CoasterNodeRecord( int X, int Y, float Height, bool LoopPylon );

/// <summary>A player-built coaster (version 2), by the index of its station in the payload's object list.</summary>
/// <remarks>Node lists are compared structurally, so a deserialized track equals the one it was serialized from.</remarks>
public sealed record CoasterTrackRecord( int Station, IReadOnlyList<CoasterNodeRecord> Nodes, bool Closed, bool Complete )
{
	public bool Equals( CoasterTrackRecord? other ) => other != null && Station == other.Station && Closed == other.Closed
		&& Complete == other.Complete && Nodes.SequenceEqual( other.Nodes );

	public override int GetHashCode() => HashCode.Combine( Station, Closed, Complete, Nodes.Count );
}

/// <summary>The park's published statistics (version 2), shown to visitors read-only; never simulated.</summary>
/// <param name="Visitors">Guests in the park at publication.</param>
/// <param name="Date">The park's in-game date as published, e.g. <c>Year 2, March</c>.</param>
public sealed record ParkStatsRecord( int Rating, long Value, long Balance, int Visitors, int EntranceFee, bool ParkOpen, string Date );

/// <summary>The OpenTPW prototype ride (sandbox Totem), in world units.</summary>
public sealed record PrototypeRideState( float X, float Y, bool Open );

/// <summary>
/// [EXT:ONLINE-007] The park payload. Version 1 carried what main could import from an original level:
/// path cells and placed/fixed objects (Info.Id, cell, footprint, rotation — the original SYSG fields)
/// and the OpenTPW prototype ride. Version 2 carries the complete built park: every non-empty cell of the
/// park cell map (paths and queues with their links and flags), every placed object with rotation, open
/// state, price and state of repair, every player-built coaster track, and the park's published statistics
/// for read-only display. <see cref="Economy"/> stays a reserved stub and must be null: a visit never runs
/// an economy, and the economy slice should publish its own payload format through
/// <see cref="IParkSnapshotSource"/>.
/// </summary>
public sealed record MinimalParkPayload(
	string Level,
	string Source,
	IReadOnlyList<CellRef> PathCells,
	IReadOnlyList<ParkObjectRecord> Objects,
	PrototypeRideState? PrototypeRide,
	JsonElement? Economy,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] IReadOnlyList<ParkCellRecord>? Cells = null,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] IReadOnlyList<CoasterTrackRecord>? Coasters = null,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] ParkStatsRecord? Stats = null,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] string? LayoutDigest = null )
{
	public const string Format = "opentpw.minimal-park";
	/// <summary>The version this build writes; readers accept 1 (original import only) and 2 (full built park).</summary>
	public const int Version = 2;
	public const int FirstSupportedVersion = 1;
	public const string SourceOriginalLevel = "original-level";
	public const string SourceSandbox = "sandbox";

	// [EXT:ONLINE-008] Bounds: MAP grids are at most 1024 cells per side (MapFile.MaximumDimension) and
	// footprints at most 16 cells (SaveObjectList.MaximumFootprint).
	public const int MaximumCell = 1023;
	public const int MaximumPathCells = 1024 * 64;
	public const int MaximumObjects = 4096;
	public const int MaximumFootprint = 16;
	public const float MaximumWorldCoordinate = 100_000f;
	/// <summary>Version 2: most non-empty cells a map of 1024 × 1024 can list; also bounds pylons per track.</summary>
	public const int MaximumCells = 1024 * 64;
	public const int MaximumCoasters = 64;
	public const int MaximumPylonsPerCoaster = 4096;
	/// <summary>The highest price an object record may carry (the original prices are a few hundred at most).</summary>
	public const int MaximumPrice = 100_000;

	internal static readonly Regex DigestPattern = new( "^[0-9a-f]{16}$", RegexOptions.CultureInvariant );

	public byte[] ToBytes()
	{
		Validate( Version );
		return StrictJson.Serialize( this );
	}

	public static MinimalParkPayload FromBytes( byte[] payload )
	{
		var value = StrictJson.Deserialize<MinimalParkPayload>( payload, "Park payload" );
		value.Validate( Version );
		return value;
	}

	/// <summary>The payload version to assume when the document itself carries none (the package manifest names it).</summary>
	public static MinimalParkPayload FromBytes( byte[] payload, int version )
	{
		var value = StrictJson.Deserialize<MinimalParkPayload>( payload, "Park payload" );
		value.Validate( version );
		return value;
	}

	public static MinimalParkPayload FromSnapshot( ParkSnapshot snapshot )
	{
		if ( snapshot.PayloadFormat != Format || snapshot.PayloadVersion is < FirstSupportedVersion or > Version )
			throw new InvalidDataException( $"Park payload '{snapshot.PayloadFormat}' version {snapshot.PayloadVersion} is not supported by this OpenTPW build." );
		var payload = FromBytes( snapshot.Payload, snapshot.PayloadVersion );
		if ( payload.Level != snapshot.Level )
			throw new InvalidDataException( "Park payload level does not match the package." );
		return payload;
	}

	public ParkSnapshot ToSnapshot( IReadOnlyList<RequiredContent> requiredContent )
	{
		var flags = new List<string> { CompatibilityFlags.ReadOnlyVisit, CompatibilityFlags.NoEconomy };
		if ( Source == SourceSandbox )
			flags.Add( CompatibilityFlags.Sandbox );
		return new ParkSnapshot( Format, Version, Level, ToBytes(), requiredContent, flags );
	}

	public void Validate() => Validate( Version );

	/// <param name="version">The version claimed by the container; the members a version may use follow from it.</param>
	public void Validate( int version )
	{
		if ( version is < FirstSupportedVersion or > Version )
			throw new InvalidDataException( $"Park payload version {version} is not supported." );
		if ( Level == null || !ParkPackage.LevelPattern.IsMatch( Level ) )
			throw new InvalidDataException( "Park payload level is invalid." );
		if ( Source is not (SourceOriginalLevel or SourceSandbox) )
			throw new InvalidDataException( "Park payload source is invalid." );
		if ( PathCells == null || Objects == null )
			throw new InvalidDataException( "Park payload lists are missing." );
		if ( PathCells.Count > MaximumPathCells || Objects.Count > MaximumObjects )
			throw new InvalidDataException( "Park payload has too many cells or objects." );
		var seen = new HashSet<(int, int)>();
		foreach ( var cell in PathCells )
		{
			if ( cell == null || !IsCell( cell.X ) || !IsCell( cell.Y ) || !seen.Add( (cell.X, cell.Y) ) )
				throw new InvalidDataException( "Park payload has an invalid or duplicate path cell." );
		}
		foreach ( var item in Objects )
		{
			if ( item == null || item.InfoId < 0 || !IsCell( item.X ) || !IsCell( item.Y )
				|| item.Width is < 0 or > MaximumFootprint || item.Height is < 0 or > MaximumFootprint
				|| item.Rotation is not (0 or 90 or 180 or 270) )
				throw new InvalidDataException( "Park payload has an invalid object record." );
			if ( (item.Open.HasValue || item.Price.HasValue || item.Repair.HasValue) && version < 2 )
				throw new InvalidDataException( "Park payload object state needs payload version 2." );
			if ( item.Price is < 0 or > MaximumPrice || (item.Repair.HasValue && (!double.IsFinite( item.Repair.Value ) || item.Repair.Value is < 0 or > 100)) )
				throw new InvalidDataException( "Park payload object state is invalid." );
		}
		if ( PrototypeRide != null && (!IsCoordinate( PrototypeRide.X ) || !IsCoordinate( PrototypeRide.Y )) )
			throw new InvalidDataException( "Park payload prototype ride position is invalid." );
		if ( Economy.HasValue && Economy.Value.ValueKind != JsonValueKind.Null )
			throw new InvalidDataException( "Park payload economy section is reserved and must be null." );
		var version2 = version >= 2;
		if ( !version2 && (Cells != null || Coasters != null || Stats != null || LayoutDigest != null) )
			throw new InvalidDataException( "Park payload layout members need payload version 2." );
		if ( Cells != null )
		{
			if ( Cells.Count > MaximumCells )
				throw new InvalidDataException( "Park payload has too many cells." );
			var cellSeen = new HashSet<(int, int)>();
			foreach ( var cell in Cells )
			{
				if ( cell == null || !IsCell( cell.X ) || !IsCell( cell.Y ) || !cellSeen.Add( (cell.X, cell.Y) ) )
					throw new InvalidDataException( "Park payload has an invalid or duplicate layout cell." );
				// [DATA:Easymode.TPWI:queue links] a queue cell's link is one of 1, 4, 16, 64 toward its predecessor, else 0.
				if ( cell.QueueLink != 0 && cell.QueueLink is not (1 or 4 or 16 or 64) )
					throw new InvalidDataException( "Park payload has an invalid queue link." );
			}
		}
		if ( Coasters != null )
		{
			if ( Coasters.Count > MaximumCoasters )
				throw new InvalidDataException( "Park payload has too many coasters." );
			for ( var index = 0; index < Coasters.Count; index++ )
			{
				var coaster = Coasters[index];
				if ( coaster == null || coaster.Nodes == null || coaster.Station < 0 || coaster.Station >= Objects.Count
					|| coaster.Nodes.Count > MaximumPylonsPerCoaster || (coaster.Complete && !coaster.Closed) )
					throw new InvalidDataException( "Park payload has an invalid coaster record." );
				foreach ( var node in coaster.Nodes )
				{
					if ( node == null || !IsCell( node.X ) || !IsCell( node.Y ) || !IsCoordinate( node.Height ) )
						throw new InvalidDataException( "Park payload has an invalid coaster pylon." );
				}
			}
		}
		if ( Stats != null )
		{
			if ( Stats.Rating is < 0 or > 1000 || Stats.Value < 0 || Stats.Balance < 0 || Stats.Visitors < 0
				|| Stats.EntranceFee < 0 || Stats.Date == null || Stats.Date.Length > 64 )
				throw new InvalidDataException( "Park payload statistics are invalid." );
		}
		if ( LayoutDigest != null && !DigestPattern.IsMatch( LayoutDigest ) )
			throw new InvalidDataException( "Park payload layout digest is invalid." );
		// The digest is part of the document, so a payload whose members disagree with it is malformed, not merely changed.
		if ( version2 && LayoutDigest != null && LayoutDigest != ComputeLayoutDigest() )
			throw new InvalidDataException( "Park payload layout digest does not match its layout." );
	}

	/// <summary>Whether this payload carries a full built layout (version 2 original level), not only the v1 import view.</summary>
	[JsonIgnore]
	public bool HasBuiltLayout => Cells != null;

	/// <summary>
	/// [EXT:ONLINE-068] The layout digest of this payload's version 2 members: FNV-1a 64 over the cells in
	/// row order, the objects in list order and the coasters with their pylons. A publisher writes it from the
	/// live park; a visit recomputes it from what it built, so "the visitor sees the park as the owner built
	/// it" is checked, not assumed. Floats are hashed by bit pattern (like <c>WorldStateHash</c>).
	/// </summary>
	public string ComputeLayoutDigest() => ParkLayoutDigest.Compute( Cells ?? Array.Empty<ParkCellRecord>(), Objects, Coasters ?? Array.Empty<CoasterTrackRecord>() );

	private static bool IsCell( int value ) => value is >= 0 and <= MaximumCell;
	private static bool IsCoordinate( float value ) => float.IsFinite( value ) && Math.Abs( value ) <= MaximumWorldCoordinate;
}

/// <summary>FNV-1a 64 over little-endian values; shared by the publisher and the visit so both sides digest alike.</summary>
public static class ParkLayoutDigest
{
	/// <summary>Distinct content digests with different meaning; every layout digest starts with it.</summary>
	public const int SchemaVersion = 1;

	public static string Compute( IReadOnlyList<ParkCellRecord> cells, IReadOnlyList<ParkObjectRecord> objects, IReadOnlyList<CoasterTrackRecord> coasters )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( objects );
		ArgumentNullException.ThrowIfNull( coasters );
		ulong hash = 14695981039346656037UL;
		void Add( long value )
		{
			for ( var shift = 0; shift < 64; shift += 8 )
			{
				hash ^= (byte)(value >> shift);
				hash *= 1099511628211UL;
			}
		}
		void AddFloat( float value ) => Add( BitConverter.SingleToInt32Bits( value ) );
		Add( SchemaVersion );
		Add( cells.Count );
		foreach ( var cell in cells.OrderBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			Add( cell.X );
			Add( cell.Y );
			Add( (long)cell.Type | (long)cell.Links << 8 | (long)cell.QueueLink << 16 | (long)cell.Flags << 24 | (long)(ushort)cell.Placements << 40 );
		}
		Add( objects.Count );
		foreach ( var item in objects )
		{
			// The digest is layout identity only: footprint width/height derive from the catalog shape, and the
			// published open state, price and repair are owner state a visit shows but does not re-derive.
			Add( item.InfoId );
			Add( item.X );
			Add( item.Y );
			Add( (long)item.Rotation | (item.Fixed ? 1L : 0L) << 8 );
		}
		Add( coasters.Count );
		foreach ( var coaster in coasters )
		{
			Add( coaster.Station );
			Add( coaster.Nodes.Count );
			Add( coaster.Closed ? 1 : 0 );
			Add( coaster.Complete ? 1 : 0 );
			foreach ( var node in coaster.Nodes )
			{
				Add( node.X );
				Add( node.Y );
				AddFloat( node.Height );
				Add( node.LoopPylon ? 1 : 0 );
			}
		}
		return Convert.ToHexString( BitConverter.GetBytes( unchecked((long)hash) ) ).ToLowerInvariant();
	}
}

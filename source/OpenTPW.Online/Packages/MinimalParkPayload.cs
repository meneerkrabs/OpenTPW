using System.Text.Json;

namespace OpenTPW.Online.Packages;

public sealed record CellRef( int X, int Y );

/// <summary>A placed object as imported from an original save's SYSG record (docs/TPWS-PAYLOAD.md).</summary>
public sealed record ParkObjectRecord( int InfoId, int X, int Y, int Width, int Height, int Rotation, bool Fixed );

/// <summary>The OpenTPW prototype ride (sandbox Totem), in world units.</summary>
public sealed record PrototypeRideState( float X, float Y, bool Open );

/// <summary>
/// [EXT:ONLINE-007] Version 1 park payload: what main can currently represent. Level (theme),
/// imported path cells and placed/fixed objects (Info.Id, cell, footprint, rotation — the
/// original SYSG fields), and the OpenTPW prototype ride. <see cref="Economy"/> is a reserved stub
/// and must be null: money, guests, staff and research belong to the economy slice, which should
/// publish its own payload format through <see cref="IParkSnapshotSource"/>.
/// </summary>
public sealed record MinimalParkPayload(
	string Level,
	string Source,
	IReadOnlyList<CellRef> PathCells,
	IReadOnlyList<ParkObjectRecord> Objects,
	PrototypeRideState? PrototypeRide,
	JsonElement? Economy )
{
	public const string Format = "opentpw.minimal-park";
	public const int Version = 1;
	public const string SourceOriginalLevel = "original-level";
	public const string SourceSandbox = "sandbox";

	// [EXT:ONLINE-008] Bounds: MAP grids are at most 1024 cells per side (MapFile.MaximumDimension) and
	// footprints at most 16 cells (SaveObjectList.MaximumFootprint).
	public const int MaximumCell = 1023;
	public const int MaximumPathCells = 1024 * 64;
	public const int MaximumObjects = 4096;
	public const int MaximumFootprint = 16;
	public const float MaximumWorldCoordinate = 100_000f;

	public byte[] ToBytes()
	{
		Validate();
		return StrictJson.Serialize( this );
	}

	public static MinimalParkPayload FromBytes( byte[] payload )
	{
		var value = StrictJson.Deserialize<MinimalParkPayload>( payload, "Park payload" );
		value.Validate();
		return value;
	}

	public static MinimalParkPayload FromSnapshot( ParkSnapshot snapshot )
	{
		if ( snapshot.PayloadFormat != Format || snapshot.PayloadVersion != Version )
			throw new InvalidDataException( $"Park payload '{snapshot.PayloadFormat}' version {snapshot.PayloadVersion} is not supported by this OpenTPW build." );
		var payload = FromBytes( snapshot.Payload );
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

	public void Validate()
	{
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
		}
		if ( PrototypeRide != null && (!IsCoordinate( PrototypeRide.X ) || !IsCoordinate( PrototypeRide.Y )) )
			throw new InvalidDataException( "Park payload prototype ride position is invalid." );
		if ( Economy.HasValue && Economy.Value.ValueKind != JsonValueKind.Null )
			throw new InvalidDataException( "Park payload economy section is reserved and must be null in version 1." );
	}

	private static bool IsCell( int value ) => value is >= 0 and <= MaximumCell;
	private static bool IsCoordinate( float value ) => float.IsFinite( value ) && Math.Abs( value ) <= MaximumWorldCoordinate;
}

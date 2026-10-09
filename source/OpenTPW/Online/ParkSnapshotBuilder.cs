using System.Security.Cryptography;
using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>
/// Builds the version 1 park snapshot (<see cref="MinimalParkPayload"/>) from what main can load:
/// the level, the imported original Easymode path cells and object records, and the prototype ride.
/// Economy state is not captured (owned by the economy slice; see docs/ONLINE.md).
/// </summary>
public static class ParkSnapshotBuilder
{
	/// <summary>[EXT:ONLINE-061] Edition label; OpenTPW cannot yet tell Theme Park World from Sim Theme Park installs.</summary>
	public const string Edition = "Theme Park World";

	public static ParkSnapshot FromOriginal( OriginalPark park, PrototypeRideState? ride )
	{
		var save = park.Save;
		var objects = new List<ParkObjectRecord>();
		if ( save != null )
		{
			objects.AddRange( save.PlacedObjects.Select( item => ToRecord( item.Record ) ) );
			objects.AddRange( save.UnresolvedObjects.Select( ToRecord ) );
			objects.AddRange( save.FixedItems.Select( ToRecord ) );
		}
		var payload = new MinimalParkPayload( park.LevelName, MinimalParkPayload.SourceOriginalLevel,
			save?.PathCells.Select( cell => new CellRef( cell.X, cell.Y ) ).ToList() ?? new List<CellRef>(), objects, ride, null );
		return payload.ToSnapshot( RequiredContent( park.LevelName, objects.Select( item => item.InfoId ), park.Catalog ) );
	}

	public static ParkSnapshot FromSandbox( string levelName, PrototypeRideState? ride )
	{
		var payload = new MinimalParkPayload( levelName, MinimalParkPayload.SourceSandbox, Array.Empty<CellRef>(), Array.Empty<ParkObjectRecord>(), ride, null );
		return payload.ToSnapshot( RequiredContent( levelName, Array.Empty<int>(), new Dictionary<int, OriginalObjectInfo>() ) );
	}

	/// <summary>Live level: the imported original park or the sandbox, with the placed prototype ride.</summary>
	public static ParkSnapshot FromLevel( Level level )
	{
		var ride = level.PlacedRide == null ? null : new PrototypeRideState( level.PlacedRide.Position.X, level.PlacedRide.Position.Y, level.PlacedRide.IsOpen );
		return level.OriginalPark != null ? FromOriginal( level.OriginalPark, ride ) : FromSandbox( level.LevelName, ride );
	}

	private static ParkObjectRecord ToRecord( SaveObject record ) =>
		new( record.InfoId, record.X, record.Y, record.Width, record.Height, record.Rotation, record.IsFixedItem );

	/// <summary>Level (hash of its terrain MAP), each object Info.Id, and bonus archives (<c>_name_N.wad</c>) by hash.</summary>
	public static IReadOnlyList<RequiredContent> RequiredContent( string levelName, IEnumerable<int> infoIds, IReadOnlyDictionary<int, OriginalObjectInfo> catalog )
	{
		var content = new List<RequiredContent> { new( ContentKinds.Level, levelName, LevelHash( levelName ) ) };
		foreach ( var id in infoIds.Distinct().Order() )
		{
			content.Add( new( ContentKinds.Object, id.ToString( System.Globalization.CultureInfo.InvariantCulture ), null ) );
			if ( catalog.TryGetValue( id, out var info ) && IsBonusArchive( info.ArchivePath ) && content.All( item => item.Id != ArchiveId( info.ArchivePath ) ) )
				content.Add( new( ContentKinds.BonusArchive, ArchiveId( info.ArchivePath ), FileHash( info.ArchivePath ) ) );
		}
		return content;
	}

	/// <summary>[DATA:Theme Park World Bonus Stuff/Bonus content/levels/*/*/_name_N.wad] Bonus archives start with '_'.</summary>
	public static bool IsBonusArchive( string archivePath ) => Path.GetFileName( archivePath.TrimEnd( '/' ) ).StartsWith( '_' );

	private static string ArchiveId( string archivePath ) => archivePath.Replace( '\\', '/' ).TrimStart( '/' ).TrimEnd( '/' ).ToLowerInvariant();

	public static string? LevelHash( string levelName )
	{
		var path = $"/levels/{levelName}/terrain/base.map";
		return FileSystem.FileExists( path ) ? FileHash( path ) : null;
	}

	private static string? FileHash( string path )
	{
		try
		{
			var full = FileSystem.GetAbsolutePath( path );
			if ( File.Exists( full ) )
			{
				using var file = File.OpenRead( full );
				return Convert.ToHexString( SHA256.HashData( file ) ).ToLowerInvariant();
			}
			using var stream = FileSystem.OpenRead( path );
			return Convert.ToHexString( SHA256.HashData( stream ) ).ToLowerInvariant();
		}
		catch ( Exception exception ) when ( exception is IOException or FileNotFoundException or DirectoryNotFoundException or InvalidOperationException )
		{
			return null;
		}
	}

	/// <summary>
	/// [EXT:ONLINE-062] Top-down thumbnail from the MAP grid and the snapshot (OpenTPW colours, not an original
	/// image): blocked/water cells, paths, object footprints, the prototype ride cell.
	/// </summary>
	public static byte[] RenderThumbnail( MapFile? map, MinimalParkPayload payload, Func<float, float, (int X, int Y)?>? rideCell = null )
	{
		var countX = map?.CellCountX ?? 64;
		var countY = map?.CellCountY ?? 64;
		var scale = Math.Clamp( ParkPackage.MaximumThumbnailDimension / Math.Max( countX, countY ), 1, 4 );
		var width = countX * scale;
		var height = countY * scale;
		var pixels = new byte[width * height * 4];
		void Fill( int x, int y, (byte R, byte G, byte B) color )
		{
			if ( x < 0 || y < 0 || x >= countX || y >= countY )
				return;
			for ( var dy = 0; dy < scale; dy++ )
			{
				for ( var dx = 0; dx < scale; dx++ )
				{
					// Image rows run along MAP Y from top to bottom.
					var offset = (((y * scale) + dy) * width + x * scale + dx) * 4;
					(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]) = (color.R, color.G, color.B, 255);
				}
			}
		}
		for ( var y = 0; y < countY; y++ )
		{
			for ( var x = 0; x < countX; x++ )
			{
				var flags = map?.GetFlagsAt( x, y ) ?? MapCellFlags.None;
				Fill( x, y, flags.HasFlag( MapCellFlags.Water ) ? ((byte)48, (byte)96, (byte)200)
					: flags.HasFlag( MapCellFlags.Blocked ) ? ((byte)70, (byte)60, (byte)50)
					: flags.HasFlag( MapCellFlags.EntranceArea ) ? ((byte)180, (byte)180, (byte)180)
					: ((byte)70, (byte)140, (byte)60) );
			}
		}
		foreach ( var cell in payload.PathCells )
			Fill( cell.X, cell.Y, (220, 200, 140) );
		foreach ( var item in payload.Objects )
		{
			if ( item.Fixed )
				continue;
			var minY = item.Rotation == 90 ? item.Y - item.Height + 1 : item.Y;
			for ( var y = minY; y < minY + Math.Max( 1, item.Height ); y++ )
				for ( var x = item.X; x < item.X + Math.Max( 1, item.Width ); x++ )
					Fill( x, y, (230, 120, 30) );
		}
		if ( payload.PrototypeRide != null && rideCell?.Invoke( payload.PrototypeRide.X, payload.PrototypeRide.Y ) is { } ride )
		{
			for ( var y = ride.Y - Level.OriginalRideFootprintRadiusCells; y <= ride.Y + Level.OriginalRideFootprintRadiusCells; y++ )
				for ( var x = ride.X - Level.OriginalRideFootprintRadiusCells; x <= ride.X + Level.OriginalRideFootprintRadiusCells; x++ )
					Fill( x, y, (200, 40, 160) );
		}
		return PngImage.EncodeRgba( width, height, pixels );
	}
}

/// <summary>Adapter so other slices can treat the live level as a snapshot source.</summary>
public sealed class LevelSnapshotSource : IParkSnapshotSource
{
	private readonly Level level;
	public LevelSnapshotSource( Level level ) => this.level = level;
	public ParkSnapshot CaptureSnapshot() => ParkSnapshotBuilder.FromLevel( level );
}

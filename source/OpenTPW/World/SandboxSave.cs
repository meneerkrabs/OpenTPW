using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace OpenTPW;

public record SandboxSave( int Version, float RideX, float RideY, bool HasRide, bool IsRunning )
{
	public const int CurrentVersion = 1;
	private const int MaximumFileSize = 4096;

	public static SandboxSave CreateEmpty() => new( CurrentVersion, 0, 0, false, false );

	public static void Save( string path, SandboxSave state )
	{
		ArgumentNullException.ThrowIfNull( state );
		Validate( state );
		var destination = GetPath( path );
		if ( File.Exists( destination ) )
			Load( destination );
		var temporary = destination + "." + Guid.NewGuid().ToString( "N" ) + ".tmp";
		var ownsTemporary = false;
		try
		{
			using ( var stream = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
			{
				ownsTemporary = true;
				JsonSerializer.Serialize( stream, state );
				stream.Flush( true );
			}
			File.Move( temporary, destination, true );
		}
		finally
		{
			if ( ownsTemporary )
				File.Delete( temporary );
		}
	}

	public static SandboxSave Load( string path )
	{
		using var stream = new FileStream( GetPath( path ), FileMode.Open, FileAccess.Read, FileShare.Read );
		var bytes = new byte[MaximumFileSize + 1];
		var length = 0;
		while ( length < bytes.Length )
		{
			var count = stream.Read( bytes, length, bytes.Length - length );
			if ( count == 0 )
				break;
			length += count;
		}
		if ( length > MaximumFileSize )
			throw new InvalidDataException( "Sandbox save exceeds the size limit." );
		using var document = ParseJson( bytes.AsMemory( 0, length ) );
		var root = document.RootElement;
		if ( root.ValueKind != JsonValueKind.Object )
			throw new InvalidDataException( "Sandbox save must be a JSON object." );
		var properties = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var property in root.EnumerateObject() )
		{
			if ( !properties.Add( property.Name ) || property.Name is not ( "Version" or "RideX" or "RideY" or "HasRide" or "IsRunning" ) )
				throw new InvalidDataException( "Sandbox save contains duplicate or unknown fields." );
		}
		if ( properties.Count != 5
			|| root.GetProperty( "Version" ).ValueKind != JsonValueKind.Number
			|| !root.GetProperty( "Version" ).TryGetInt32( out var version )
			|| root.GetProperty( "RideX" ).ValueKind != JsonValueKind.Number
			|| !root.GetProperty( "RideX" ).TryGetSingle( out var rideX )
			|| root.GetProperty( "RideY" ).ValueKind != JsonValueKind.Number
			|| !root.GetProperty( "RideY" ).TryGetSingle( out var rideY )
			|| root.GetProperty( "HasRide" ).ValueKind is not ( JsonValueKind.True or JsonValueKind.False )
			|| root.GetProperty( "IsRunning" ).ValueKind is not ( JsonValueKind.True or JsonValueKind.False ) )
			throw new InvalidDataException( "Sandbox save has missing or incorrectly typed fields." );
		var state = new SandboxSave( version, rideX, rideY, root.GetProperty( "HasRide" ).GetBoolean(), root.GetProperty( "IsRunning" ).GetBoolean() );
		Validate( state );
		return state;
	}

	private static JsonDocument ParseJson( ReadOnlyMemory<byte> bytes )
	{
		try
		{
			return JsonDocument.Parse( bytes, new JsonDocumentOptions { MaxDepth = 8 } );
		}
		catch ( JsonException exception )
		{
			throw new InvalidDataException( "Sandbox save contains malformed JSON.", exception );
		}
	}

	private static string GetPath( string path )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( path );
		if ( string.Equals( Path.GetExtension( path ), ".tpws", StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidDataException( "Original TPWS files are not sandbox saves." );
		return Path.GetFullPath( path );
	}

	private static void Validate( SandboxSave state )
	{
		if ( state.Version != CurrentVersion )
			throw new InvalidDataException( "Unsupported sandbox save version." );
		if ( !ParkPlacement.IsWithinBounds( new Vector3( state.RideX, state.RideY, 0 ), 0 ) )
			throw new InvalidDataException( "Sandbox ride coordinates must be finite and within the park." );
		if ( state.IsRunning && !state.HasRide )
			throw new InvalidDataException( "An absent ride cannot be running." );
	}
}

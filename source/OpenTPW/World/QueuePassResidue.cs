using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>Native queue metadata on an empty or ordinary path cell. Values are snapshot copies, not a second live map.</summary>
public sealed record QueuePassResidue( [property: JsonRequired] int X, [property: JsonRequired] int Y, [property: JsonRequired] byte Type, [property: JsonRequired] byte QueueLink, [property: JsonRequired] ushort Owner );

public sealed partial class ParkCellMap
{
	// Only provenance/classification is stored here. All observable values live in the usual cell arrays.
	private readonly HashSet<int> queuePassCells = new();
	public bool HasQueuePassResidues => queuePassCells.Count != 0;
	internal bool HasExceptionalQueueMetadata => Enumerable.Range( 0, types.Length ).Any( i => types[i] == 0 && (queueLinks[i] != 0 || owners[i] != 0) || types[i] == 1 && queueLinks[i] != 0 );
	internal bool HasQueuePassResidue( int x, int y ) => queuePassCells.Contains( Index( x, y ) );

	internal IReadOnlyList<QueuePassResidue> CaptureQueuePassResidues() => queuePassCells.OrderBy( i => i )
		.Select( i => new QueuePassResidue( i % Width, i / Width, types[i], queueLinks[i], owners[i] ) ).ToArray();

	/// <summary>Passes 82/83 may write these fields even when the earlier type pass failed. Not a generic snapshot-validation bypass.</summary>
	internal void ApplyQueuePassMetadata( int x, int y, byte direction, ushort owner )
	{
		var index = Index( x, y );
		if ( types[index] is not (0 or 1 or 3) || direction is not (0 or 1 or 4 or 16 or 64) || owner > 128 * 128
			|| Tracks.Get( x, y ) is { Type: not 0 } || PoolBridges?.Covers( x, y ) == true )
			throw new InvalidOperationException( "Unsupported queue pass cell or metadata." );
		var before = Changed != null ? FieldsAt( index ) : default;
		queueLinks[index] = direction;
		owners[index] = owner;
		if ( types[index] is 0 or 1 )
			queuePassCells.Add( index );
		NormalizeQueuePassCell( index );
		Version++;
		NoteChange( x, y, index, before );
	}

	private void NormalizeQueuePassCell( int index )
	{
		if ( !queuePassCells.Contains( index ) )
			return;
		if ( types[index] is not (0 or 1) || (types[index] == 1 ? queueLinks[index] == 0 : links[index] == 0 && queueLinks[index] == 0 && owners[index] == 0) )
			queuePassCells.Remove( index );
	}

	private void AddQueuePassCanonicalState( StateHasher hash )
	{
		var anomalous = Enumerable.Range( 0, types.Length ).Where( i => types[i] == 0 && (queueLinks[i] != 0 || owners[i] != 0) || types[i] == 1 && queueLinks[i] != 0 ).ToArray();
		if ( anomalous.Length == 0 )
			return; // Keep legacy/no-residue pins unchanged.
		hash.Add( 0x51505231 ); // QPR1: raw residue fields, never an origin/cause tag.
		hash.Add( anomalous.Length );
		foreach ( var index in anomalous )
		{
			hash.Add( index );
			hash.Add( types[index] | (long)queueLinks[index] << 8 | (long)owners[index] << 16 | (long)links[index] << 32 );
		}
	}
}

using System.Security.Cryptography;
using System.Text.Json;
using OpenTPW.Online;
using OpenTPW.Online.Coop;

namespace OpenTPW.Server;

/// <summary>A member of a stored co-op session: its id, slot, account and name.</summary>
public sealed record CoopMemberRecord( uint Id, byte Slot, string AccountKey, string Name );

/// <summary>
/// [EXT:online-coop-session] A co-op session as stored (<c>coop/sessions.json</c>), so it survives a restart: its id and code, title, listing, most players,
/// the host's build and mods (a joiner must match them), the host's member id, the next member id, the last frame the host relayed, the members and the
/// accounts kicked from it.
/// </summary>
public sealed record CoopSessionRecord( string Id, string Code, string Title, bool Listed, byte MaximumPlayers, string Build, IReadOnlyList<CoopMod> Mods,
	uint HostMember, uint NextMember, ulong LastFrameSeq, IReadOnlyList<CoopMemberRecord> Members, IReadOnlyList<string> Kicked )
{
	/// <summary>Whether <see cref="LastFrameSeq"/> is the host's last frame (written at a shutdown); a change stored while frames flow leaves it behind.</summary>
	public bool SeqExact { get; init; }
}

/// <summary>
/// [EXT:online-coop-session] The co-op sessions' files in the data folder: <c>coop/sessions.json</c> (rewritten atomically after each change, under this
/// store's own lock, never the account store's) and the join snapshots in <c>coop/blobs/&lt;session&gt;/&lt;sha256&gt;</c>
/// ([EXT:online-coop-snapshot-join]), which the nightly backup leaves out.
/// </summary>
public sealed class CoopStore
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
	private readonly object gate = new();
	private readonly Dictionary<string, CoopSessionRecord> records;

	public CoopStore( string dataDirectory )
	{
		Root = Path.Combine( Path.GetFullPath( dataDirectory ), "coop" );
		Blobs = Path.Combine( Root, "blobs" );
		Directory.CreateDirectory( Blobs );
		var file = Path.Combine( Root, "sessions.json" );
		records = File.Exists( file )
			? (JsonSerializer.Deserialize<List<CoopSessionRecord>>( File.ReadAllBytes( file ), JsonOptions ) ?? new()).ToDictionary( record => record.Id )
			: new();
	}

	public string Root { get; }
	public string Blobs { get; }

	/// <summary>The sessions stored when the server started.</summary>
	public IReadOnlyList<CoopSessionRecord> Load()
	{
		lock ( gate )
			return records.Values.ToList();
	}

	/// <summary>Stores <paramref name="record"/> (a new session or a change) before the change is announced.</summary>
	public void Put( CoopSessionRecord record )
	{
		lock ( gate )
		{
			records[record.Id] = record;
			Save();
		}
	}

	/// <summary>Forgets an ended session and deletes its snapshots.</summary>
	public void Remove( string id )
	{
		lock ( gate )
		{
			if ( records.Remove( id ) )
				Save();
		}
		DeleteBlobs( id );
	}

	/// <summary>Stores the frame positions of every session at once (a restart).</summary>
	public void PutAll( IEnumerable<CoopSessionRecord> changed )
	{
		lock ( gate )
		{
			foreach ( var record in changed )
				records[record.Id] = record;
			Save();
		}
	}

	/// <summary>Whether the last write of <c>sessions.json</c> failed, so the file may hold sessions that changed or ended since.</summary>
	public bool Unsaved
	{
		get
		{
			lock ( gate )
				return unsaved;
		}
	}

	private bool unsaved;

	private void Save()
	{
		unsaved = true;
		AtomicFile.Write( Path.Combine( Root, "sessions.json" ), JsonSerializer.SerializeToUtf8Bytes( records.Values.ToList(), JsonOptions ) );
		unsaved = false;
	}

	// ---- join snapshots ----

	public string BlobFolder( string id ) => Path.Combine( Blobs, id );

	public string BlobPath( string id, string sha256 ) => Path.Combine( BlobFolder( id ), sha256 );

	/// <summary>The bytes the stored snapshots take, every session together (the store's cap); an upload in progress counts by its reservation instead.</summary>
	public long BlobBytes()
	{
		if ( !Directory.Exists( Blobs ) )
			return 0;
		return new DirectoryInfo( Blobs ).EnumerateFiles( "*", SearchOption.AllDirectories ).Where( file => file.Extension != ".tmp" ).Sum( file => file.Length );
	}

	/// <summary>
	/// Streams <paramref name="body"/> to a temporary file of the session's folder, at most <paramref name="maximumBytes"/>, hashing it on the way; returns
	/// the file, its SHA-256 and size. The caller moves it over its digest or deletes it.
	/// </summary>
	public async Task<(string Path, string Sha256, long Bytes)> ReceiveAsync( string id, Stream body, long maximumBytes, CancellationToken cancel )
	{
		var folder = BlobFolder( id );
		Directory.CreateDirectory( folder );
		var temporary = Path.Combine( folder, $"{Guid.NewGuid():N}.tmp" );
		using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		long total = 0;
		try
		{
			await using ( var output = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous ) )
			{
				var buffer = new byte[81920];
				while ( true )
				{
					var count = await body.ReadAsync( buffer, cancel );
					if ( count == 0 )
						break;
					total += count;
					if ( total > maximumBytes )
						throw new StoreException( StatusCodes.Status413PayloadTooLarge, "The snapshot is too large." );
					hash.AppendData( buffer, 0, count );
					await output.WriteAsync( buffer.AsMemory( 0, count ), cancel );
				}
				await output.FlushAsync( cancel );
				output.Flush( true );
			}
			return (temporary, Convert.ToHexStringLower( hash.GetHashAndReset() ), total);
		}
		catch
		{
			File.Delete( temporary );
			throw;
		}
	}

	/// <summary>Moves a received snapshot over its digest and keeps the newest <paramref name="keep"/> of the session's snapshots.</summary>
	public void Commit( string id, string temporary, string sha256, int keep )
	{
		var target = BlobPath( id, sha256 );
		File.Move( temporary, target, overwrite: true );
		File.SetLastWriteTimeUtc( target, DateTime.UtcNow );
		foreach ( var old in new DirectoryInfo( BlobFolder( id ) ).EnumerateFiles().Where( file => file.Extension != ".tmp" )
			.OrderByDescending( file => file.LastWriteTimeUtc ).ThenBy( file => file.Name, StringComparer.Ordinal ).Skip( keep ) )
			old.Delete();
	}

	public void DeleteBlobs( string id )
	{
		var folder = BlobFolder( id );
		if ( Directory.Exists( folder ) )
			Directory.Delete( folder, true );
	}

	/// <summary>Removes the snapshot folders of sessions that no longer exist and every temporary file (a start after a crash).</summary>
	public void SweepOrphans( IReadOnlySet<string> live )
	{
		if ( !Directory.Exists( Blobs ) )
			return;
		foreach ( var folder in Directory.EnumerateDirectories( Blobs ) )
		{
			if ( !live.Contains( Path.GetFileName( folder ) ) )
				Directory.Delete( folder, true );
			else
			{
				foreach ( var temporary in Directory.EnumerateFiles( folder, "*.tmp" ) )
					File.Delete( temporary );
			}
		}
	}
}

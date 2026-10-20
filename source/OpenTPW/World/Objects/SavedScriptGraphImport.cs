using System.Buffers.Binary;
using System.Text;

namespace OpenTPW;

/// <summary>Transactional, ordinary-child-only RSSE continuation. Sound and animation/table residuals remain DET-013.</summary>
internal static class SavedScriptGraphImport
{
	private const int MaximumNodes = 4096, MaximumCandidates = 4096;
	private const long MaximumBytes = 64L * 1024 * 1024, MaximumComparisonBytes = 16L * 1024 * 1024;

	internal static IReadOnlyList<string> Apply( RideScriptWorld world, SaveRsseBlock block, IEnumerable<SavedScriptImport.Candidate> candidates, uint? clock )
	{
		Action commit;
		RideVM[] nodes;
		try { (commit, nodes) = Prepare( world, block, candidates, clock ); }
		catch ( Exception error ) when ( error is InvalidDataException or IOException or ArgumentException or OverflowException )
		{
			return new[] { $"[APPROX:DET-013] RSSE ordinary-child graph refused without changes: {error.Message}" };
		}
		commit();
		// Presentation follows a completed commit; callback failures are never represented as atomic refusal.
		foreach ( var node in nodes )
			if ( node.MusicDuckByte != 0 ) node.RequestMusicDuck( node.MusicDuckByte );
		return new[] { $"RSSE ordinary-child graph imported: {nodes.Length} scripts, saved clock {clock}, pass {block.TickCounter}; reverse saved list order. [APPROX:DET-013] Sound graphs remain unsupported; NAME, animation, object, node, bounce and walk state remain unrestored; TRIGWAITANIM retains its retrigger gate." };
	}

	private static void Require( bool condition, string message )
	{
		if ( !condition ) throw new InvalidDataException( message );
	}

	private static (Action, RideVM[]) Prepare( RideScriptWorld world, SaveRsseBlock block, IEnumerable<SavedScriptImport.Candidate> candidates, uint? clock )
	{
		Require( GameEditions.Current == GameEdition.ThemeParkWorld, "ordinary graph evidence is limited to Theme Park World" );
		Require( clock.HasValue, "a saved clock is required" );
		Require( block.Records.Count is > 0 and <= MaximumNodes, "record count exceeds the graph bound" );
		Require( block.NextScriptId is > 0 and < int.MaxValue, "next ID is not a safe allocation frontier" );
		var records = block.Records.ToArray();
		var byId = new Dictionary<uint, SaveRsseRecord>();
		long stateBytes = 0;
		foreach ( var r in records )
		{
			Require( r.Raw.Length == SaveRsseRecord.Bytes, "invalid record struct" );
			Require( r.ScriptId > 0 && r.ScriptId < block.NextScriptId && byId.TryAdd( r.ScriptId, r ), "duplicate, invalid or out-of-frontier script ID" );
			Require( r.SoundScriptId == 0, $"script {r.ScriptId} has an unsupported sound edge" );
			stateBytes += r.Raw.LongLength + r.Code.LongLength + r.Stack.LongLength + r.Variables.LongLength + r.Strings.LongLength + r.Limbo.LongLength + r.Bounce.LongLength + r.Walk.LongLength + r.Nodes.LongLength;
			Require( stateBytes <= MaximumBytes, "saved graph exceeds the aggregate state bound" );
		}
		var incoming = new HashSet<uint>();
		foreach ( var r in records )
		{
			if ( r.ChildScriptId != 0 )
				Require( r.ChildScriptId != r.ScriptId && incoming.Add( r.ChildScriptId ) && byId.TryGetValue( r.ChildScriptId, out var child ) && child.ParentScriptId == r.ScriptId && child.ThingId == r.ThingId && child.MeshInstance == r.MeshInstance,
					$"script {r.ScriptId} has an unresolved, duplicate or inconsistent child" );
			if ( r.ParentScriptId != 0 )
				Require( byId.TryGetValue( r.ParentScriptId, out var parent ) && parent.ChildScriptId == r.ScriptId, $"script {r.ScriptId} is orphaned" );
		}
		var roots = records.Where( r => r.ParentScriptId == 0 ).ToArray();
		Require( roots.Length > 0, "graph has no root (cycle)" );
		var pool = candidates.Take( MaximumNodes + 1 ).ToArray();
		Require( pool.Length <= MaximumNodes && world.Scripts.Count == roots.Length && world.ClockMilliseconds == 0 && world.PassCounter == 0, "world is not an exact fresh root set" );
		var vms = new Dictionary<uint, RideVM>();
		var entries = new Dictionary<uint, ObjectCatalogEntry>();
		var preparations = new List<Action>();
		var files = new Dictionary<ObjectCatalogEntry, List<(string Path, RideScriptFile File)>>();
		long inputBytes = 0, comparedBytes = 0;
		var candidateCount = 0;
		var seenRoots = new HashSet<RideVM>();
		foreach ( var r in roots )
		{
			var owners = pool.Where( c => c.ThingId == r.ThingId ).ToArray();
			Require( owners.Length == 1, $"root {r.ScriptId} has no unique imported owner" );
			var c = owners[0];
			Require( ReferenceEquals( c.Script.World, world ) && world.Scripts.Contains( c.Script ) && seenRoots.Add( c.Script ) && c.Script.Parent == null && c.Script.Child == null && c.Script.SoundChild == null && c.Script.TimeMilliseconds == 0 && c.Script.ClockOriginMilliseconds == 0 && c.Script.ExecutedInstructions == 0 && c.Script.CaptureSnapshot().UsesWorldRandom && c.Script.SliceCount == 0,
				$"root {r.ScriptId} is not fresh and shared-RNG" );
			Require( c.Entry.Root.IsData && OriginalObjectRuntime.HasCatalogResolver( c.Script, c.Entry ), "root has no trusted data-catalog resolver" );
			Require( c.Entry.ScriptPath != null && Folder( r.Folder ) == DirectoryOf( c.Entry.ScriptPath ), $"root {r.ScriptId} folder does not match" );
			Require( Matches( c.Script.Script, r, ref comparedBytes ), $"root {r.ScriptId} embedded identity differs" );
			vms.Add( r.ScriptId, c.Script ); entries.Add( r.ScriptId, c.Entry );
		}
		var queue = new Queue<SaveRsseRecord>( roots );
		var visited = new HashSet<uint>();
		while ( queue.TryDequeue( out var r ) )
		{
			Require( visited.Add( r.ScriptId ), "graph cycle" );
			var vm = vms[r.ScriptId];
			Require( vm.SliceBudget == r.TimeSlice, $"script {r.ScriptId} scheduler budget differs" );
			var reason = vm.PrepareSavedRunState( r, clock!.Value, true, false, out var apply );
			Require( reason == null, $"script {r.ScriptId}: {reason}" );
			preparations.Add( apply! );
			if ( r.ChildScriptId == 0 ) continue;
			var child = byId[r.ChildScriptId];
			var entry = entries[r.ScriptId];
			Require( Folder( child.Folder ) == Folder( r.Folder ), $"child {child.ScriptId} folder differs" );
			if ( !files.TryGetValue( entry, out var catalog ) )
			{
				catalog = new();
				foreach ( var path in entry.Scripts )
				{
					Require( ++candidateCount <= MaximumCandidates, "catalog candidate bound exceeded" );
					if ( DirectoryOf( path ) != Folder( r.Folder ) ) continue;
					Require( inputBytes < MaximumBytes, "catalog byte bound exhausted before opening another candidate" );
					using var stream = entry.FileSystem.OpenRead( path );
					catalog.Add( (path, ReadCatalogScript( stream, ref inputBytes )) );
				}
				files.Add( entry, catalog );
			}
			var matches = new List<(string Path, RideScriptFile File)>();
			foreach ( var file in catalog ) if ( Matches( file.File, child, ref comparedBytes ) ) matches.Add( file );
			Require( matches.Count == 1, $"child {child.ScriptId} has no unique embedded identity" );
			var chosen = matches[0];
			var name = Path.GetFileName( chosen.Path );
			Require( !name.Contains( '\\' ) && !name.Contains( '/' ), "unsupported resolver filename separators" );
			Require( entry.Scripts.Count( p => string.Equals( Path.GetFileName( p ), name, StringComparison.OrdinalIgnoreCase ) ) == 1, "ambiguous resolver filename" );
			var staged = new RideVM( chosen.File, new RideVMOptions { World = world, Effects = vm.Options.Effects, ResolveScript = vm.Options.ResolveScript, SourceName = $"{vm.SourceName} > {name}", Seed = null }, vm, register: false );
			vms.Add( child.ScriptId, staged ); entries.Add( child.ScriptId, entry ); queue.Enqueue( child );
		}
		Require( visited.Count == records.Length, "orphaned cycle or unreachable record" );
		var ordered = records.Select( r => (Vm: vms[r.ScriptId], Id: (int)r.ScriptId) ).ToArray();
		var finish = world.PrepareOriginalGraphCommit( ordered, (int)block.NextScriptId, block.TickCounter, clock!.Value );
		var links = records.Where( r => r.ChildScriptId != 0 ).Select( r => (Parent: vms[r.ScriptId], Child: vms[r.ChildScriptId]) ).ToArray();
		return (() => { foreach ( var apply in preparations ) apply(); foreach ( var (parent, child) in links ) parent.AdoptChild( child, false ); finish(); }, ordered.Select( pair => pair.Vm ).ToArray());
	}

	// Reserve both budgets before every read, including an EOF probe. Exact exhaustion is conservatively refused.
	internal static RideScriptFile ReadCatalogScript( Stream stream, ref long inputBytes )
	{
		using var data = new MemoryStream();
		var buffer = new byte[8192];
		while ( true )
		{
			var remaining = Math.Min( MaximumBytes - inputBytes, RideScriptFile.MaximumFileBytes - data.Length );
			Require( remaining > 0, "catalog byte bound exhausted" );
			var count = stream.Read( buffer, 0, (int)Math.Min( buffer.Length, remaining ) );
			if ( count == 0 ) break;
			inputBytes += count;
			data.Write( buffer, 0, count );
		}
		data.Position = 0;
		return new RideScriptFile( data );
	}

	private static string Folder( string path )
	{
		var parts = path.Replace( '\\', '/' ).Trim( '/' ).Split( '/' );
		Require( parts.Length > 0 && parts.All( part => part.Length > 0 && part != "." && part != ".." && !part.Contains( ':' ) ), "unsupported script folder" );
		if ( string.Equals( parts[0], "data", StringComparison.OrdinalIgnoreCase ) ) parts = parts.Skip( 1 ).ToArray();
		return string.Join( '/', parts ).ToLowerInvariant();
	}
	private static string DirectoryOf( string path ) => Folder( Path.GetDirectoryName( path.Replace( '\\', '/' ) ) ?? "" );

	private static bool Matches( RideScriptFile file, SaveRsseRecord r, ref long work )
	{
		Require( file.StackSize >= 0 && file.LimboSize >= 0 && file.BounceSize >= 0 && file.WalkSize >= 0 && file.StackSize <= RideVM.MaximumHeaderTableSize && file.LimboSize <= RideVM.MaximumHeaderTableSize && file.BounceSize <= RideVM.MaximumHeaderTableSize && file.WalkSize <= RideVM.MaximumHeaderTableSize && file.TimeSlice > 0, "invalid installed script header dimensions" );
		work += r.Code.LongLength + r.Strings.LongLength;
		Require( work <= MaximumComparisonBytes, "identity comparison bound exceeded" );
		if ( r.CodeWordCount != file.CodeWordCount || r.Code.LongLength != file.CodeWordCount * 4L || r.StackSize != file.StackSize || r.Stack.LongLength != file.StackSize * 4L || r.VariableCount != file.VariableCount || r.Variables.LongLength != file.VariableCount * 4L || r.LimboSize != file.LimboSize || r.Limbo.LongLength != file.LimboSize * 8L || r.BounceSize != file.BounceSize || r.Bounce.LongLength != file.BounceSize * 16L || r.WalkSize != file.WalkSize || r.Walk.LongLength != file.WalkSize * 32L || r.StringBytes != file.StringBlobLength || r.Strings.Length != file.StringBlobLength || r.TimeSlice != file.TimeSlice || r.Nodes.LongLength != r.NodeCount * 4L ) return false;
		var word = 0;
		foreach ( var instruction in file.Instructions )
		{
			if ( instruction.WordOffset != word || BinaryPrimitives.ReadUInt32LittleEndian( r.Code.AsSpan( word++ * 4 ) ) != (0x80000000u | instruction.Opcode) ) return false;
			foreach ( var operand in instruction.Operands )
				if ( BinaryPrimitives.ReadUInt32LittleEndian( r.Code.AsSpan( word++ * 4 ) ) != ((uint)operand.Kind << 16 | operand.Value) ) return false;
		}
		if ( word != file.CodeWordCount ) return false;
		var position = 0;
		foreach ( var (offset, text) in file.Strings.OrderBy( pair => pair.Key ) )
		{
			var bytes = Encoding.Latin1.GetBytes( text );
			if ( offset != position || position + bytes.Length >= r.Strings.Length || !r.Strings.AsSpan( position, bytes.Length ).SequenceEqual( bytes ) || r.Strings[position + bytes.Length] != 0 ) return false;
			position += bytes.Length + 1;
		}
		return position == r.Strings.Length;
	}
}

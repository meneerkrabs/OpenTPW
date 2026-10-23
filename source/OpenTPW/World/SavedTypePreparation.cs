namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-snapshot-join] What a saved layout's types read on their first use, read ahead in steps of their own before the restore places their objects (a co-op join's or
/// resync's build, <see cref="Level"/>'s restore of a saved layout, docs/COOP.md "The game's join"), the types in the order the layout first uses them. First each type's model and,
/// when its parts show a sign and it is not the gate (whose slots show the park's name), its sign: composed on the desktop's worker (<see cref="ObjectSigns.ComposeAhead"/>), which then
/// works while the rest is read, or where there is none over steps (<see cref="ObjectSigns.ComposeAheadSteps"/>). Then each type's clips, a step each; each texture its parts and its
/// animated texture slots draw (a sign slot's shipped placeholder, drawn only when its sign cannot be, is left to the object); and its hoarding boards over the level's terrain, with
/// the level's four hoarding textures once a type has boards. Each step gives the share done.
/// <para>
/// The steps fill only the caches an object reads (the models, the path and bonus textures, the signs ahead, the level's hoarding boards), so a type's first object finds them as its
/// later ones do. They read the layout's records and the level's catalogue, gate and terrain, and nothing of the park: no object, runtime or controller is made and no seed, allocator
/// or <see cref="Level.Current"/> is read or touched. A read that fails leaves nothing and drops what it logged, so the object meets the failure as it did: a texture's lookup ahead of
/// use remembers no failed read of a directory (<see cref="ObjectAssets.ResolveTexture(ObjectCatalogEntry, string, bool)"/>), and the level's hoarding textures are only made, never
/// kept for the boards, which read them themselves. A read that succeeds logs at once (the reads log nothing when they succeed). The steps take the build's files as they are once the
/// build has them (the files stage), unchanged while it runs: a file that changed after its type's step is read from that step's cache.
/// </para>
/// </summary>
internal static class SavedTypePreparation
{
	/// <summary>
	/// What the steps read with: the level's catalogue and gate (null when its <c>global.sam</c> names none), its terrain and name for the hoarding (null without an original park),
	/// its hoarding cache, and the loads of an object's texture and of a level's hoarding texture by name, ahead of use.
	/// </summary>
	internal sealed record Reads( ObjectCatalog Catalog, int? GateInfoId, ModelHeightfield? Field, string? LevelName, ObjectHoardingCache Hoarding,
		Action<ObjectCatalogEntry, string> LoadTexture, Action<string, string> LoadHoardingTexture );

	/// <summary>The steps (see <see cref="SavedTypePreparation"/>) for <paramref name="layout"/>'s types.</summary>
	internal static IEnumerable<float> Steps( ParkLayoutSnapshot.Data layout, Reads reads )
	{
		var types = new List<ObjectCatalogEntry>();
		var seen = new HashSet<ObjectCatalogEntry>( ReferenceEqualityComparer.Instance );
		// a record the catalogue does not know is the restore's to refuse
		foreach ( var record in layout.Objects ?? [] )
			if ( record != null && reads.Catalog.Find( record.InfoId ) is { } known && seen.Add( known ) )
				types.Add( known );
		var models = new ModelFile?[types.Count];
		for ( var index = 0; index < types.Count; index++ )
		{
			var entry = types[index];
			var share = 0.5f * index / types.Count;
			ModelFile? model = null;
			using ( LoadProfile.Measure( "type-model" ) )
				Prepare( () => model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath ) );
			yield return share;
			if ( model is not { Kind: ModelFileKind.Geometry } geometry )
				continue;
			models[index] = geometry;
			if ( ObjectRenderParts.TextureNames( geometry ).Any( ObjectSigns.IsSignSlot ) && !SignSlotPlan.IsGate( entry.IsFixedItem, entry.InfoId, entry.ArchivePath, reads.GateInfoId ) )
			{
				if ( ObjectSigns.ComposeAheadEnabled )
					ObjectSigns.ComposeAhead( entry );
				else
					foreach ( var _ in ObjectSigns.ComposeAheadSteps( entry ) )
						yield return share;
			}
		}
		var hoardingTextures = false;
		for ( var index = 0; index < types.Count; index++ )
		{
			var entry = types[index];
			var share = 0.5f + 0.5f * index / types.Count;
			foreach ( var animation in entry.Animations )
			{
				using ( LoadProfile.Measure( "type-model" ) )
					Prepare( () => ObjectAssets.LoadModel( entry.FileSystem, animation.Path ) );
				yield return share;
			}
			if ( models[index] is not { } geometry )
				continue;
			var frames = new List<string>();
			Prepare( () => frames.AddRange( OriginalObject.FrameAnimatedSlots( entry, geometry ).SelectMany( slot => slot.Frames ) ) );
			foreach ( var name in ObjectRenderParts.TextureNames( geometry ).Where( name => !ObjectSigns.IsSignSlot( name ) ).Concat( frames ).Distinct( StringComparer.OrdinalIgnoreCase ) )
			{
				using ( LoadProfile.Measure( "type-texture" ) )
					Prepare( () => reads.LoadTexture( entry, name ) );
				yield return share;
			}
			// the boards ObjectHoardingRenderer.Create reads, on the same terrain, and its textures once a type has boards
			if ( reads.Field is not { } field || reads.LevelName is not { } levelName )
				continue;
			ObjectHoardingGeometry? boards = null;
			using ( LoadProfile.Measure( "type-hoarding" ) )
				Prepare( () => boards = reads.Hoarding.Geometry( entry, geometry, field ) );
			yield return share;
			if ( boards == null || hoardingTextures )
				continue;
			hoardingTextures = true;
			foreach ( var name in ObjectHoardingGeometry.TextureNames )
			{
				using ( LoadProfile.Measure( "type-hoarding" ) )
					Prepare( () => reads.LoadHoardingTexture( levelName, name ) );
				yield return share;
			}
		}
	}

	/// <summary>A read of the steps: what it logs is written when it succeeds and dropped when it fails, which the object then meets itself.</summary>
	private static void Prepare( Action read )
	{
		IReadOnlyList<(Logger.Level Severity, string Text, bool Quiet)> logged;
		using ( var capture = Logger.BeginCapture() )
		{
			try
			{
				read();
			}
			catch
			{
				return;
			}
			logged = capture.Messages;
		}
		Logger.Replay( logged );
	}
}

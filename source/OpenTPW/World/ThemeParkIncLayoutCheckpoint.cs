using System.Text.Json;

namespace OpenTPW;

/// <summary>An application-owned layout, explicitly starting a new experimental session, never a native save.</summary>
// [EXT:tpi-layout-checkpoint] an application-owned layout format of the experimental Theme Park Inc slice (an OpenTPW design decision, docs/PARK-SAVES.md): it keeps placement, not running guest/script/clock state, and loading starts a fresh experimental session; no original Theme Park Inc save or layout format is claimed
internal sealed record ThemeParkIncLayoutCheckpoint
{
	public required string Format { get; init; }
	public required int Version { get; init; }
	public required string RestartPolicy { get; init; }
	public required string Theme { get; init; }
	public required ulong Seed { get; init; }
	public required DateTimeOffset SavedUtc { get; init; }
	public required ParkLayoutSnapshot.Data Layout { get; init; }
	public required IReadOnlyDictionary<string, string> AssetHashes { get; init; }

	public const string FormatName = "opentpw-tpi-layout";
	public const int CurrentVersion = 2;
	public const string FreshSession = "new-session";
	public const int MaximumFileSize = ParkResumeSaveFile.MaximumFileSize;

	internal void ValidateShape()
	{
		if (Format != FormatName || Version != CurrentVersion || RestartPolicy != FreshSession)
			throw new InvalidDataException("Unsupported Theme Park Inc layout checkpoint format, version or restart policy.");
		if (Theme is not ("arabian" or "science" or "water") || SavedUtc == default || SavedUtc.Offset != TimeSpan.Zero)
			throw new InvalidDataException("Invalid Theme Park Inc layout checkpoint theme or UTC date.");
		if (Layout == null || Layout.Cells == null || Layout.Objects == null ||
			Layout.ObjectScriptKey != new WorldSeed(Seed).ObjectScriptKey)
			throw new InvalidDataException("The checkpoint layout or world seed is inconsistent.");
		// Reuse the cell validator and reconstruction contract; no live map is changed.
		_ = ParkLayoutSnapshot.RestoreCellMap(Layout);
		if (Layout.NextSeed < 1 || Layout.NextSeed == int.MaxValue || Layout.NextAttractionId < 1 || Layout.NextAttractionId == int.MaxValue ||
			Layout.Objects.Any(item => item == null))
			throw new InvalidDataException("The checkpoint contains invalid object records or exhausted allocators.");
		var ordinals = new HashSet<int>();
		var previousId = 0;
		foreach (var item in Layout.Objects)
		{
			var ordinal = item.CreationSeed ^ Layout.ObjectScriptKey;
			if (item.InfoId < 0 || string.IsNullOrWhiteSpace(item.Archive) || item.Archive.Length > ParkResumeAssets.MaximumKeyLength ||
				!float.IsFinite(item.BaseHeight) || !ObjectFootprint.IsValidRotation(item.Rotation) ||
				ordinal < 1 || ordinal >= Layout.NextSeed || !ordinals.Add(ordinal) ||
				item.AttractionId <= previousId || item.AttractionId >= Layout.NextAttractionId)
				throw new InvalidDataException("The checkpoint object identity, placement or seed is inconsistent.");
			previousId = item.AttractionId;
		}
		if (AssetHashes == null || AssetHashes.Count == 0 || AssetHashes.Count > ParkResumeAssets.MaximumManifestEntries ||
			AssetHashes.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > ParkResumeAssets.MaximumKeyLength ||
				pair.Value == null || !ParkResumeAssets.IsValidSha256(pair.Value)))
			throw new InvalidDataException("The checkpoint asset manifest is invalid or empty.");
	}

	internal void Validate(ObjectCatalog catalog, IReadOnlyDictionary<string, string> currentAssets, int width, int height)
	{
		ValidateShape();
		if (catalog.Theme != Theme || Layout.Cells.Width != width || Layout.Cells.Height != height)
			throw new InvalidDataException("The checkpoint theme or terrain dimensions do not match the selected world.");
		ParkLayoutSnapshot.ValidateSnapshot(Layout, catalog);
		if (Layout.Objects.Any(item => CoasterDefinition.IsCoaster(catalog.Get(item.InfoId).Settings)))
			throw new NotSupportedException("Coaster layouts are not supported by this TPI layout checkpoint.");
		if (AssetHashes.Count != currentAssets.Count || AssetHashes.Any(pair => !currentAssets.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
			throw new InvalidDataException("The checkpoint's game assets changed or are missing.");
	}

	internal ParkCellMap RestoreInto(ParkObjects target, IReadOnlyDictionary<string, string> currentAssets, int width, int height)
	{
		Validate(target.Catalog, currentAssets, width, height);
		var cells = ParkLayoutSnapshot.RestoreCellMap(Layout);
		ParkLayoutSnapshot.RestoreObjects(Layout, target);
		return cells;
	}

	internal static byte[] Serialize(ThemeParkIncLayoutCheckpoint snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		snapshot.ValidateShape();
		var bytes = ParkResumeJson.Serialize(snapshot);
		if (bytes.Length > MaximumFileSize) throw new InvalidDataException("Layout checkpoint exceeds the size limit.");
		return bytes;
	}

	internal static ThemeParkIncLayoutCheckpoint Deserialize(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length > MaximumFileSize) throw new InvalidDataException("Layout checkpoint exceeds the size limit.");
		try
		{
			using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
			RejectDuplicates(document.RootElement);
			var snapshot = ParkResumeJson.Deserialize<ThemeParkIncLayoutCheckpoint>(bytes);
			if (snapshot.Version == 1)
			{
				if (snapshot.Layout?.Cells == null || snapshot.Layout.Cells.QueuePassSchemaVersion != 0 || snapshot.Layout.Cells.QueuePassResidues != null)
					throw new InvalidDataException("Legacy layout checkpoint contains unsupported queue pass fields.");
				snapshot = snapshot with { Version = CurrentVersion };
			}
			snapshot.ValidateShape();
			return snapshot;
		}
		catch (JsonException error) { throw new InvalidDataException("Layout checkpoint contains malformed or unknown fields.", error); }
	}

	private static void RejectDuplicates(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (var property in element.EnumerateObject())
			{
				if (!names.Add(property.Name)) throw new InvalidDataException("Layout checkpoint contains duplicate fields.");
				RejectDuplicates(property.Value);
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
			foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
	}

	private static string OwnPath(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (!Path.GetExtension(path).Equals(".tpilayout", StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("TPI layout checkpoints require the application-owned .tpilayout suffix.");
		return Path.GetFullPath(path);
	}

	internal static void Save(string path, ThemeParkIncLayoutCheckpoint snapshot)
	{
		var destination = OwnPath(path);
		var bytes = Serialize(snapshot);
		var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
		var ownsTemporary = false;
		try
		{
			using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				ownsTemporary = true;
				stream.Write(bytes);
				stream.Flush(true);
			}
			File.Move(temporary, destination, true);
		}
		finally { if (ownsTemporary) File.Delete(temporary); }
	}

	internal static ThemeParkIncLayoutCheckpoint Load(string path)
	{
		using var stream = new FileStream(OwnPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
		if (stream.Length > MaximumFileSize) throw new InvalidDataException("Layout checkpoint exceeds the size limit.");
		var bytes = new byte[stream.Length];
		stream.ReadExactly(bytes);
		return Deserialize(bytes);
	}
}

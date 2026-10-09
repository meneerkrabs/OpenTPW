using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenTPW;

if (args.Length == 1 && args[0] == "--self-test")
{
    var same = CompareSettings("A 1 # old comment\nB true\n", "A 1.0 # new comment\nB TRUE\n");
    if (same.SemanticChanges.Count != 0 || same.ParsedEntriesEqual) throw new Exception("SAM normalization test failed.");
    var changed = CompareSettings("A 1\nB true\n", "A 2\nB false\n");
    if (changed.SemanticChanges.Count != 2) throw new Exception("SAM change test failed.");
    var oldScript = SyntheticScript(1);
    var newScript = SyntheticScript(2);
    var script = CompareScript(oldScript, newScript);
    if (script.ChangedInstructions.Count != 1 || script.ChangedInstructions[0].WordOffset != 0) throw new Exception("RSE operand test failed.");
    var identical = CompareScript(oldScript, oldScript);
    if (identical.ChangedInstructions.Count != 0) throw new Exception("RSE identity test failed.");
    var shortened = CompareScript(SyntheticScriptWords(0x8000000Fu, 0x80000012u, 5u, 0u, 0x8000000Fu), SyntheticScriptWords(0x80000012u, 5u, 0u));
    if (shortened.ChangedInstructions.Count != 2 || shortened.ChangedInstructions.Any(i => i.OldOpcode != 15 || i.NewOpcode != null)) throw new Exception("RSE deletion alignment test failed.");
    if (CompareSettings("A 1\nA 2\n", "A 2\nA 1\n").SemanticChanges.Count != 1) throw new Exception("SAM duplicate order test failed.");
    var fixtureRoot = Directory.CreateTempSubdirectory("tpw-patch-analysis-").FullName;
    try
    {
        File.WriteAllBytes(Path.Combine(fixtureRoot, "before.wad"), SyntheticWad(1));
        File.WriteAllBytes(Path.Combine(fixtureRoot, "after.wad"), SyntheticWad(2));
        var fs = new BaseFileSystem(fixtureRoot);
        var oldMembers = ReadWad(fs, "before.wad");
        var newMembers = ReadWad(fs, "after.wad");
        var memberDiff = CompareMembers(oldMembers, newMembers);
        if (memberDiff.Count != 1 || memberDiff[0].Path != "member.bin" || memberDiff[0].Before!.Sha256 == memberDiff[0].After!.Sha256) throw new Exception("WAD member difference test failed.");
        if (CompareMembers(oldMembers, oldMembers).Count != 0) throw new Exception("WAD identity test failed.");
    }
    finally { Directory.Delete(fixtureRoot, true); }
    Console.WriteLine("SAM typed normalization/change, WAD member/identity and RSE operand/identity checks passed.");
    return;
}
if (args.Length != 3) throw new ArgumentException("Usage: PatchAnalysis <old-installation> <new-installation> <metadata.json>; or --self-test");
var oldRoot = Path.GetFullPath(args[0]);
var newRoot = Path.GetFullPath(args[1]);
if (!Directory.Exists(Path.Combine(oldRoot, "Data")) || !Directory.Exists(Path.Combine(newRoot, "Data")))
    throw new DirectoryNotFoundException("Both existing roots must contain Data.");
var oldFs = new BaseFileSystem(oldRoot);
var newFs = new BaseFileSystem(newRoot);
var oldFiles = Inventory(oldRoot);
var newFiles = Inventory(newRoot);
var changes = new List<FileChange>();
int unchanged = 0;
foreach (var name in oldFiles.Keys.Union(newFiles.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
{
    oldFiles.TryGetValue(name, out var oldName);
    newFiles.TryGetValue(name, out var newName);
    var before = oldName == null ? null : DigestFile(Path.Combine(oldRoot, oldName));
    var after = newName == null ? null : DigestFile(Path.Combine(newRoot, newName));
    if (before == after) { unchanged++; continue; }
    var change = new FileChange(name, before == null ? "added" : after == null ? "removed" : "changed", before, after);
    if (Path.GetExtension(name).Equals(".wad", StringComparison.OrdinalIgnoreCase))
    {
        var oldMembers = oldName == null ? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) : ReadWad(oldFs, oldName);
        var newMembers = newName == null ? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) : ReadWad(newFs, newName);
        change.Members = CompareMembers(oldMembers, newMembers);
        change.OldMemberCount = oldMembers.Count;
        change.NewMemberCount = newMembers.Count;
    }
    else if (Path.GetExtension(name).Equals(".sam", StringComparison.OrdinalIgnoreCase))
        change.Settings = CompareSettings(oldName == null ? "" : oldFs.ReadAllText(oldName), newName == null ? "" : newFs.ReadAllText(newName));
    else if (oldName != null && newName != null && Path.GetExtension(name).Equals(".rse", StringComparison.OrdinalIgnoreCase))
        change.Script = CompareScript(oldFs.ReadAllBytes(oldName), newFs.ReadAllBytes(newName));
    changes.Add(change);
}
var report = new { schema = 1, scope = "Data only; no gameplay claims", oldFiles = oldFiles.Count, newFiles = newFiles.Count, unchangedFiles = unchanged, changes };
File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
Console.WriteLine($"Compared Data files: old={oldFiles.Count}, new={newFiles.Count}, unchanged={unchanged}, differences={changes.Count}.");

static Dictionary<string, string> Inventory(string root) => Directory.GetFiles(Path.Combine(root, "Data"), "*", SearchOption.AllDirectories)
    .ToDictionary(p => Path.GetRelativePath(root, p).Replace('\\', '/'), p => Path.GetRelativePath(root, p), StringComparer.OrdinalIgnoreCase);
static Digest DigestFile(string path) { using var input = File.OpenRead(path); return new(input.Length, Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant()); }
static Digest DigestBytes(byte[] bytes) => new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
static string HashString(string value) => DigestBytes(Encoding.UTF8.GetBytes(value)).Sha256;
static Dictionary<string, byte[]> ReadWad(BaseFileSystem fs, string path)
{
    using var stream = fs.OpenRead(path);
    using var wad = new WadArchive(stream);
    var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    Walk(wad.Root, "");
    return result;
    void Walk(ArchiveDirectory directory, string prefix)
    {
        foreach (var item in directory.Children)
        {
            var name = prefix + item.Name;
            if (item is ArchiveDirectory child) Walk(child, name + "/");
            else if (item is ArchiveFile file) result.Add(name, file.GetData());
        }
    }
}
static List<MemberChange> CompareMembers(Dictionary<string, byte[]> oldMembers, Dictionary<string, byte[]> newMembers)
{
    var result = new List<MemberChange>();
    foreach (var name in oldMembers.Keys.Union(newMembers.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
    {
        oldMembers.TryGetValue(name, out var before);
        newMembers.TryGetValue(name, out var after);
        if (before != null && after != null && before.AsSpan().SequenceEqual(after)) continue;
        var change = new MemberChange(name, before == null ? "added" : after == null ? "removed" : "changed", before == null ? null : DigestBytes(before), after == null ? null : DigestBytes(after));
        if (before != null && after != null && Path.GetExtension(name).Equals(".sam", StringComparison.OrdinalIgnoreCase))
            change.Settings = CompareSettings(Encoding.ASCII.GetString(before), Encoding.ASCII.GetString(after));
        if (before != null && after != null && Path.GetExtension(name).Equals(".rse", StringComparison.OrdinalIgnoreCase))
            change.Script = CompareScript(before, after);
        result.Add(change);
    }
    return result;
}
static TypedValue Typed(string value)
{
    if (bool.TryParse(value, out var boolean)) return new("boolean", boolean ? "true" : "false");
    if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return new("number", number.ToString("G29", CultureInfo.InvariantCulture));
    return new("text-sha256", HashString(value));
}
static SettingsDiff CompareSettings(string before, string after)
{
    var oldEntries = new SAMParser(before).Parse();
    var newEntries = new SAMParser(after).Parse();
    var differences = new List<SettingsChange>();
    // Preserve duplicate keys and their ordering instead of silently collapsing them.
    var oldGroups = oldEntries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(e => Typed(e.Value)).ToArray(), StringComparer.OrdinalIgnoreCase);
    var newGroups = newEntries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(e => Typed(e.Value)).ToArray(), StringComparer.OrdinalIgnoreCase);
    foreach (var key in oldGroups.Keys.Union(newGroups.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
    {
        oldGroups.TryGetValue(key, out var oldValues);
        newGroups.TryGetValue(key, out var newValues);
        if (oldValues != null && newValues != null && oldValues.SequenceEqual(newValues)) continue;
        differences.Add(new(key, oldValues, newValues));
    }
    string NonCommentText(string text) => string.Join("\n", text.Split('\n').Select(line => line.Split('#')[0]).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
    return new(oldEntries.Count, newEntries.Count, oldEntries.SequenceEqual(newEntries), NonCommentText(before) == NonCommentText(after), differences);
}
static ScriptDiff CompareScript(byte[] before, byte[] after)
{
    var oldScript = new RideScriptFile(new MemoryStream(before));
    var newScript = new RideScriptFile(new MemoryStream(after));
    var oldInstructions = oldScript.Instructions;
    var newInstructions = newScript.Instructions;
    // Bound the dynamic-programming alignment; never allocate a corpus-sized grid.
    if ((long)(oldInstructions.Count + 1) * (newInstructions.Count + 1) > 1_000_000)
        throw new InvalidDataException("RSE alignment exceeds one million cells.");
    var lengths = new int[oldInstructions.Count + 1, newInstructions.Count + 1];
    bool SameShape(RideScriptInstruction a, RideScriptInstruction b) => a.Opcode == b.Opcode && a.Operands.Select(o => o.Kind).SequenceEqual(b.Operands.Select(o => o.Kind));
    for (int i = oldInstructions.Count - 1; i >= 0; i--)
        for (int j = newInstructions.Count - 1; j >= 0; j--)
            lengths[i, j] = SameShape(oldInstructions[i], newInstructions[j]) ? 1 + lengths[i + 1, j + 1] : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
    var changed = new List<InstructionChange>();
    int oldIndex = 0, newIndex = 0;
    while (oldIndex < oldInstructions.Count || newIndex < newInstructions.Count)
    {
        var oldInstruction = oldIndex < oldInstructions.Count ? oldInstructions[oldIndex] : null;
        var newInstruction = newIndex < newInstructions.Count ? newInstructions[newIndex] : null;
        if (oldInstruction != null && newInstruction != null && SameShape(oldInstruction, newInstruction))
        {
            var operandChanges = new List<OperandChange>();
            for (int i = 0; i < oldInstruction.Operands.Count; i++)
            {
                var oldOperand = oldInstruction.Operands[i];
                var newOperand = newInstruction.Operands[i];
                if (oldOperand != newOperand) operandChanges.Add(new(i, oldOperand.Kind.ToString(), oldOperand.Value, newOperand.Kind.ToString(), newOperand.Value));
            }
            if (operandChanges.Count != 0) changed.Add(new(oldInstruction.WordOffset, newInstruction.WordOffset, oldInstruction.Opcode, newInstruction.Opcode, operandChanges));
            oldIndex++; newIndex++;
        }
        else if (oldInstruction != null && (newInstruction == null || lengths[oldIndex + 1, newIndex] >= lengths[oldIndex, newIndex + 1]))
        {
            changed.Add(new(oldInstruction.WordOffset, null, oldInstruction.Opcode, null, new()));
            oldIndex++;
        }
        else
        {
            changed.Add(new(null, newInstruction!.WordOffset, null, newInstruction.Opcode, new()));
            newIndex++;
        }
    }
    return new(Header(oldScript), Header(newScript), changed,
        StringChanges(oldScript.Strings, newScript.Strings),
        HashString(string.Join("\0", oldScript.VariableNames)) != HashString(string.Join("\0", newScript.VariableNames)));
}
static object Header(RideScriptFile s) => new { s.VariableCount, s.StackSize, s.TimeSlice, s.LimboSize, s.BounceSize, s.WalkSize, s.CodeWordCount, s.StringBlobLength, instructionCount = s.Instructions.Count };
static List<StringChange> StringChanges(IReadOnlyDictionary<int, string> before, IReadOnlyDictionary<int, string> after)
{
    var result = new List<StringChange>();
    foreach (var offset in before.Keys.Union(after.Keys).Order())
    {
        before.TryGetValue(offset, out var oldValue);
        after.TryGetValue(offset, out var newValue);
        if (oldValue != newValue) result.Add(new(offset, oldValue == null ? null : HashString(oldValue), newValue == null ? null : HashString(newValue)));
    }
    return result;
}
static byte[] SyntheticScript(ushort literal) => SyntheticScriptWords(0x80000001u, literal);
static byte[] SyntheticScriptWords(params uint[] words)
{
    using var memory = new MemoryStream();
    using var writer = new BinaryWriter(memory, Encoding.ASCII, true);
    writer.Write(new byte[] { (byte)'R', (byte)'S', (byte)'S', (byte)'E', (byte)'Q', 15, 1, 0 });
    for (int i = 0; i < 6; i++) writer.Write(0);
    writer.Write(Encoding.ASCII.GetBytes("Pad Pad Pad Pad "));
    writer.Write((uint)words.Length);
    foreach (var word in words) writer.Write(word);
    writer.Write(0u);
    return memory.ToArray();
}
static byte[] SyntheticWad(byte value)
{
    using var memory = new MemoryStream();
    using var writer = new BinaryWriter(memory, Encoding.ASCII, true);
    var name = Encoding.ASCII.GetBytes("member.bin\0");
    writer.Write(Encoding.ASCII.GetBytes("DWFB")); writer.Write(1);
    writer.Write(new byte[64]); writer.Write(1); writer.Write(88); writer.Write(40); writer.Write(0);
    writer.Write(0); writer.Write(128); writer.Write(name.Length); writer.Write(128 + name.Length);
    writer.Write(1); writer.Write(0); writer.Write(1); writer.Write(new byte[12]);
    writer.Write(name); writer.Write(value);
    return memory.ToArray();
}
record Digest(long Bytes, string Sha256);
record TypedValue(string Kind, string Value);
record SettingsChange(string Key, TypedValue[]? Before, TypedValue[]? After);
record SettingsDiff(int OldEntries, int NewEntries, bool ParsedEntriesEqual, bool NonCommentTextEqual, List<SettingsChange> SemanticChanges);
record OperandChange(int OperandIndex, string? OldKind, ushort? OldValue, string? NewKind, ushort? NewValue);
record InstructionChange(int? WordOffset, int? NewWordOffset, ushort? OldOpcode, ushort? NewOpcode, List<OperandChange> Operands);
record StringChange(int BlobOffset, string? OldSha256, string? NewSha256);
record ScriptDiff(object Before, object After, List<InstructionChange> ChangedInstructions, List<StringChange> ChangedStrings, bool VariableNamesChanged);
record MemberChange(string Path, string Status, Digest? Before, Digest? After)
{
    public SettingsDiff? Settings { get; set; }
    public ScriptDiff? Script { get; set; }
}
record FileChange(string Path, string Status, Digest? Before, Digest? After)
{
    public int? OldMemberCount { get; set; }
    public int? NewMemberCount { get; set; }
    public List<MemberChange>? Members { get; set; }
    public SettingsDiff? Settings { get; set; }
    public ScriptDiff? Script { get; set; }
}

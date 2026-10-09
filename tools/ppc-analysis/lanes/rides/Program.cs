using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenTPW;
using OpenTPW.PpcEvidence;

if (args.Length != 2) throw new ArgumentException("Usage: CorpusWitness <installation-root> <metadata-output.json>");
var root = Path.GetFullPath(args[0]);
var levelRoot = Path.Combine(root, "Data", "levels");
if (!Directory.Exists(levelRoot)) throw new DirectoryNotFoundException(levelRoot);
var scripts = new List<object>();
var commands = new SortedDictionary<int, SortedDictionary<int, int>>();
var observations = new SortedDictionary<string, List<ControllerUse>>(StringComparer.Ordinal);
var trackSettings = new List<object>();
var animationCounts = new SortedDictionary<int, int>();
var animationSpeedCalls = new List<object>();
var totemBindings = new List<object>();
int count = 0, instructions = 0;
foreach (var archive in Directory.GetFiles(levelRoot, "*.wad", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    using var wad = new WadArchive(archive);
    var rootMembers = wad.Root.Children.OfType<ArchiveFile>()
        .ToDictionary(f => f.Name ?? throw new InvalidDataException("Unnamed archive member."), StringComparer.OrdinalIgnoreCase);
    Walk(wad.Root, "");
    void Walk(ArchiveDirectory directory, string prefix)
    {
        foreach (var item in directory.Children)
        {
            var member = prefix + item.Name;
            if (item is ArchiveDirectory child) Walk(child, member + "/");
            else if (item is ArchiveFile setting && member.EndsWith(".sam", StringComparison.OrdinalIgnoreCase))
            {
                var data = setting.GetData();
                var values = new SAMParser(Encoding.ASCII.GetString(data)).Parse()
                    .Where(e => e.Key.StartsWith("TrackInfo.", StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith(".Direction", StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith(".WhichTrackType", StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith(".uiMaxCars", StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith(".uiFrontCarTypeIndex", StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith(".uiCentreCarTypeIndex", StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith(".uiRearCarTypeIndex", StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith(".fFrontCarSpacing", StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith(".fStdCarSpacing", StringComparison.OrdinalIgnoreCase)
                        || e.Key.EndsWith(".fEndCarSpacing", StringComparison.OrdinalIgnoreCase))
                    .Where(e => double.TryParse(e.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    .Select(e => new { key = e.Key, value = double.Parse(e.Value, CultureInfo.InvariantCulture) }).ToArray();
                if (values.Length > 0) trackSettings.Add(new { path = Path.GetRelativePath(root, archive).Replace('\\', '/') + "/" + member,
                    sha256 = Hash(data), values });
            }
            else if (item is ArchiveFile file && member.EndsWith(".rse", StringComparison.OrdinalIgnoreCase))
            {
                var data = file.GetData();
                var script = new RideScriptFile(new MemoryStream(data));
                count++;
                instructions += script.Instructions.Count;
                foreach (var instruction in script.Instructions.Where(i => i.Opcode is 16 or 17 or 18 or 19 or 21 or 23 or 25 or 27))
                {
                    animationCounts[instruction.Opcode] = animationCounts.GetValueOrDefault(instruction.Opcode) + 1;
                    var scriptPath = Path.GetRelativePath(root, archive).Replace('\\', '/') + "/" + member;
                    if (instruction.Opcode == 21)
                        animationSpeedCalls.Add(new { path = scriptPath, sha256 = Hash(data), word = instruction.WordOffset,
                            kind = instruction.Operands[3].Kind.ToString(), raw = instruction.Operands[3].Value });
                    if (scriptPath.EndsWith("jungle/rides/totem.wad/Totem.RSE", StringComparison.OrdinalIgnoreCase)
                        && instruction.Opcode is 16 or 17 or 18 or 19 or 23)
                    {
                        var category = instruction.Operands[0];
                        var variant = instruction.Operands[1];
                        if (category.Kind != RideScriptOperandKind.Literal || variant.Kind != RideScriptOperandKind.Literal)
                            throw new InvalidDataException("Totem witness animation selectors must be literal.");
                        var binding = OriginalAnimationState.BindingMetadata(category.Value, variant.Value).RequireValue();
                        var numbered = "totem" + binding.NumberedSuffix;
                        ArchiveFile? clip = rootMembers.GetValueOrDefault(numbered);
                        if (clip is null && binding.UnnumberedFallbackSuffix is not null)
                            clip = rootMembers.GetValueOrDefault("totem" + binding.UnnumberedFallbackSuffix);
                        if (clip is null) throw new InvalidDataException($"Totem selector has no corresponding PC member: category{category.Value}, variant{variant.Value}.");
                        totemBindings.Add(new { path = scriptPath, sha256 = Hash(data), word = instruction.WordOffset,
                            opcode = instruction.Opcode, category = category.Value, variant = variant.Value,
                            channel = instruction.Opcode == 23 ? instruction.Operands[3].Value : OriginalAnimationState.DefaultChannel,
                            member = clip.Name, memberSha256 = Hash(clip.GetData()) });
                    }
                }
                foreach (var instruction in script.Instructions.Where(i => i.Opcode is 53 or 54 or 55))
                {
                    if (instruction.Operands[0].Kind != RideScriptOperandKind.Literal)
                        throw new InvalidDataException("Controller command must be literal in this witness corpus.");
                    if (!commands.TryGetValue(instruction.Opcode, out var histogram))
                        commands[instruction.Opcode] = histogram = new();
                    var command = instruction.Operands[0].Value;
                    histogram[command] = histogram.GetValueOrDefault(command) + 1;
                    var parameter = instruction.Operands[1];
                    var key = $"{instruction.Opcode:D2}:{command:D2}";
                    if (!observations.TryGetValue(key, out var uses)) observations[key] = uses = new();
                    var instructionIndex = script.Instructions.ToList().IndexOf(instruction);
                    var nextOpcode = instructionIndex + 1 < script.Instructions.Count ? script.Instructions[instructionIndex + 1].Opcode : (int?)null;
                    uses.Add(new ControllerUse(Path.GetRelativePath(root, archive).Replace('\\', '/') + "/" + member, Hash(data),
                        instruction.WordOffset, parameter.Kind.ToString(), parameter.Value,
                        parameter.Kind == RideScriptOperandKind.Variable ? script.VariableNames[parameter.Value] : null, nextOpcode));
                }
                var path = Path.GetRelativePath(root, archive).Replace('\\', '/') + "/" + member;
                if (path.Contains("/rides/totem.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/bumper.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/gokarts.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/wateride.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/b_drip.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/twetours.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/moonshot.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/bouncy.wad/", StringComparison.OrdinalIgnoreCase))
                {
                    var running = script.VariableNames.ToList().IndexOf("VAR_RUNNING");
                    var writes = script.Instructions.Where(i => i.Opcode == 3 && i.Operands.Count == 2
                        && i.Operands[0].Kind == RideScriptOperandKind.Variable && i.Operands[0].Value == running
                        && i.Operands[1].Kind == RideScriptOperandKind.Literal)
                        .Select(i => new { word = i.WordOffset, value = i.Operands[1].Value }).ToArray();
                    scripts.Add(new { path, sha256 = Hash(data), script.CodeWordCount, instructionCount = script.Instructions.Count,
                        script.StackSize, script.TimeSlice, script.LimboSize, script.BounceSize, script.WalkSize,
                        hostVariableIndices = new[] { "VAR_LETMEON", "VAR_LETMEOFF", "VAR_CAPACITY", "VAR_DURATION", "VAR_ONRIDE", "VAR_RUNNING", "VAR_WORN" }
                            .ToDictionary(name => name, name => script.VariableNames.ToList().IndexOf(name)),
                        runningWrites = writes, controllerUses = script.Instructions.Count(i => i.Opcode is 53 or 54 or 55),
                        animationChannels = script.Instructions.Where(i => i.Opcode is 23 or 25)
                            .Select(i => i.Operands[3]).Where(o => o.Kind == RideScriptOperandKind.Literal)
                            .Select(o => o.Value).Distinct().Order().ToArray() });
                    if (path.EndsWith("jungle/rides/totem.wad/Totem.RSE", StringComparison.OrdinalIgnoreCase)
                        && (Hash(data) != "5e1ab461c3692c32ead298defb847eb623db24cc4068cfe9760c53dd70233aaf"
                            || script.CodeWordCount != 357 || script.Instructions.Count != 129))
                        throw new InvalidDataException("Totem identity differs from the documented PC witness.");
                }
            }
        }
    }
}
if (count != 308) throw new InvalidDataException($"Expected 308 baseline/verified Patch 2 scripts, observed {count}.");
File.WriteAllText(args[1], JsonSerializer.Serialize(new { schema = 1, scope = "PC asset structure; no machine instructions or game execution",
    scriptCount = count, instructionCount = instructions, controllerCommandCounts = commands, selectedScripts = scripts,
    commandObservations = observations.Select(pair => new { commandKey = pair.Key, count = pair.Value.Count,
        operandKinds = pair.Value.GroupBy(v => v.ParameterKind).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new { kind = g.Key, count = g.Count() }).ToArray(),
        representative = pair.Value[0] }).ToArray(), trackSettings, animationOpcodeCounts = animationCounts,
    animationSpeedCalls, totemBindings },
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Parsed {count} scripts, {instructions} instructions; wrote metadata only.");
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
sealed record ControllerUse(string Path, string Sha256, int Word, string ParameterKind, ushort RawValue, string? VariableName, int? NextOpcode);

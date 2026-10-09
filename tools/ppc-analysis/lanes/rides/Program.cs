using System.Security.Cryptography;
using System.Text.Json;
using OpenTPW;

if (args.Length != 2) throw new ArgumentException("Usage: CorpusWitness <installation-root> <metadata-output.json>");
var root = Path.GetFullPath(args[0]);
var levelRoot = Path.Combine(root, "Data", "levels");
if (!Directory.Exists(levelRoot)) throw new DirectoryNotFoundException(levelRoot);
var scripts = new List<object>();
var commands = new SortedDictionary<int, SortedDictionary<int, int>>();
int count = 0, instructions = 0;
foreach (var archive in Directory.GetFiles(levelRoot, "*.wad", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    using var wad = new WadArchive(archive);
    Walk(wad.Root, "");
    void Walk(ArchiveDirectory directory, string prefix)
    {
        foreach (var item in directory.Children)
        {
            var member = prefix + item.Name;
            if (item is ArchiveDirectory child) Walk(child, member + "/");
            else if (item is ArchiveFile file && member.EndsWith(".rse", StringComparison.OrdinalIgnoreCase))
            {
                var data = file.GetData();
                var script = new RideScriptFile(new MemoryStream(data));
                count++;
                instructions += script.Instructions.Count;
                foreach (var instruction in script.Instructions.Where(i => i.Opcode is 53 or 54 or 55))
                {
                    if (instruction.Operands[0].Kind != RideScriptOperandKind.Literal)
                        throw new InvalidDataException("Controller command must be literal in this witness corpus.");
                    if (!commands.TryGetValue(instruction.Opcode, out var histogram))
                        commands[instruction.Opcode] = histogram = new();
                    var command = instruction.Operands[0].Value;
                    histogram[command] = histogram.GetValueOrDefault(command) + 1;
                }
                var path = Path.GetRelativePath(root, archive).Replace('\\', '/') + "/" + member;
                if (path.Contains("/rides/totem.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/bumper.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/gokarts.wad/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/rides/wateride.wad/", StringComparison.OrdinalIgnoreCase)
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
    scriptCount = count, instructionCount = instructions, controllerCommandCounts = commands, selectedScripts = scripts },
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Parsed {count} scripts, {instructions} instructions; wrote metadata only.");
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

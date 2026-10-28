using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

// This audits bytes; it never loads an assembly into the CLR or executes game code.
return AuditProgram.Run(args);

internal static class AuditProgram
{
    private const long MaxDllBytes = 256L * 1024 * 1024;
    private const int MaxShaderBytes = 1024 * 1024;
    private const int MaxFrameworkFiles = 4096;
    private static readonly string[] AppAssemblyNames =
    [
        "OpenTPW.Web", "OpenTPW.Web.Veldrid", "OpenTPW.Online"
    ];
    private static readonly string[] ShaderPaths =
    [
        "content/shaders/blit.shader", "content/shaders/web/blit.json",
        "content/shaders/test.shader", "content/shaders/web/test.json"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static int Run(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine("Usage: PathOracleAudit --web-root WWWROOT --repository-root CHECKOUT " +
                "--allowlist tools/web-path-render/artifact-allowlist.json --output REPORT.json");
            return 2;
        }

        var report = new AuditReport();
        try
        {
            Audit(options, report);
            report.Passed = report.Errors.Count == 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or BadImageFormatException or
            JsonException or OverflowException or NotSupportedException)
        {
            // Malformed metadata, bad paths and missing files are failures, not skipped inputs.
            report.Errors.Add($"{error.GetType().Name}: {error.Message}");
        }

        try
        {
            EnsureNoLinks(options.Output);
            Directory.CreateDirectory(Path.GetDirectoryName(options.Output)!);
            File.WriteAllText(options.Output, JsonSerializer.Serialize(report, JsonOptions) + "\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Cannot write audit report: {error.Message}");
            return 1;
        }

        Console.WriteLine($"Path oracle resource audit: {(report.Passed ? "PASS" : "FAIL")}; " +
            $"{report.Assemblies.Count} final PE assemblies inspected.");
        foreach (string error in report.Errors)
            Console.Error.WriteLine(error);
        return report.Passed ? 0 : 1;
    }

    private static void Audit(Options options, AuditReport report)
    {
        EnsureNoLinks(options.WebRoot);
        EnsureNoLinks(options.RepositoryRoot);
        if (!Directory.Exists(options.WebRoot) || !Directory.Exists(options.RepositoryRoot))
            throw new IOException("The published web root and repository root must both exist.");
        EnsureNoLinks(options.Allowlist);
        byte[] allowlistBytes = ReadBoundedFile(options.Allowlist, MaxShaderBytes);
        report.AllowlistSha256 = Hash(allowlistBytes);
        Dictionary<string, ShaderRecord> shaders = ReadShaders(options, allowlistBytes, report);

        string frameworkRoot = Path.Combine(options.WebRoot, "_framework");
        EnsureNoLinks(frameworkRoot);
        if (!Directory.Exists(frameworkRoot))
            throw new IOException("The actual published wwwroot/_framework directory is missing.");

        List<string> dlls = EnumerateFrameworkFiles(frameworkRoot)
            .Where(path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal).ToList();
        if (dlls.Count == 0)
            throw new InvalidOperationException("No final PE DLLs found; publish the isolated diagnostic with WasmEnableWebcil=false.");

        foreach (string dll in dlls)
        {
            var assembly = new AssemblyRecord { Path = Relative(options.WebRoot, dll) };
            report.Assemblies.Add(assembly);
            InspectAssembly(dll, assembly, shaders, report);
        }

        foreach (string expected in AppAssemblyNames)
        {
            int count = report.Assemblies.Count(assembly => assembly.AssemblyName == expected);
            if (count != 1)
                report.Errors.Add($"Expected exactly one final assembly named {expected}; found {count}.");
        }
        foreach (var duplicate in report.Assemblies.Where(assembly => assembly.AssemblyName is not null)
            .GroupBy(assembly => (assembly.AssemblyName, assembly.Culture)).Where(group => group.Count() > 1))
        {
            report.Errors.Add($"Duplicate final assembly name/culture: {duplicate.Key.AssemblyName}/{duplicate.Key.Culture}.");
        }
    }

    private static Dictionary<string, ShaderRecord> ReadShaders(Options options, byte[] bytes, AuditReport report)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Count(property => property.Name == "shaders") != 1 ||
            !root.TryGetProperty("shaders", out JsonElement entries) ||
            entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() != ShaderPaths.Length)
            throw new InvalidOperationException("The allowlist must have one shaders array containing exactly four entries.");

        if (root.EnumerateObject().Select(property => property.Name).Distinct().Count() !=
            root.EnumerateObject().Count())
            throw new InvalidOperationException("Duplicate top-level allowlist properties are forbidden.");

        var shaders = new Dictionary<string, ShaderRecord>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || entry.EnumerateObject().Count() != 3 ||
                entry.EnumerateObject().Select(property => property.Name).Distinct().Count() != 3)
                throw new InvalidOperationException("Each shader entry must contain exactly path, logicalName and sha256.");
            string path = RequiredString(entry, "path");
            string logicalName = RequiredString(entry, "logicalName");
            string sha256 = RequiredString(entry, "sha256");
            if (!ShaderPaths.Contains(path, StringComparer.Ordinal) || !paths.Add(path))
                throw new InvalidOperationException($"Unexpected or duplicate shader source path: {path}.");
            if (logicalName != path)
                throw new InvalidOperationException($"Shader logicalName must equal its exact content/shaders path: {path}.");
            if (sha256.Length != 64 || sha256.Any(character => !((character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f'))))
                throw new InvalidOperationException($"Shader {path} must pin a lowercase SHA-256.");

            string source = Path.GetFullPath(Path.Combine(options.RepositoryRoot, path));
            if (!IsUnder(options.RepositoryRoot, source) || source == options.Output)
                throw new InvalidOperationException("Invalid shader source/report path relationship.");
            EnsureNoLinks(source);
            byte[] sourceBytes = ReadBoundedFile(source, MaxShaderBytes);
            var shader = new ShaderRecord
            {
                Path = path, LogicalName = logicalName, Sha256 = sha256,
                SourceSha256 = Hash(sourceBytes), Length = sourceBytes.Length
            };
            report.Shaders.Add(shader);
            if (shader.SourceSha256 != shader.Sha256)
                throw new InvalidOperationException($"Repository shader hash differs from its pin: {path}.");
            if (!shaders.TryAdd(logicalName, shader))
                throw new InvalidOperationException($"Duplicate shader logical name: {logicalName}.");
        }
        return shaders;
    }

    private static void InspectAssembly(string path, AssemblyRecord assembly,
        Dictionary<string, ShaderRecord> shaders, AuditReport report)
    {
        byte[] bytes = ReadBoundedFile(path, MaxDllBytes);
        assembly.Length = bytes.Length;
        assembly.Sha256 = Hash(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            throw new BadImageFormatException($"Final DLL has no managed metadata: {assembly.Path}.");
        MetadataReader metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly)
            throw new BadImageFormatException($"Final DLL is not an assembly: {assembly.Path}.");
        AssemblyDefinition definition = metadata.GetAssemblyDefinition();
        string assemblyName = metadata.GetString(definition.Name);
        string culture = metadata.GetString(definition.Culture);
        if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName.Any(char.IsControl))
            throw new BadImageFormatException($"Invalid assembly name: {assembly.Path}.");
        assembly.AssemblyName = assemblyName;
        assembly.Version = definition.Version.ToString();
        assembly.Culture = culture;
        bool isApp = AppAssemblyNames.Contains(assemblyName, StringComparer.Ordinal);
        if (assemblyName.StartsWith("OpenTPW", StringComparison.OrdinalIgnoreCase) && !isApp)
            report.Errors.Add($"Unexpected OpenTPW assembly: {assembly.AssemblyName} ({assembly.Path}).");
        if (IsTestAssembly(assemblyName))
            report.Errors.Add($"Test/framework assembly is forbidden: {assembly.AssemblyName} ({assembly.Path}).");
        if (isApp && culture.Length != 0)
            report.Errors.Add($"Project assembly must be culture-neutral: {assembly.AssemblyName}.");

        if (metadata.AssemblyFiles.Count != 0)
            report.Errors.Add($"Linked assembly files/modules are forbidden: {assemblyName}.");
        if (metadata.ManifestResources.Count > 10000)
            throw new BadImageFormatException($"Too many manifest resources: {assembly.Path}.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        byte[]? resourceBlock = null;
        foreach (ManifestResourceHandle handle in metadata.ManifestResources)
        {
            ManifestResource resource = metadata.GetManifestResource(handle);
            var item = new ResourceRecord
            {
                Name = metadata.GetString(resource.Name),
                Attributes = resource.Attributes.ToString(),
                Offset = resource.Offset,
                Embedded = resource.Implementation.IsNil,
                Implementation = resource.Implementation.IsNil ? "embedded" : resource.Implementation.Kind.ToString()
            };
            assembly.Resources.Add(item);
            if (!names.Add(item.Name))
                report.Errors.Add($"Duplicate manifest resource in {assembly.AssemblyName}: {item.Name}.");
            if (!item.Embedded)
            {
                report.Errors.Add($"Linked/external manifest resource is forbidden: {assembly.AssemblyName}/{item.Name}.");
                continue;
            }
            resourceBlock ??= ReadResourceBlock(pe, assembly.Path);
            long offset = resource.Offset;
            if (offset < 0 || offset > resourceBlock.Length - 4L)
                throw new BadImageFormatException($"Manifest resource offset is outside its directory: {assembly.AssemblyName}/{item.Name}.");
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(resourceBlock.AsSpan((int)offset, 4));
            if (length > resourceBlock.Length - offset - 4L)
                throw new BadImageFormatException($"Manifest resource length is outside its directory: {assembly.AssemblyName}/{item.Name}.");
            item.Length = length;
            item.Sha256 = Hash(resourceBlock.AsSpan((int)offset + 4, checked((int)length)));

            if (assembly.AssemblyName == "OpenTPW.Web")
            {
                if (!shaders.TryGetValue(item.Name, out ShaderRecord? shader))
                    report.Errors.Add($"Unexpected Web embedded resource: {item.Name}.");
                else if (item.Sha256 != shader.Sha256 || item.Length != shader.Length)
                    report.Errors.Add($"Published Web resource differs from pinned repository shader: {item.Name}.");
            }
        }

        if (assembly.AssemblyName == "OpenTPW.Web" &&
            (assembly.Resources.Count != shaders.Count || !names.SetEquals(shaders.Keys)))
            report.Errors.Add("OpenTPW.Web must contain exactly the four pinned shader resources.");
        if (isApp && assembly.AssemblyName != "OpenTPW.Web" && assembly.Resources.Count != 0)
            report.Errors.Add($"{assembly.AssemblyName} must have zero manifest resources.");
    }

    private static bool IsTestAssembly(string name) =>
        new[] { "MSTest", "Microsoft.VisualStudio.TestPlatform", "Microsoft.TestPlatform",
            "Microsoft.Testing", "testhost", "nunit", "xunit" }
            .Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
        name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase) ||
        name.Contains(".Tests.", StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadResourceBlock(PEReader pe, string path)
    {
        DirectoryEntry directory = pe.PEHeaders.CorHeader?.ResourcesDirectory
            ?? throw new BadImageFormatException($"Missing CLR header: {path}.");
        if (directory.RelativeVirtualAddress <= 0 || directory.Size < 4 || directory.Size > MaxDllBytes)
            throw new BadImageFormatException($"Invalid CLR resources directory: {path}.");
        return pe.GetSectionData(directory.RelativeVirtualAddress).GetContent(0, directory.Size).ToArray();
    }

    private static IEnumerable<string> EnumerateFrameworkFiles(string root)
    {
        int count = 0;
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (++count > MaxFrameworkFiles)
                    throw new IOException("Too many entries in published _framework.");
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Links are forbidden in published _framework.");
                if ((attributes & FileAttributes.Directory) != 0)
                    directories.Push(entry);
                else
                    yield return entry;
            }
        }
    }

    private static byte[] ReadBoundedFile(string path, long limit)
    {
        EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0 || stream.Length > limit)
            throw new IOException($"Input file has an invalid size: {Path.GetFileName(path)}.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new IOException("Input file changed size during audit.");
        return bytes;
    }

    // Inspect ancestors as well: checking only a leaf does not reject a symlinked directory.
    private static void EnsureNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Symbolic links/reparse points are forbidden: {current}.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static string RequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String &&
        property.GetString() is string text && text.Length != 0
            ? text : throw new InvalidOperationException($"Missing or invalid shader field: {name}.");

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static bool IsUnder(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private sealed record Options(string WebRoot, string RepositoryRoot, string Allowlist, string Output)
    {
        internal static Options Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] names = ["--web-root", "--repository-root", "--allowlist", "--output"];
            if (args.Length != names.Length * 2)
                throw new ArgumentException("Exactly four named arguments are required.");
            for (int index = 0; index < args.Length; index += 2)
            {
                if (!names.Contains(args[index], StringComparer.Ordinal) || string.IsNullOrWhiteSpace(args[index + 1]) ||
                    !values.TryAdd(args[index], args[index + 1]))
                    throw new ArgumentException("Unknown, empty or duplicate argument.");
            }
            string repository = Path.GetFullPath(values["--repository-root"]);
            string allowlist = Path.GetFullPath(values["--allowlist"], repository);
            if (!IsUnder(repository, allowlist))
                throw new ArgumentException("The allowlist must be inside the repository root.");
            string output = Path.GetFullPath(values["--output"]);
            if (!string.Equals(Path.GetExtension(output), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The report filename must end in .json.");
            string webRoot = Path.GetFullPath(values["--web-root"]);
            if (IsUnder(webRoot, output) || output == allowlist || ShaderPaths.Any(path =>
                Path.GetFullPath(Path.Combine(repository, path)) == output))
                throw new ArgumentException("The report must be outside the web root and cannot overwrite audit inputs.");
            EnsureNoLinks(output);
            return new Options(webRoot, repository, allowlist, output);
        }
    }

    private sealed class AuditReport
    {
        public int SchemaVersion { get; } = 1;
        public string AuditKind { get; } = "final-published-pe-manifest-resources";
        public DateTimeOffset GeneratedAtUtc { get; } = DateTimeOffset.UtcNow;
        public bool Passed { get; set; }
        public string? AllowlistSha256 { get; set; }
        public List<string> Errors { get; } = [];
        public List<ShaderRecord> Shaders { get; } = [];
        public List<AssemblyRecord> Assemblies { get; } = [];
    }

    private sealed class ShaderRecord
    {
        public required string Path { get; init; }
        public required string LogicalName { get; init; }
        public required string Sha256 { get; init; }
        public required string SourceSha256 { get; init; }
        public long Length { get; init; }
    }

    private sealed class AssemblyRecord
    {
        public required string Path { get; init; }
        public long Length { get; set; }
        public string? Sha256 { get; set; }
        public string? AssemblyName { get; set; }
        public string? Version { get; set; }
        public string? Culture { get; set; }
        public List<ResourceRecord> Resources { get; } = [];
    }

    private sealed class ResourceRecord
    {
        public required string Name { get; init; }
        public required string Attributes { get; init; }
        public long Offset { get; init; }
        public bool Embedded { get; init; }
        public required string Implementation { get; init; }
        public long? Length { get; set; }
        public string? Sha256 { get; set; }
    }
}

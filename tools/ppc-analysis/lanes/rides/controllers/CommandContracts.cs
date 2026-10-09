using System.Text.Json;

namespace OpenTPW.PpcEvidence;

public enum ParameterDirection { NumericInput, Ignored, RequiredVariableInput, RequiredVariableOutput, OptionalVariableOutput }
public enum AccumulatorDirection { Preserve, EffectResult, OriginalInput, HostField40 }
[Flags] public enum ReferenceFlags { None = 0, Sign = 1, Zero = 2 }
public readonly record struct CommandContract(string Family, int RawCommand, ParameterDirection Parameter, AccumulatorDirection Accumulator);
public readonly record struct CommandProjection(ReferenceFlags Flags, int? ParameterOutput, bool NoOperation);

/// <summary>Reads existing interpreted metadata, not original executable/file bytes.</summary>
public sealed class CommandContracts
{
    private readonly Dictionary<(string, int), CommandContract> contracts;
    private CommandContracts(Dictionary<(string, int), CommandContract> values) => contracts = values;
    public int Count => contracts.Count;
    public IEnumerable<CommandContract> Values => contracts.Values;

    public static CommandContracts FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("sha256").GetString() != "04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5")
            throw new ArgumentException("controller metadata identity differs from the proved Mac executable");
        var values = new Dictionary<(string, int), CommandContract>();
        foreach (var item in root.GetProperty("commands").EnumerateArray())
        {
            var family = item.GetProperty("family").GetString() ?? throw new ArgumentException("missing family");
            var command = item.GetProperty("raw_command").GetInt32();
            var parameter = item.GetProperty("parameter").GetString() switch
            {
                "required variable input" => ParameterDirection.RequiredVariableInput,
                "required variable output" => ParameterDirection.RequiredVariableOutput,
                "optional variable output" => ParameterDirection.OptionalVariableOutput,
                "consumed and ignored" => ParameterDirection.Ignored,
                "resolved input" or "resolved input multiplied by 30" or "resolved input negated" or "resolved input multiplied by 1000" => ParameterDirection.NumericInput,
                _ => throw new ArgumentException("unrecognized parameter contract")
            };
            var accumulator = item.GetProperty("accumulator").GetString() switch
            {
                "preserve" => AccumulatorDirection.Preserve,
                "result" => AccumulatorDirection.EffectResult,
                "original input" => AccumulatorDirection.OriginalInput,
                "host field +40" => AccumulatorDirection.HostField40,
                _ => throw new ArgumentException("unrecognized accumulator contract")
            };
            values.Add((family, command), new(family, command, parameter, accumulator));
        }
        if (values.Count != 39) throw new ArgumentException("expected all 39 reviewed commands");
        return new(values);
    }

    public PrimitiveResult<CommandContract> Find(string family, int command) => contracts.TryGetValue((family, command), out var value)
        ? PrimitiveResult<CommandContract>.Applied(value)
        : PrimitiveResult<CommandContract>.Reject(PrimitiveStatus.UnsupportedSchema, "raw command has no reviewed controller contract");

    public static PrimitiveResult<CommandProjection> Project(CommandContract contract, bool parameterIsVariable,
        int originalInput, ReferenceFlags previous, int? availableEffectResult)
    {
        if (contract.Parameter is ParameterDirection.RequiredVariableInput or ParameterDirection.RequiredVariableOutput && !parameterIsVariable)
            return PrimitiveResult<CommandProjection>.Reject(PrimitiveStatus.SkippedOperandGate, "native variable-kind gate skips this call");
        if (contract.Accumulator == AccumulatorDirection.Preserve)
            return PrimitiveResult<CommandProjection>.Applied(new(previous, null, contract.Family == "COAST" && contract.RawCommand == 7));
        if (contract.Accumulator == AccumulatorDirection.OriginalInput)
            return PrimitiveResult<CommandProjection>.Applied(new(Flags(originalInput), null, false));
        if (availableEffectResult is null)
            return PrimitiveResult<CommandProjection>.Reject(PrimitiveStatus.UnsupportedEffect, "no controller/query result was supplied; zero must not be fabricated");
        var output = parameterIsVariable && contract.Parameter is ParameterDirection.RequiredVariableOutput or ParameterDirection.OptionalVariableOutput;
        return PrimitiveResult<CommandProjection>.Applied(new(Flags(availableEffectResult.Value), output ? availableEffectResult : null, false));
    }

    private static ReferenceFlags Flags(int value) => value == 0 ? ReferenceFlags.Zero : value < 0 ? ReferenceFlags.Sign : ReferenceFlags.None;
}

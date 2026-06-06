using System.Text;
using Reasonet.Events;
using Reasonet.Messages;
using Reasonet.Providers;
using Reasonet.Session;

namespace Reasonet.Agent;

/// <summary>
/// Runner interface — both Agent and Coordinator satisfy it.
/// </summary>
public interface IRunner
{
    Task<string> RunAsync(string input, CancellationToken ct = default);
}

/// <summary>
/// Two-model Coordinator: a planner proposes an approach, then the executor carries it out.
/// </summary>
public sealed class Coordinator : IRunner
{
    private readonly IProvider _planner;
    private readonly AgentSession _plannerSession;
    private readonly Pricing? _plannerPricing;
    private readonly Agent _executor;
    private readonly double _temperature;
    private readonly ISink _sink;
    private readonly Func<string, bool>? _shouldPlan;

    public Coordinator(
        IProvider planner,
        AgentSession plannerSession,
        Pricing? plannerPricing,
        Agent executor,
        double temperature,
        ISink sink,
        Func<string, bool>? shouldPlan = null)
    {
        _planner = planner;
        _plannerSession = plannerSession;
        _plannerPricing = plannerPricing;
        _executor = executor;
        _temperature = temperature;
        _sink = sink;
        _shouldPlan = shouldPlan;
    }

    public async Task<string> RunAsync(string input, CancellationToken ct = default)
    {
        // Skip planning for trivial inputs
        if (_shouldPlan != null && !_shouldPlan(input))
        {
            _sink.Emit(new Event(EventKind.Phase, Phase: "executor · executing"));
            return await _executor.RunAsync(input, ct);
        }

        _sink.Emit(new Event(EventKind.Phase, Phase: $"{_planner.Name} · planning"));
        var plan = await PlanAsync(input, ct);

        _sink.Emit(new Event(EventKind.Phase, Phase: "executor · executing"));
        var handoff = $"Task: {input}\n\nA planner proposed this approach:\n{plan}\n\nCarry it out, adapting as needed.";
        return await _executor.RunAsync(handoff, ct);
    }

    private async Task<string> PlanAsync(string input, CancellationToken ct)
    {
        _plannerSession.Add(new Message { Role = Messages.Role.User, Content = input });

        var request = new ProviderRequest(
            _plannerSession.Snapshot(),
            Array.Empty<ToolSchema>(),
            _temperature
        );

        var text = new StringBuilder();
        Usage? usage = null;

        await foreach (var chunk in _planner.StreamAsync(request, ct))
        {
            switch (chunk.Type)
            {
                case ChunkType.Text when chunk.Text != null:
                    text.Append(chunk.Text);
                    _sink.Emit(new Event(EventKind.Text, Text: chunk.Text));
                    break;
                case ChunkType.Usage:
                    usage = chunk.Usage;
                    break;
                case ChunkType.Error:
                    if (chunk.Error != null) throw chunk.Error;
                    break;
            }
        }

        _sink.Emit(new Event(EventKind.Usage, Usage: usage, Pricing: _plannerPricing));

        var plan = text.ToString();
        _plannerSession.Add(new Message { Role = Messages.Role.Assistant, Content = plan });
        return plan;
    }
}

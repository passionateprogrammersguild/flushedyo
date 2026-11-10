using Microsoft.Extensions.Logging;

namespace FlushedYo.Core.Commands;

/// <summary>Runs <c>flushedyo workflow</c>: computes the agent workflow state via <see cref="AgentWorkflowService"/>.</summary>
public sealed partial class WorkflowService(AgentWorkflowService workflow, ILogger<WorkflowService> logger)
{
    /// <summary>Executes the workflow command.</summary>
    /// <param name="json">True when <c>--json</c> was passed.</param>
    /// <param name="output">Writer for command output (stdout).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code: 0 when setup is complete, 1 when a next step is pending.</returns>
    public async Task<int> ExecuteAsync(bool json, TextWriter output, CancellationToken cancellationToken)
    {
        var state = await workflow.GetAsync(cancellationToken);
        if (json)
        {
            output.WriteLine(CliJson.Serialize(state));
        }
        else
        {
            output.WriteLine(AgentSurfaceFormatter.FormatWorkflow(state));
        }

        LogNext(logger, state.Next?.Code ?? "ready");
        // Tally's convention: a pending next step is useful output, not an error, but the exit
        // code tells agents whether work remains.
        return state.Next is null ? 0 : 1;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent workflow next step: {Next}")]
    private static partial void LogNext(ILogger logger, string next);
}

using System;

namespace Github.ActionsRunner.Controller;

public sealed class RunnerOrchestratorOptions
{
    public required string Owner { get; set; }
    public required string Repository { get; set; }
    public required string RunnerLabel { get; set; }
    public required int MaxConcurrency { get; set; }
    public required TimeSpan QueueCheckDelay { get; set; }
    public bool BindHostDockerSocket { get; set; } = false;
}
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Github.ActionsRunner.Controller;

public sealed partial class RunnerOrchestrator : BackgroundService
{
    private const string RunnerImage = "ghcr.io/mehyaa/github-runner";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDockerClient _dockerClient;
    private readonly RunnerOrchestratorOptions _options;
    private readonly ILogger<RunnerOrchestrator> _logger;

    private readonly string _normalizedRepo;

    private readonly SemaphoreSlim _concurrencySemaphore;

    public RunnerOrchestrator(
        IHttpClientFactory httpClientFactory,
        IDockerClient dockerClient,
        RunnerOrchestratorOptions options,
        ILogger<RunnerOrchestrator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _dockerClient = dockerClient;
        _options = options;
        _logger = logger;

        _normalizedRepo = NormalizeRepoName(_options.Repository);

        _concurrencySemaphore = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Controller started. User: {Owner}, Repository: {Repo}, Max Parallel: {Max}",
            _options.Owner,
            _options.Repository,
            _options.MaxConcurrency);

        while (!stoppingToken.IsCancellationRequested)
        {
            var httpClient = _httpClientFactory.CreateClient("GitHubApi");

            if (_concurrencySemaphore.CurrentCount == 0)
            {
                await Task.Delay(_options.QueueCheckDelay, stoppingToken);

                continue;
            }

            try
            {
                var queuedCount = await GetQueuedCountAsync(httpClient, stoppingToken);

                if (queuedCount <= 0)
                {
                    continue;
                }

                _logger.LogInformation("{Count} queued job(s) found", queuedCount);

                while (queuedCount > 0 && _concurrencySemaphore.CurrentCount > 0)
                {
                    await _concurrencySemaphore.WaitAsync(stoppingToken);

                    queuedCount--;

                    _ = Task.Run(() => SpawnRunnerAsync(httpClient, stoppingToken), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occured when checking the queue");
            }
            finally
            {
                await Task.Delay(_options.QueueCheckDelay, stoppingToken);
            }
        }
    }

    private async Task SpawnRunnerAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var runnerName = $"ephemeral-runner-{_normalizedRepo}-{Guid.NewGuid().ToString("N")[..6]}";

        var containerId = string.Empty;

        try
        {
            var runnerToken = await GetRunnerTokenAsync(httpClient, cancellationToken);

            _logger.LogInformation("Starting container: {RunnerName}", runnerName);

            var createParams = new CreateContainerParameters
            {
                Image = $"{RunnerImage}:latest",
                Name = runnerName,
                Env =
                    [
                        $"REPO_URL=https://github.com/{_options.Owner}/{_options.Repository}",
                        $"RUNNER_TOKEN={runnerToken}",
                        $"RUNNER_NAME={runnerName}",
                        $"RUNNER_LABELS=self-hosted,{_options.RunnerLabel}",
                        "EPHEMERAL=true",
                        "DISABLE_AUTO_UPDATE=true"
                    ],
                HostConfig = new()
                {
                    AutoRemove = true,
                    Binds =
                        _options.BindHostDockerSocket
                            ? ["/var/run/docker.sock:/var/run/docker.sock"]
                            : Array.Empty<string>()
                }
            };

            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = RunnerImage,
                    Tag = "latest"
                },
                null,
                new Progress<JSONMessage>(),
                cancellationToken);

            var container = await _dockerClient.Containers.CreateContainerAsync(createParams, cancellationToken);

            containerId = container.ID;

            await _dockerClient.Containers.StartContainerAsync(containerId, new(), cancellationToken);

            await _dockerClient.Containers.WaitContainerAsync(containerId, cancellationToken);

            _logger.LogInformation("Runner ({RunnerName}) completed", runnerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Runner ({RunnerName}) creation failed", runnerName);

            if (!string.IsNullOrEmpty(containerId))
            {
                try
                {
                    await _dockerClient.Containers.RemoveContainerAsync(
                        containerId,
                        new ContainerRemoveParameters { Force = true },
                        cancellationToken);
                }
                catch
                {
                    // ignored
                }
            }
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async Task<int> GetQueuedCountAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var path = $"repos/{_options.Owner}/{_options.Repository}/actions/runs?status=queued";

        using var response = await httpClient.GetAsync(path, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(json);

        var queuedCount = doc.RootElement.GetProperty("total_count").GetInt32();

        return queuedCount;
    }

    private async Task<string> GetRunnerTokenAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var path = $"repos/{_options.Owner}/{_options.Repository}/actions/runners/registration-token";

        using var tokenResponse = await httpClient.PostAsync(path, null, cancellationToken);

        tokenResponse.EnsureSuccessStatusCode();

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(tokenJson);

        var runnerToken = doc.RootElement.GetProperty("token").GetString()!;

        return runnerToken;
    }

    private static string NormalizeRepoName(string repo)
    {
        var normalized = NonWordCharsRegex().Replace(repo, "-");
        normalized = AllDashesRegex().Replace(normalized, "-");
        return normalized.ToLowerInvariant();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\W+")]
    private static partial System.Text.RegularExpressions.Regex NonWordCharsRegex();

    [System.Text.RegularExpressions.GeneratedRegex("-+")]
    private static partial System.Text.RegularExpressions.Regex AllDashesRegex();
}
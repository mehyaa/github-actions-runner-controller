using Docker.DotNet;
using Github.EphemeralRunner.Controller;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Net.Http.Headers;

const string userAgent = "Ephemeral-Runner-Controller/4.0-MultiRepo";
const string gitHubApiUrlPrefix = "https://api.github.com/";
const string gitHubApiVersionHeaderKey = "X-GitHub-Api-Version";
const string gitHubApiVersionHeaderValue = "2022-11-28";

const string dockerSocketLinux = "unix:///var/run/docker.sock";
const string dockerSocketWindows = "npipe://./pipe/docker_engine";

var builder = Host.CreateApplicationBuilder(args);

var pat = builder.Configuration.GetValue<string>("GITHUB_PAT");

if (string.IsNullOrEmpty(pat))
{
    Console.WriteLine("GITHUB_PAT is not defined. Please set the GITHUB_PAT environment variable with a valid GitHub Personal Access Token that has the necessary permissions to manage runners.");
    Environment.Exit(1);
    return;
}

var owner = builder.Configuration.GetValue<string>("GITHUB_OWNER");

if (string.IsNullOrEmpty(owner))
{
    Console.WriteLine("GITHUB_OWNER is not defined. Please set the GITHUB_OWNER environment variable with a valid GitHub owner.");
    Environment.Exit(1);
    return;
}

var repo = builder.Configuration.GetValue<string>("GITHUB_REPO");

if (string.IsNullOrEmpty(repo))
{
    Console.WriteLine("GITHUB_REPO is not defined. Please set the GITHUB_REPO environment variable with a valid GitHub repo.");
    Environment.Exit(1);
    return;
}

var runnerLabel = builder.Configuration.GetValue<string>("RUNNER_LABEL", "custom-ephemeral-runner");
var maxConcurrency = builder.Configuration.GetValue("MAX_CONCURRENCY", 5);
var checkDelaySecond = builder.Configuration.GetValue("QUEUE_CHECK_DELAY", 15);

var runnerOrchestratorOptions = new RunnerOrchestratorOptions
{
    Owner = owner,
    Repository = repo,
    RunnerLabel = runnerLabel,
    MaxConcurrency = maxConcurrency,
    QueueCheckDelay = TimeSpan.FromSeconds(checkDelaySecond)
};

builder.Services.AddSingleton(runnerOrchestratorOptions);

builder.Services.AddHttpClient("GitHubApi", client =>
{
    client.BaseAddress = new Uri(gitHubApiUrlPrefix);
    client.DefaultRequestHeaders.Add("User-Agent", userAgent);
    client.DefaultRequestHeaders.Add(gitHubApiVersionHeaderKey, gitHubApiVersionHeaderValue);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat);
});

builder.Services.AddSingleton<IDockerClient>(_ =>
{
    var dockerUri = OperatingSystem.IsWindows() ? dockerSocketWindows : dockerSocketLinux;

    return new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
});

builder.Services.AddHostedService<RunnerOrchestrator>();

var host = builder.Build();

await host.RunAsync();

# GitHub Ephemeral Runner Controller

This project is a .NET Worker Service that monitors a GitHub repository for queued Action jobs and automatically spawns ephemeral GitHub Runners using Docker when a queue is detected.

## 🚀 Features

-   **On-Demand Resources:** Creates runners only when there are jobs in the queue.
-   **Ephemeral Runners:** Each runner performs exactly one job and is automatically destroyed afterwards, ensuring a clean environment for every run.
-   **Concurrency Control:** Limit the maximum number of simultaneous runners via `MAX_CONCURRENCY`.
-   **Docker Based:** Runners run inside Docker containers using the `myoung34/github-runner` image.
-   **Cross-Platform:** Supports both Linux (`unix:///var/run/docker.sock`) and Windows (`npipe://./pipe/docker_engine`) Docker sockets.

## 🛠️ How It Works

The service polls the GitHub API at set intervals (`QUEUE_CHECK_DELAY`):
1.  Checks if there are any jobs with a `queued` status in the specified repository.
2.  If jobs are found and the current runner count is below `MAX_CONCURRENCY`:
    *   Fetches a new **Registration Token** from GitHub.
    *   Starts a new container on the local Docker Engine using `myoung34/github-runner:latest`.
3.  Once the runner completes its job (`EPHEMERAL=true`), the container automatically removes itself (`AutoRemove = true`).

## 📋 Prerequisites

-   **Docker:** Required to host the runner containers.
-   **GitHub Personal Access Token (PAT):** A PAT with permissions to manage runners and read repo status.
    *   `repo` scope is required.

## ⚙️ Configuration

You can configure the service using the following environment variables:

| Variable            | Description                               | Default                   |
| :------------------ | :---------------------------------------- | :------------------------ |
| `GITHUB_PAT`        | GitHub Personal Access Token (Required)   | -                         |
| `GITHUB_OWNER`      | Repository owner (User or Organization)   | -                         |
| `GITHUB_REPO`       | Repository name to monitor                | -                         |
| `RUNNER_LABEL`      | Additional label to assign to the runners | `custom-ephemeral-runner` |
| `MAX_CONCURRENCY`   | Maximum number of parallel runners        | `5`                       |
| `QUEUE_CHECK_DELAY` | Frequency of queue checks (in seconds)    | `15`                      |

## 🚀 Getting Started

### Running with Docker

You can run the controller itself as a container:

```bash
docker build -t github-runner-controller .

docker run -d \
  --name runner-controller \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -e GITHUB_PAT=your_pat_here \
  -e GITHUB_OWNER=your_username \
  -e GITHUB_REPO=your_repo \
  -e MAX_CONCURRENCY=5 \
  github-runner-controller
```

### Running with .NET CLI

To run directly in your development environment:

```bash
# Restore dependencies
dotnet restore

# Set environment variables (PowerShell example)
$env:GITHUB_PAT="your_pat"
$env:GITHUB_OWNER="your_username"
$env:GITHUB_REPO="your_repo"

# Start the application
dotnet run
```

## 📜 License

This project is licensed under the [MIT](LICENSE) License.

## 🙏 Credits

This project utilizes the [myoung34/github-runner](https://github.com/myoung34/docker-github-runner) project for the runner image.
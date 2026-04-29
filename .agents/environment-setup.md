# Environment Setup for .NET oriented repos

> **SCOPE:** This setup is specifically for running the **Antigravity AI coding agent** inside a Docker container with **Docker Outside Docker (DooD)** enabled (host Docker socket mounted into the container). This is NOT a general developer setup guide — it documents the dependencies the agent needs installed in its container environment to build, test, and run the project.

## What This Repo Needs

The key tools required are:

| Tool | Purpose | Status |
|---|---|---|
| **git** | Version control | ✅ Installed (2.39.5) |
| **.NET 10 SDK** | Build/run all 7 projects (all target `net10.0`) | ✅ Installed (10.0.203) via `dotnet-install.sh` to `~/.dotnet` |
| **Docker + Docker Compose** | Run minikube / Kubernetes locally for testing | ✅ Available via Docker Outside Docker (29.4.1 + Compose v5.1.3) |
| **kubectl** | Interact with the local cluster and setup RBAC | ✅ Available via Docker |
| **minikube** | Local Kubernetes cluster for testing the MCP server | ✅ Available via Docker |
| **npm** | Not strictly required (no JS build step), but useful alongside Node | ✅ Installed (9.2.0) |
| **Node.js** | Runtime (was pre-installed) | ✅ Available (v18.20.4) |
| **curl / wget** | Download installers | ✅ Available |
| **sudo** | Install packages (user is in sudo group) | ✅ Available |

### Environment Summary

- **OS:** Debian 12 (bookworm), x86_64
- **User:** `devuser` (uid 1000, in `sudo` group)
- **Package manager:** `apt-get`
- **Docker:** Docker Outside Docker (DooD) — host Docker socket mounted at `/var/run/docker.sock`

---

## Install Steps

### 1. Install git

```bash
sudo apt-get update && sudo apt-get install -y git
```

### 2. Install .NET 10 SDK

Used Option A (user-local install script):

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0
```

PATH configured in `~/.bashrc`:
```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools
```

> **NOTE:** Since the SDK is installed to `~/.dotnet`, new shell sessions need to source `~/.bashrc` or set the env vars above for `dotnet` to be on PATH.

### 3. Docker (Docker Outside Docker)

Docker was already available via DooD (host Docker socket mounted). No installation needed.
- Docker Engine: 29.4.1
- Docker Compose: v5.1.3

### 4. Install npm

```bash
sudo apt-get install -y npm
```

### 5. Kubernetes Tools (via Docker)

Since Docker Outside Docker (DooD) is configured, there is no need to install `kubectl` or `minikube` directly onto the container. Instead, run them as Docker containers.

To use `kubectl` (for example, to run `./scripts/create-demo-kubeconfig.sh`), you can set up a shell alias that proxies the command to the `bitnami/kubectl` image:

```bash
alias kubectl='docker run --rm -i --network host -v ~/.kube:/root/.kube -v "$(pwd)":"$(pwd)" -w "$(pwd)" bitnami/kubectl:latest'
```

For `minikube`, a similar approach can be taken using a containerized version, or by directly interacting with the cluster running on the host machine.
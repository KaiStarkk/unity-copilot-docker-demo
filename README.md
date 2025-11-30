# Unity Editor in Docker for GitHub Copilot Coding Agent

This repository demonstrates how to run Unity Editor in a Docker container that persists while GitHub Copilot coding agent works on your project.

## The Challenge

Running Unity Editor in GitHub Actions for Copilot coding agent requires solving several problems:

1. **License activation** - Unity licenses are bound to machine IDs, which change on every CI run
2. **Container persistence** - The Unity container must stay alive while Copilot works
3. **Project access** - The container needs access to your checked-out repository code
4. **Background execution** - Unity must run in the background without blocking the workflow

## Solution Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    GitHub Actions Runner                     │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────────┐      ┌──────────────────────────────┐ │
│  │ copilot-setup-   │      │    Unity Service Container   │ │
│  │ steps job        │      │    (unityci/editor)          │ │
│  │                  │      │                              │ │
│  │ 1. Checkout      │      │  - Unity Editor (batchmode)  │ │
│  │ 2. Activate ───────────>│  - Health server :8090       │ │
│  │    license       │      │  - Project files (copied)    │ │
│  │ 3. Copy project  │      │                              │ │
│  │ 4. Start Unity   │      │  Persists while Copilot      │ │
│  │                  │      │  works on the codebase       │ │
│  └──────────────────┘      └──────────────────────────────┘ │
│                                                              │
│  ┌──────────────────────────────────────────────────────────┐│
│  │              Copilot Coding Agent                        ││
│  │    Can interact with Unity via port 8090                 ││
│  └──────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## Prerequisites

### 1. Unity License Setup

You need a Unity license. **Personal (free) licenses work**, but require proper setup.

#### Generate License File Locally

1. Open **Unity Hub** on your local machine
2. Go to **Preferences → Licenses**
3. Click **Add** and select **"Get a free personal license"**
4. Find the generated `.ulf` file:
   - **Windows:** `C:\ProgramData\Unity\Unity_lic.ulf`
   - **macOS:** `/Library/Application Support/Unity/Unity_lic.ulf`
   - **Linux:** `~/.local/share/unity3d/Unity/Unity_lic.ulf`

#### Add GitHub Secrets

Go to your repository **Settings → Secrets and variables → Actions** and add:

| Secret Name | Value |
|-------------|-------|
| `UNITY_LICENSE` | Full contents of the `.ulf` file |
| `UNITY_EMAIL` | Your Unity account email |
| `UNITY_PASSWORD` | Your Unity account password (avoid special characters) |

### 2. Enable GitHub Copilot Coding Agent

Ensure your repository has GitHub Copilot coding agent enabled. See [GitHub's documentation](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/coding-agent).

## How It Works

### Service Container Approach

The workflow uses GitHub Actions' `services` feature to run Unity as a sidecar container:

```yaml
services:
  unity:
    image: unityci/editor:ubuntu-2022.3.61f1-linux-il2cpp-3
    ports:
      - 8090:8090
    options: --name unity-editor
```

**Why services?** Service containers persist throughout the entire job, including when Copilot takes over after setup steps complete.

### Project File Injection

Since services start before checkout, we copy the project into the running container:

```yaml
- name: Copy Unity project to service container
  run: docker cp ./UnityProject/. unity-editor:/project/
```

### Keep-Alive Mechanism

The `Startup.cs` script prevents Unity from exiting by:
1. Starting a TCP health server on port 8090
2. Entering an infinite loop that checks for shutdown signals

```csharp
public static void Init()
{
    StartHealthServer();      // Port 8090 for health checks
    Debug.Log("Unity-MCP-Ready");  // Signal initialization complete
    KeepAlive();              // Block forever (until shutdown signal)
}
```

## File Structure

```
.
├── .github/
│   └── workflows/
│       └── copilot-setup-steps.yml    # Main workflow
├── UnityProject/
│   ├── Assets/
│   │   └── Editor/
│   │       ├── Editor.asmdef          # Assembly definition
│   │       └── Scripts/
│   │           └── Startup.cs         # Keep-alive script
│   └── ProjectSettings/
│       ├── ProjectSettings.asset
│       └── ProjectVersion.txt
└── README.md
```

## Usage

### Testing the Workflow

1. Fork or clone this repository
2. Add the required secrets (see Prerequisites)
3. Go to **Actions** tab
4. Run the **"Copilot Setup Steps"** workflow manually

### Using with Copilot Coding Agent

Once the workflow succeeds, Copilot coding agent can:
- Access Unity Editor via the service container
- Make changes to your Unity project
- Verify changes compile correctly

## Troubleshooting

### License Errors

**"Machine bindings don't match"**
- Don't manually mount `.ulf` files - use `game-ci/unity-activate@v2`

**"com.unity.editor.headless was not found"**
- This is a known issue with GameCI Docker images v3+
- Ensure you're using the activation action, not manual license files

**"Personal" option not showing on license.unity3d.com**
- Use browser dev tools to remove `display: none;` from the Personal option div
- See [GameCI troubleshooting](https://game.ci/docs/3/troubleshooting/common-issues/)

### Container Issues

**Unity container exits immediately**
- Check that `Startup.cs` has the keep-alive loop
- Ensure `-executeMethod Editor.Startup.Init` is correct

**Port 8090 not accessible**
- Verify the service has `ports: - 8090:8090`
- Check Unity logs for TCP server startup messages

### Debugging

View Unity logs:
```bash
docker exec unity-editor cat /tmp/unity.log
```

Check if Unity is running:
```bash
docker exec unity-editor pgrep -x Unity
```

## Alternative Approaches

### Docker Run Instead of Services

If you prefer explicit control, you can use `docker run -d`:

```yaml
- name: Start Unity container
  run: |
    docker run -d --name unity-editor \
      -p 8090:8090 \
      -v "${{ github.workspace }}/UnityProject:/project" \
      unityci/editor:ubuntu-2022.3.61f1-linux-il2cpp-3 \
      /bin/bash -c "unity-editor -batchmode -nographics ..."
```

This approach allows direct volume mounts but has less guaranteed persistence.

## References

- [GitHub Copilot Coding Agent Setup](https://docs.github.com/en/copilot/customizing-copilot/customizing-the-development-environment-for-copilot-coding-agent)
- [GameCI Activation Guide](https://game.ci/docs/github/activation/)
- [GameCI Docker Images](https://game.ci/docs/docker/docker-images/)
- [GitHub Actions Service Containers](https://docs.github.com/en/actions/use-cases-and-examples/using-containerized-services/about-service-containers)
- [Original Challenge Discussion](https://github.com/IvanMurzak/Unity-MCP/pull/308)

## License

MIT License - feel free to use this as a starting point for your own Unity + Copilot integration.

## Contributing

Contributions welcome! Please open an issue or PR if you find improvements or fixes.

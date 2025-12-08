# Unity + MCP for GitHub Copilot Coding Agent

This repository demonstrates running Unity Editor with Unity-MCP integration for GitHub Copilot's coding agent. After setup, Copilot can interact with Unity via the Model Context Protocol (MCP).

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│                    GitHub Actions Runner                          │
│                                                                    │
│  ┌─────────────────────┐    SignalR     ┌──────────────────────┐  │
│  │  Unity Editor       │◄──────────────►│  Unity-MCP-Server    │  │
│  │  (Docker container) │  Docker network │  (GitHub Service)    │  │
│  │                     │                 │  localhost:8080      │  │
│  │  + Unity-MCP Plugin │                 │                      │  │
│  │  + Health :8090     │                 │                      │  │
│  └─────────────────────┘                 └──────────▲───────────┘  │
│                                                     │              │
│                                          MCP Protocol              │
│                                                     │              │
│                                   ┌─────────────────┴────────────┐ │
│                                   │   GitHub Copilot Agent       │ │
│                                   │   (connects to localhost:8080)│ │
│                                   └──────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

## Key Features

- **GitHub Services**: Unity-MCP-Server runs as a proper GitHub Actions service
- **Docker networking**: Unity Editor and MCP Server communicate via Docker bridge network
- **License handling**: GameCI activation with automatic machine binding
- **Keep-alive**: Unity stays running while Copilot works

## Quick Start

### 1. Fork this repository

### 2. Generate Unity License

Run the **"Generate Unity License"** workflow:

1. Go to **Actions** tab
2. Select **"Generate Unity License"**
3. Click **"Run workflow"** → Choose **"manual"**
4. Download the `.alf` artifact

Complete manual activation:
1. Go to https://license.unity3d.com/manual
2. Upload your `.alf` file
3. **If "Personal" option is hidden**: Press F12 → Elements → Search `option-personal` → Remove `style="display: none;"`
4. Select **Personal** license
5. Download the `.ulf` file

### 3. Set GitHub Secrets

Go to **Settings → Secrets and variables → Actions**:

| Secret | Value |
|--------|-------|
| `UNITY_LICENSE` | Contents of your `.ulf` file |
| `UNITY_EMAIL` | Your Unity account email |
| `UNITY_PASSWORD` | Your Unity account password |

Or via CLI:
```bash
gh secret set UNITY_LICENSE < Unity_v2022.x.ulf
gh secret set UNITY_EMAIL
gh secret set UNITY_PASSWORD
```

**Important**: Delete the `.alf` and `.ulf` files from your local machine after setting the secrets. See [Security](#security) section below.

### 4. Test the Setup

1. Go to **Actions** tab
2. Run **"Copilot Setup Steps"** manually
3. Verify Unity starts and MCP Server connects

## How It Works

### Service Container (Unity-MCP-Server)

The MCP Server runs as a GitHub Actions service - it starts automatically and persists throughout the job:

```yaml
services:
  mcp-server:
    image: ivanmurzakdev/unity-mcp-server:latest
    ports:
      - 8080:8080
    env:
      TRANSPORT: http
      SIGNALR_URL: http://unity-editor:8090/unityhub
```

### Unity Container (docker run)

Unity needs license preparation before starting, so it uses `docker run` after the activation step:

```yaml
- name: Start Unity Editor container
  run: |
    docker run -d \
      --name unity-editor \
      --network unity-mcp-net \
      -v "${{ github.workspace }}/UnityProject:/project:rw" \
      unityci/editor:ubuntu-2022.3.61f1-linux-il2cpp-3 \
      /bin/bash -c "tail -f /dev/null"
```

### Docker Networking

Both containers are connected to the same Docker network (`unity-mcp-net`), allowing them to communicate:

- MCP Server reaches Unity at `unity-editor:8090`
- Copilot reaches MCP Server at `localhost:8080`

### Keep-Alive Script

The `Startup.cs` script prevents Unity from exiting:

```csharp
public static void Init()
{
    StartHealthServer();              // TCP server on port 8090
    Debug.Log("Unity-MCP-Ready");     // Signal initialization complete
    KeepAlive();                      // Block forever
}
```

## File Structure

```
.
├── .github/
│   └── workflows/
│       ├── copilot-setup-steps.yml     # Main workflow (uses services!)
│       └── generate-unity-license.yml  # License generation helper
├── UnityProject/
│   ├── Assets/
│   │   └── Editor/
│   │       └── Scripts/
│   │           └── Startup.cs          # Keep-alive script
│   ├── Packages/
│   │   └── manifest.json               # Unity-MCP package reference
│   └── ProjectSettings/
└── README.md
```

## Unity-MCP Package

This project includes Unity-MCP as a package dependency in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.ivanmurzak.unity-mcp": "https://github.com/IvanMurzak/Unity-MCP.git?path=Assets/Unity-MCP"
  }
}
```

Unity will automatically download and install the package on first run.

## Troubleshooting

### License Errors

**"Machine bindings don't match"**
- Use `game-ci/unity-activate@v2` instead of manually copying license files

**"Personal" option not showing on license.unity3d.com**
- Open DevTools (F12) → Elements → Search `option-personal` → Remove `style="display: none;"`

### Container Issues

**Unity container exits immediately**
- Check `Startup.cs` has the keep-alive loop
- Ensure `-executeMethod Editor.Startup.Init` is specified

**MCP Server can't reach Unity**
- Verify both containers are on `unity-mcp-net` network
- Check Unity is listening on port 8090

### Debugging

View Unity logs:
```bash
docker exec unity-editor cat /tmp/unity.log
```

Check network connectivity:
```bash
docker network inspect unity-mcp-net
```

Test MCP Server health:
```bash
curl http://localhost:8080/health
```

## Unity Version Compatibility

| Version | Status |
|---------|--------|
| Unity 2022.3 LTS | Tested, working |
| Unity 6 (6000.x) | Should work (Pro license may be required) |

To change Unity version, update the image tag:
```yaml
image: unityci/editor:ubuntu-6000.0.51f1-linux-il2cpp-3.1.0
```

## Security

### License File Sensitivity

Both Unity license files contain sensitive information:

| File | Description | Sensitivity |
|------|-------------|-------------|
| `.alf` (Activation License File) | Machine fingerprint data used to request a license | **Sensitive** - Contains machine identifiers. While it requires your Unity account to convert to a ULF, it still exposes machine-specific data. |
| `.ulf` (Unity License File) | The actual activated license | **Highly Sensitive** - Combined with Unity credentials, this grants access to Unity. Treat like a password. |

### Best Practices for Public Repositories

If your repository is public, take extra precautions when generating licenses:

1. **Temporarily make the repo private** before running the license generation workflow
2. **Download artifacts immediately** after workflow completion
3. **Delete artifacts** from the workflow run (Actions → Run → Artifacts → Delete)
4. **Make the repo public again** only after artifacts are deleted
5. **Never commit** `.alf` or `.ulf` files to the repository

### Security Checklist

After setting up your license:

- [ ] `UNITY_LICENSE` secret is set (check: Settings → Secrets)
- [ ] No `.alf` or `.ulf` files in repository
- [ ] No license artifacts remaining in Actions runs
- [ ] Local `.alf` and `.ulf` files deleted from your machine
- [ ] License content never printed to workflow logs

### What This Workflow Does

The `generate-unity-license.yml` workflow is designed with security in mind:

- **ALF files**: Uploaded as short-lived artifacts (1-day retention), never printed to logs
- **ULF files**: Set directly as GitHub Secret via `gh secret set`, never uploaded or logged
- **Automatic cleanup**: License files are deleted from the runner after use

### GitHub Copilot Agent Permissions

When using this with GitHub Copilot coding agent:

- Copilot can only modify code in **your own repositories** where you've enabled it
- It requires **explicit user approval** for PRs before merging
- The MCP connection only allows Copilot to interact with Unity within the isolated container
- Service containers are destroyed when the workflow times out (59 minutes max)

## References

- [GitHub Copilot Coding Agent Setup](https://docs.github.com/en/copilot/customizing-copilot/customizing-the-development-environment-for-copilot-coding-agent)
- [Unity-MCP Project](https://github.com/IvanMurzak/Unity-MCP)
- [GameCI Activation Guide](https://game.ci/docs/github/activation/)
- [GitHub Actions Service Containers](https://docs.github.com/en/actions/use-cases-and-examples/using-containerized-services/about-service-containers)

## Contributing

Contributions welcome! This is part of the [Unity-MCP challenge](https://github.com/IvanMurzak/Unity-MCP/pull/308).

## License

MIT License

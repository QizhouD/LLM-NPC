# Unity-digital-Human

Cross-platform Unity 6 project for a digital-human / agent prototype (URP, Input System, conversation and animation systems).

Any machine that installs the **exact Editor version below** and **Git LFS** can clone, open, compile, and continue development.

## Requirements

| Tool | Version | Notes |
| --- | --- | --- |
| Unity Editor | **6000.2.15f1** (revision `0707b6d1e918`) | Must match `ProjectSettings/ProjectVersion.txt` |
| Git | 2.40+ | |
| Git LFS | latest | Required before clone |
| OS | Windows 10/11, macOS 12+, Ubuntu 22.04+ | |
| Unity CLI (optional) | latest beta | Install + module management, headless automation |
| Unity Pipeline package | `com.unity.pipeline` `0.5.0-exp.1` | Already in `Packages/manifest.json` |

Do **not** open this project with a different Unity major/minor (for example 2022.3 or 6000.0). The package lock and URP 17 assets will migrate or fail.

## What is versioned

Committed:

- `Assets/` including every `.meta`
- `ProjectSettings/` including `ProjectVersion.txt`
- `Packages/manifest.json` and `Packages/packages-lock.json`
- `.gitignore`, `.gitattributes`, this README

Not committed (regenerated locally):

- `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, `MemoryCaptures/`, `Recordings/`, `.utmp/`
- `*.csproj`, `*.sln`, `*.slnx`

Editor settings (already applied, do not change without a team decision):

- Version Control Mode = **Visible Meta Files**
- Asset Serialization Mode = **Force Text**
- Line Endings For New Scripts = **Unix (LF)** recommended

## Clone (new device)

Install Git LFS **before** cloning. A clone without LFS checks out pointer files instead of real PNGs/fonts, and textures/fonts will be missing in the Editor.

```bash
git lfs install
git clone https://github.com/<YOUR_ORG_OR_USER>/Unity-digital-Human.git
cd Unity-digital-Human
git lfs pull
```

Install Unity **6000.2.15f1** via Unity Hub or Unity CLI (see below), then open the folder.

### First open — Library regeneration

The first open on a new machine **rebuilds `Library/`**. That can take **10–40+ minutes** depending on CPU, disk, and Package Manager download speed. This is expected.

Do not commit `Library/` after it appears. Let Package Manager restore packages from `packages-lock.json`. Wait until the Editor is idle (no “Importing…” / “Compiling…” in the status bar) before you edit assets.

If Unity Hub offers to **upgrade** the project, **Decline**. Open it with `6000.2.15f1` only.

## Open with Unity Hub

1. Install Editor `6000.2.15f1`.
2. Hub → Open → select this repository folder.
3. Wait for import to finish.
4. Open `Assets/Scenes/SampleScene.unity` (or the Digital Human scene you use).

## Open with Unity CLI

```bash
# Install this exact Editor if missing
unity install 6000.2.15f1 --accept-eula --yes

# Open the project with the resolved Editor
unity open .
```

The Pipeline package is already in the manifest (`com.unity.pipeline` `0.5.0-exp.1`). If a clone is missing it:

```bash
unity pipeline install
unity pipeline list
```

## Daily workflow

1. `git pull` (Git LFS downloads binaries automatically after `git lfs install`).
2. Open the project; wait for compile.
3. Work only under `Assets/`, `ProjectSettings/`, `Packages/`.
4. Never delete `.meta` files by hand. Rename/move assets **inside Unity** so meta GUIDs follow the asset.
5. Before push:

```bash
git status
git lfs status
git pull --rebase
git push
```

## Modules (optional, per-platform)

Install only what that machine builds:

```bash
unity modules list 6000.2.15f1
unity install-modules -e 6000.2.15f1 -m android ios webgl --cm
```

Windows standalone is included with the Windows Editor. macOS/Linux Editors need their own installs.

## Unity CLI and Unity Pipeline

Install the CLI (experimental, standalone `unity` binary):

**Windows (PowerShell)**

```powershell
$env:UNITY_CLI_CHANNEL = 'beta'
irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex
```

**macOS / Linux**

```bash
curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh | UNITY_CLI_CHANNEL=beta bash
```

```bash
unity --version
unity upgrade
unity doctor
unity auth login
unity install 6000.2.15f1 --accept-eula --yes
unity open .
unity pipeline install
unity command
unity command eval "return Application.unityVersion;"
```

Docs: [Use the Unity CLI](https://docs.unity.com/en-us/hub/use-unity-cli), [CLI reference](https://docs.unity.com/en-us/hub/unity-cli-reference).

## Headless / CI notes

Unity CLI + Pipeline can drive a running or batch Editor without Hub UI. Library still regenerates on a cold CI workspace — cache `Library/` in CI if builds must be fast, but **never** commit that cache.

Exact batch flags depend on the installed CLI (`unity --help`). The legacy Editor binary still accepts `-batchmode -nographics -projectPath`.

## License / secrets

Do not commit API keys for LLM backends. Use environment variables or a local ignored ScriptableObject (`*Secrets*.asset`).

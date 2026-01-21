# QGST - Quick GPU Selector Tool

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10">
  <img src="https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows" alt="Windows">
</p>

> **[Türkçe README için tıklayın](README.tr.md)**

<p align="center">
  <strong>Simple and powerful GPU selection for Windows multi-GPU systems</strong>
</p>

QGST lets you choose which GPU runs your applications. Perfect for laptops with integrated + discrete GPUs or desktops with multiple graphics cards.

## ✨ Features

- 🎮 **One-Time Run** - Launch apps with a specific GPU (preference auto-reverts after exit)
- 💾 **Set as Default** - Permanently assign GPU preference to applications
- 🖱️ **Context Menu** - Right-click `.exe`, `.lnk`, `.bat`, `.cmd`, `.url` files
- 🎯 **Smart Detection** - Identifies all GPUs, distinguishes identical models
- 🌍 **Multi-Language** - English and Turkish support
- ⚡ **CLI Tool** - Full command-line interface for automation
- 🔒 **Crash-Safe** - Auto-reverts preferences even if app crashes

## 📋 Requirements

- **OS**: Windows 10 (1803+) or Windows 11
- **.NET**: .NET 10 Runtime (x64)
- **GPU**: Any modern GPU (DirectX 11+ recommended)

> **Note**: GPU Preference Store requires Windows 10 1803+. Older versions have limited support.

## 🚀 Installation

1. Download the latest release from [Releases](../../releases)
2. Extract to any folder (e.g., `C:\Tools\QGST`)
3. Run `QGST.UI.exe`
4. Settings → **Register Context Menu**

That's it! Fully portable, no installer needed.

## 📖 Usage

### Context Menu (Quickest Way)

Right-click any `.exe`, `.lnk`, `.bat`, `.cmd`, or `.url` file:

- **Run with GPU (One-Time)** - Choose GPU, app runs once, preference reverts
- **Set Default GPU** - Choose GPU, preference saved permanently  
- **Reset QGST Changes** - Remove GPU preference

### Graphical UI

```powershell
QGST.UI.exe [--target <path>] [--gpu <id>] [--one-time|--set-default]
```

**Options:**

| Option | Description |
|--------|-------------|
| `--target <path>` | Pre-load target application |
| `--gpu <id>` | Pre-select GPU |
| `--one-time` / `--set-default` | Choose mode |
| `--reset` | Reset preferences |

### Command-Line (CLI)

The CLI provides full automation capabilities:

```powershell
qgst <command> [options]
```

#### Available Commands

| Command | Description |
|---------|-------------|
| `list-gpus` | List all detected GPUs with details |
| `resolve` | Resolve shortcuts/batch files to executables |
| `run` | Run an application with specified GPU |
| `set-default` | Set permanent GPU preference |
| `reset` | Reset preferences (all or specific) |
| `export-backup` | Export configuration to backup |
| `import-backup` | Restore configuration from backup |
| `register-context-menu` | Add Explorer context menu |
| `unregister-context-menu` | Remove Explorer context menu |
| `diagnostics` | Export system diagnostics |
| `help` | Show help information |
| `version` | Show version information |

#### CLI Examples

**List GPUs:**
```powershell
# Human-readable list
qgst list-gpus

# JSON output (for scripting)
qgst list-gpus --json

# Force refresh cache
qgst list-gpus --refresh
```

**Resolve Shortcuts:**
```powershell
# Resolve a .lnk file to its target
qgst resolve --target "C:\Users\User\Desktop\Game.lnk"

# JSON output
qgst resolve --target "C:\Shortcuts\App.lnk" --json
```

**Run Applications:**
```powershell
# One-time run with GPU 1
qgst run --target "C:\Games\Game.exe" --gpu 1 --one-time

# Run with arguments
qgst run --target "C:\Apps\App.exe" --gpu gpu-0 --args "--fullscreen"

# Run without revert (permanent)
qgst run --target "C:\Games\Game.exe" --gpu 2
```

**Set Default GPU:**
```powershell
# Set GPU 0 as default for an application
qgst set-default --target "C:\Games\Game.exe" --gpu 0

# Use GPU ID instead of index
qgst set-default --target "C:\Apps\App.exe" --gpu "gpu-1"
```

**Reset Operations:**
```powershell
# Reset preference for a specific app
qgst reset --target "C:\Games\Game.exe"

# Reset all preferences only
qgst reset --prefs

# Reset context menu only
qgst reset --contextmenu

# Reset everything (full cleanup)
qgst reset --all

# Reset without creating backup
qgst reset --all --no-backup
```

**Backup & Restore:**
```powershell
# Export backup to default location
qgst export-backup

# Export to specific folder
qgst export-backup --out "D:\Backups\QGST"

# Restore from backup
qgst import-backup --in "D:\Backups\QGST\backup-20260121"
```

**Diagnostics:**
```powershell
# Export to default location
qgst diagnostics

# Export to specific file
qgst diagnostics --out "C:\Support\qgst-diagnostics.json"
```

## 🔧 How It Works

QGST writes to Windows GPU Preference Store:
```
HKCU\Software\Microsoft\DirectX\UserGpuPreferences
```

**Preference Values:**
- `1` = Power Saving (integrated GPU)
- `2` = High Performance (discrete GPU)
- For multiple discrete GPUs: Uses LUID/Device IDs for precise targeting

**One-Time Mode:**
1. Save current preference
2. Set desired GPU
3. Launch app and wait for exit
4. Revert to original preference
5. Auto-cleanup if QGST crashes

**File Resolution:**
- `.lnk` → Resolves via Windows Shell
- `.bat`/`.cmd` → Parses for executable paths
- `.url` → Detects Steam games

## 📂 Data Location

`%LOCALAPPDATA%\QGST\`

```
QGST/
├── config/         # Settings and mappings
├── state/          # Applied preferences, pending reverts
├── cache/          # GPU inventory cache
├── logs/           # Daily logs
├── backup/         # Config backups
└── locales/        # Language files
```

## 🏗️ Project Structure

```
QGST/
├── QGST.Core/       # Core library
│   ├── Models/      # Data models
│   ├── Services/    # Business logic
│   └── Data/        # Localization files
├── QGST.UI/         # WPF GUI
└── QGST.CLI/        # Command-line tool
```

## 🛠️ Building

**Requirements:** .NET 10 SDK, Windows 10 SDK

```powershell
git clone https://github.com/yourusername/QGST.git
cd QGST
dotnet build -c Release

# Output: build/Release/
```

## 🌍 Localization

**Supported:** English (en), Turkish (tr)

**Add new language:**
1. Copy `QGST.Core/Data/locales/en.json` to `de.json`
2. Translate all values
3. Update `LocalizationService.cs` `AvailableLanguages` array

## 🔍 Troubleshooting

**No GPUs detected**
- Update GPU drivers
- Run: `qgst list-gpus --refresh`

**Context menu missing**
- Settings → Register Context Menu
- Restart Explorer: `Stop-Process -Name explorer -Force`

**Preference not working**
- Some UWP apps don't support GPU selection
- Try running game .exe directly (not through launcher)
- Check for conflicting NVIDIA/AMD settings

**Export diagnostics:**
```powershell
qgst diagnostics --out diagnostics.json
```

**Full reset:**
```powershell
qgst reset --all
```


## 🤝 Contributing

Contributions welcome!

1. Fork the repository
2. Create feature branch: `git checkout -b feature/amazing`
3. Commit changes: `git commit -m 'Add amazing feature'`
4. Push: `git push origin feature/amazing`
5. Open Pull Request

**Areas for contribution:** Translations, bug fixes, features, documentation, UI/UX improvements.

---

<p align="center">
  <strong>Made with ❤️ for multi-GPU systems</strong>
  <br>
  <sub>© 2026 QGST Project</sub>
</p>

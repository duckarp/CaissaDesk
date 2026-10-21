<div align="center">

# 🏆 CaissaDesk

### Modern, High-Performance Chess Tournament Management & Pairing Workstation

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512bd4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Photino.Blazor](https://img.shields.io/badge/Photino-Blazor-0078d4.svg?style=flat-square)](https://www.tryphotino.io/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20Windows-brightgreen.svg?style=flat-square)](https://github.com/duckarp/CaissaDesk/releases)
[![Latest Release](https://img.shields.io/github/v/release/duckarp/CaissaDesk?style=flat-square&color=blue)](https://github.com/duckarp/CaissaDesk/releases/latest)
[![CI Build](https://img.shields.io/github/actions/workflow/status/duckarp/CaissaDesk/ci.yml?branch=main&style=flat-square&label=CI)](https://github.com/duckarp/CaissaDesk/actions/workflows/ci.yml)
[![Tests](https://img.shields.io/badge/Tests-86%20Passing-success.svg?style=flat-square)](https://github.com/duckarp/CaissaDesk/actions)
[![License](https://img.shields.io/badge/License-GPLv3-blue.svg?style=flat-square)](LICENSE)

*Caissalytics' sister desktop workstation: a modern, lightweight, open-source alternative to legacy tournament software. Built with .NET 10, Photino.Blazor native desktop shell, SQLite, and pluggable Swiss pairing engines (including support for the FIDE-endorsed bbpPairings and JaVaFo).*

[Key Features](#-key-features) • [Pairing Engines](#-fide-pairing-engines) • [Tiebreak Systems](#-fide-tiebreak-engine) • [Downloads](#-installation--downloads) • [Building](#-building-from-source)

</div>

---

## 🌟 Key Features

- **⚡ Blazing-Fast Desktop Performance**: Powered by .NET 10 and Photino native OS webviews. Starts instantaneously and uses a fraction of the memory of Electron applications (~50–90 MB RAM vs 600+ MB).
- **♟️ Swiss & Round Robin Tournaments**: Full support for Swiss System (Dutch pairing algorithm) and Round Robin (Berger tables) events of any size.
- **🛡️ Arbiter Confirmation Safeguards**: Vital safety modals with two-step confirmations protect against accidental tournament deletions, archiving, or premature round resets.
- **🔍 Smart FIDE & National Scouting**:
  - Live lookup by FIDE ID or player name on [ratings.fide.com](https://ratings.fide.com) to automatically resolve official spelling, titles, federation, birth year, gender, and time-control ratings (Standard, Rapid, Blitz).
  - National database scouting (Slovak Chess Federation / SŠZ rating and club integration).
  - Configurable tournament seeding precedence (FIDE first vs. National first).
  - Full title hierarchy including standard FIDE titles (`GM`, `IM`, `FM`, `CM`, `WGM`, `WIM`, `WFM`, `WCM`) and FIDE Online Arena titles (`AGM`, `AIM`, `AFM`, `ACM`).
- **⚙️ Pluggable FIDE Pairing Engine Architecture**: Run pairings using the built-in simplified Dutch engine (pure C#, zero external dependencies) or switch to the FIDE-endorsed external engines (**bbpPairings** or **JaVaFo**), recommended for rated events, with integrated test diagnostics and automated downloads.
- **🎯 Comprehensive Arbiter Round Operations**:
  - Fast keyboard-friendly result entry (`1-0`, `0-1`, `½-½`, `1F-0`, `0-1F`, `0F-0F`, unplayed).
  - Late player registrations with automatic half-point or zero-point byes for past rounds.
  - Advance bye scheduling (half-point, full-point, zero-point byes per round).
  - Mid-tournament withdrawals and player reactivations.
  - Arbiter manual overrides: swap players between boards or swap colors.
  - Round reset controls: regenerate unplayed rounds or safely reset the current round.
- **📊 Real-Time Standings & FIDE Tiebreaks**: Instant recalculation of points, games played, and comprehensive tiebreak stacks with customizable criteria ordering.
- **📄 Official Printing & FIDE TRF Export**:
  - Export official 132-column FIDE Tournament Report Files (`.trf`) with compliant tags and player lines for rating submission.
  - Print-ready A4 layouts (`@media print`): Board Pairings (with signature lines), Alphabetical Wall Lists, Standings Crosstables, Berger matrices, and Table Match Cards (4 per page).
- **💾 Safe Local Storage & File Exchange**: Thread-safe SQLite persistence with WAL mode ensuring zero data loss during live tournaments, plus standalone tournament export/import (`.cdesk`).
- **🌍 Bilingual Interface (English & Slovak)**: Full runtime switching between English and Slovak with persistent user preference storage.
- **🎨 Modern Control Center UI**: Caissalytics-inspired layout featuring sleek dark/light themes, tabbed multi-tournament workspace, and vertical navigation settings.

---

## ⚙️ Pairing Engines

CaissaDesk features an engine registry supporting multiple pairing backends:

| Engine | Type | Description |
|---|---|---|
| **Native Swiss (simplified Dutch)** | Built-in C# | Pure .NET engine with zero external dependencies. Implements a simplified subset of the FIDE Dutch rules (C.04.3): score brackets, floaters, color balance, and pairing-allocated byes. It is **not** FIDE-endorsed and may produce pairings that differ from bbpPairings/JaVaFo; use it for casual events and use an endorsed engine for FIDE-rated tournaments. |
| **bbpPairings** | Native C++ binary | FIDE-endorsed Dutch pairing engine by Jeremy Bierema. Configurable via local binary or automated download in settings. |
| **JaVaFo** | Java (`.jar`) | FIDE-endorsed Dutch pairing engine by Roberto Ricca, run via system Java runtime. |
| **Berger Engine** | Deterministic rotational | Mathematical rotational pairing tables for Round Robin tournaments (even and odd player counts). |

> [!TIP]
> The engine registry includes automatic fallback: if a selected external engine executable is unavailable, CaissaDesk falls back to the native simplified Dutch engine so rounds are never blocked, and shows the arbiter a warning that the fallback was used.

---

## 📊 FIDE Tiebreak Engine

Configure and reorder the tiebreak criteria to match tournament regulations:

- **Direct Encounter (`DE`)**: Resolves head-to-head encounters between two or more tied players.
- **Buchholz Full (`BH`)**: Sum of scores of all opponents.
- **Buchholz Cut 1 (`BH-1`) & Cut 2 (`BH-2`)**: Buchholz with lowest opponent score(s) removed.
- **Median Buchholz (`Med-BH`)**: Highest and lowest opponent scores removed.
- **Sonneborn-Berger (`SB`)**: Sum of defeated opponents' scores plus half of drawn opponents' scores.
- **Koya System (`Koya`) & Extended Koya (`Ext-Koya`)**: Points scored against opponents with $\ge 50\%$ score.
- **Average Rating of Opponents Full (`AROC`) & Cut 1 (`AROC-1`)**: Opponents' average rating with optional cut.
- **Decisive Wins (`Wins`)**: Number of games won.
- **Black Pieces Played (`Black`)**: Number of games played with the black pieces.

---

## 📦 Installation & Downloads

Pre-built standalone desktop packages for **Linux (x64)** and **Windows (x64)** are available on the [Releases page](https://github.com/duckarp/CaissaDesk/releases):

### Windows (x64)
1. Download `CaissaDesk-win-x64.zip` from the latest release.
2. Extract the archive.
3. Run `CaissaDesk.exe`.

### Linux (x64)
1. Download `CaissaDesk-linux-x64.tar.gz` from the latest release.
2. Extract the archive:
   ```bash
   tar -xzf CaissaDesk-linux-x64.tar.gz
   ```
3. Run the binary:
   ```bash
   ./CaissaDesk
   ```
*(Requires `webkit2gtk-4.1` or `webkit2gtk-4.0` installed via your distribution package manager: `sudo apt install libwebkit2gtk-4.1-0` on Ubuntu/Debian).*

---

## 🛠️ Building from Source

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Linux or Windows development machine

### Clone and Run
```bash
# Clone the repository
git clone https://github.com/duckarp/CaissaDesk.git
cd CaissaDesk

# Restore dependencies
dotnet restore

# Run all unit tests
dotnet test --configuration Release

# Launch the desktop application
dotnet run --project CaissaDesk/CaissaDesk.csproj
```

### Packaging Standalone Releases Locally
Use the included publisher scripts to build single-file executables:

```bash
# Linux
./publish.sh all     # or ./publish.sh linux / ./publish.sh windows

# Windows
publish.bat
```

---

## 🧪 Architecture & Test Suite

The solution is cleanly partitioned into modular projects:

```
CaissaDesk/
├── CaissaDesk/               # Desktop UI (Photino.Blazor, Razor components, arbiter controllers)
├── Caissa.Shared/            # Shared domain models (Tournament, Player, Round, Pairing, Tiebreak)
└── CaissaDesk.Tests/         # Comprehensive xUnit test suite (86+ passing tests)
```

The test suite covers:
- Core tournament models & lifecycle
- FIDE tiebreak calculation precision
- Dutch Swiss & Berger pairing mechanics
- FIDE TRF 132-column export formatting
- SQLite repository migrations and round persistence
- FIDE and national scouting parsers
- Settings persistence and bilingual dictionary parity

---

## 📄 License

This project is licensed under the **GNU General Public License v3.0 (GPLv3)**. See the [LICENSE](LICENSE) file for details.

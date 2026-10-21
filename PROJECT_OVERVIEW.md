# CaissaDesk & Caissalytics: Cross-Project Architectural Overview & Shared Assets

This document provides a persistent reference mapping the relationship, shared foundation, distinct identities, and asset reuse between **CaissaDesk** and **Caissalytics**.

---

## 1. Project Roles & Visions

| Dimension | **Caissalytics**  | **CaissaDesk**  | **CaissaResults** (separate project, not part of this repository) |
|---|---|---|---|
| **Primary Domain** | Player analytics, deep Stockfish UCI analysis, personal repertoire, endgame training, tactics, game databases. | Tournament pairing & arbiter workstation, FIDE pairings engine, standings, FIDE TRF reports. | Public web results portal, live broadcasting, tournament archives, player cards, crosstables (Chess-Results modern alternative). |
| **Target User** | Competitive chess player, chess coach, club analyst. | Tournament Director (TD), Chief Arbiter, club organizer. | Spectators, players, coaches, federations, arbiters. |
| **Key Output** | Game reviews, PGN studies, blunder dossiers, printable homework worksheets. | Round pairings, tournament standings, cross-tables, FIDE TRF rating exchange files, cloud sync. | Real-time standings, mobile pairing cards, TRF download, player performance rating (TPR) ledger. |
| **Visual Accent** | **Cyan / Sky Blue** (`#06b6d4` &rarr; `#0ea5e9` &rarr; `#0284c7`) | **Tournament Championship Gold / Amber** (`#fef08a` &rarr; `#facc15` &rarr; `#eab308` &rarr; `#ca8a04`) | **Broadcast Emerald / Mint** (`#10b981` &rarr; `#34d399` &rarr; `#059669`) |
| **Brand Badge** | `CAISSA` (white) + `LYTICS` (sky blue) | `CAISSA` (white) + `DESK` (warm gold) | `CAISSA` (white) + `RESULTS` (emerald green) |
| **App Icon** | Halftone Knight Matrix in Cyan/Blue | Halftone Knight Matrix in Championship Gold/Amber | Broadcast Knight in Vibrant Emerald |

---

## 2. Shared Technology Stack

- **Target Framework**: `.NET 10.0` (`net10.0`, C# 12/13, Nullable enabled, Implicit usings)
- **Desktop Shell**: `Photino.Blazor` (`v4.0.13`) native OS webview (WebKitGTK on Linux, Edge WebView2 on Windows)
- **Data & Storage**: `Microsoft.Data.SqlClient` (`v6.0.2` for MSSQL on IIS/Windows) + `Microsoft.Data.Sqlite` (`v10.0.11` for local/embedded)
- **Networking**: `Microsoft.Extensions.Http` (`v10.0.11`)
- **Embedded Assets**: `Microsoft.Extensions.FileProviders.Embedded` + `PhysicalFileProvider` composite file system
- **Design Tokens**: Dark canvas (`#0e1117`), elevated surfaces (`#161b22`, `#21262d`), subtle borders (`#30363d`), system sans + monospace typography

---

## 3. Directory Structure & Asset Mapping

```
CaissaDesk/
├── CaissaDesk.slnx                            <-- Unified solution (.slnx format)
├── IMPLEMENTATION_PLAN.md                     <-- Roadmap
├── PROJECT_OVERVIEW.md                        <-- This persistent architecture reference
├── icon.svg / icon.ico / icon-*.png           <-- Assets
├── Caissa.Shared/                             <-- Zero-drift shared class library (.NET 10)
│   ├── Models/ (Tournament, Player, Round, Pairing, Enums)
│   ├── Tiebreak/ (TiebreakService, TiebreakModels, ITiebreakService)
│   ├── PairingEngine/ (TrfHandler - FIDE TRF exporter/importer)
│   └── Sync/ (TournamentSyncPayload, LiveRoundUpdate, SyncResult, DTOs)
├── CaissaResults/                             <-- Chess-Results Web Portal (ASP.NET Core Blazor, .NET 10)
│   ├── Program.cs                             <-- Kestrel, SignalR Hub, REST/Sync API, SSR + InteractiveServer
│   ├── Hubs/TournamentHub.cs                 <-- Real-time SignalR tournament room broadcast
│   ├── Data/ (ICaissaResultsRepository, SQLite persistence, demo tournament seeder)
│   ├── Endpoints/SyncApiEndpoints.cs          <-- REST API (/api/sync/publish, /api/tournaments, /api/tournaments/{id}/trf)
│   ├── Services/TournamentWebQueryService.cs  <-- TPR performance calculations, crosstables, standings
│   └── Components/
│       ├── Pages/Home.razor                   <-- Tournament directory, search, filtering, sync instructions
│       └── Pages/TournamentPortal.razor       <-- Live standings, round pairings, starting list, crosstable, player cards
└── CaissaDesk/                                <-- Desktop Arbiter Workstation (Photino.Blazor)
    ├── Components/Tournament/TournamentWorkbench.razor <-- 1-Click "Publish to Web" & "View Web"
    ├── Components/Settings/SettingsControlCenter.razor <-- CaissaResults cloud server configuration & connection test
    └── Data/CaissaResultsSyncService.cs       <-- HTTP sync client for CaissaResults portal
    ├── Core/                              <-- Shared standalone chess domain
    │   ├── BoardPosition.cs
    │   ├── FenParser.cs
    │   ├── Move.cs / Square.cs / Piece.cs
    │   ├── MoveGenerator.cs               <-- Legal move generation & check/stalemate detection
    │   ├── MoveNode.cs / GameTree.cs      <-- Tree navigation
    │   ├── SanParser.cs / PgnHandler.cs   <-- SAN & PGN parsers
    │   ├── GameResultHelper.cs
    │   ├── TimeControlInfo.cs
    │   ├── DateHelper.cs
    │   └── MaterialHelper.cs
    ├── Data/                              <-- External scouting and tournament services
    │   ├── FideModels.cs                  <-- FidePlayerCard, FideSearchResult, ChessResultsTournamentEntry
    │   ├── IFideScoutingService.cs        <-- Scraping ratings.fide.com for instant player lookup
    │   ├── FideScoutingService.cs
    │   ├── IChessResultsScoutingService.cs<-- Scraping chess-results.com
    │   └── ChessResultsScoutingService.cs
    ├── Localization/                      <-- Bilingual UI engine
    │   ├── ILocalizationService.cs
    │   ├── LanguageInfo.cs
    │   └── LocalizationService.cs         <-- English (en) & Slovak (sk) tournament terminology
    └── wwwroot/
        ├── index.html                     <-- Loading splash with gold spinner & knight branding
        ├── css/
        │   ├── theme.css                  <-- Dark theme palette + Gold/Amber brand tokens
        │   ├── tabs.css                   <-- App header, workspace tabs, brand styling
        │   └── layout.css                 <-- Workspace containers, data tables, panels
        ├── js/
        │   ├── soundService.js            <-- Acoustic feedback handler
        │   └── workspaceStorage.js
        ├── sounds/                        <-- Lichess wooden moves, check, victory sounds
        └── images/icon.svg                <-- Gold knight SVG
```

---

## 4. Key Rules & Pairing Engine Architecture (CaissaDesk)

1. **Pluggable Pairing Provider Pattern (`IPairingEngine`)**:
   - Arbiters have full control to select their preferred pairing backend:
     - **Native C# Dutch Engine**: Built directly into the app, 0 dependencies, cross-platform, unit-testable.
     - **bbpPairings**: FIDE-approved open-source (Apache 2.0) native C++ engine.
     - **JaVaFo**: FIDE reference engine (invoked via `javafo.jar` if Java runtime is available).
     - **Berger Tables**: Pure C# deterministic rotation engine for Round Robin.
   - All engines interface seamlessly via standard FIDE TRF communication.
2. **FIDE Swiss System (Dutch System C.04)**:
   - Absolute pairing criteria (no rematches, color balance $\in \{-1, 0, +1\}$ with rare $+2/-2$ exceptions, no $>2$ consecutive games of same color).
   - Score bracket sorting, floater identification (down-floaters & up-floaters), exchange/transposition rules.
2. **Round Robin (Berger Tables)**:
   - Official Berger pairing rotation tables for round-by-round color balancing.
3. **Standings & Tiebreaks**:
   - Buchholz (Full, Cut 1, Cut 2, Median).
   - Sonneborn-Berger.
   - Direct Encounter.
   - Greater number of wins / Black games.
   - AROC (Average Rating of Opponents Cut 1).
4. **FIDE TRF (Tournament Report File) Export**:
   - Fixed-column 132-character record format (`012` tournament line, `001` player lines with round-by-round pairings/results/colors).
5. **Speed & Ergonomics**:
   - Numeric keypad result entry (`1` for 1-0, `2` for 0-1, `X`/`=` for ½-½, `+` for 1F-0 default, `-` for 0-1F default).

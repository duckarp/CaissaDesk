# CaissaDesk: Feature Status & Roadmap Overview

A modern, high-performance, open-source chess tournament management workstation built on **.NET 10** and **Photino.Blazor**.

---

## 🟢 Completed Features

### 1. Core Architecture & Multi-Tournament Workspace
- **Desktop Framework**: Built on `.NET 10` (`net10.0`) and `Photino.Blazor` for high-performance native desktop execution on Linux and Windows.
- **Multi-Tab Workspace**: Manage multiple concurrent tournaments (e.g. Open A, Open B, Blitz Side-Event) plus Dashboard and Settings simultaneously without losing state.
- **Embedded SQLite Persistence**: Thread-safe database (`caissadesk.db`) with WAL mode, ensuring zero data loss during live tournaments.
- **Responsive Display & Typography**: Optimized layout utilizing full screen real estate on 1080p, 1440p, and ultrawide monitors without vertical overflow or letter clipping.
- **Unified Branding & App Icons**: Championship Gold halftone knight icon matching Caissalytics in exact dimensions, aspect ratio (0.825), multi-res ICO/PNG sets, and Linux desktop hicolor integration.

### 2. Tournament Lifecycle & Management
- **Tournament Setup Wizard**: Create tournaments with Name, System (Swiss vs. Round Robin), Time Control Category (Rapid, Blitz, Standard) & custom specifications, Chief & Deputy Arbiters, Venue, Organizer, Federation, and FIDE-rated status.
- **Tournament Modification**: Edit existing tournament metadata with validation (e.g. preventing total rounds from being set lower than rounds already finished).
- **Archiving System**: Mark tournaments as finished and archive them into a dedicated filterable/sortable archive list with full restore capabilities.
- **File Exchange (`.cdesk`)**: Import and export standalone tournament files via native OS save/open dialogs (`FileExportService`).

### 3. Pluggable Pairing Engines
- **Native C# Dutch Swiss Engine**: Pure C# implementation of FIDE Handbook C.04 (no external dependencies):
  - Rematch prevention (absolute criterion).
  - Score bracket sorting with up-floater and down-floater allocation.
  - Strict color balance ($\in \{-1, 0, +1\}$) and max consecutive same-color limits.
  - Automatic odd-player pairing-allocated bye assignment.
- **Berger Tables Engine**: Deterministic rotational pairing tables for Round Robin tournaments (even and odd player counts).
- **External Engine Integration**:
  - Provider support for **bbpPairings** (native C++ binary) and **JaVaFo** (`javafo.jar`).
  - Automated download and installation of missing engines directly from the Settings Control Center.
  - Engine detection and validation with helpful arbiter guidance.

### 4. Player Roster & Smart FIDE Scouting
- **Dual Ratings**: Full support for both FIDE Rating and National Rating with configurable tournament seeding precedence (FIDE First vs. National First).
- **FIDE Online Arena (FOA) Titles**: Added `ACM`, `AFM`, `AIM`, and `AGM` alongside standard FIDE titles (`GM`, `IM`, `FM`, `CM`, `WGM`, `WIM`, `WFM`, `WCM`).
- **Seeding Hierarchy**: Official title weighting order: `GM > WGM > IM > WIM > FM > WFM > CM > WCM > AGM > AIM > AFM > ACM > un-titled`.
- **Smart FIDE Lookup**: Automatically searches `ratings.fide.com` by FIDE ID; falls back to search by Full Name + Federation filter; auto-populates official name, title, federation, birth year, gender, and rating matching the event's time control.
- **Registered Player Editor**: Edit any player's stats, ratings, club, birth year, gender, IDs, or status without disrupting existing tournament pairings.
- **"Update All from FIDE" Batch Sync**: One-click live refresh of ratings, titles, federations, and birth years for all registered players before round 1 begins.

### 5. Arbiter Round Operations & Byes
- **Result Entry Dialog**: Enter board results (`1-0`, `0-1`, `½-½`, defaults `1F-0`, `0-1F`, `0F-0F`, byes) and arbiter remarks.
- **Arbiter Override Mode**: Manual board creation, manual player pairing, swapping players across boards, and flipping board colors.
- **Attendance & Bye Management**: Schedule future ½-point byes, 0-point byes, register late entries, and handle player withdrawals/re-entries.
- **Unfinished Games Guard**: Warning modal preventing inadvertent pairing of the next round while games are still ongoing.

### 6. Standings, Tiebreaks & Cross-Tables
- **Real-Time Standings**: Instant recalculation of points, games played, and tiebreak scores.
- **Full FIDE Tiebreak Engine**:
  - Direct Encounter (`DE`) with pairwise and tied-group resolution.
  - Buchholz Full (`BH`), Buchholz Cut 1 (`BH-1`), Buchholz Cut 2 (`BH-2`), and Median Buchholz (`Med-BH`).
  - Sonneborn-Berger (`SB`).
  - Koya System (`Koya`) and Extended Koya System (`Ext-Koya`) with automated step-down score resolution.
  - Average Rating of Opponents Full (`AROC`) and Average Rating of Opponents Cut 1 (`AROC-1`).
  - Decisive Wins (`Wins`) and Games with Black pieces (`Black`).
- **Interactive Arbiter Tiebreak Stack Manager**:
  - One-click official presets: FIDE Standard Swiss, Round Robin (Berger), FIDE Championship.
  - Interactive priority reordering (move up/down), criteria deletion, and addition of any criteria with localized descriptions and short codes.
  - Standings table automatically renders all active tiebreak columns in configured order.
- **Berger Cross-Table**: Interactive $N \times N$ matrix view for Round Robin tournaments with progressive scores and head-to-head cells.

### 7. Official Printing & TRF Export Suite
- **FIDE TRF 132-Column Export**: Compliant Tournament Report File (`.trf`) export with official FIDE tags and player lines (with FOA titles).
- **Native OS Save Dialog**: Real desktop file saving for `.cdesk` and `.trf` files via OS native file pickers.
- **Printable Sheets (A4 / `@media print`)**:
  - Pairings by Board (with arbiter & player signature lines).
  - Alphabetical Pairings (for hall wall boards).
  - Standings Sheets.
  - Berger Cross-Tables.
  - Table Match Cards / Score Slips (4 per A4 page).

### 8. Bilingual Localization
- Comprehensive runtime language switching between **English (🇬🇧)** and **Slovak (🇸🇰)** with settings persistence.

---

## 🟡 What Is Still Not Done (Future Roadmap & Post-MVP)

1. **Bulk Player Import from External Files**:
   - Currently arbiters add players individually, use FIDE auto-lookup, or import from `.cdesk`.
   - *Planned*: Bulk import from CSV, Excel (`.xlsx`), or plain text roster files (e.g. from Google Forms or national federation registration lists).

2. **Chess-Results.com Direct Upload**:
   - `ChessResultsScoutingService` currently scrapes tournament info; direct upload/FTP publishing or generating custom upload packages for online servers is not yet implemented.

3. **Team & Special Tournament Systems**:
   - Team Swiss and Team Round Robin (match points vs. game points, board order rosters).
   - Knockout / Playoff / Armageddon brackets.
   - Scheveningen System.

5. **DGT Electronic Board Relay / Live Broadcast**:
   - Live PGN output / serial connection to DGT e-Boards or DGT LiveChess server.

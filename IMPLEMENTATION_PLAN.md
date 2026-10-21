# CaissaDesk: Implementation Plan & Architectural Roadmap

A modern, high-performance, open-source chess tournament management workstation built with **.NET 10** and **Photino.Blazor**.

---

## 1. Architectural Alignment & Design Decisions (From Grill-Me Session)

1. **Storage & Persistence**:
   - **Hybrid Model**: Auto-saved in a local SQLite application database (`caissadesk.db`) so no progress is ever lost.
   - **Standalone Tournament Exchange**: Full export and import to single-file `.cdesk` (compressed JSON/SQLite archive) for easy sharing between arbiters' laptops or backups.
2. **Tournament Formats**:
   - **MVP**: Individual Swiss System (**FIDE Dutch System C.04**) and **Round Robin (Berger Tables)**.
   - *Post-MVP*: Team Tournaments, Scheveningen, Knockout / Playoff.
3. **Player Ratings & Seeding**:
   - Dual rating tracking: **FIDE Rating** (Standard, Rapid, Blitz) and **National Rating** (e.g., LOK, USCF, ECF, DWZ).
   - Configurable seeding rule per tournament: FIDE First vs. National First (with automatic fallbacks).
   - Instant FIDE profile fetching via `IFideScoutingService` (FIDE ID &rarr; Name, Title, Federation, Ratings, Birth Year).
4. **Swiss Pairing Engine Architecture (Pluggable Provider Pattern)**:
   - Provide arbiters with ultimate control via `IPairingEngine` provider abstraction:
     1. **Native C# Dutch Engine** (Built-in, pure C#, zero external dependencies, 100% cross-platform, unit-testable).
     2. **bbpPairings Provider** (FIDE-approved open-source Apache 2.0 C++ engine).
     3. **JaVaFo Provider** (FIDE reference engine; invoked via `javafo.jar` when Java is installed).
     4. **Berger Tables Engine** (Pure C# deterministic rotation for Round Robin).
   - Universal communication via **FIDE TRF** format: TRF serves both as the engine input/output and official rating export.
5. **Tiebreak Engine**:
   - Configurable, re-orderable tiebreak stack with official presets (**FIDE Standard Swiss**, **FIDE Official Championship**, **Round Robin**).
   - Supported criteria: Points, Buchholz (Full, Cut 1, Cut 2, Median), Sonneborn-Berger, Direct Encounter, Most Wins, Most Black Games, AROC.
6. **Arbiter Result Entry Workflow**:
   - **Board Result Dialog Modal**: Click a board to open a focused result dialog with explicit actions (1-0, 0-1, ½-½, 1F-0, 0-1F, 0F-0F, Byes, Arbiter remarks).
7. **Export & Printing**:
   - **FIDE TRF (Tournament Report File)**: Official 132/140-column fixed-width exchange file for rating officers.
   - **Printable Views (Print CSS / A4)**: Pairings by board (with signature lines), Alphabetical pairings (for wall postings), Standings table, and Berger Cross-tables.
8. **Multi-Tournament Workspace**:
   - Tabbed workspace architecture (`WorkspaceTabs.razor`): manage multiple concurrent tournaments (e.g. Open A, Open B, Blitz Side-Event) without closing or losing state.
9. **Attendance, Byes & Withdrawals**:
   - Per-round player attendance control: Half-point byes (pre-requested for specific rounds), Zero-point byes, Pairing-allocated 1-point byes, Late registrations, and Mid-tournament withdrawals / re-entry.

---

## 2. Gradual Implementation Phases

```
┌────────────────────────────────────────────────────────────────────────┐
│                        IMPLEMENTATION PHASES                           │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
    ┌───────────────────────────────┴───────────────────────────────┐
    ▼                                                               ▼
[ Phase 1: Core Domain & Storage ]                          [ Phase 2: Tournament Setup & Roster ]
• Tournament, Player, Round, Pairing                        • Tournament config wizard
• SQLite store & .cdesk file export/import                  • Roster grid + FIDE ID auto-lookup
• 100% unit-tested CRUD & serialization                     • Seeding order generator
    │                                                               │
    └───────────────────────────────┬───────────────────────────────┘
                                    ▼
[ Phase 3: Round Robin (Berger Tables) & Board Result Dialog ]
• Berger pairing rotation algorithm
• Board pairings view + Board Result Dialog modal
• Classic N x N cross-table view
                                    │
                                    ▼
[ Phase 4: Standings & FIDE Tiebreaks Engine ]
• Buchholz (Full, Cut 1, Cut 2, Median), Sonneborn-Berger
• Direct Encounter, Most Wins, AROC
• Unit-tested with known official test tournaments
                                    │
                                    ▼
[ Phase 5: FIDE Dutch Swiss Pairing Engine (FIDE C.04) ]
• Bracket generation, down-floaters/up-floaters, rematch prevention
• Strict color alternation & balance rules
• Odd player bye allocation + manual pairing adjustments
                                    │
                                    ▼
[ Phase 6: Live Arbiter Round Operations & Byes ]
• Round generator, past round reviews, score corrections
• Player withdrawals, late entries, half-point bye scheduling
                                    │
                                    ▼
[ Phase 7: FIDE TRF Export & Printable Sheets Suite ]
• FIDE TRF 132-column export for rating calculation
• Printable pairing lists (by board & alphabetical)
• Printable standings & table score slips
```

---

### Phase 1: Core Domain Entities & Persistence
- **Tasks**:
  - Implement domain models in `CaissaDesk.Core.Tournament`:
    - `Tournament` (Metadata, Format, TimeControl, RatingCategory, RoundCount, TiebreakOrder).
    - `Player` (Id, Seed, FideId, NationalId, FullName, Title, Federation, Club, FideElo, NationalElo, BirthYear, Gender, Status).
    - `Round` & `Pairing` (RoundNumber, BoardNumber, WhitePlayerId, BlackPlayerId, Result, Remarks).
  - Implement `ITournamentRepository` with SQLite (`caissadesk.db`) and `.cdesk` ZIP/JSON file import/export.
  - Create test project `CaissaDesk.Tests` with xUnit covering serialization, CRUD, and data integrity.

### Phase 2: Tournament Setup & Player Roster UI
- **Tasks**:
  - Tournament creation modal / form (Name, System [Swiss/RR], Time Control, Rated status).
  - Player Roster component with real-time FIDE ID lookup via `FideScoutingService`.
  - Seeding / Starting rank generator (FIDE rules: Title &rarr; Rating &rarr; Alphabetical).
  - Manual player re-ordering & bulk CSV/text import.

### Phase 3: Round Robin (Berger Engine) & Result Entry
- **Tasks**:
  - Pure C# `BergerPairingEngine` implementing `IPairingEngine`.
  - Board pairings view for Round Robin rounds.
  - `ResultEntryDialog` modal component (1-0, 0-1, ½-½, 1F-0, 0-1F, 0F-0F, remarks).
  - Berger Cross-Table view ($N \times N$ matrix with progressive scores).

### Phase 4: Standings & FIDE Tiebreak Engine
- **Tasks**:
  - Pure C# `TiebreakService`:
    - Direct Encounter (mini-tables among tied players).
    - Buchholz (Full, Cut 1, Cut 2, Median) with virtual opponent adjustment option.
    - Sonneborn-Berger.
    - Most Wins & Most Black games.
    - AROC (Average Rating of Opponents Cut 1).
  - Unit tests validating calculations against known official tournament result sheets.
  - Standings table UI with live sorting and tiebreak breakdown.

### Phase 5: Swiss Pairing Engine Providers (Native C#, bbpPairings & JaVaFo)
- **Tasks**:
  - Define `IPairingEngine` contract with provider discovery & availability detection.
  - Implement **Native C# Dutch Engine** following FIDE Handbook C.04:
    - Score brackets and heterogeneous bracket handling.
    - Strict color preferences & balance rules ($\le 1$, max 2 consecutive same color).
    - Floater allocation (down-floaters vs up-floaters).
    - Rematch protection (absolute criterion).
    - Bye handling for odd player counts.
  - Implement **bbpPairings Provider** (invoking open-source Apache 2.0 C++ binary via TRF).
  - Implement **JaVaFo Provider** (invoking `javafo.jar` via TRF when Java runtime is present).
  - Engine selection UI in tournament configuration (with badge indicating availability / certification).
  - Cross-validation unit test suite comparing pairings across engines with standard FIDE test cases.

### Phase 6: Arbiter Round Operations & Byes (COMPLETED)
- **Status**: Completed
- **Tasks**:
  - Round navigation bar (Round 1, Round 2, ...).
  - Attendance & Bye manager (schedule ½-point byes for future rounds, late entries, mid-tournament withdrawals).
  - Arbiter manual pairing override tools (swap boards, manual pairings, delete board, color flip).
  - Live standings recalculation upon result confirmation.

### Phase 7: FIDE TRF Export & Printable Sheets (COMPLETED)
- **Status**: Completed
- **Tasks**:
  - `TrfHandler`: Generates compliant 132-column FIDE Tournament Report Files (`.trf`) with direct file download.
  - Printable Views with clean `@media print` CSS:
    - Pairings by Board (with player signatures).
    - Alphabetical Pairings (for tournament wall boards).
    - Standings sheets.
    - Berger Cross-Tables.
    - Table match cards / score slips (4 per A4 page).
  - Standalone `.cdesk` tournament file export and import.

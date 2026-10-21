using Caissa.Shared.Models;

namespace CaissaDesk.Core.Arbiter;

public class ArbiterService : IArbiterService
{
    public void SetPlayerStatus(Tournament tournament, string playerId, PlayerStatus status)
    {
        var player = tournament.GetPlayer(playerId);
        if (player == null) return;
        player.Status = status;
    }

    public void SetAdvanceBye(Tournament tournament, string playerId, int roundNumber, ByeType byeType)
    {
        var player = tournament.GetPlayer(playerId);
        if (player == null) return;

        if (byeType == ByeType.None)
        {
            player.ByeRequests.Remove(roundNumber);
        }
        else
        {
            player.ByeRequests[roundNumber] = byeType;
        }
    }

    public void WithdrawPlayer(Tournament tournament, string playerId, string? reason = null)
    {
        var player = tournament.GetPlayer(playerId);
        if (player == null) return;

        player.Status = PlayerStatus.Withdrawn;
        
        // Remove pending advance byes for uncompleted future rounds since player is now withdrawn
        int latestRoundWithPairings = tournament.Rounds.Count;
        var roundsToRemove = player.ByeRequests.Keys
            .Where(r => r > latestRoundWithPairings)
            .ToList();
            
        foreach (var r in roundsToRemove)
        {
            player.ByeRequests.Remove(r);
        }
    }

    public void ReactivatePlayer(Tournament tournament, string playerId)
    {
        var player = tournament.GetPlayer(playerId);
        if (player == null) return;

        player.Status = PlayerStatus.Active;
    }

    public void AddLatePlayer(Tournament tournament, Player player, ByeType pastRoundsByeType = ByeType.HalfPoint)
    {
        // 1. Assign next seed
        // Max + 1, not Count + 1: ranks may have gaps (deleted players) and Count + 1 could collide
        int nextSeed = tournament.Players.Count > 0 ? tournament.Players.Max(p => p.StartRank) + 1 : 1;
        player.StartRank = nextSeed;
        player.Status = PlayerStatus.Active;
        tournament.Players.Add(player);

        // 2. If rounds have already taken place, allocate missed rounds byes
        if (pastRoundsByeType != ByeType.None && tournament.Rounds.Count > 0)
        {
            var byeResult = pastRoundsByeType.ToMatchResult();

            string byeRemarks = pastRoundsByeType switch
            {
                ByeType.HalfPoint => "½-Point Bye (Late Entry)",
                ByeType.ZeroPoint => "0-Point Bye (Late Entry)",
                ByeType.FullPoint => "1-Point Bye (Late Entry)",
                _ => "Late Entry Bye"
            };

            foreach (var round in tournament.Rounds)
            {
                // Check if player already in this round
                bool alreadyInRound = round.Pairings.Any(p => p.WhitePlayerId == player.Id || p.BlackPlayerId == player.Id);
                if (!alreadyInRound)
                {
                    int nextBoard = round.Pairings.Count > 0 ? round.Pairings.Max(p => p.BoardNumber) + 1 : 1;
                    round.Pairings.Add(new Pairing
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        RoundNumber = round.RoundNumber,
                        BoardNumber = nextBoard,
                        WhitePlayerId = player.Id,
                        BlackPlayerId = null,
                        Result = byeResult,
                        Remarks = byeRemarks,
                        IsManualOverride = true
                    });
                }
            }
        }
    }

    public bool SwapBoardColors(Round round, int boardNumber)
    {
        var pairing = round.Pairings.FirstOrDefault(p => p.BoardNumber == boardNumber);
        if (pairing == null || pairing.IsBye) return false;

        (pairing.WhitePlayerId, pairing.BlackPlayerId) = (pairing.BlackPlayerId, pairing.WhitePlayerId);
        // An entered result follows the players, not the colours: 1-0 for the old White becomes 0-1
        pairing.Result = pairing.Result.WithColorsSwapped();
        pairing.IsManualOverride = true;
        return true;
    }

    public bool SwapPlayersAcrossBoards(Round round, int board1, bool isWhite1, int board2, bool isWhite2)
    {
        var p1 = round.Pairings.FirstOrDefault(p => p.BoardNumber == board1);
        var p2 = round.Pairings.FirstOrDefault(p => p.BoardNumber == board2);
        if (p1 == null || p2 == null) return false;

        string? player1 = isWhite1 ? p1.WhitePlayerId : p1.BlackPlayerId;
        string? player2 = isWhite2 ? p2.WhitePlayerId : p2.BlackPlayerId;

        // Swapping with an empty slot would turn a bye board into a two-player board (still scored as a bye)
        // and leave a one-player game behind; only swap actual players.
        if (string.IsNullOrEmpty(player1) || string.IsNullOrEmpty(player2)) return false;
        if (ReferenceEquals(p1, p2) && isWhite1 == isWhite2) return false;

        if (isWhite1) p1.WhitePlayerId = player2;
        else p1.BlackPlayerId = player2;

        if (isWhite2) p2.WhitePlayerId = player1;
        else p2.BlackPlayerId = player1;

        p1.IsManualOverride = true;
        p2.IsManualOverride = true;
        return true;
    }

    public Pairing AddManualPairing(Round round, string whitePlayerId, string? blackPlayerId, MatchResult result = MatchResult.NotPlayed, string? remarks = null)
    {
        int nextBoard = round.Pairings.Count > 0 ? round.Pairings.Max(p => p.BoardNumber) + 1 : 1;

        var pairing = new Pairing
        {
            Id = Guid.NewGuid().ToString("N"),
            RoundNumber = round.RoundNumber,
            BoardNumber = nextBoard,
            WhitePlayerId = whitePlayerId,
            BlackPlayerId = blackPlayerId,
            Result = result,
            Remarks = remarks,
            IsManualOverride = true
        };

        round.Pairings.Add(pairing);
        return pairing;
    }

    public bool DeletePairing(Round round, string pairingId)
    {
        int removed = round.Pairings.RemoveAll(p => p.Id == pairingId);
        if (removed == 0) return false;

        // Renumber remaining boards sequentially
        int board = 1;
        foreach (var p in round.Pairings.OrderBy(p => p.BoardNumber))
        {
            p.BoardNumber = board++;
        }

        return true;
    }

    public bool MoveBoard(Round round, int fromBoardNumber, int toBoardNumber)
    {
        var target = round.Pairings.FirstOrDefault(p => p.BoardNumber == fromBoardNumber);
        if (target == null || fromBoardNumber == toBoardNumber) return false;

        var ordered = round.Pairings.OrderBy(p => p.BoardNumber).ToList();
        ordered.Remove(target);

        int insertIndex = Math.Clamp(toBoardNumber - 1, 0, ordered.Count);
        ordered.Insert(insertIndex, target);

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].BoardNumber = i + 1;
        }

        target.IsManualOverride = true;
        return true;
    }
}

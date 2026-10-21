using Caissa.Shared.Models;

namespace CaissaDesk.Core.Arbiter;

public interface IArbiterService
{
    void SetPlayerStatus(Tournament tournament, string playerId, PlayerStatus status);
    void SetAdvanceBye(Tournament tournament, string playerId, int roundNumber, ByeType byeType);
    void WithdrawPlayer(Tournament tournament, string playerId, string? reason = null);
    void ReactivatePlayer(Tournament tournament, string playerId);
    void AddLatePlayer(Tournament tournament, Player player, ByeType pastRoundsByeType = ByeType.HalfPoint);
    
    bool SwapBoardColors(Round round, int boardNumber);
    bool SwapPlayersAcrossBoards(Round round, int board1, bool isWhite1, int board2, bool isWhite2);
    Pairing AddManualPairing(Round round, string whitePlayerId, string? blackPlayerId, MatchResult result = MatchResult.NotPlayed, string? remarks = null);
    bool DeletePairing(Round round, string pairingId);
    bool MoveBoard(Round round, int fromBoardNumber, int toBoardNumber);
}

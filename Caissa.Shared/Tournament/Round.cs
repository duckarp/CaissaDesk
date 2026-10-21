namespace Caissa.Shared.Models;

public class Round
{
    public int RoundNumber { get; set; } = 1;
    public DateTime? StartTime { get; set; }
    public bool IsFinished { get; set; }
    public List<Pairing> Pairings { get; set; } = [];
}

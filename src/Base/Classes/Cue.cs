namespace Cue2.Base.Classes;

public class Cue : ICue
{
    private static int _nextId = 0;
    public int Id { get; set; }
    public string Name { get; set; }
    public string Command { get; set; }
    public string CueNum { get; set; }

    public void CreateCue()
    {
        Id = _nextId++;
        
    }
}
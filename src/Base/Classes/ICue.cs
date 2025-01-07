namespace Cue2.Base.Classes;

public interface ICue
{
    int Id { get; }
    string Name { get; set; }
    string Command { get; set; }
    string CueNum { get; set; }
    object ShellBar { get; set; }
}
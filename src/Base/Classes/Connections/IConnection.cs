namespace Cue2.Base.Classes.Connections;

public interface IConnection
{
    int ConnectionId { get; }
    int Name { get; set; }
}
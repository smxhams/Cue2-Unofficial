namespace Cue2.Domain.Connections;

public interface IConnection
{
    int ConnectionId { get; }
    int Name { get; set; }
}
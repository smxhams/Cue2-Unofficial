namespace Cue2.Domain.Connections;

public class NetworkConnection : IConnection
{
    public int ConnectionId { get; }
    public int Name { get; set; }
}
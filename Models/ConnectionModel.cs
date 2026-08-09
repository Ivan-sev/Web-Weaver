using System;

namespace WebWeaver.Models;

public class ConnectionModel
{
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    public string FromPort { get; set; } = "right";
    public string ToPort { get; set; } = "left";
}
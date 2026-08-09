using System.Collections.Generic;

namespace WebWeaver.Models;

public class MapData
{
    public List<NodeModel> Nodes { get; set; } = new();
    public List<ConnectionModel> Connections { get; set; } = new();
}

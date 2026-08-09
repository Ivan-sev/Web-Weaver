using System;
using System.Collections.Generic;
using System.Text;

namespace WebWeaver.Models
{
    public class MapSaveData
    {
        public List<NodeModel> Nodes { get; set; } = new();
        public List<ConnectionModel> Connections { get; set; } = new();
        public double TranslateX { get; set; }
        public double TranslateY { get; set; }
        public double Scale { get; set; } = 1;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace UIMarkerEditor
{

    public class MarkerShare
    {
        public string Name { get; set; } = string.Empty;
        public int MapID { get; set; }
        public MarkerSharePoint A { get; set; } = new();
        public MarkerSharePoint B { get; set; } = new();
        public MarkerSharePoint C { get; set; } = new();
        public MarkerSharePoint D { get; set; } = new();
        public MarkerSharePoint One { get; set; } = new();
        public MarkerSharePoint Two { get; set; } = new();
        public MarkerSharePoint Three { get; set; } = new();
        public MarkerSharePoint Four { get; set; } = new();
    }

    public class MarkerSharePoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public bool Active { get; set; }
    }
}

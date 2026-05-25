using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Models
{
    public class ReviewResponse
    {
        public string? Message { get; set; }
        public string? FixedCode { get; set; }
        
        public int PerformanceScore { get; set; }
        public int SecurityScore { get; set; }
        public int MaintainabilityScore { get; set; }

        public string? MermaidChart { get; set; }
        public string? HackerExploit { get; set; }
        public string? UnitTests { get; set; }

        public List<Bug>? Bugs { get; set; }
    }

    public class Bug
    {
        public int Line { get; set; }

        public string? Description { get; set; }
    }
}

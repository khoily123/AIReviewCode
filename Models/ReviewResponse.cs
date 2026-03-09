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

        public List<Bug>? Bugs { get; set; }
    }

    public class Bug
    {
        public int Line { get; set; }

        public string? Description { get; set; }
    }
}

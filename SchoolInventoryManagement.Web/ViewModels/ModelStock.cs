using System.Collections.Generic;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Available units of one model, in all and per room. Built for the
    // Approvals page's "5 available, 3 at the IT Room".
    public class ModelStock
    {
        public int Total { get; set; }
        public Dictionary<string, int> ByLocation { get; set; } = new();
    }
}

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Recording units coming back on a request -- all of them, or just the
    // ones ticked. They share one condition and one return location; if
    // they came back in different states, record them in separate goes.
    public class ReturnRequestViewModel
    {
        public int RequestID { get; set; }

        // The assignments (units) coming back now.
        public List<int> AssignmentIDs { get; set; } = new();

        [Required]
        [Display(Name = "Condition on return")]
        public ConditionStatus ConditionOnReturn { get; set; } = ConditionStatus.Good;

        // A unit tagged with its own condition (assignment id -> condition);
        // the rest take ConditionOnReturn.
        public Dictionary<int, string> UnitConditions { get; set; } = new();

        // Remarks on the return, kept with each unit's record.
        [MaxLength(500)]
        public string? Remarks { get; set; }

        // When they came back (defaults to now; earlier when recorded late).
        [Required(ErrorMessage = "Say when the units came back.")]
        [Display(Name = "Returned on")]
        public System.DateTime? ReturnedOn { get; set; }

        // Not asked any more: units go back to the Property Department room
        // of the borrower's branch. Kept for older forms.
        public int? ReturnLocationID { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}
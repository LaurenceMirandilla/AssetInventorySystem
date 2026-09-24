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

        [Required(ErrorMessage = "Say where the units are being put back.")]
        [Display(Name = "Return to location")]
        public int? ReturnLocationID { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}
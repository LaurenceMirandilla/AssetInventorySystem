using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.DAL.Entities
{
    // One step in a new-item request's history: the stage it moved into,
    // who moved it there, when, and what they said. The first step is the
    // submission itself (Status = AwaitingDeptHead, by the requester).
    // Added by script 010.
    public class NewItemRequestStep
    {
        [Key]
        public int StepID { get; set; }

        public int NewItemRequestID { get; set; }

        [Required]
        public NewItemStatus Status { get; set; }

        public int ActedByUserID { get; set; }

        public DateTime ActedAt { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [ForeignKey(nameof(NewItemRequestID))]
        public NewItemRequest Request { get; set; } = null!;

        [ForeignKey(nameof(ActedByUserID))]
        public User ActedByUser { get; set; } = null!;
    }
}

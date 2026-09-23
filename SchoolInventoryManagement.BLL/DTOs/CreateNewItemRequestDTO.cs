using System;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateNewItemRequestDTO
    {
        [Required]
        [MaxLength(150)]
        public string ItemName { get; set; } = null!;

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = null!;

        [Required]
        public DateTime? NeededBy { get; set; }
    }
}
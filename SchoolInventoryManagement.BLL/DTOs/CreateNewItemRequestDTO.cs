using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // One new-item ticket: one or more items sharing a reason and a date,
    // approved or rejected together.
    public class CreateNewItemRequestDTO
    {
        public List<CreateNewItemRequestItemDTO> Items { get; set; } = new();

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = null!;

        [Required]
        public DateTime? NeededBy { get; set; }
    }

    public class CreateNewItemRequestItemDTO
    {
        [Required]
        [MaxLength(150)]
        public string ItemName { get; set; } = null!;

        // How many are wanted.
        [Range(1, 1000)]
        public int Quantity { get; set; } = 1;

        // Estimated price of ONE, in pesos. Required on new requests.
        [Range(0.01, 9999999999.99)]
        public decimal? EstimatedUnitPrice { get; set; }
    }
}

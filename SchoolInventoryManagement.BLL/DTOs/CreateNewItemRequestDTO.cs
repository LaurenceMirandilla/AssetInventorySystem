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

        // How many are wanted.
        [Range(1, 1000)]
        public int Quantity { get; set; } = 1;

        // Estimated price of ONE, in pesos. Required on new requests.
        [Range(0.01, 9999999999.99)]
        public decimal? EstimatedUnitPrice { get; set; }
    }
}
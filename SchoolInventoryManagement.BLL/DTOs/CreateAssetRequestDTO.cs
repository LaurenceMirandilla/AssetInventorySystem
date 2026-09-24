using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // One request (ticket) with one or more lines. Each line is a Model and
    // how many of it; staff pick the actual units at approval.
    // Borrow: RequestedLocationID must be null.
    // Transfer: RequestedLocationID required.
    public class CreateAssetRequestDTO
    {
        [Required]
        public RequestType RequestType { get; set; }

        public List<CreateAssetRequestItemDTO> Items { get; set; } = new();

        public int? RequestedLocationID { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        // Required on every new request; nullable so a missing value reaches
        // the service's own check and its readable message.
        public DateTime? NeededFrom { get; set; }
        public DateTime? ReturnBy { get; set; }
    }

    public class CreateAssetRequestItemDTO
    {
        public int ModelID { get; set; }
        public int Quantity { get; set; } = 1;
    }
}
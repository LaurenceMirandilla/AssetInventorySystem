using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // One new-item ticket: one or more items sharing a reason, a date and
    // a place to bring them, approved or rejected together. A ticket is
    // for assets or for supplies, never a mix.
    public class CreateNewItemRequestDTO
    {
        public CategoryKind Kind { get; set; } = CategoryKind.Asset;

        public List<CreateNewItemRequestItemDTO> Items { get; set; } = new();

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = null!;

        [Required]
        public DateTime? NeededBy { get; set; }

        // No longer asked: the requester's department room is used
        // (Scripts/029).
        public int? DeliverToLocationID { get; set; }
    }

    public class CreateNewItemRequestItemDTO
    {
        [Required]
        [MaxLength(150)]
        public string ItemName { get; set; } = null!;

        // How many are wanted.
        [Range(1, 1000)]
        public int Quantity { get; set; } = 1;

        // Brand, size, model, colour. Optional.
        [MaxLength(500)]
        public string? Description { get; set; }

        // Supplies only: ream, box, bottle.
        [MaxLength(30)]
        public string? Unit { get; set; }

        // Set when an existing model (assets) or supply (supplies) was
        // picked from the search list.
        public int? ModelID { get; set; }
        public int? SupplyID { get; set; }
    }

    // What the request form offers in its search boxes.
    public class NewItemFormLookupsDTO
    {
        public string DepartmentName { get; set; } = null!;
        public string BranchName { get; set; } = null!;
        public int BranchID { get; set; }
        // Where it is brought: the department's main room (Scripts/029).
        // Null when the department has no room yet.
        public string? DeliveryRoomName { get; set; }
        public List<NewItemModelChoiceDTO> Models { get; set; } = new();
        public List<NewItemSupplyChoiceDTO> Supplies { get; set; } = new();
        public List<NewItemLocationChoiceDTO> Locations { get; set; } = new();
    }

    public class NewItemModelChoiceDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Category { get; set; } = null!;
        // Units the school has now, disposed ones left out.
        public int Units { get; set; }
    }

    public class NewItemSupplyChoiceDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Category { get; set; } = null!;
        public string Unit { get; set; } = null!;
        // In stock at the requester's own branch.
        public int Quantity { get; set; }
    }

    public class NewItemLocationChoiceDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;
    }

    // "Who can request new items": every department with its people.
    public class RequesterDepartmentDTO
    {
        public int DepartmentID { get; set; }
        public string DepartmentName { get; set; } = null!;
        public string BranchName { get; set; } = null!;
        public List<RequesterPersonDTO> People { get; set; } = new();
    }

    public class RequesterPersonDTO
    {
        public int UserID { get; set; }
        public string Name { get; set; } = null!;
        public string RoleName { get; set; } = null!;
        public bool CanRequest { get; set; }
    }
}

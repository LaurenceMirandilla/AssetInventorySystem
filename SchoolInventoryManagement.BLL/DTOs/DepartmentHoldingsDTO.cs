using System.Collections.Generic;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // My Assets: what a person's department has, shared by everyone in it.
    //   Borrowed: units out on a borrow by anyone in the department, each
    //             still naming who borrowed it (accountability).
    //   Kept:     units the department keeps for good (bought for it, or
    //             handed to it).
    public class DepartmentHoldingsDTO
    {
        public string DepartmentName { get; set; } = "";
        public List<AssetAssignmentResponseDTO> Borrowed { get; set; } = new();
        public List<DepartmentAssetDTO> Kept { get; set; } = new();
    }

    public class DepartmentAssetDTO
    {
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public string ModelName { get; set; } = null!;
        public string? LocationName { get; set; }
        public ConditionStatus Condition { get; set; }
    }
}

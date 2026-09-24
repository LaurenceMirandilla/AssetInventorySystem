using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // N units of ONE model, registered in a single step. Everything except
    // the code, the name and (optionally) the serial number is shared by
    // every unit -- which is what a delivery of 30 identical laptops is.
    public class BulkCreateAssetsDTO
    {
        public int ModelID { get; set; }
        public int Quantity { get; set; }

        // Codes are generated from the model's category prefix, carrying on
        // from the highest number already used: CHAIR-0058, CHAIR-0059, ...
        // Names come out as "{BaseName} - Unit {number}", using the same
        // number as the code, so CHAIR-0058 is "... - Unit 58".
        public string BaseName { get; set; } = null!;

        // Optional, one per line, in code order. Either empty or exactly
        // Quantity lines -- a count mismatch means a paste went wrong.
        public string? SerialNumbers { get; set; }

        public string? Description { get; set; }
        public DateTime? AcquisitionDate { get; set; }
        public decimal? AcquisitionCost { get; set; }
        public string? WarrantyInformation { get; set; }
        public ConditionStatus Condition { get; set; } = ConditionStatus.New;
        public int? CurrentLocationID { get; set; }
        public int BranchID { get; set; }
    }
}
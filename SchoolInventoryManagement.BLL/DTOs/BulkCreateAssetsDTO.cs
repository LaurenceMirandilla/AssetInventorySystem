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

        // Codes come out as {CodePrefix}{number:D4}: "LAP-" + 1 -> "LAP-0001".
        public string CodePrefix { get; set; } = null!;
        public int StartingNumber { get; set; } = 1;

        // Names come out as "{BaseName} - Unit {number}", using the same
        // number as the code, so LAP-0007 is always "... - Unit 7".
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
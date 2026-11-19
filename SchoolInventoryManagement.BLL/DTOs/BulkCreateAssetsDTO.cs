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

        // One warranty file shared by every unit in the batch.
        public string? WarrantyFileURL { get; set; }
        public ConditionStatus Condition { get; set; } = ConditionStatus.New;
        public int? CurrentLocationID { get; set; }
        public int BranchID { get; set; }

        // Must belong to BranchID. The branch's Property Department: the
        // units are in stock and can be borrowed. Any other department: they
        // are issued to it and cannot be borrowed.
        public int DepartmentID { get; set; }

        // Registering the units bought for an arrived new-item request:
        // they stay with Property, held for that request, until the
        // requester confirms receipt; then they are the department's.
        public int? NewItemRequestID { get; set; }

        // "Add barcode" on the form. On: every unit is saved with its barcode.
        public bool CreateBarcode { get; set; }
    }
}
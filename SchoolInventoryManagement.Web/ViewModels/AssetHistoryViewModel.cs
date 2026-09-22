using System.Collections.Generic;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class AssetHistoryViewModel
    {
        public AssetResponseDTO Asset { get; set; } = null!;
        public List<AssetAssignmentResponseDTO> Assignments { get; set; } = new();
        public List<AssetMovementResponseDTO> Movements { get; set; } = new();
        public List<DisposalRecordResponseDTO> Disposals { get; set; } = new();
        public List<AuditLog> AuditEntries { get; set; } = new();
    }
}
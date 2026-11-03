using System.Collections.Generic;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // One person's tabs on the Users page (Views/Users/_UserActivity).
    public class UserActivityViewModel
    {
        public int UserID { get; set; }
        public List<AssetAssignmentResponseDTO> Holding { get; set; } = new();
        public List<AssetRequestResponseDTO> Requests { get; set; } = new();
        public List<AssetAssignmentResponseDTO> Past { get; set; } = new();
    }
}

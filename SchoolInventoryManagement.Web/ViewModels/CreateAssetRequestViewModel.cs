using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // One form serves both request types; the view shows only the fields
    // that belong to the selected RequestType, and the controller nulls
    // out the other side before handing it to the service. That keeps a
    // stale value from a toggled-away field out of the DTO, which the
    // service and CK_AssetRequests_TypeFieldRules would both reject.
    public class CreateAssetRequestViewModel
    {
        [Required]
        public RequestType RequestType { get; set; } = RequestType.Borrow;

        // Borrow: the requester picks a Model, never a specific unit —
        // which unit goes out is staff's call at approval time.
        public int? ModelID { get; set; }

        // Transfer: the requester picks an existing unit and where it should go.
        public int? AssetID { get; set; }
        public int? RequestedLocationID { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}

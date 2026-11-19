using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // The Red tag form (Assets/RedTag): why the Property Department is
    // marking the unit for disposal.
    public class RedTagViewModel
    {
        public int AssetID { get; set; }

        [Required(ErrorMessage = "Say why it is being red-tagged.")]
        [MaxLength(500)]
        [Display(Name = "Reason")]
        public string Reason { get; set; } = null!;

        [Required]
        public string AssetRowVersionBase64 { get; set; } = null!;
    }
}

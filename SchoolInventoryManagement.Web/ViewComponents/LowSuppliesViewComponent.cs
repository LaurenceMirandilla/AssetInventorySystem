using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;

namespace SchoolInventoryManagement.Web.ViewComponents
{
    // Dashboard card: supplies that are running low or out. Shown to the
    // roles that manage or oversee stock; nothing when all is well.
    public class LowSuppliesViewComponent : ViewComponent
    {
        private readonly ISupplyService _supplies;

        public LowSuppliesViewComponent(ISupplyService supplies)
        {
            _supplies = supplies;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var user = UserClaimsPrincipal;
            var sees = user.IsInRole(RoleNames.AssetOfficer)
                || user.IsInRole(RoleNames.Administrator)
                || user.IsInRole(RoleNames.Principal);
            if (!sees)
                return Content(string.Empty);

            var low = await _supplies.GetLowSuppliesAsync();
            return low.Count == 0 ? Content(string.Empty) : View(low);
        }
    }
}

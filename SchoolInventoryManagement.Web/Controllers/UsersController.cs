using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Class-level [Authorize] only guarantees a signed-in user, because
    // ChangePassword is self-service and open to everyone. Every management
    // action carries its own role attribute instead of relying on a
    // class-wide one — UserService re-checks all of them regardless.
    [Authorize]
    public class UsersController : BaseController
    {
        private const string UserManagerRoles =
            RoleNames.Administrator + "," + RoleNames.Principal;

        // Shared with Views/Users/Index.cshtml, which builds the dropdown
        // from these same three values.
        public const string StatusFilterActive = "Active";
        public const string StatusFilterInactive = "Inactive";
        public const string StatusFilterAll = "All";

        private readonly IUserService _userService;
        private readonly IBranchService _branchService;
        private readonly IDepartmentService _departmentService;

        public UsersController(
            IUserService userService,
            IBranchService branchService,
            IDepartmentService departmentService)
        {
            _userService = userService;
            _branchService = branchService;
            _departmentService = departmentService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Users?status=Active
        // Defaults to Active: deactivated and anonymized accounts are never
        // removed, so without a filter they accumulate in the list forever
        // and bury the people who can actually sign in.
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Index(string status = StatusFilterActive)
        {
            // "All" is the one value that means "do not filter at all".
            var serviceFilter = status == StatusFilterAll ? null : status;

            var users = await _userService.GetAllUsersAsync(serviceFilter);

            ViewData["StatusFilter"] = status;
            return View(users);
        }

        // POST /Users/Anonymize/5 — irreversible. Blanks the name and email
        // and locks the account, keeping the FK history pointing at a
        // tombstone. Distinct from Deactivate, which is reversible and keeps
        // the identity.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Anonymize(int id, string rowVersionBase64)
        {
            try
            {
                await _userService.AnonymizeUserAsync(
                    id, RowVersionHelper.FromBase64(rowVersionBase64), CurrentUserId);

                TempData["StatusMessage"] =
                    "Account anonymized. The records it is attached to are intact, " +
                    "but no longer name the person.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            // Anonymized accounts are Inactive, so send the manager to a view
            // that actually contains the row they just changed.
            return RedirectToAction(nameof(Index), new { status = StatusFilterInactive });
        }

        // GET /Users/Create
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Create()
        {
            await PopulateDropdownsAsync();
            return View(new UserCreateViewModel());
        }

        // POST /Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Create(UserCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync();
                return View(model);
            }

            try
            {
                var dto = new CreateUserDTO
                {
                    RoleID = model.RoleID,
                    DepartmentID = model.DepartmentID,
                    BranchID = model.BranchID,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    Email = model.Email,
                    Password = model.Password
                };

                await _userService.CreateUserAsync(dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateDropdownsAsync();
                return View(model);
            }
        }

        // GET /Users/Edit/5
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user is null)
                return NotFound();

            await PopulateDropdownsAsync();

            return View(new UserEditViewModel
            {
                UserID = user.UserID,
                Email = user.Email,
                RoleID = user.RoleID,
                DepartmentID = user.DepartmentID,
                BranchID = user.BranchID,
                FirstName = user.FirstName,
                LastName = user.LastName,
                RowVersionBase64 = RowVersionHelper.ToBase64(user.RowVersion)
            });
        }

        // POST /Users/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Edit(int id, UserEditViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync();
                return View(model);
            }

            try
            {
                var dto = new UpdateUserDTO
                {
                    RoleID = model.RoleID,
                    DepartmentID = model.DepartmentID,
                    BranchID = model.BranchID,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    RowVersion = RowVersionHelper.FromBase64(model.RowVersionBase64)
                };

                await _userService.UpdateUserAsync(id, dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                await PopulateDropdownsAsync();
                return View(model);
            }
        }

        // POST /Users/Deactivate/5 — the soft delete. Users are never
        // removed; their FK history has to stay intact.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Deactivate(int id, string rowVersionBase64)
        {
            try
            {
                await _userService.DeactivateUserAsync(
                    id, RowVersionHelper.FromBase64(rowVersionBase64), CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // POST /Users/Reactivate/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Reactivate(int id, string rowVersionBase64)
        {
            try
            {
                await _userService.ReactivateUserAsync(
                    id, RowVersionHelper.FromBase64(rowVersionBase64), CurrentUserId);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // GET /Users/ResetPassword/5 — administrative reset of someone
        // else's password. Separate action from ChangePassword because the
        // two have different permissions and different form fields.
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> ResetPassword(int id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user is null)
                return NotFound();

            return View(new ResetPasswordViewModel
            {
                UserID = user.UserID,
                UserFullName = $"{user.FirstName} {user.LastName}",
                Email = user.Email,
                RowVersionBase64 = RowVersionHelper.ToBase64(user.RowVersion)
            });
        }

        // POST /Users/ResetPassword/5
        // The id comes from the route, not the posted model, so a tampered
        // hidden UserID cannot redirect the reset at a different account.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> ResetPassword(int id, ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return await RedisplayResetAsync(id, model);

            try
            {
                await _userService.ResetPasswordAsync(
                    id,
                    model.NewPassword,
                    RowVersionHelper.FromBase64(model.RowVersionBase64),
                    CurrentUserId);

                TempData["StatusMessage"] =
                    $"Password reset for {model.Email}. Give them the new password directly " +
                    "and have them change it themselves.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return await RedisplayResetAsync(id, model);
            }
        }

        // Re-reads the user so the name, email and RowVersion shown on a
        // failed post are current rather than whatever was posted back.
        private async Task<IActionResult> RedisplayResetAsync(int id, ResetPasswordViewModel model)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user is null)
                return NotFound();

            model.UserID = user.UserID;
            model.UserFullName = $"{user.FirstName} {user.LastName}";
            model.Email = user.Email;
            model.RowVersionBase64 = RowVersionHelper.ToBase64(user.RowVersion);

            return View("ResetPassword", model);
        }

        // GET /Users/ChangePassword — self-service, any signed-in user.
        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

        // POST /Users/ChangePassword
        // Always acts on CurrentUserId. ChangePasswordAsync takes a userId
        // but checks only the current password, so passing anything else
        // here would let one user reset another's.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                await _userService.ChangePasswordAsync(
                    CurrentUserId, model.CurrentPassword, model.NewPassword);

                TempData["StatusMessage"] = "Your password has been changed.";
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return View(model);
            }
        }

        private async Task PopulateDropdownsAsync()
        {
            var roles = await _userService.GetAllRolesAsync();
            ViewBag.Roles = new SelectList(roles, "RoleID", "RoleName");

            var branches = await _branchService.GetAllBranchesAsync();
            ViewBag.Branches = new SelectList(branches, "BranchID", "BranchName");

            // Labelled with the branch, since department names can repeat
            // across branches — they're only unique within one.
            var departments = await _departmentService.GetAllDepartmentsAsync();
            ViewBag.Departments = new SelectList(
                departments.Select(d => new { d.DepartmentID, Label = $"{d.BranchName} — {d.DepartmentName}" }),
                "DepartmentID", "Label");

            // Same list with BranchID kept, so Create can tag each option
            // with data-branch and filter it to the chosen branch.
            ViewBag.DepartmentsByBranch = departments;
        }
    }
}

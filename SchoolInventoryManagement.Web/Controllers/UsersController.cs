using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Services;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.Web.Helpers;
using SchoolInventoryManagement.Web.Middleware;
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
        private readonly AccountDefaults _accountDefaults;
        private readonly IAssetAssignmentService _assignmentService;
        private readonly IAssetRequestService _requestService;
        private readonly UserFacts _userFacts;

        public UsersController(
            IUserService userService,
            IBranchService branchService,
            IDepartmentService departmentService,
            AccountDefaults accountDefaults,
            IAssetAssignmentService assignmentService,
            IAssetRequestService requestService,
            UserFacts userFacts)
        {
            _userService = userService;
            _branchService = branchService;
            _departmentService = departmentService;
            _accountDefaults = accountDefaults;
            _assignmentService = assignmentService;
            _requestService = requestService;
            _userFacts = userFacts;
        }

        // GET /Users/Activity/5 -- the Holding now / Requests / Past items
        // tabs for one person, loaded into the Users page when they are
        // picked, so the list itself stays quick.
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Activity(int id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user is null)
                return NotFound();

            return PartialView("_UserActivity", new UserActivityViewModel
            {
                UserID = id,
                Holding = await _assignmentService.GetActiveAssignmentsForUserAsync(id),
                Requests = await _requestService.GetMyRequestsAsync(id),
                Past = await _assignmentService.GetPastAssignmentsForUserAsync(id)
            });
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // GET /Users?status=Active
        // Defaults to Active: deactivated accounts are never
        // removed, so without a filter they accumulate in the list forever
        // and bury the people who can actually sign in.
        [Authorize(Roles = UserManagerRoles)]
        public async Task<IActionResult> Index(string status = StatusFilterActive)
        {
            // Every account comes to the page; its Status filter starts on
            // the one asked for (Active by default), and the others are a
            // click away without reloading.
            var users = await _userService.GetAllUsersAsync(null);

            ViewData["StatusFilter"] = status;
            return View(users);
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
            // The first password: the shared default, unless the
            // administrator typed another. Either way they choose their
            // own at first sign-in.
            var typed = string.IsNullOrEmpty(model.Password) || model.Password == _accountDefaults.DefaultPassword
                ? null : model.Password;
            if (model.RoleID <= 0)
                ModelState.AddModelError(nameof(model.RoleID), "Pick a role.");
            if (typed is null && !_accountDefaults.IsUsable)
                ModelState.AddModelError(string.Empty,
                    "No usable starting password is set. Type one, or add \"Accounts\": { \"DefaultPassword\": \"...\" } to appsettings.json " +
                    "(at least 8 characters, with a number).");
            if (typed is not null)
            {
                if (typed.Length < PasswordPolicy.MinLength)
                    ModelState.AddModelError(nameof(model.Password), PasswordPolicy.TooShortMessage);
                else if (!PasswordPolicy.HasNumber(typed))
                    ModelState.AddModelError(nameof(model.Password), PasswordPolicy.NoNumberMessage);
                if (typed != model.ConfirmPassword)
                    ModelState.AddModelError(nameof(model.ConfirmPassword), "The two passwords don't match.");
            }

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
                    Username = model.Username,
                    Password = typed ?? _accountDefaults.DefaultPassword!
                };

                var created = await _userService.CreateUserAsync(dto, CurrentUserId);
                TempData["StatusMessage"] = typed is null
                    ? $"{created.FirstName} {created.LastName} was added. They sign in as {created.Username} with the starting " +
                      $"password {_accountDefaults.DefaultPassword} and must choose their own the first time."
                    : $"{created.FirstName} {created.LastName} was added. They sign in as {created.Username} with the password " +
                      "you set, and must choose their own the first time.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                UsernameError(ex);
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
            await LoadAuditHistoryAsync("User", id);

            return View(new UserEditViewModel
            {
                UserID = user.UserID,
                Username = user.Username,
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
                await LoadAuditHistoryAsync("User", id);
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
                    Username = model.Username,
                    RowVersion = RowVersionHelper.FromBase64(model.RowVersionBase64)
                };

                await _userService.UpdateUserAsync(id, dto, CurrentUserId);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                UsernameError(ex);
                await PopulateDropdownsAsync();
                await LoadAuditHistoryAsync("User", id);
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
                TempData["ErrorMessage"] = UserMessageFor(ex);
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
                TempData["ErrorMessage"] = UserMessageFor(ex);
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
                Username = user.Username,
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
                    $"Password reset for {model.Username}. Give them the new password directly " +
                    "They'll be asked to choose their own the next time they sign in.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return await RedisplayResetAsync(id, model);
            }
        }

        // Re-reads the user so the name, username and RowVersion shown on a
        // failed post are current rather than whatever was posted back.
        private async Task<IActionResult> RedisplayResetAsync(int id, ResetPasswordViewModel model)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user is null)
                return NotFound();

            model.UserID = user.UserID;
            model.UserFullName = $"{user.FirstName} {user.LastName}";
            model.Username = user.Username;
            model.RowVersionBase64 = RowVersionHelper.ToBase64(user.RowVersion);

            return View("ResetPassword", model);
        }

        // GET /Users/ChangePassword — self-service, any signed-in user.
        public IActionResult ChangePassword()
        {
            ViewBag.MustChange = MustChange;
            return View(new ChangePasswordViewModel());
        }

        // Signed in on a starting password (MustChangePasswordFilter).
        private bool MustChange => User.HasClaim(AccountDefaults.ClaimType, "true");

        // POST /Users/ChangePassword
        // Always acts on CurrentUserId. ChangePasswordAsync takes a userId
        // but checks only the current password, so passing anything else
        // here would let one user reset another's.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            // Same password again: refused here so it shows under the field
            // (the service refuses it too).
            if (!string.IsNullOrEmpty(model.NewPassword) && model.NewPassword == model.CurrentPassword)
                ModelState.AddModelError(nameof(model.NewPassword), PasswordPolicy.SameAsCurrentMessage);

            ViewBag.MustChange = MustChange;

            if (!ModelState.IsValid)
                return View(model);

            try
            {
                await _userService.ChangePasswordAsync(
                    CurrentUserId, model.CurrentPassword, model.NewPassword);

                // They stay signed in. If they were on a starting password,
                // the cookie is re-issued without that flag, which opens
                // the rest of the app again.
                if (MustChange)
                {
                    var auth = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    var identity = new ClaimsIdentity(
                        User.Claims.Where(c => c.Type != AccountDefaults.ClaimType),
                        CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(identity),
                        auth.Properties ?? new AuthenticationProperties());
                }

                TempData["StatusMessage"] = "Your password has been changed. You're still signed in.";
                return RedirectToAction("Index", "Home");
            }
            catch (UnauthorizedAccessException ex) when (ExceptionHandlingMiddleware.IsExpected(ex))
            {
                // Wrong current password: shown under that box, not at the top.
                ModelState.AddModelError(nameof(model.CurrentPassword), "That isn't your current password.");
                return View(model);
            }
            catch (Exception ex)
            {
                HandleServiceException(ex);
                return View(model);
            }
        }

        // A taken or malformed username is shown under the Username box;
        // anything else at the top of the form as usual.
        private void UsernameError(Exception ex)
        {
            if (ExceptionHandlingMiddleware.IsExpected(ex) &&
                (ex.Message == UsernamePolicy.TakenMessage || ex.Message == UsernamePolicy.Message))
                ModelState.AddModelError("Username", ex.Message);
            else
                HandleServiceException(ex);
        }

        private async Task PopulateDropdownsAsync()
        {
            // Shown on Add user, so the administrator can tell the new person.
            ViewBag.DefaultPassword = _accountDefaults.IsUsable ? _accountDefaults.DefaultPassword : null;

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

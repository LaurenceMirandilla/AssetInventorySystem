using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.Web.Helpers;

namespace SchoolInventoryManagement.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;
        private readonly LoginThrottle _loginThrottle;
        private readonly AccessDeniedText _accessDeniedText;

        public AccountController(IAuthService authService, LoginThrottle loginThrottle, AccessDeniedText accessDeniedText)
        {
            _authService = authService;
            _loginThrottle = loginThrottle;
            _accessDeniedText = accessDeniedText;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginDTO model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Locked out after too many wrong passwords: refused without
            // checking the password, and not counted as another try.
            var locked = _loginThrottle.LockoutRemaining(model.Username);
            if (locked is not null)
                return LockedOut(model, locked.Value);

            var user = await _authService.ValidateCredentialsAsync(model.Username, model.Password);

            if (user is null)
            {
                var (lockout, triesLeft) = _loginThrottle.RecordFailure(model.Username);
                if (lockout is not null)
                    return LockedOut(model, lockout.Value);

                ModelState.AddModelError(string.Empty, triesLeft <= 2
                    ? $"Invalid username or password. {triesLeft} more {(triesLeft == 1 ? "try" : "tries")} before a {(int)LoginThrottle.LockoutLength.TotalSeconds}-second wait."
                    : "Invalid username or password.");
                return View(model);
            }

            _loginThrottle.Reset(model.Username);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                new Claim("username", user.Username),
                new Claim(ClaimTypes.Role, user.Role.RoleName)
            };
            // Starting password: every page but Change password is closed
            // until they choose their own.
            if (user.MustChangePassword)
                claims.Add(new Claim(AccountDefaults.ClaimType, "true"));

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                new AuthenticationProperties { IsPersistent = model.RememberMe });

            return user.MustChangePassword
                ? RedirectToAction("ChangePassword", "Users")
                : RedirectToAction("Index", "Home");
        }

        // The sign-in page again, with the wait shown and the button held
        // until it is over.
        private IActionResult LockedOut(LoginDTO model, TimeSpan remaining)
        {
            var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
            ViewBag.LockoutSeconds = seconds;
            ModelState.AddModelError(string.Empty,
                $"Too many failed sign-in attempts. Try again in {seconds} seconds.");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // Where the sign-in cookie sends a signed-in user whose role a page
        // doesn't allow, with that page as ReturnUrl. Says who the page is
        // for and who they're signed in as.
        public IActionResult AccessDenied(string? returnUrl)
        {
            ViewBag.WhoItIsFor = Url.IsLocalUrl(returnUrl) ? _accessDeniedText.For(returnUrl) : null;
            return View();
        }
    }
}

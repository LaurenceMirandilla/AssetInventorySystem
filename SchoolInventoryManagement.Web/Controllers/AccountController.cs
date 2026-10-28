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

        public AccountController(IAuthService authService, LoginThrottle loginThrottle)
        {
            _authService = authService;
            _loginThrottle = loginThrottle;
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
            var locked = _loginThrottle.LockoutRemaining(model.Email);
            if (locked is not null)
                return LockedOut(model, locked.Value);

            var user = await _authService.ValidateCredentialsAsync(model.Email, model.Password);

            if (user is null)
            {
                var (lockout, triesLeft) = _loginThrottle.RecordFailure(model.Email);
                if (lockout is not null)
                    return LockedOut(model, lockout.Value);

                ModelState.AddModelError(string.Empty, triesLeft <= 2
                    ? $"Invalid email or password. {triesLeft} more {(triesLeft == 1 ? "try" : "tries")} before a {(int)LoginThrottle.LockoutLength.TotalSeconds}-second wait."
                    : "Invalid email or password.");
                return View(model);
            }

            _loginThrottle.Reset(model.Email);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.RoleName)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                new AuthenticationProperties { IsPersistent = model.RememberMe });

            return RedirectToAction("Index", "Home");
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

        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}

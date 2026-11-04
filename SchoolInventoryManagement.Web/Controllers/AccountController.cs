using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
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
        private readonly IAntiforgery _antiforgery;

        public AccountController(IAuthService authService, LoginThrottle loginThrottle, AccessDeniedText accessDeniedText, IAntiforgery antiforgery)
        {
            _authService = authService;
            _loginThrottle = loginThrottle;
            _accessDeniedText = accessDeniedText;
            _antiforgery = antiforgery;
        }

        [HttpGet]
        public IActionResult Login()
        {
            // Already signed in (Back button, an old tab): nothing to do here.
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            return View();
        }

        // The anti-forgery check is done by hand here instead of with
        // [ValidateAntiForgeryToken]. Clicking Sign in twice sends two posts
        // with the same token; the first one signs the person in, so the
        // second arrives with the new sign-in cookie and a token made for
        // "nobody signed in" -- the attribute rejected it with 400 Bad
        // Request and the error page showed. Now a post from someone who is
        // already signed in just goes on to Home (it changes nothing), and
        // an out-of-date token reloads the sign-in page instead of erroring.
        [HttpPost]
        public async Task<IActionResult> Login(LoginDTO model)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");

            try
            {
                await _antiforgery.ValidateRequestAsync(HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                ModelState.Clear();
                ModelState.AddModelError(string.Empty, "The sign-in page had expired. Please sign in again.");
                return View(new LoginDTO { Username = model.Username, RememberMe = model.RememberMe });
            }

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

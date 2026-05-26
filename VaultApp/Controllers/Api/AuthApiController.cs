using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VaultApp.Models;
using VaultApp.Models.Api;
using VaultApp.Services;

namespace VaultApp.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthApiController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEncryptionService _enc;
    private readonly IVaultService _vault;
    private readonly IJwtTokenService _jwt;
    private readonly IUserAccountService _accounts;

    public AuthApiController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEncryptionService enc,
        IVaultService vault,
        IJwtTokenService jwt,
        IUserAccountService accounts)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _enc = enc;
        _vault = vault;
        _jwt = jwt;
        _accounts = accounts;
    }

    private string UserId => _userManager.GetUserId(User)!;

    /// <summary>Register a new account and receive a JWT.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthTokenResponse>> Register([FromBody] ApiRegisterRequest model)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EncryptionKeyHash = _enc.HashEncryptionKey(model.EncryptionKey)
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError("", e.Description);
            return ValidationProblem(ModelState);
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                await _vault.ClaimPendingSharesAsync(user.Id, user.Email);
            }
            catch
            {
                // Same as MVC: log elsewhere if needed; registration succeeded.
            }
        }

        var (token, expiresAt) = _jwt.CreateToken(user);
        return Ok(new AuthTokenResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            Email = user.Email ?? "",
            UserId = user.Id
        });
    }

    /// <summary>Sign in and receive a JWT.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthTokenResponse>> Login([FromBody] ApiLoginRequest model)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
            return Unauthorized(new { error = "Invalid credentials." });

        if (string.IsNullOrEmpty(user.EncryptionKeyHash) ||
            !_enc.VerifyEncryptionKey(model.EncryptionKey, user.EncryptionKeyHash))
            return Unauthorized(new { error = "Invalid credentials." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return Unauthorized(new { error = "Account locked out. Try again later." });
        if (!result.Succeeded)
            return Unauthorized(new { error = "Invalid credentials." });

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                await _vault.ClaimPendingSharesAsync(user.Id, user.Email);
            }
            catch
            {
                //
            }
        }

        var (token, expiresAt) = _jwt.CreateToken(user);
        return Ok(new AuthTokenResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            Email = user.Email ?? "",
            UserId = user.Id
        });
    }

    /// <summary>Logout is client-side for JWT (discard the token). This endpoint always succeeds when authorized.</summary>
    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        return NoContent();
    }

    /// <summary>
    /// Permanently delete the authenticated user's account and all associated vault data.
    /// Requires password and encryption key confirmation.
    /// </summary>
    [HttpDelete("account")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(DeleteAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DeleteAccountResponse>> DeleteAccount([FromBody] ApiDeleteAccountRequest model)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var (ok, error) = await _accounts.DeleteAccountAsync(
            UserId, model.Password, model.EncryptionKey);

        if (!ok)
        {
            if (string.Equals(error, "User not found.", StringComparison.Ordinal))
                return NotFound(new { error });

            if (string.Equals(error, "Invalid credentials.", StringComparison.Ordinal))
                return Unauthorized(new { error });

            return BadRequest(new { error });
        }

        return Ok(new DeleteAccountResponse
        {
            Message = "Your account has been deleted successfully."
        });
    }
}

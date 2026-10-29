using ChessGameD.Web.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChessGameD.Web.Controllers.Api;

/// <summary>当前身份的只读信息。</summary>
[ApiController]
[Route("api/account")]
public class AccountApiController : ControllerBase
{
    private readonly CurrentUser _current;

    public AccountApiController(CurrentUser current)
    {
        _current = current;
    }

    /// <summary>返回当前登录状态。</summary>
    [HttpGet("me")]
    [AllowAnonymous]
    public IActionResult Me()
        => Ok(new
        {
            isAuthenticated = _current.IsAuthenticated,
            userName = _current.UserName,
            isAdmin = _current.IsAdmin,
            hasAnonymousSession = !string.IsNullOrEmpty(_current.AnonKey),
        });
}

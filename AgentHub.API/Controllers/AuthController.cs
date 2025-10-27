using AgentHub.Contracts.DTOs.Auth;
using AgentHub.Contracts.Responses;
using AgentHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AgentHub.API.Controllers;

/// <summary>
/// 认证控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    /// <param name="request">注册请求</param>
    /// <returns>认证响应</returns>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 400)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(ApiResponse<AuthResponse>.ErrorResponse(
                    "输入验证失败",
                    errors
                ));
            }

            var response = await _authService.RegisterAsync(request);
            _logger.LogInformation("用户 {Username} 注册成功", request.Username);

            return Ok(ApiResponse<AuthResponse>.SuccessResponse(
                response,
                "注册成功"
            ));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("用户注册失败: {Message}", ex.Message);
            return BadRequest(ApiResponse<AuthResponse>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户注册时发生错误");
            return StatusCode(500, ApiResponse<AuthResponse>.ErrorResponse(
                "服务器内部错误，请稍后重试"
            ));
        }
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    /// <param name="request">登录请求</param>
    /// <returns>认证响应</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), 400)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return BadRequest(ApiResponse<AuthResponse>.ErrorResponse(
                    "输入验证失败",
                    errors
                ));
            }

            var response = await _authService.LoginAsync(request);
            _logger.LogInformation("用户 {Username} 登录成功", request.Username);

            return Ok(ApiResponse<AuthResponse>.SuccessResponse(
                response,
                "登录成功"
            ));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("用户登录失败: {Message}", ex.Message);
            return BadRequest(ApiResponse<AuthResponse>.ErrorResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户登录时发生错误");
            return StatusCode(500, ApiResponse<AuthResponse>.ErrorResponse(
                "服务器内部错误，请稍后重试"
            ));
        }
    }

    /// <summary>
    /// 验证Token（需要认证）
    /// </summary>
    /// <returns>验证结果</returns>
    [HttpGet("verify")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public IActionResult VerifyToken()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            UserId = userId,
            Username = username,
            IsAuthenticated = true
        }, "Token有效"));
    }
}

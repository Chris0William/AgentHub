using AgentHub.Contracts.DTOs.Auth;
using AgentHub.Core.Domain.Models;
using AgentHub.Core.Interfaces;
using AgentHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AgentHub.Infrastructure.Services.Auth;

/// <summary>
/// 认证服务实现
/// </summary>
public class AuthService : IAuthService
{
    private readonly AgentHubDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthService(AgentHubDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // 检查用户名是否已存在
        if (await _context.Users.AnyAsync(u => u.Username == request.Username))
        {
            throw new InvalidOperationException("用户名已存在");
        }

        // 检查邮箱是否已存在
        if (!string.IsNullOrEmpty(request.Email))
        {
            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            {
                throw new InvalidOperationException("邮箱已被使用");
            }
        }

        // 创建用户
        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Email = request.Email,
            Phone = request.Phone,
            CreatedAt = DateTime.Now,
            IsActive = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // 创建用户档案
        var profile = new UserProfile
        {
            UserId = user.Id,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _context.UserProfiles.Add(profile);
        await _context.SaveChangesAsync();

        // 生成Token
        var token = GenerateJwtToken(user.Id, user.Username);
        var expiresAt = DateTime.UtcNow.AddMinutes(
            _configuration.GetValue<int>("JWT:ExpirationMinutes", 1440)
        );

        return new AuthResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email
        };
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        // 查找用户
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user == null)
        {
            throw new InvalidOperationException("用户名或密码错误");
        }

        // 验证密码
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new InvalidOperationException("用户名或密码错误");
        }

        // 检查用户是否激活
        if (!user.IsActive)
        {
            throw new InvalidOperationException("账户已被禁用");
        }

        // 更新最后登录时间
        user.LastLogin = DateTime.Now;
        await _context.SaveChangesAsync();

        // 生成Token
        var token = GenerateJwtToken(user.Id, user.Username);
        var expiresAt = DateTime.UtcNow.AddMinutes(
            _configuration.GetValue<int>("JWT:ExpirationMinutes", 1440)
        );

        return new AuthResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email
        };
    }

    /// <summary>
    /// 生成JWT Token
    /// </summary>
    public string GenerateJwtToken(long userId, string username)
    {
        var jwtSettings = _configuration.GetSection("JWT");
        var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret未配置");
        var key = Encoding.ASCII.GetBytes(secretKey);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(jwtSettings.GetValue<int>("ExpirationMinutes", 1440)),
            Issuer = jwtSettings["Issuer"],
            Audience = jwtSettings["Audience"],
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            )
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }
}

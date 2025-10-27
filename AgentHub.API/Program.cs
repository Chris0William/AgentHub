using AgentHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;

// 配置Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build())
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/agenthub-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("正在启动 AgentHub API...");

    var builder = WebApplication.CreateBuilder(args);

    // 使用Serilog
    builder.Host.UseSerilog();

    var configuration = builder.Configuration;

    // 添加DbContext
    builder.Services.AddDbContext<AgentHubDbContext>(options =>
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
    });

    // 配置JWT认证
    var jwtSettings = configuration.GetSection("JWT");
    var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret未配置");
    var key = Encoding.ASCII.GetBytes(secretKey);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

    builder.Services.AddAuthorization();

    // 配置CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "http://localhost:3000" };

            policy.WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
    });

    // 添加Controllers
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = null; // 保持原始命名
            options.JsonSerializerOptions.WriteIndented = true; // 开发环境格式化JSON
        });

    // 添加SignalR
    builder.Services.AddSignalR();

    // 添加Swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "AgentHub API",
            Version = "v1",
            Description = "智能Agent平台 API 文档"
        });

        // 添加JWT认证支持
        options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Description = "JWT授权(示例: Bearer {token})",
            Name = "Authorization",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });

        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // 注册应用服务
    builder.Services.AddScoped<AgentHub.Core.Interfaces.IAuthService, AgentHub.Infrastructure.Services.Auth.AuthService>();
    builder.Services.AddScoped<AgentHub.Core.Interfaces.IConversationService, AgentHub.Infrastructure.Services.Conversation.ConversationService>();

    // 注册LLM服务
    builder.Services.AddHttpClient<AgentHub.Core.Interfaces.ILLMService, AgentHub.Infrastructure.AI.QwenService>();

    // 注册外部API服务
    builder.Services.AddHttpClient<AgentHub.Infrastructure.ExternalApis.IJuheCalendarService, AgentHub.Infrastructure.ExternalApis.JuheCalendarService>();
    builder.Services.AddHttpClient<AgentHub.Infrastructure.ExternalApis.IJuheHoroscopeService, AgentHub.Infrastructure.ExternalApis.JuheHoroscopeService>();

    // 注册房产数据服务（Singleton，因为是无状态服务）
    builder.Services.AddSingleton<AgentHub.Core.Interfaces.IRealEstateService, AgentHub.Infrastructure.ExternalApis.RealEstateService>();

    // 注册网络搜索服务 - 支持动态切换搜索引擎
    builder.Services.AddHttpClient(); // 确保HttpClientFactory可用
    var searchEngine = configuration["ExternalApis:SearchEngine"] ?? "SearXNG";
    builder.Services.AddSingleton<AgentHub.Core.Interfaces.IWebSearchService>(sp =>
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var logger = sp.GetRequiredService<ILogger<AgentHub.Core.Interfaces.IWebSearchService>>();

        return searchEngine.ToLower() switch
        {
            "searxng" => new AgentHub.Infrastructure.ExternalApis.SearXNGSearchService(
                httpClientFactory,
                configuration,
                sp.GetRequiredService<ILogger<AgentHub.Infrastructure.ExternalApis.SearXNGSearchService>>()
            ),
            "bing" => new AgentHub.Infrastructure.ExternalApis.BingSearchService(
                httpClientFactory,
                configuration,
                sp.GetRequiredService<ILogger<AgentHub.Infrastructure.ExternalApis.BingSearchService>>()
            ),
            _ => new AgentHub.Infrastructure.ExternalApis.SearXNGSearchService(
                httpClientFactory,
                configuration,
                sp.GetRequiredService<ILogger<AgentHub.Infrastructure.ExternalApis.SearXNGSearchService>>()
            )
        };
    });

    // 注册Semantic Kernel服务
    builder.Services.AddSingleton<AgentHub.Core.Interfaces.ISemanticKernelService, AgentHub.Infrastructure.AI.SemanticKernelService>();

    var app = builder.Build();

    // 配置HTTP请求管道
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "AgentHub API v1");
            options.RoutePrefix = "swagger";
        });
    }

    app.UseHttpsRedirection();

    app.UseSerilogRequestLogging();

    // 启用静态文件服务
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseCors("AllowFrontend");

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // TODO: 配置SignalR Hub
    // app.MapHub<ChatHub>("/hubs/chat");

    // 健康检查端点
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow
    }))
    .WithName("HealthCheck")
    .WithTags("Health");

    // SPA回退路由 - 所有非API请求都返回index.html（支持前端路由）
    app.MapFallbackToFile("index.html");

    Log.Information("AgentHub API 启动成功!");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "AgentHub API 启动失败");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

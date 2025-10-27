namespace AgentHub.Infrastructure.ExternalApis;

/// <summary>
/// 聚合数据老黄历API服务接口
/// </summary>
public interface IJuheCalendarService
{
    /// <summary>
    /// 获取今日宜忌
    /// </summary>
    Task<string> GetTodayTaboosAsync();
}

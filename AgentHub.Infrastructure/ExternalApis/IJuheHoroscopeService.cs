namespace AgentHub.Infrastructure.ExternalApis;

/// <summary>
/// 聚合数据星座运势API服务接口
/// </summary>
public interface IJuheHoroscopeService
{
    /// <summary>
    /// 获取星座运势
    /// </summary>
    /// <param name="constellation">星座名称（如：白羊座、金牛座等）</param>
    /// <param name="type">运势类型：today-今日，tomorrow-明日，week-本周，month-本月，year-本年</param>
    /// <returns>星座运势详情</returns>
    Task<string> GetHoroscopeAsync(string constellation, string type = "today");
}

namespace AgentHub.Core.Interfaces;

/// <summary>
/// 网络搜索服务接口
/// </summary>
public interface IWebSearchService
{
    /// <summary>
    /// 搜索网络信息
    /// </summary>
    /// <param name="query">搜索关键词</param>
    /// <param name="count">返回结果数量,默认5条</param>
    /// <returns>搜索结果摘要</returns>
    Task<string> SearchAsync(string query, int count = 5);
}

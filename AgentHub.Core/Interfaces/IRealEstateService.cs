namespace AgentHub.Core.Interfaces;

/// <summary>
/// 房产数据服务接口
/// </summary>
public interface IRealEstateService
{
    /// <summary>
    /// 搜索楼盘信息
    /// </summary>
    /// <param name="city">城市名称，如：东莞、深圳</param>
    /// <param name="keyword">关键词，如：楼盘名称、区域</param>
    /// <param name="minPrice">最低价格（万元）</param>
    /// <param name="maxPrice">最高价格（万元）</param>
    /// <param name="limit">返回数量，默认10</param>
    Task<string> SearchPropertyAsync(string city, string? keyword = null, int? minPrice = null, int? maxPrice = null, int limit = 10);

    /// <summary>
    /// 获取房价趋势
    /// </summary>
    /// <param name="city">城市名称</param>
    /// <param name="district">区域名称（可选）</param>
    Task<string> GetPriceTrendAsync(string city, string? district = null);

    /// <summary>
    /// 获取楼盘详情
    /// </summary>
    /// <param name="propertyId">楼盘ID或名称</param>
    Task<string> GetPropertyDetailAsync(string propertyId);

    /// <summary>
    /// 推荐楼盘（基于预算和需求）
    /// </summary>
    /// <param name="city">城市</param>
    /// <param name="budget">预算（万元）</param>
    /// <param name="rooms">房型，如：2室、3室</param>
    /// <param name="district">意向区域（可选）</param>
    Task<string> RecommendPropertyAsync(string city, int budget, string? rooms = null, string? district = null);
}

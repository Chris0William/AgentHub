using AgentHub.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace AgentHub.Infrastructure.AI.Plugins;

/// <summary>
/// 房产信息插件 - 提供楼盘搜索、价格查询、推荐等功能
/// </summary>
public class RealEstatePlugin
{
    private readonly IRealEstateService _realEstateService;
    private readonly ILogger<RealEstatePlugin> _logger;

    public RealEstatePlugin(
        IRealEstateService realEstateService,
        ILogger<RealEstatePlugin> logger)
    {
        _realEstateService = realEstateService;
        _logger = logger;
    }

    /// <summary>
    /// 搜索楼盘
    /// </summary>
    [KernelFunction, Description("搜索指定城市的楼盘信息，可按价格筛选。适合用户问'有哪些楼盘'、'XX楼盘怎么样'等问题")]
    public async Task<string> SearchProperty(
        [Description("城市名称，如：东莞、深圳、广州")] string city,
        [Description("关键词，如：楼盘名称、区域名。不确定可不填")] string? keyword = null,
        [Description("最低价格（万元），如：100、200。不限可不填")] int? minPrice = null,
        [Description("最高价格（万元），如：300、500。不限可不填")] int? maxPrice = null)
    {
        _logger.LogInformation($"【工具调用】SearchProperty: city={city}, keyword={keyword}, minPrice={minPrice}, maxPrice={maxPrice}");

        try
        {
            var result = await _realEstateService.SearchPropertyAsync(city, keyword, minPrice, maxPrice, 10);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "搜索楼盘失败");
            return $"搜索楼盘时出错: {ex.Message}";
        }
    }

    /// <summary>
    /// 查询房价趋势
    /// </summary>
    [KernelFunction, Description("查询指定城市或区域的房价走势和趋势。适合用户问'房价怎么样'、'房价是涨还是跌'等问题")]
    public async Task<string> GetPriceTrend(
        [Description("城市名称，如：东莞、北京")] string city,
        [Description("区域名称（可选），如：南城、松山湖")] string? district = null)
    {
        _logger.LogInformation($"【工具调用】GetPriceTrend: city={city}, district={district}");

        try
        {
            var result = await _realEstateService.GetPriceTrendAsync(city, district);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询房价趋势失败");
            return $"查询房价趋势时出错: {ex.Message}";
        }
    }

    /// <summary>
    /// 查询楼盘详情
    /// </summary>
    [KernelFunction, Description("查询具体楼盘的详细信息，包括均价、户型、地址等。适合用户问'XX楼盘的详细信息'")]
    public async Task<string> GetPropertyDetail(
        [Description("楼盘名称或ID，如：海逸豪庭、万科城")] string propertyName)
    {
        _logger.LogInformation($"【工具调用】GetPropertyDetail: propertyName={propertyName}");

        try
        {
            var result = await _realEstateService.GetPropertyDetailAsync(propertyName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询楼盘详情失败");
            return $"查询楼盘详情时出错: {ex.Message}";
        }
    }

    /// <summary>
    /// 推荐楼盘
    /// </summary>
    [KernelFunction, Description("根据预算、户型等需求推荐合适的楼盘。适合用户问'帮我推荐楼盘'、'XX万能买什么房'等问题")]
    public async Task<string> RecommendProperty(
        [Description("城市名称，如：东莞、深圳")] string city,
        [Description("购房预算（万元），如：200、300")] int budget,
        [Description("期望户型（可选），如：2室、3室、3室2厅")] string? rooms = null,
        [Description("意向区域（可选），如：南城、松山湖")] string? district = null)
    {
        _logger.LogInformation($"【工具调用】RecommendProperty: city={city}, budget={budget}, rooms={rooms}, district={district}");

        try
        {
            var result = await _realEstateService.RecommendPropertyAsync(city, budget, rooms, district);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "推荐楼盘失败");
            return $"推荐楼盘时出错: {ex.Message}";
        }
    }
}

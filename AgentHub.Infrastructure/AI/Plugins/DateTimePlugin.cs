using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Globalization;

namespace AgentHub.Infrastructure.AI.Plugins;

/// <summary>
/// 日期时间插件
/// 提供当前日期、时间等实时信息
/// </summary>
public class DateTimePlugin
{
    /// <summary>
    /// 获取当前日期时间
    /// 当用户询问「今天」「现在」「当前」的日期、时间、星期时,必须使用此函数
    /// </summary>
    [KernelFunction, Description("获取当前的日期和时间。当用户询问今天、现在的日期、时间、星期时使用此函数")]
    public string GetCurrentDateTime()
    {
        var now = DateTime.Now;
        var chineseCulture = new CultureInfo("zh-CN");

        // 获取中文星期
        var dayOfWeek = now.ToString("dddd", chineseCulture);

        // 构建详细信息
        var result = $"当前时间: {now:yyyy年MM月dd日 HH:mm:ss}\n";
        result += $"星期: {dayOfWeek}\n";
        result += $"农历信息: 请使用ConvertToLunarDate函数获取详细农历信息";

        return result;
    }

    /// <summary>
    /// 获取当前日期（简洁版）
    /// </summary>
    [KernelFunction, Description("获取今天的日期（年月日）。当用户只询问日期时使用")]
    public string GetToday()
    {
        var now = DateTime.Now;
        var chineseCulture = new CultureInfo("zh-CN");
        var dayOfWeek = now.ToString("dddd", chineseCulture);

        return $"{now:yyyy年MM月dd日} {dayOfWeek}";
    }

    /// <summary>
    /// 获取当前时间
    /// </summary>
    [KernelFunction, Description("获取当前时间（时分秒）")]
    public string GetCurrentTime()
    {
        return DateTime.Now.ToString("HH:mm:ss");
    }

    /// <summary>
    /// 获取当前星期
    /// </summary>
    [KernelFunction, Description("获取今天是星期几")]
    public string GetDayOfWeek()
    {
        var chineseCulture = new CultureInfo("zh-CN");
        return DateTime.Now.ToString("dddd", chineseCulture);
    }
}

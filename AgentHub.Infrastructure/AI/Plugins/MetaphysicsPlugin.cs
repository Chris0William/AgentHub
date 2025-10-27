using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Globalization;
using AgentHub.Infrastructure.ExternalApis;

namespace AgentHub.Infrastructure.AI.Plugins;

/// <summary>
/// 玄学命理插件
/// </summary>
public class MetaphysicsPlugin
{
    private readonly IJuheCalendarService _juheCalendarService;
    private readonly IJuheHoroscopeService _juheHoroscopeService;

    public MetaphysicsPlugin(
        IJuheCalendarService juheCalendarService,
        IJuheHoroscopeService juheHoroscopeService)
    {
        _juheCalendarService = juheCalendarService;
        _juheHoroscopeService = juheHoroscopeService;
    }
    /// <summary>
    /// 计算生肖
    /// </summary>
    [KernelFunction, Description("根据出生年份计算生肖")]
    public string GetChineseZodiac(
        [Description("出生年份,例如:1990")] int year)
    {
        Console.WriteLine($"【工具调用】GetChineseZodiac 被调用, year={year}");
        var zodiacs = new[] { "猴", "鸡", "狗", "猪", "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊" };
        var index = year % 12;
        var zodiac = zodiacs[index];

        return $"{year}年出生的人属{zodiac}。";
    }

    /// <summary>
    /// 计算星座
    /// </summary>
    [KernelFunction, Description("根据出生月日计算星座")]
    public string GetConstellation(
        [Description("出生月份(1-12)")] int month,
        [Description("出生日期(1-31)")] int day)
    {
        Console.WriteLine($"【工具调用】GetConstellation 被调用, month={month}, day={day}");
        var constellations = new[]
        {
            "摩羯座", "水瓶座", "双鱼座", "白羊座", "金牛座", "双子座",
            "巨蟹座", "狮子座", "处女座", "天秤座", "天蝎座", "射手座"
        };

        var dates = new[] { 20, 19, 21, 20, 21, 22, 23, 23, 23, 24, 23, 22 };

        var constellation = day < dates[month - 1]
            ? constellations[month - 1]
            : constellations[month % 12];

        return $"{month}月{day}日出生的人是{constellation}。";
    }

    /// <summary>
    /// 五行查询
    /// </summary>
    [KernelFunction, Description("查询天干地支对应的五行属性")]
    public string GetFiveElements(
        [Description("天干地支字符,如:甲、子等")] string character)
    {
        var elements = new Dictionary<string, string>
        {
            // 天干
            { "甲", "木" }, { "乙", "木" },
            { "丙", "火" }, { "丁", "火" },
            { "戊", "土" }, { "己", "土" },
            { "庚", "金" }, { "辛", "金" },
            { "壬", "水" }, { "癸", "水" },
            // 地支
            { "子", "水" }, { "亥", "水" },
            { "寅", "木" }, { "卯", "木" },
            { "巳", "火" }, { "午", "火" },
            { "申", "金" }, { "酉", "金" },
            { "辰", "土" }, { "戌", "土" }, { "丑", "土" }, { "未", "土" }
        };

        if (elements.TryGetValue(character, out var element))
        {
            return $"{character}属{element}";
        }

        return $"未找到'{character}'对应的五行属性。请确认输入的是天干地支字符。";
    }

    /// <summary>
    /// 天干地支纪年
    /// </summary>
    [KernelFunction, Description("将公历年份转换为天干地支纪年")]
    public string GetGanZhiYear(
        [Description("公历年份,例如:2024")] int year)
    {
        var heavenlyStems = new[] { "庚", "辛", "壬", "癸", "甲", "乙", "丙", "丁", "戊", "己" };
        var earthlyBranches = new[] { "申", "酉", "戌", "亥", "子", "丑", "寅", "卯", "辰", "巳", "午", "未" };

        var stemIndex = year % 10;
        var branchIndex = year % 12;

        var ganZhi = $"{heavenlyStems[stemIndex]}{earthlyBranches[branchIndex]}";

        return $"{year}年是{ganZhi}年。";
    }

    /// <summary>
    /// 生命灵数计算
    /// </summary>
    [KernelFunction, Description("根据出生日期计算生命灵数")]
    public string GetLifePathNumber(
        [Description("出生年份")] int year,
        [Description("出生月份")] int month,
        [Description("出生日期")] int day)
    {
        // 将年月日的所有数字相加
        var sum = 0;
        sum += SumDigits(year);
        sum += SumDigits(month);
        sum += SumDigits(day);

        // 继续相加直到得到单个数字(除了11、22、33这些大师数)
        while (sum > 9 && sum != 11 && sum != 22 && sum != 33)
        {
            sum = SumDigits(sum);
        }

        var descriptions = new Dictionary<int, string>
        {
            { 1, "领导者,独立自主,开拓创新" },
            { 2, "合作者,敏感细腻,善于协调" },
            { 3, "创造者,乐观开朗,富有表达力" },
            { 4, "建设者,务实稳重,注重细节" },
            { 5, "自由者,灵活多变,喜欢冒险" },
            { 6, "关怀者,责任心强,注重和谐" },
            { 7, "探索者,深思熟虑,追求真理" },
            { 8, "实干家,目标明确,追求成功" },
            { 9, "人道主义者,慷慨大方,胸怀宽广" },
            { 11, "大师数,直觉敏锐,精神导师" },
            { 22, "大师数,伟大建设者,实现梦想" },
            { 33, "大师数,大爱无疆,奉献精神" }
        };

        var description = descriptions.ContainsKey(sum) ? descriptions[sum] : "未知";

        return $"{year}年{month}月{day}日出生的生命灵数是{sum},特质:{description}";
    }

    private int SumDigits(int number)
    {
        var sum = 0;
        while (number > 0)
        {
            sum += number % 10;
            number /= 10;
        }
        return sum;
    }

    /// <summary>
    /// 公历转农历
    /// </summary>
    [KernelFunction, Description("将公历日期转换为农历日期")]
    public string ConvertToLunarDate(
        [Description("公历年份,例如:1995")] int year,
        [Description("公历月份(1-12)")] int month,
        [Description("公历日期(1-31)")] int day)
    {
        Console.WriteLine($"【工具调用】ConvertToLunarDate 被调用, year={year}, month={month}, day={day}");

        try
        {
            var solarDate = new DateTime(year, month, day);
            var chineseCalendar = new ChineseLunisolarCalendar();

            // 获取农历年月日
            var lunarYear = chineseCalendar.GetYear(solarDate);
            var lunarMonth = chineseCalendar.GetMonth(solarDate);
            var lunarDay = chineseCalendar.GetDayOfMonth(solarDate);

            // 判断是否闰月
            // ChineseLunisolarCalendar的GetMonth已经返回正确的月份编号
            // 闰月会返回比实际月份大1的值,需要特别处理
            var leapMonth = chineseCalendar.GetLeapMonth(lunarYear);
            var isLeapMonth = false;
            var actualMonth = lunarMonth;

            // 如果当前月份大于闰月编号,说明已经过了闰月,需要减1
            if (leapMonth > 0 && lunarMonth > leapMonth)
            {
                actualMonth = lunarMonth - 1;
            }
            // 如果当前月份等于闰月编号,说明这是闰月本身
            else if (leapMonth > 0 && lunarMonth == leapMonth)
            {
                isLeapMonth = true;
                actualMonth = leapMonth - 1; // 闰月的月份名称是前一个月
            }

            // 转换月份和日期为中文
            var lunarMonthStr = GetLunarMonthName(actualMonth, isLeapMonth);
            var lunarDayStr = GetLunarDayName(lunarDay);

            // 获取天干地支年份
            var ganZhiYear = GetGanZhiYearForLunar(lunarYear);

            return $"公历{year}年{month}月{day}日对应农历{lunarYear}年{lunarMonthStr}{lunarDayStr}({ganZhiYear}年)";
        }
        catch (Exception ex)
        {
            return $"日期转换失败:{ex.Message}。请确认输入的是有效的公历日期。";
        }
    }

    /// <summary>
    /// 获取农历月份名称
    /// </summary>
    private string GetLunarMonthName(int month, bool isLeapMonth)
    {
        var monthNames = new[] { "正月", "二月", "三月", "四月", "五月", "六月",
                                "七月", "八月", "九月", "十月", "冬月", "腊月" };
        var monthName = monthNames[month - 1];
        return isLeapMonth ? $"闰{monthName}" : monthName;
    }

    /// <summary>
    /// 获取农历日期名称
    /// </summary>
    private string GetLunarDayName(int day)
    {
        var dayNames = new[]
        {
            "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
            "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
            "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
        };
        return dayNames[day - 1];
    }

    /// <summary>
    /// 获取农历年份的天干地支
    /// </summary>
    private string GetGanZhiYearForLunar(int lunarYear)
    {
        var heavenlyStems = new[] { "庚", "辛", "壬", "癸", "甲", "乙", "丙", "丁", "戊", "己" };
        var earthlyBranches = new[] { "申", "酉", "戌", "亥", "子", "丑", "寅", "卯", "辰", "巳", "午", "未" };

        var stemIndex = lunarYear % 10;
        var branchIndex = lunarYear % 12;

        return $"{heavenlyStems[stemIndex]}{earthlyBranches[branchIndex]}";
    }

    /// <summary>
    /// 农历转公历
    /// </summary>
    [KernelFunction, Description("将农历日期转换为公历日期")]
    public string ConvertToSolarDate(
        [Description("农历年份,例如:1995")] int lunarYear,
        [Description("农历月份(1-12)")] int lunarMonth,
        [Description("农历日期(1-30)")] int lunarDay)
    {
        Console.WriteLine($"【工具调用】ConvertToSolarDate 被调用, lunarYear={lunarYear}, lunarMonth={lunarMonth}, lunarDay={lunarDay}");

        try
        {
            var chineseCalendar = new ChineseLunisolarCalendar();

            // 农历转公历：使用ToDateTime方法
            var solarDate = chineseCalendar.ToDateTime(lunarYear, lunarMonth, lunarDay, 0, 0, 0, 0);

            // 获取天干地支年份
            var ganZhiYear = GetGanZhiYearForLunar(lunarYear);

            return $"农历{lunarYear}年{lunarMonth}月{lunarDay}日对应公历{solarDate.Year}年{solarDate.Month}月{solarDate.Day}日({ganZhiYear}年)";
        }
        catch (Exception ex)
        {
            return $"日期转换失败:{ex.Message}。请确认输入的是有效的农历日期。";
        }
    }

    /// <summary>
    /// 获取今日宜忌(调用聚合数据API)
    /// </summary>
    [KernelFunction, Description("获取今日宜忌事项")]
    public async Task<string> GetTodayTaboos()
    {
        Console.WriteLine("【工具调用】GetTodayTaboos 函数被调用");
        return await _juheCalendarService.GetTodayTaboosAsync();
    }

    /// <summary>
    /// 查询星座运势(调用聚合数据API)
    /// </summary>
    [KernelFunction, Description("查询星座运势，支持今日、明日、本周、本月、本年运势")]
    public async Task<string> GetHoroscope(
        [Description("星座名称，如：白羊座、金牛座、双子座、巨蟹座、狮子座、处女座、天秤座、天蝎座、射手座、摩羯座、水瓶座、双鱼座")] string constellation,
        [Description("运势类型：today-今日，tomorrow-明日，week-本周，month-本月，year-本年，默认为today")] string type = "today")
    {
        Console.WriteLine($"【工具调用】GetHoroscope 被调用, constellation={constellation}, type={type}");
        return await _juheHoroscopeService.GetHoroscopeAsync(constellation, type);
    }
}

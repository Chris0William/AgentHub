using AgentHub.Core.Domain.Enums;
using System.Text.Json;

namespace AgentHub.Core.Domain.Models;

/// <summary>
/// 用户档案实体
/// </summary>
public class UserProfile
{
    /// <summary>
    /// 档案ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public long UserId { get; set; }

    // 基础信息
    /// <summary>
    /// 真实姓名
    /// </summary>
    public string? FullName { get; set; }

    /// <summary>
    /// 性别
    /// </summary>
    public Gender? Gender { get; set; }

    // 出生信息
    /// <summary>
    /// 出生时间(公历)
    /// </summary>
    public DateTime? BirthDateTime { get; set; }

    /// <summary>
    /// 出生时间(农历)
    /// </summary>
    public string? BirthLunar { get; set; }

    /// <summary>
    /// 出生省份
    /// </summary>
    public string? BirthProvince { get; set; }

    /// <summary>
    /// 出生城市
    /// </summary>
    public string? BirthCity { get; set; }

    /// <summary>
    /// 出生地经度
    /// </summary>
    public decimal? BirthLongitude { get; set; }

    /// <summary>
    /// 出生地纬度
    /// </summary>
    public decimal? BirthLatitude { get; set; }

    /// <summary>
    /// 是否使用真太阳时
    /// </summary>
    public bool UseSolarTime { get; set; } = false;

    // 扩展信息
    /// <summary>
    /// 婚姻状况
    /// </summary>
    public MaritalStatus? MaritalStatus { get; set; }

    /// <summary>
    /// 职业
    /// </summary>
    public string? Occupation { get; set; }

    /// <summary>
    /// 学历
    /// </summary>
    public string? Education { get; set; }

    /// <summary>
    /// 关注领域(JSON)
    /// </summary>
    public string? FocusAreasJson { get; set; }

    /// <summary>
    /// 重要事件(JSON)
    /// </summary>
    public string? ImportantEventsJson { get; set; }

    // 命理档案
    /// <summary>
    /// 八字信息(JSON)
    /// </summary>
    public string? BaziInfoJson { get; set; }

    /// <summary>
    /// 紫微信息(JSON)
    /// </summary>
    public string? ZiweiInfoJson { get; set; }

    /// <summary>
    /// 占星信息(JSON)
    /// </summary>
    public string? AstroInfoJson { get; set; }

    /// <summary>
    /// 生命灵数
    /// </summary>
    public int? LifePathNumber { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // 导航属性
    /// <summary>
    /// 关联的用户
    /// </summary>
    public User User { get; set; } = null!;

    // 辅助属性(不映射到数据库)
    /// <summary>
    /// 关注领域列表
    /// </summary>
    public List<string> FocusAreas
    {
        get => string.IsNullOrEmpty(FocusAreasJson)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(FocusAreasJson) ?? new List<string>();
        set => FocusAreasJson = JsonSerializer.Serialize(value);
    }
}

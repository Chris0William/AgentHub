# 房产API接入指南

## 📋 已完成的接入工作

### 1. 创建的文件

```
src/AgentHub.Core/Interfaces/IRealEstateService.cs              # 房产服务接口
src/AgentHub.Infrastructure/ExternalApis/RealEstateService.cs   # 房产服务实现（基于SearXNG搜索）
src/AgentHub.Infrastructure/AI/Plugins/RealEstatePlugin.cs      # Semantic Kernel插件
```

### 2. 修改的文件

- `src/AgentHub.API/Program.cs` - 注册房产服务
- `src/AgentHub.Infrastructure/AI/SemanticKernelService.cs` - 注册房产插件

### 3. 当前实现方案

**数据源**：基于 SearXNG 网络搜索
**优势**：
- ✅ 快速接入，无需额外API密钥
- ✅ 数据免费，无配额限制
- ✅ 支持实时信息
- ✅ 6小时缓存机制，提高性能

**限制**：
- ⚠️ 数据准确性依赖搜索引擎质量
- ⚠️ 无法获取结构化楼盘数据
- ⚠️ 不支持高级筛选（如精确户型、朝向等）

---

## 🧪 测试房产功能

### 启动服务

```bash
cd src/AgentHub.API
dotnet run
```

### 测试用例

通过前端或API测试以下问题：

1. **搜索楼盘**
   - "东莞有哪些在售楼盘？"
   - "深圳200-300万的楼盘有哪些？"
   - "东莞松山湖附近的楼盘"

2. **查询房价**
   - "东莞房价怎么样？"
   - "深圳南山区房价走势"

3. **楼盘详情**
   - "海逸豪庭楼盘详情"
   - "万科城的均价和户型"

4. **推荐楼盘**
   - "我预算200万，帮我推荐东莞的楼盘"
   - "预算300万，想在深圳买3室的房子"

---

## 🚀 升级方案：接入官方API

### 方案一：贝壳开放平台（推荐）

**官网**：https://open.ke.com/

**步骤**：

1. **注册开发者账号**
   - 访问贝壳开放平台
   - 企业认证（需要营业执照）
   - 申请API权限

2. **获取API密钥**
   ```json
   // appsettings.json
   {
     "ExternalApis": {
       "RealEstate": {
         "Provider": "Beike",
         "ApiKey": "your-beike-api-key",
         "BaseUrl": "https://open.ke.com/api/v1"
       }
     }
   }
   ```

3. **创建贝壳服务实现**
   ```bash
   # 创建新文件
   src/AgentHub.Infrastructure/ExternalApis/BeikeRealEstateService.cs
   ```

4. **实现示例代码**
   ```csharp
   public class BeikeRealEstateService : IRealEstateService
   {
       private readonly HttpClient _httpClient;
       private readonly string _apiKey;
       private readonly string _baseUrl;

       public async Task<string> SearchPropertyAsync(...)
       {
           var url = $"{_baseUrl}/property/search?city={city}&keyword={keyword}";
           var response = await _httpClient.GetAsync(url);
           // 解析贝壳API响应...
       }
   }
   ```

5. **修改服务注册**
   ```csharp
   // Program.cs
   var realEstateProvider = configuration["ExternalApis:RealEstate:Provider"] ?? "SearXNG";

   builder.Services.AddScoped<IRealEstateService>(sp =>
   {
       return realEstateProvider switch
       {
           "Beike" => new BeikeRealEstateService(...),
           "YunFang" => new YunFangRealEstateService(...),
           _ => new RealEstateService(...) // 默认SearXNG
       };
   });
   ```

### 方案二：云房数据API

**官网**：https://www.yunfangdata.com/dataapi.html

**特点**：
- 覆盖287个城市
- 提供新房、二手房、租房数据
- 按API调用次数收费

**接入步骤**：
1. 注册账号并充值
2. 获取API密钥
3. 创建 `YunFangRealEstateService.cs`
4. 参考API文档实现接口

### 方案三：聚合数据（简单快速）

**官网**：https://www.juhe.cn/

**接入代码示例**：

```csharp
public class JuheRealEstateService : IRealEstateService
{
    private const string API_BASE = "http://apis.juhe.cn/fapig";
    private readonly string _apiKey;

    public async Task<string> GetPriceTrendAsync(string city, string? district)
    {
        var url = $"{API_BASE}/query?key={_apiKey}&city={city}";
        var response = await _httpClient.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();

        // 解析JSON并格式化
        return FormatPriceTrend(json);
    }
}
```

---

## 🛠️ 升级为爬虫方案（免费但需维护）

### 创建安居客爬虫服务

```csharp
public class AnjukeScraperService : IRealEstateService
{
    public async Task<string> SearchPropertyAsync(string city, ...)
    {
        // 1. 构建安居客URL
        var url = $"https://m.anjuke.com/xinfang/{cityCode}/loupan/";

        // 2. 使用HtmlAgilityPack解析HTML
        var web = new HtmlWeb();
        var doc = await web.LoadFromWebAsync(url);

        // 3. 提取楼盘信息
        var properties = doc.DocumentNode
            .SelectNodes("//div[@class='lp-item']")
            .Select(node => new {
                Name = node.SelectSingleNode(".//h3").InnerText,
                Price = node.SelectSingleNode(".//span[@class='price']").InnerText,
                Address = node.SelectSingleNode(".//p[@class='address']").InnerText
            });

        // 4. 格式化返回
        return FormatProperties(properties);
    }
}
```

**所需NuGet包**：
```bash
dotnet add package HtmlAgilityPack
```

**优势**：
- 免费
- 数据实时
- 自主可控

**劣势**：
- 需要处理反爬虫（IP限制、验证码）
- HTML结构变化需要维护
- 法律风险（需遵守robots.txt）

---

## 📊 各方案对比

| 方案 | 成本 | 数据质量 | 接入难度 | 稳定性 | 推荐度 |
|------|------|----------|----------|--------|--------|
| SearXNG搜索（当前） | 免费 | ⭐⭐⭐ | ⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| 贝壳开放平台 | 付费 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| 云房数据 | 付费 | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| 聚合数据 | 付费 | ⭐⭐⭐ | ⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| 安居客爬虫 | 免费 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐ |

---

## 🎯 推荐接入路径

### 阶段一：MVP验证（当前完成✅）
- 使用 SearXNG 搜索
- 快速上线，验证用户需求
- 收集用户反馈

### 阶段二：数据升级
- 根据用户反馈，评估是否需要更准确的数据
- **如果有预算**：接入贝壳开放平台或云房数据
- **如果没预算**：自建爬虫 + SearXNG组合

### 阶段三：功能增强
- 添加楼盘对比功能
- 风水分析（结合玄学能力）
- 购房建议生成

---

## 🔧 配置示例

### appsettings.json（完整配置）

```json
{
  "ExternalApis": {
    "SearchEngine": "SearXNG",
    "RealEstate": {
      "Provider": "SearXNG",  // 可选: SearXNG, Beike, YunFang, Juhe, Anjuke
      "ApiKey": "",           // 官方API需要
      "BaseUrl": "",          // 官方API需要
      "CacheDuration": 6      // 缓存小时数
    },
    "SearXNG": {
      "BaseUrl": "http://localhost:8080"
    }
  }
}
```

---

## 📝 AI提示词优化

为了让AI更好地使用房产工具，已在 `ChatController.cs` 的系统提示词中添加：

```
可用的房产工具：
1. SearchProperty - 搜索楼盘
2. GetPriceTrend - 查询房价走势
3. GetPropertyDetail - 查询楼盘详情
4. RecommendProperty - 推荐楼盘

使用时机：
- 用户问"有哪些楼盘"时使用 SearchProperty
- 用户问"房价怎么样"时使用 GetPriceTrend
- 用户问"XX楼盘详情"时使用 GetPropertyDetail
- 用户问"帮我推荐"时使用 RecommendProperty
```

---

## ⚠️ 注意事项

1. **遵守法律法规**
   - 使用爬虫时遵守robots.txt
   - 不要频繁请求，设置合理延迟
   - 商业使用请获取授权

2. **数据准确性声明**
   - 向用户明确数据来源
   - 建议用户实地考察
   - 免责声明必不可少

3. **隐私保护**
   - 不存储用户搜索记录
   - 敏感信息加密传输

---

## 📞 联系方式

如需进一步咨询房产API接入方案，可联系：
- 贝壳开放平台：https://open.ke.com/
- 云房数据：https://www.yunfangdata.com/
- 聚合数据：https://www.juhe.cn/

---

## 🎉 总结

房产API功能已成功接入，当前基于SearXNG搜索实现。您可以：

1. **立即测试**：通过玄学Agent测试房产查询功能
2. **评估效果**：收集用户反馈，决定是否升级
3. **按需升级**：根据本文档选择合适的API方案

祝您项目顺利！🚀

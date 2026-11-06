# 绵绵 AI 桌宠 v2.0.5

面向 Windows 10/11 的 .NET 10 WPF 私人 AI 桌宠。v2.0.5 增加摸摸头互动和生日、纪念日小纸条，让白粉侧视小羊陪你自然地聊几句。

## 开发环境

- Windows 10 22H2 或 Windows 11（x64）
- .NET SDK 10.0.302 或兼容的 10.0.x SDK
- VS Code
- C# Dev Kit 扩展

## 私人构建前的唯一配置

打开 `MianMianPet\Services\InternalAiConfig.cs`，只替换这一项：

```csharp
internal const string ApiKey = "PASTE_YOUR_VERIFIED_DEEPSEEK_API_KEY_HERE";
```

将其换成 DeepSeek API Key。Base URL、模型、输出长度、温度和上下文窗口集中在同一个内部配置类中。AI 默认回复 1～3 句话，输出上限为 480 token，最多带入最近 16 条成功消息（约 8 轮），并受 12,000 字符上下文预算限制。触及输出上限时自动接完一次，整次回复共用 2 分钟超时。

## 内置体验配置

主动陪伴、闲置睡眠、昼夜分段和人设化失败回复集中在：

```text
MianMianPet\Configuration\PetExperienceConfig.cs
```

## 配置生日与纪念日

首次启动后，会在 `%LOCALAPPDATA%\MianMianPet\celebrations.json` 生成配置。通常对应 `C:\Users\你的用户名\AppData\Local\MianMianPet\celebrations.json`。程序在启动时及运行期间每分钟读取一次，修改后最多约一分钟生效。

默认配置为 `[]`，所有日期和文案自行填写。下面用元旦和中秋节演示格式，可以按需要增删项目、修改日期和替换祝福。

```json
[
  {
    "id": "new-year",
    "calendar": "solar",
    "month": 1,
    "day": 1,
    "message": "新年快乐，{UserTitle}。\n\n{PetName}送你一朵新年的小云。♡"
  },
  {
    "id": "mid-autumn",
    "calendar": "lunar",
    "month": 8,
    "day": 15,
    "message": "{UserTitle}，中秋快乐。\n\n今晚的小云替你捧着一轮月亮。♡"
  }
]
```

| 字段 | 填写方式 |
| --- | --- |
| `id` | 每项唯一且稳定的标识，用于记录当年是否已拆开，例如 `birthday` |
| `calendar` | `solar` 表示公历，`lunar` 表示农历 |
| `month`、`day` | 数字月份和日期。正月为 `1`，冬月为 `11`，腊月为 `12` |
| `message` | 1～2,000 个字符的纸条。`{UserTitle}`、`{PetName}` 会替换为当前称呼和小羊名字，`\n` 表示换行 |

每项每年在电脑本地日期对应的当天出现一朵爱心小云，点开即可阅读。拆开记录保存到本机数据库，重启后当年仍会记得。相同日期可以配置多项，按配置顺序领取，下一项会在下一次分钟检查后出现。纸条可以离线打开。

农历按普通月份过生日，闰月跳过，月份编号仍填写 1～12。农历换算支持 1901～2100 农历年。公历 2 月 29 日只在闰年出现。程序在日期当天运行时触发，错过当天的纸条留到下一年。

将数组改为 `[]` 可以关闭所有纪念日。已拆开项目的日期或文案修改后，仍按原 `id` 保留当年领取记录。测试时可添加一个新的 `id`，将公历日期设为今天，测试后移除该项。配置格式错误时会暂停显示纪念日小云，并记录到同目录下的 `logs`，修正后下次检查恢复。

## 构建与运行

用 VS Code 打开包含 `MianMianPet.slnx` 的根目录，然后运行：

```powershell
.\build.ps1
dotnet run --project .\MianMianPet\MianMianPet.csproj
```

单击小羊会打开独立、可激活的输入窗口：

- `Enter`：发送
- `Shift + Enter`：换行
- `Esc`：关闭；如果 IME 正在组合，则先交给输入法处理

## 发布

```powershell
.\publish.ps1
```

输出位于 `publish\win-x64`，使用 self-contained x64 单文件发布。。

版本变化见 `CHANGELOG.md`。

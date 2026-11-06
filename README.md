# 绵绵 AI 桌宠 v2.0.4

面向 Windows 10/11 的 .NET 10 WPF 私人 AI 桌宠。v2.0.4 的目标不是功能堆叠，而是一只安静陪伴、自然互动、偶尔带来惊喜的白粉侧视小羊。

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

将其换成 DeepSeek API Key。Base URL、模型、输出长度、温度、上下文窗口和 2 秒气泡时间都集中在同一个内部配置类中，设置页面不会显示这些内容。

> 桌面程序中的内置 API Key 无法真正保密。请仅向自己或可信任对象分发包含真实 Key 的构建，不要将源代码或二进制公开发布。公开分发前应改用自建后端代理。

当前交付包没有包含真实 API Key，因为此前源码和需求附件均未提供该密钥；除此之外不需要首次运行配置。

## 内置体验配置

主动陪伴、闲置睡眠、昼夜分段和人设化失败回复集中在：

```text
MianMianPet\Configuration\PetExperienceConfig.cs
```

昼夜效果会根据电脑本地时间自动切换，不需要用户设置。

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
- 回复中点击停止按钮：取消当前请求

## 发布

```powershell
.\publish.ps1
```

输出位于 `publish\win-x64`，使用 self-contained x64 单文件发布。未启用裁剪，避免影响 WPF XAML 和 SQLite 运行时行为。

版本变化见 `CHANGELOG.md`。

# 绵绵 AI 桌宠

绵绵是一只 Windows 桌面小羊。点它可以聊天，空闲时它会散步或邀你捉迷藏。前台应用全屏时，绵绵会自动收起。

聊天时，`Enter` 发送，`Shift + Enter` 换行，`Esc` 关闭输入框。

## 开启聊天

首次启动会生成 `%LOCALAPPDATA%\MianMianPet\config.json`。把 DeepSeek API Key 填到 `ai.apiKey`，然后从托盘退出并重开绵绵。

```json
{
  "ai": {
    "apiKey": ""
  }
}
```

其余字段可以沿用默认值。配置有误时可查看同目录下的 `logs`。含有 Key 的配置文件请留在本机。

## 生日和纪念日

在同一目录下编辑 `celebrations.json`。默认内容是 `[]`，可以照下面的格式添加。

```json
[
  {
    "id": "birthday",
    "calendar": "solar",
    "month": 6,
    "day": 12,
    "message": "生日快乐，{UserTitle}！"
  }
]
```

`solar` 是公历，`lunar` 是农历（跳过闰月）。每项用不同的 `id`。程序每分钟检查一次，当天会弹出爱心小云，点开就能看到纸条。文案里可以用 `{UserTitle}` 和 `{PetName}` 代替称呼和名字。

## 构建和发布

需要 Windows 10 22H2 或 Windows 11（x64），以及 .NET 10.0.302 SDK（或同系列更新的补丁版本）。在项目根目录运行下面两条命令。

```powershell
.\build.ps1
dotnet run --project .\MianMianPet\MianMianPet.csproj
```

运行 `.\publish.ps1` 打包，单文件程序在 `publish\win-x64\MianMianPet.exe`。

运行 `dotnet run --project .\MianMianPet.Tests` 做回归检查，无需真实 API Key。测试会短暂切换到测试窗口，结果保存在 `TestResults`。

[更新记录](CHANGELOG.md)

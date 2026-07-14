# FastRead

FastRead 是一个 Windows 阅读辅助工具：在浏览器、PDF 阅读器、Word、记事本等应用中选中文字，按下全局快捷键，即可让大模型总结内容或给出详细解释。

## 功能

- `Ctrl+Alt+S`：使用简体中文总结选中的内容；
- `Ctrl+Alt+E`：使用简体中文详细解释选中的内容；
- 总结和解释可分别设置最大输出字符数；
- 输出语言可配置为“简体中文”或“English”，默认是简体中文；
- API 地址、模型、快捷键和超时均可配置；
- 支持 OpenAI Chat Completions 兼容接口；
- 支持流式输出，也兼容普通 JSON 响应；
- 结果可一键复制，请求可随时取消；
- 常驻系统托盘，支持随 Windows 启动；
- API Key 保存在 Windows 凭据管理器中，不写入配置文件；
- 不保存选中的原文、模型回答或阅读历史。

## 系统要求

- Windows 10 或 Windows 11；
- 直接构建运行需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)；
- 使用自包含发布版本时，目标电脑无需预装 .NET。

## 快速开始

在项目根目录执行：

```powershell
dotnet build FastRead.sln
dotnet run --project src/FastRead/FastRead.csproj
```

程序启动后会出现在系统托盘。首次运行若尚未保存 API Key，将自动打开设置窗口。

在设置中至少填写：

1. API 地址和模型已默认配置为 MiniMax-M3；
2. 只需填写 MiniMax API Key，保存后会进入 Windows 凭据管理器；
4. 点击“测试连接”，确认配置可用；
5. 点击“保存”。

随后在任意应用中选中文字：

- 按 `Ctrl+Alt+S` 获取摘要；
- 按 `Ctrl+Alt+E` 获取详细解释。

结果会在独立窗口中流式显示。再次触发任务会取消上一次仍在进行的请求。

## 设置说明

| 设置 | 默认值 | 说明 |
| --- | --- | --- |
| API 地址 | `https://api.minimaxi.com/v1/chat/completions` | MiniMax 国内 Chat Completions 接口 |
| 模型名称 | `MiniMax-M3` | 默认关闭思考输出，只展示最终结果 |
| 输出语言 | `简体中文` | 可切换为 `English`，语言要求会写入总结和解释 Prompt |
| 总结最大字符数 | 500 | 可设为 50～20,000 |
| 解释最大字符数 | 1,500 | 可设为 50～20,000 |
| 总结快捷键 | `Ctrl+Alt+S` | 必须包含至少一个修饰键 |
| 解释快捷键 | `Ctrl+Alt+E` | 不能与总结快捷键相同 |
| 请求超时 | 60 秒 | 可设为 5～300 秒 |
| 随 Windows 启动 | 关闭 | 只写入当前用户的启动项 |

输出字符数通过提示词和 `max_tokens` 共同约束，并在客户端达到上限时停止生成。模型分词和指令遵循能力不同，因此它是回答上限，而不是保证回答一定达到的长度。

出于安全考虑，远程服务必须使用 HTTPS。`http://localhost`、`127.0.0.1` 等回环地址允许使用 HTTP，方便连接本机模型代理。

## 接口兼容要求

服务需要接受以下结构的 `POST` 请求：

```json
{
  "model": "your-model",
  "messages": [
    { "role": "system", "content": "..." },
    { "role": "user", "content": "..." }
  ],
  "temperature": 0.2,
  "max_tokens": 1000,
  "stream": true
}
```

并支持其中一种响应：

- SSE：`data:` 行中的 `choices[0].delta.content`；
- 普通 JSON：`choices[0].message.content`。

若兼容服务错误地把 SSE 标记为 `application/json`，FastRead 也会根据响应内容自动识别。普通响应同时兼容字符串和文本块数组形式的 `message.content`。

认证头为 `Authorization: Bearer <API Key>`。如果本机兼容服务不检查密钥，也仍需在设置中填入任意非空值。

## 隐私与本地数据

- 非敏感设置：`%LOCALAPPDATA%\FastRead\settings.json`；
- API Key：Windows 凭据管理器，目标名 `FastRead/ApiKey`；
- 开机启动：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`；
- 原文和回答：只在内存中处理，不写日志或历史文件。

选中的文字会发送给你配置的模型服务。请根据内容敏感程度和服务商的数据政策决定是否使用。

## 工作原理与限制

Windows 没有读取任意应用当前选区的统一接口。FastRead 会暂存剪贴板、向前台应用发送 `Ctrl+C`、读取文本，再尽力恢复原剪贴板。

因此存在以下限制：

- 目标应用必须支持复制，受 DRM 保护的内容可能无法读取；
- FastRead 与目标应用的权限等级应相同。普通权限程序通常不能向管理员权限窗口发送输入；
- 复杂剪贴板格式由来源程序决定，FastRead 会尽力恢复，但无法保证所有自定义格式都可重建；
- 当前版本不支持截图 OCR；
- 当前版本仅支持 Windows。

## 常见问题

### 按快捷键没有结果

确认已经选中文字，并且在目标应用中手动按 `Ctrl+C` 可以复制。再检查系统托盘中是否存在 FastRead。如果启动时提示快捷键冲突，请在设置中换一个组合键。

### 提示认证失败

检查 API Key、完整 API 地址、模型名称和账户权限。API Key 输入框留空保存代表保留原密钥；使用“清除密钥”可从 Windows 凭据管理器删除它。

### 本机模型接口无法使用

确认地址包含完整路径，例如 `http://127.0.0.1:端口/v1/chat/completions`，并确认该服务兼容 Chat Completions 的请求和响应格式。

### 原剪贴板没有恢复

纯文本和常见格式通常可以恢复。某些应用提供的延迟渲染或私有剪贴板格式在被覆盖后无法重新取得，这是通用复制方案的系统限制。

## 开发与测试

项目使用 .NET 8 Windows Forms，不依赖第三方 NuGet 包。

```powershell
# Debug 构建
dotnet build FastRead.sln -c Debug

# Release 构建
dotnet build FastRead.sln -c Release

# 核心逻辑测试
dotnet run --project tests/FastRead.Tests/FastRead.Tests.csproj -c Release
```

测试覆盖快捷键解析、配置校验、提示词、SSE 增量响应、普通 JSON 响应和错误解析。剪贴板、全局快捷键与系统托盘属于 Windows 集成功能，需要按照[开发文档](开发文档.md)中的清单手工验收。

## 发布

生成 Windows x64 自包含单文件：

```powershell
dotnet publish src/FastRead/FastRead.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o publish/win-x64
```

生成结果位于 `publish/win-x64/FastRead.exe`。

## 文档

- [设计文档](设计文档.md)：产品范围、交互、架构、安全和验收标准；
- [开发文档](开发文档.md)：目录、实现约定、测试策略和发布清单。

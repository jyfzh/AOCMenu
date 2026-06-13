<div align="center">
  <img src="docs/images/aocmenu.png" alt="AOCMenu 海报" width="40%" />
</div>

# AOCMenu

AOC 显示器 **USB 私有协议** OSD 控制工具，提供命令行（CLI）和图形界面（WinUI 3）两种操作方式。
仅支持通过 USB 私有协议（非 DDC/CI）操作的显示器硬件设置。

---

## 目录

- [构建](#构建)
- [技术栈](#技术栈)
- [架构 → docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md)
- [用户手册 → docs/GUIDE.md](./docs/GUIDE.md)

---

## 构建

```bash
# 还原项目
dotnet restore

# 构建 CLI
dotnet build src/AOC/aoc.csproj -c Release

# 构建 WinUI GUI
dotnet build src/AOC.UI/AOC.UI.csproj -c Release

# 运行测试
dotnet test tests/aoc.Tests/aoc.Tests.csproj
```

---

## 技术栈

| 层 | 技术 |
|---|---|
| CLI | .NET 10, MSBuild, x64 |
| GUI | WinUI 3, CommunityToolkit.Mvvm, CommunityToolkit.WinUI, H.NotifyIcon |
| IPC | JSON-RPC over Named Pipe (System.IO.Pipes) |
| 代理 | .NET 10, x86 (32-bit), Zeasn SDK 反射调用 |
| 测试 | xUnit, 单元测试覆盖 CLI 解析/服务/基础设施 |

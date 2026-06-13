# AOCMenu 架构

---

## 架构概览

```
AOCMenu (64-bit CLI)
  │  JSON-RPC over Named Pipe
  ▼
ZeasnProxy.exe (32-bit host)
  │  Reflection over Zeasn SDK
  ▼
AOCOper (Zeasn.Equipment.Base.Lib.dll)
  │  Internal routing via USB HID Vendor Protocol
  ├─ Zeasn.USB.ENE.Lib.dll
  ├─ Zeasn.USB.SunPlus.Lib.dll
  ├─ Zeasn.USB.IOne.Lib.dll
  ├─ Zeasn.USB.BeiYing.Lib.dll
  └─ Zeasn.USB.CmediaSDK.Lib.dll
  ▼
AOC Monitor (USB upstream connection required)
```

---

## 架构细节

### IPC 通讯协议

CLI/GUI 与 ZeasnProxy 之间通过 **命名管道（Named Pipe）** 进行 **JSON-RPC** 通讯：

```
请求: {"method":"Call","params":["SetGamma","2"],"id":1}
响应: {"jsonrpc":"2.0","result":"ok","id":1}

请求: {"method":"Ok","params":["SetGamma","2"],"id":2}
响应: {"jsonrpc":"2.0","result":{"success":true},"id":2}
```

支持三种 RPC 方法：

| 方法 | 说明 |
|---|---|
| `Call` | 调用 SDK 方法并返回原始结果（JSON 元素） |
| `Ok` | 调用 SDK 方法并返回成功/失败状态 |
| `TryInitialize` | 初始化 SDK（加载 AOCOper DLL，连接显示器） |
| `Shutdown` | 关闭代理进程 |

### 代理进程生命周期

```
第一次调用:
  1. AOCMenu/AOC.UI 启动
  2. 检查 "ZeasnProxy_Running" EventWaitHandle
  3. 未运行 → 启动 ZeasnProxy.exe
  4. 等待代理就绪信号 ("Proxy started, pipe=..., pid=N")
  5. 通过命名管道发送 TryInitialize（加载 SDK）
  6. 执行设置操作

后续调用:
  1. 检测到 ZeasnProxy 已在运行
  2. 直接连接已有管道
  3. 快速执行操作（已初始化，跳过 SDK 加载时间）

自动退出:
  ZeasnProxy 内置 30 秒空闲超时，无操作后自动退出
```

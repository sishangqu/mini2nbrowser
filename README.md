<div align="center">

# 2ⁿ Browser

**一个用 C# / WPF / WebView2 打造的极简、快速、可扩展的桌面浏览器**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-Desktop-512BD4?logo=windows&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![WebView2](https://img.shields.io/badge/WebView2-1.0.4129-0078D4?logo=microsoftedge&logoColor=white)](https://learn.microsoft.com/microsoft-edge/webview2/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4?logo=windows&logoColor=white)](#)
[![License](https://img.shields.io/badge/License-MIT-success.svg)](#)

**v1.1.0** · 多标签 · 无痕浏览 · 扩展支持 · 油猴脚本 · 双主题 · 密码管理 · 系统托盘

</div>

---

## ✨ 特性一览

### 🚀 极速体验
- **分层编译**（Tiered Compilation）+ **ReadyToRun** 预编译，毫秒级冷启动
- **ProfileOptimization** 记录热点方法，二次启动并行编译
- **内嵌主页**（HomePage.html 作为 EmbeddedResource，零 IO 加载）
- **首个标签页延迟到 ApplicationIdle 创建**，窗口先渲染再加载内容
- **内存优化**：30 秒定时器 + 最小化时激进 GC + SetProcessWorkingSetSize

### 🗂 标签页管理
- 多标签 WebView2 实例，独立用户数据隔离
- 左侧栏悬停展开（36 → 180px 动画过渡）
- 中键关闭、Ctrl+T 新建、Ctrl+W 关闭
- 标签页显示网站图标 + 标题

### 🕵️ 无痕浏览
- 基于 `CoreWebView2CreationProperties.IsInPrivateModeEnabled` 的真无痕模式
- 无痕标签页：不记录历史、不保存密码、不自动填充、不加载扩展
- 无痕窗口的新窗口请求自动继承无痕状态
- URL 栏紫色无痕图标 + 标题 `[无痕]` 前缀
- 快捷键 `Ctrl+Shift+N`

### 🧩 扩展系统（v1.1 新增）
- **CRX 文件导入**：自动解析 CRX v2/v3 头部格式，剥离签名后解压
- **解压文件夹导入**：直接加载含 `manifest.json` 的扩展目录
- **自动修复**：`_metadata` → `metadata` 重命名（WebView2 要求）
- **扩展管理 UI**：启用/禁用开关、删除（含磁盘清理）、图标显示
- **配置持久化**：`extensions.json` 记录所有扩展，重启自动加载
- **版本检测**：WebView2 Runtime ≥ 1.0.2045 才启用，低版本友好提示
- **安全提示**：界面明确标注"仅导入可信来源扩展"

### 📝 油猴脚本
- 自定义 JavaScript 脚本，URL 匹配规则自动注入
- 支持正则/关键字匹配，编辑器内嵌
- 配置持久化到 `scripts.json`

### 🔒 智能防护
- 内置 30+ 广告/追踪域名拦截（返回 204 空响应）
- 拦截列表包含：doubleclick、googlesyndication、google-analytics、facebook 等主流广告网络

### 🔑 密码管理
- **DPAPI 加密存储**（Windows 用户级，跨设备不可解密）
- 自动捕获表单提交的账号密码
- 自动填充已保存的登录信息
- 可视化管理面板

### 🌐 导航与搜索
- 智能地址解析：URL / 域名 / 搜索词自动识别
- 预置 Bing / 百度 / Google，可自定义搜索引擎
- 书签管理（JSON 持久化，浮动面板）
- 历史记录（最多 500 条，可搜索过滤）

### 📥 下载管理
- 实时进度、速度、剩余时间
- 角标提示活跃下载数
- Toast 通知下载完成

### 🎨 双主题
- 深色 / 浅色主题一键切换
- 同步切换：窗口、托盘图标、WebView2 PreferredColorScheme

### 🪟 系统托盘
- 关闭窗口最小化到托盘，进程驻留保活 WebView2（热启动毫秒级）
- 托盘菜单：显示浏览器 / 完全退出
- **单实例热启动**：再次双击 exe 通过命名管道唤醒已有窗口

### 🪟 自定义窗口
- 无边框自绘标题栏，整行可拖动
- Win11 圆角（DWM `DWMWA_WINDOW_CORNER_PREFERENCE`）
- 双击标题栏最大化/还原
- 最大化状态下拖动自动还原并跟随光标

### 👥 多 Profile 隔离（v1.1 新增）
- 命令行 `--profile <name>` 启动独立实例
- 数据完全隔离：配置、历史、书签、密码、扩展、Cookie
- 独立托盘图标（提示中显示 profile 名）
- 同 profile 单实例热启动，不同 profile 可并存

---

## 📦 下载与安装

### 运行环境
- **Windows 10 1809+** / Windows 11
- **Microsoft Edge WebView2 Runtime**（[下载](https://developer.microsoft.com/microsoft-edge/webview2/)）

### 三种发行版

| 版本 | 大小 | 说明 | 适合场景 |
|---|---|---|---|
| **框架依赖版** (exe) | ~5 MB | 单文件 exe，需目标机已安装 .NET 8 Desktop Runtime | 体积小，多机共用 |
| **独立运行时版** (exe) | ~75 MB | 单文件 exe，内置 .NET 8 运行时，免安装 | 单机部署，开箱即用 |
| **框架依赖版** (zip) | ~5 MB | 压缩包，解压即用，需 .NET 8 Desktop Runtime | 绿色便携，无需安装 |

> 💡 **关于 zip 版本**：当前的 zip 版本是绿色解压即用形态，**未来计划演化为标准安装版**（带安装向导、注册表关联、开始菜单快捷方式等）。现阶段为保持便携性，仍以 zip 形式分发。

### 启动方式

```bash
# 默认实例（数据存 exe 同目录）
mini2nbrowser.exe

# 独立 profile（数据存 exe 目录 \Profiles\work\）
mini2nbrowser.exe --profile work

# 简写
mini2nbrowser.exe -p personal
```

---

## 🛠 开发

### 技术栈
- **C# 12** / **.NET 8** / **WPF**
- **WebView2** 1.0.4129（基于 Chromium 内核）
- **Hardcodet.NotifyIcon.Wpf**（系统托盘）

### 项目结构

```
mini2nbrowser/
├── App.xaml(.cs)              # 单实例 + 命名管道热启动 + 多 profile
├── MainWindow.xaml(.cs)       # 主界面 + 标签页 + 所有浏览器功能
├── ExtensionsManager.cs        # 扩展管理（导入/加载/删除/持久化）
├── ExtensionsWindow.xaml(.cs)  # 扩展管理 UI
├── CrxLoader.cs                # CRX v2/v3 解析与解压
├── ExtensionInfo.cs            # 扩展数据模型
├── DarkTheme.xaml              # 深色主题
├── LightTheme.xaml             # 浅色主题
├── HomePage.html               # 内嵌新标签页（EmbeddedResource）
├── app.ico / app_dark.ico      # 应用图标
└── mini2nbrowser.csproj        # 项目配置
```

### 从源码构建

```bash
# 克隆
git clone https://github.com/<your-name>/mini2nbrowser.git
cd mini2nbrowser

# 开发调试
dotnet run --project mini2nbrowser\mini2nbrowser.csproj

# 发布框架依赖版（单文件）
dotnet publish mini2nbrowser\mini2nbrowser.csproj -c Release ^
  -o publish\framework-dependent ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  --self-contained false

# 发布独立带运行时版（单文件 + 压缩 + ReadyToRun）
dotnet publish mini2nbrowser\mini2nbrowser.csproj -c Release ^
  -o publish\self-contained ^
  -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:SelfContained=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishReadyToRun=true
```

---

## 🎯 快捷键

| 快捷键 | 功能 |
|---|---|
| `Ctrl+T` | 新建标签页 |
| `Ctrl+W` | 关闭当前标签页 |
| `Ctrl+D` | 添加/取消书签 |
| `Ctrl+Shift+N` | 新建无痕标签页 |

---

## 📁 数据存储

所有用户数据按 profile 隔离，存储于 exe 同目录（或 `Profiles\<name>\` 子目录）：

```
mini2nbrowser.exe
├── config.json          # 配置（主题、搜索引擎、防护开关）
├── bookmarks.json       # 书签
├── history.json         # 历史记录（最多 500 条）
├── scripts.json         # 油猴脚本
├── passwords.json       # 密码（DPAPI 加密）
├── extensions.json     # 扩展记录
├── Extensions\          # 扩展解压目录
├── WebViewData\         # WebView2 用户数据（Cookie、缓存等）
└── Profiles\            # 多 profile 数据
    ├── work\
    └── personal\
```

---

## 🔒 安全说明

- **密码存储**：使用 Windows DPAPI（用户级）加密，仅当前 Windows 账户可解密
- **扩展导入**：仅处理用户本地提供的 CRX/文件夹，不爬取任何商店
- **路径穿越防护**：CRX 解压时校验所有路径必须在目标目录内
- **Profile 名清理**：非法字符自动替换，禁止 `.` / `..`

---

## 📝 版本历史

### v1.1.0
- 🆕 无痕浏览模式（`Ctrl+Shift+N`）
- 🆕 扩展系统（CRX 导入 + 文件夹导入 + 管理 UI）
- 🆕 多 Profile 数据隔离（`--profile` 参数）
- 🐛 修复热启动 bug（单文件发布时数据目录错误）
- 🐛 修复窗口无法拖动问题

### v1.0.0
- 基础浏览器功能（多标签、导航、书签、历史）
- 油猴脚本、密码管理、智能防护
- 双主题、系统托盘、内存优化

---

## 📄 许可证

MIT License - 详见 [LICENSE](LICENSE)

---

<div align="center">

**如果这个项目对你有帮助，欢迎 ⭐ Star 支持！**

</div>

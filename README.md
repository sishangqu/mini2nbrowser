<div align="center">

# 2ⁿ Browser

**一个用 C# / WPF / WebView2 打造的极简、快速、可扩展的桌面浏览器**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-Desktop-512BD4?logo=windows&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![WebView2](https://img.shields.io/badge/WebView2-1.0.4129-0078D4?logo=microsoftedge&logoColor=white)](https://learn.microsoft.com/microsoft-edge/webview2/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4?logo=windows&logoColor=white)](#)
[![License](https://img.shields.io/badge/License-MIT-success.svg)](#)

**v1.9.1** · 多标签 · 无痕浏览 · 扩展支持 · 油猴脚本 · 双主题 · 密码管理 · 系统托盘 · PDF 批注 · 搜索引擎关键字 · 地址栏智能联想 · 网页媒体嗅探下载 · m3u8 纯 C# 合并 · 离线北极熊游戏 · 标签静音 · 网页截图 · 鼠标手势 · 页内查找 · 全屏模式 · 强制暗色网页 · 阅读模式 · 画中画 · 二维码生成 · **深度稳定性修复**

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
- 左侧栏悬停展开（32 → 180px 动画过渡），可折叠状态更紧凑
- 中键关闭、Ctrl+T 新建、Ctrl+W 关闭
- 标签页显示网站图标 + 标题，尺寸与间距全面优化，无遮挡无重叠

### 🔎 地址栏智能联想（v1.5 新增）
- **本地 + 云端混合方案**：输入即时查询本地 SQLite（历史 / 书签），250ms 防抖后请求云端 Suggest
- **CancellationTokenSource 取消竞态**：新输入到达自动取消上一个未完成请求，避免乱序覆盖
- **来源色彩区分**：绿色 = 书签、蓝色 = 历史、紫色 = 云端，一目了然
- **键盘导航**：↑↓ 选择、Enter 跳转、Esc 关闭、Tab 补全
- **双写兼容**：新数据同时写入 SQLite 与旧 JSON，v1.4.x 用户无缝迁移
- **无痕模式自动禁用云端联想**：避免搜索数据上传，保护隐私

### 🎬 网页媒体嗅探与下载（v1.6 新增）
- **WebView2 网络监听**：通过 `WebResourceResponseReceived` 事件捕获所有媒体响应
- **双层匹配**：Content-Type + URL 扩展名联合判定，识别视频 / 音频 / 流媒体
- **去重限流**：URL 哈希去重，单页最多保留 500 条，避免列表爆炸
- **增强型多线程下载器**（移植自 ddm 项目，C++ → C# 移植）
  - 多线程分块下载，最大 32 线程
  - 服务器并发探测（4 → 32 递增，70% 成功率阈值）
  - 站点缓存持久化（7 天有效，JSON 存储）
  - 指数退避重试（最多 8 次）
  - 滑动窗口速度采样（250ms / 8 点窗口）
  - 单线程降级（不支持 Range 或拿不到大小时自动降级）
- **UI 实时状态**：进度、已下载/总大小、活跃线程数、即时速度、平均速度、ETA
- **状态流转**：已嗅探 → 探测大小 → 探测并发 → 下载中 → 合并中 → 已完成

### 📺 m3u8 纯 C# 合并（v1.6 新增）
- **零外部依赖**：完全使用 C# 实现 m3u8 流媒体下载与合并，**不需要 FFmpeg**
- **master playlist 自动选流**：BANDWIDTH 最高自动选择
- **8 路并发分片下载**：自动重试 3 次，失败重试间隔 500ms
- **TS 二进制拼接**：按分片顺序追加，生成可播放的 MPEG-TS 文件
- **现代播放器兼容**：VLC、PotPlayer、mpv、MPC-HC 等均可直接播放 .ts
- **加密分片检测**：识别 `#EXT-X-KEY` 加密流并提示用户使用专业工具
- **速度统计同步**：与文件下载一致的速度采样、平均速度、ETA 计算

### 🐻 离线北极熊游戏（v1.5 新增）
- **导航失败自动激活**：网页加载失败时显示离线游戏
- **北极主题**：白色北极熊主角，极光、星空、飘雪、滚动冰裂纹背景
- **障碍物**：冰柱（跳跃避让）+ 海鸟（蹲下避让）
- **帧率无关动画**：dt 标准化物理计算，不同刷新率下表现一致
- **动作流畅**：站立 / 滑行两套动画，跑步时身体弹跳、双脚交替、跳跃时收脚
- **空格重玩**：碰撞冰柱触发 Game Over，按空格重置

### 🔇 标签页静音（v1.7 新增）
- **右键菜单**：标签页右键 → "静音/取消静音此标签"
- **WebView2 原生 IsMuted**：一键切换该标签下所有媒体音量，不破坏页面其他功能
- 不影响其他标签页，适合"听歌工作"场景

### 📷 网页截图（v1.7 新增）
- **更多菜单 → 网页截图（整页）...**
- **WebView2 CapturePreview**：原生 API 直接捕获当前可视区域为 PNG
- **智能文件名**：自动用页面标题 + 时间戳命名（已剔除非法字符）
- **保存对话框**：用户自选位置，避免覆盖

### 🖱 鼠标手势（v1.7 新增）
- **右键拖动**：在网页区域按住右键拖动 > 30px 触发手势
- **4 方向手势**：
  - ↑ 上：滚动到页首（smooth 平滑滚动）
  - ↓ 下：滚动到页底
  - ← 左：后退（如可后退）
  - → 右：前进（如可前进）
- **不破坏右键菜单**：拖动 < 30px 视为普通右键，网页菜单正常显示
- 仅在 WebView 区域生效，标签栏 / 地址栏 / 工具栏右键行为不变

### 🔍 页内查找（v1.8 新增）
- **Ctrl+F 打开查找栏**：右上角浮动条，不遮挡网页内容
- **window.find + innerText 计数**：实时显示 "x / y" 匹配状态
- **快捷键**：F3 = 下一个，Shift+F3 = 上一个，Esc = 关闭
- **清除高亮**：关闭查找栏自动调用 `getSelection().removeAllRanges()`

### 🖥 全屏模式（v1.8 新增）
- **F11 一键切换**：WindowStyle=None + Maximized 实现无边框全屏
- **更多菜单也可触发**：菜单项 "全屏模式"
- 自动保存原窗口状态与样式，退出全屏完美还原

### 🌙 强制暗色网页（v1.8 新增）
- **更多菜单 → 强制暗色网页（当前标签）**：可勾选切换
- **CSS filter: invert + hue-rotate**：浅色网页瞬间变暗，无需重写
- **图片/视频反向处理**：保持图片色彩不被反转破坏
- 不修改原页面 DOM 结构，再次切换移除

### 📖 阅读模式（v1.9 新增）
- **更多菜单 → 阅读模式（当前标签）** 或 **Ctrl+Shift+R**
- **Readability 风格提取**：按文本密度评分选最佳容器，剔除导航/侧边栏/广告
- **段落识别**：h1-h3 / p / li / blockquote / pre 分别标记，输出结构化纯文本
- **三套主题**：跟随系统 / 浅色 / 深色 / 护眼黄，菜单栏 ○ 按钮循环切换
- **字号调节**：A- / A+ 按钮，12-28px 范围
- **Esc 关闭**，适合新闻、博客、长文阅读

### 🎬 画中画（v1.9 新增）
- **更多菜单 → 画中画（当前页视频）**
- **HTML5 requestPictureInPicture API**：视频浮窗置顶，浏览器外可继续观看
- **自动选最大视频**：多视频场景选 videoWidth 最大的一个
- **自动播放**：进入画中画前调用 play()，避免静默无法触发
- **再次点击退出**：检测 pictureInPictureElement，切换浮窗

### 📲 二维码生成（v1.9 新增）
- **更多菜单 → 生成当前页二维码...**
- **QRCoder 纯 C# 库**：无原生依赖，MIT 协议，后台线程生成不卡 UI
- **ECCLevel.M 纠错**：约 15% 纠错能力，扫码容错性强
- **240×240 PNG**：手机扫码直达当前页 URL
- **遮罩弹窗**：Esc 关闭，显示完整 URL 文本

### 📑 PDF 批注面板（v1.4 新增）
- **本地 PDF 直接打开**：菜单项"打开本地 PDF..."或拖入窗口
- **侧边浮动面板**：不遮挡 WebView2 原生 PDF 控件（缩放/保存/旋转/分页）
- **高亮 6 色盘 + 文字 7 色盘**：快速切换批注与文字颜色
- **6 种批注工具**：高亮 / 下划线 / 删除线 / 便签 / 自由划线 / 橡皮擦
- **字号 + 线宽独立调节**，撤销 / 全部清除 / 导出批注 / 一键打印
- 跟随窗口移动与缩放自适应

### 🔍 搜索引擎系统（v1.4 新增）
- **预置 7 个引擎**：必应（默认）、百度、搜狗、360、头条搜索、GitHub、StackOverflow
- **地址栏关键字触发**：输入 `gh wpf` → GitHub 搜索；`bd xxx` → 百度搜索；`so xxx` → StackOverflow
- **一键切换默认引擎**，管理面板支持新增 / 删除 / 编辑 URL 模板
- **配置持久化**：JSON 保存到 `%AppData%/mini2nbrowser/config.json`，重启自动恢复
- **旧配置自动迁移**：`{0}` → `%s` 占位符、旧引擎名自动映射
- 支持自定义 `Keyword` / `SearchUrl` / `SuggestUrl`

### 🕵️ 无痕浏览
- 独立用户数据目录，关闭即焚
- 不写入历史、Cookie、缓存
- **无痕模式下自动禁用云端联想**（v1.5）

### 🧩 扩展系统（v1.1 新增）
- 支持 CRX v2/v3 自动解析与解压
- 支持文件夹形式直接导入
- 扩展管理 UI：启用 / 禁用 / 删除 / 查看详情
- 扩展元数据持久化（extensions.json）

### 📝 油猴脚本
- 自定义 JavaScript 注入到所有页面
- 脚本编辑器（支持新增 / 编辑 / 删除）
- 脚本持久化（scripts.json）

### 🔒 智能防护
- 拦截弹窗、屏蔽恶意网站
- 自定义黑名单 / 白名单
- 关闭确认对话框

### 🔑 密码管理
- 表单自动填充
- 密码以 DPAPI 加密存储
- 一键查看 / 删除

### 🌐 导航与搜索
- 地址栏智能识别 URL / 搜索词
- 历史记录（最多 500 条，可搜索过滤）
- 历史与书签 SQLite 双写（v1.5+ 用于地址栏联想）

### 📥 下载管理
- 实时进度、速度、剩余时间
- 角标提示活跃下载数
- Toast 通知下载完成
- 媒体嗅探下载集成（v1.6+）

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
- 「框架依赖」版需要系统安装 .NET 8 Desktop Runtime；「自包含」版自带运行时

### 三种发行版

| 版本 | 发布目录 | EXE 大小 | 总大小 | 说明 | 适合场景 |
|---|---|---|---|---|---|
| **框架依赖 多文件** | `F:\1.9.1\程序\框架依赖-多文件\` | ~0.6 MB | ~6 MB | DLL 分开，依赖清晰 | 开发调试、二次打包 |
| **框架依赖 单文件** ✅推荐 | `F:\1.9.1\程序\框架依赖-单文件\` | ~5.6 MB | ~7.4 MB | 单 EXE + WebView2Loader/e_sqlite3 随行，原生库不可压缩 | **日常分发、用户共享**，需系统安装 .NET 8 Desktop Runtime |
| **自包含 单文件** | `F:\1.9.1\程序\自包含-单文件\` | ~76 MB | ~76 MB | 自带 .NET 8 Runtime（ReadyToRun 预编译 + 压缩），首次启动稍慢 | **离线 / 干净系统 / 免装运行时**，开箱即用 |

> 💡 **如何选？** 大多数情况下用「框架依赖 单文件」即可（~5MB）。Windows 11 22H2+ / Windows 10 21H2+ 基本都已预装 .NET 8 Desktop Runtime；如果目标环境完全不确定，选「自包含 单文件」。

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
- **Microsoft.Data.Sqlite** 8.0.10（本地历史 / 书签 / 联想查询）

### 项目结构

```
mini2nbrowser/
├── App.xaml(.cs)              # 单实例 + 命名管道热启动 + 多 profile
├── MainWindow.xaml(.cs)       # 主界面 + 标签页 + 所有浏览器功能
├── ExtensionsManager.cs        # 扩展管理（导入/加载/删除/持久化）
├── ExtensionsWindow.xaml(.cs)  # 扩展管理 UI
├── CrxLoader.cs                # CRX v2/v3 解析与解压
├── ExtensionInfo.cs            # 扩展数据模型
├── BrowserLocalDb.cs            # SQLite 封装（历史/书签/联想查询）【v1.5】
├── MediaSniffer.cs              # 网页媒体嗅探 + 多线程下载器 + m3u8 纯 C# 合并【v1.6】
├── DarkTheme.xaml              # 深色主题
├── LightTheme.xaml             # 浅色主题
├── HomePage.html               # 内嵌新标签页（EmbeddedResource）
├── DinoGame.html               # 内嵌离线北极熊游戏（EmbeddedResource）【v1.5】
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

# 发布框架依赖 多文件
dotnet publish mini2nbrowser\mini2nbrowser.csproj -c Release -r win-x64 ^
  --self-contained false -o publish\fd-multi

# 发布框架依赖 单文件（✅推荐日常分发）
dotnet publish mini2nbrowser\mini2nbrowser.csproj -c Release -r win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=true ^
  -o publish\fd-single

# 发布自包含 单文件（自带 .NET 8 Runtime，压缩 + ReadyToRun）
dotnet publish mini2nbrowser\mini2nbrowser.csproj -c Release -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishReadyToRun=true ^
  -o publish\sc-single
```

---

## 🎯 快捷键

| 快捷键 | 功能 |
|---|---|
| `Ctrl+T` | 新建标签页 |
| `Ctrl+W` | 关闭当前标签页 |
| `Ctrl+D` | 添加/取消书签 |
| `Ctrl+Shift+N` | 新建无痕标签页 |
| `Ctrl+L` | 聚焦地址栏（可用关键字快速搜索） |
| `↑` / `↓` | 地址栏联想列表上下选择（v1.5） |
| `Enter` | 跳转到选中或输入的地址 |
| `Esc` | 关闭地址栏联想列表（v1.5） |
| `Tab` | 用选中项补全地址栏（v1.5） |

### 地址栏关键字速查（v1.4 新增）

| 关键字 | 引擎 | 示例 |
|---|---|---|
| `bd` | 百度 | `bd WPF 教程` |
| `sg` / `sogou` | 搜狗 | `sg 天气` |
| `360` | 360 搜索 | `360 mini2nbrowser` |
| `tt` / `toutiao` | 头条搜索 | `tt AI 新闻` |
| `gh` / `github` | GitHub 搜索 | `gh dotnet/wpf` |
| `so` / `stackoverflow` | StackOverflow | `so webview2 focus issue` |
| *(无)* | 默认引擎（出厂=必应，可自定义） | `anything` |

---

## 📁 数据存储

所有用户数据按 profile 隔离：
- **浏览器数据**（历史/书签/密码/扩展/Cookie）存于 exe 同目录（或 `Profiles\<name>\` 子目录）
- **全局配置**（主题/搜索引擎/防护开关）存于 `%AppData%\mini2nbrowser\config.json`，多 Profile 共享
- **媒体站点缓存**存于 `%AppData%\mini2nbrowser\media_site_cache.json`（v1.6+）

```
mini2nbrowser.exe
├── bookmarks.json       # 书签（v1.5+ 同时写入 SQLite）
├── history.json         # 历史记录（最多 500 条；v1.5+ 同时写入 SQLite）
├── browser.db          # SQLite 数据库：History / Bookmark 表 + 索引【v1.5】
├── scripts.json        # 油猴脚本
├── passwords.json      # 密码（DPAPI 加密）
├── extensions.json     # 扩展记录
├── Extensions\          # 扩展解压目录
├── WebViewData\         # WebView2 用户数据（Cookie、缓存等）
└── Profiles\            # 多 profile 数据
    ├── work\
    └── personal\

%AppData%\mini2nbrowser\
├── config.json          # 全局配置：主题 / 搜索引擎 / 防护开关（v1.4+ 迁移到此）
├── media_site_cache.json # 媒体下载站点缓存：每站点并发上限 + 时间戳，7 天有效【v1.6】
└── crash.log            # 崩溃日志（v1.9.1+）：自动捕获未处理异常，2MB 自动轮转
```

---

## 🔒 安全说明

- **密码存储**：使用 Windows DPAPI（用户级）加密，仅当前 Windows 账户可解密
- **扩展导入**：仅处理用户本地提供的 CRX/文件夹，不爬取任何商店
- **路径穿越防护**：CRX 解压时校验所有路径必须在目标目录内
- **Profile 名清理**：非法字符自动替换，禁止 `.` / `..`
- **无痕模式**：自动禁用云端地址栏联想，防止搜索词上传（v1.5+）
- **媒体嗅探**：仅监听 WebView2 自身网络请求，不主动抓取第三方站点

---

## ⚠️ 已知限制

- **m3u8 加密流**：`#EXT-X-KEY` 加密分片暂不支持自动下载，请使用专业工具
- **DASH/MPD**：动态自适应流暂不支持自动下载
- **云端联想接口**：使用第三方 Suggest 接口，可能存在速率限制 / 格式变更 / IP 封禁，开源发布时需附带风险说明
- **TS 合并**：纯 C# 拼接生成 .ts 文件，部分老旧播放器（Windows Media Player）需安装解码器才能播放

---

## 📝 版本历史

### v1.9.1（最新，2026-08）—— **深度稳定性修复**
- 🛡️ **三层全局异常防护**
  - `DispatcherUnhandledException`（UI 线程）+ `AppDomain.UnhandledException`（非 UI 最后防线）+ `TaskScheduler.UnobservedTaskException`（Task GC 回收）全覆盖
  - `IsRecoverable` 智能判定：COM 异常 / ObjectDisposed / IOException / NullReference / 集合修改异常标记为已处理后继续运行，不杀进程
  - 崩溃日志自动写入 `%AppData%\mini2nbrowser\crash.log`（2MB 自动轮转 crash.log.old）
- 🔒 **关闭流程全面加固**
  - 新增 `volatile bool _isShuttingDown` 跨线程可见标志，所有定时器/事件回调在入口处快速退出
  - 关窗顺序：设标志 → 取消下载 CTS → 隐藏所有 WebView2 → Children.Clear → 延迟（Background 优先级）Dispose，避免事件回调访问已释放 COM 对象
  - 所有 Dispatcher.Invoke 替换为 BeginInvoke + shutdown 双重检查，防止高频事件（下载进度、图标更新）死锁 UI 线程
- 🧵 **线程安全修复**
  - `System.Timers.Timer`（CleanMemory / CheckAndFreezeIdleTabs）回调统一 Dispatcher.BeginInvoke 封送，禁止跨线程访问 WindowState / WPF 控件
  - `MediaSniffer.Remove` 自动检测并封送 `ObservableCollection.Remove` 到 UI 线程，解决下载完成时跨线程修改集合崩溃
  - 密码保存、下载状态更新、Protection BlockCount 更新等 async void 全部包在 try/catch 中
- 🖼️ **Favicon 内存修复**
  - FaviconChanged 先将图标数据复制到 `byte[]` 再封送到 UI 线程，避免 MemoryStream using 块在 Dispatcher 队列执行前被释放导致 BitmapImage 损坏
  - BitmapImage 使用 CacheOption=OnLoad + Freeze() 确保跨线程安全
- 🧹 **标签页安全关闭**
  - CloseTab 先 Collapse 再 Remove 最后延迟 Dispose（DispatcherPriority.Background），让同步事件链完成后再释放 COM 对象
  - CreateAndAddTab 失败时自动清理部分创建的 Tab/WebView2，不残留半初始化控件
- 📡 **Named Pipe 通信加固**
  - 管道服务端改用 `await Dispatcher.InvokeAsync` 替代阻塞 WaitOne + Invoke，防止死锁
  - StreamReader/Writer 显式指定 Encoding.UTF8，修复 HTML 实体（`&amp;`）损坏问题
  - 空引用保护，防止窗口创建前收到激活消息导致 NullReference
- ✅ **承压测试通过**：75秒空闲稳定性 / Named Pipe IPC 热启动 / 多 Profile 并发实例 / CleanMemory 周期回收 全部无崩溃

### v1.9.0（2026-08）
- 🆕 **阅读模式**
  - 更多菜单 → "阅读模式（当前标签）" 或 Ctrl+Shift+R
  - Readability 风格提取：按文本密度评分选最佳容器，剔除导航/侧边栏/广告
  - 段落识别 h1-h3/p/li/blockquote/pre 分别标记，输出结构化纯文本
  - 三套主题（跟随系统/浅色/深色/护眼黄）+ 字号调节（12-28px）
  - Esc 关闭，适合新闻、博客、长文阅读
- 🆕 **画中画**
  - 更多菜单 → "画中画（当前页视频）"
  - HTML5 requestPictureInPicture API，视频浮窗置顶可继续观看
  - 自动选最大视频，自动 play() 触发，再次点击退出
- 🆕 **二维码生成**
  - 更多菜单 → "生成当前页二维码..."
  - QRCoder 纯 C# 库，无原生依赖，后台线程生成不卡 UI
  - ECCLevel.M 纠错，240×240 PNG，手机扫码直达当前页

### v1.8.0（2026-08）
- 🆕 **页内查找**
  - Ctrl+F 打开右上角浮动查找栏（不遮挡网页）
  - window.find + innerText 计数，实时显示 "x / y" 匹配状态
  - F3 = 下一个，Shift+F3 = 上一个，Esc = 关闭并清除高亮
- 🆕 **全屏模式**
  - F11 一键切换：WindowStyle=None + Maximized 无边框全屏
  - 更多菜单也可触发，自动保存/还原原窗口状态
- 🆕 **强制暗色网页**
  - 更多菜单 → "强制暗色网页（当前标签）" 可勾选切换
  - CSS filter: invert(0.92) hue-rotate(180deg) 让浅色网页瞬间变暗
  - 图片/视频/iframe 反向处理，保持图片色彩
  - 不修改 DOM 结构，再次切换移除样式

### v1.7.0（2026-08）
- 🆕 **标签页静音**
  - 标签页右键菜单 → "静音/取消静音此标签"
  - 调用 WebView2 原生 `IsMuted` 属性，一键切换该标签所有媒体音量
  - 不影响其他标签页，适合后台听歌工作
- 🆕 **网页截图**
  - 更多菜单 → "网页截图（整页）..."
  - 调用 `CoreWebView2.CapturePreviewAsync` 原生 API，PNG 无损保存
  - 智能文件名：页面标题 + 时间戳（自动剔除非法字符）
  - 用户自选保存位置，避免覆盖
- 🆕 **鼠标手势**（4 方向）
  - 仅在 WebView 区域生效，标签栏/地址栏右键行为不变
  - 右键拖动 > 30px 触发手势：< 30px 视为普通右键，不破坏网页右键菜单
  - ↑ 上：滚动到页首（smooth 平滑滚动）
  - ↓ 下：滚动到页底
  - ← 左：后退（如可后退）
  - → 右：前进（如可前进）
- 🐛 移除未使用字段 `_gestureTriggered`，消除编译警告

### v1.6.0（2026-08）
- 🆕 **网页媒体嗅探与下载**
  - 监听 `WebResourceResponseReceived` 事件，自动识别视频 / 音频 / 流媒体
  - Content-Type + URL 扩展名双层匹配，URL 去重限流，最多保留 500 条
  - UI 列表展示 + 一键下载，含进度 / 速度 / 线程数 / ETA
- 🆕 **增强型多线程下载器**（移植自 ddm 项目，C++ → C# 移植）
  - 多线程分块（最大 32 线程）、并发探测（4→32 递增，70% 成功率阈值）
  - 站点缓存（7 天有效，JSON 存储）、指数退避重试（最多 8 次）
  - 滑动窗口速度采样（250ms / 8 点窗口）
  - 单线程降级（不支持 Range 或拿不到大小时自动降级）
- 🆕 **m3u8 纯 C# 合并**（零外部依赖，**不需要 FFmpeg**）
  - master playlist 自动选流（BANDWIDTH 最高）
  - 8 路并发分片下载，自动重试 3 次
  - TS 二进制拼接，VLC / PotPlayer / mpv / MPC-HC 直接播放
  - 加密分片检测，识别后提示用户
- 🆕 **MediaSniffer.cs / FFmpegHelper.cs → 移除**
  - 媒体嗅探与下载核心逻辑全部由纯 C# 实现，无外部依赖
- 🐛 修复下载状态字段更新不及时问题
- 🐛 修复 m3u8 下载无速度统计问题

### v1.5.0（2026-08）
- 🆕 **地址栏智能联想**（本地 SQLite + 云端 Suggest 混合方案）
  - 输入即时查询本地 SQLite（历史 / 书签）
  - 250ms 防抖后请求云端 Suggest，CancellationTokenSource 取消竞态
  - 来源色彩区分（书签绿 / 历史蓝 / 云端紫）
  - 键盘导航（↑↓/Enter/Esc/Tab）
  - 无痕模式自动禁用云端联想
- 🆕 **BrowserLocalDb.cs**：SQLite 封装，WAL 模式 + 索引优化
  - History / Bookmark 表双索引（URL / 标题 / 访问时间）
  - JSON 数据自动迁移到 SQLite，旧数据双写保持兼容
- 🆕 **离线北极熊游戏**（DinoGame.html，EmbeddedResource）
  - 导航失败自动激活，避免空白页尴尬
  - 北极主题：极光、星空、飘雪、滚动冰裂纹
  - 障碍物：冰柱（跳跃）+ 海鸟（蹲下）
  - 帧率无关动画（dt 标准化），动作流畅
  - 碰撞冰柱 → Game Over，空格重玩
- 🆕 依赖 Microsoft.Data.Sqlite 8.0.10

### v1.4.0（2026-08）
- 🆕 **搜索引擎系统**
  - 预置 7 个引擎：必应（默认）、百度、搜狗、360、头条搜索、GitHub、StackOverflow
  - 地址栏关键字触发：`gh xxx` → GitHub、`bd xxx` → 百度、`so xxx` → StackOverflow 等
  - 管理面板：新增 / 删除 / 编辑、一键切换默认引擎、支持 `Keyword` + `SearchUrl` + `SuggestUrl`
  - 全局配置持久化到 `%AppData%\mini2nbrowser\config.json`，多 Profile 共享，旧配置自动迁移
- 🆕 **PDF 批注侧边面板**
  - 菜单项「打开本地 PDF...」或拖入窗口即可打开
  - WPF 右侧浮动面板，**不遮挡 WebView2 原生 PDF 控件**（缩放/保存/旋转/分页）
  - 高亮 6 色盘 + 文字 7 色盘，6 种批注工具（高亮/下划线/删除线/便签/自由划线/橡皮擦）
  - 字号 + 线宽独立调节，撤销 / 全部清除 / 导出批注 / 一键打印
- 🆕 **侧边栏 UI 全面优化**
  - 折叠宽度 36 → 32 px，标签高度 32 → 28 px，按钮尺寸 32 → 28 px
  - Grid 重排为 4 行结构，彻底修复标签重叠与按钮遮挡问题
  - TextBlock 从 ControlTemplate 移至 Content 层，修复文字不显示/无法控制问题
  - 新建标签按钮更紧凑，间距和图标全面缩小
- 🐛 修复按钮重叠、标签遮挡、PDF 工具栏覆盖原生控件等 UI bug
- 🐛 修复 C# JavaScript 字符串转义错误（`\"1\"` → `\"\"1\"\"`）

### v1.3.0
- 🆕 工具栏布局重构，新增 PDF 相关入口
- 🆕 本地 PDF 文件打开支持（`BtnOpenPdf_Click`）
- 🆕 地址栏智能搜索升级：关键词 + UrlEncode 双重适配
- 🐛 修复搜索首页「默认引擎」下拉框

### v1.2.0
- 🆕 首页（新标签页）重构：默认引擎选择器 + 引擎关键字展示
- 🆕 引擎编辑器浮层：新增 Keyword / SuggestUrl 等字段
- 🐛 修复 TextBlock 在 ControlTemplate 内不可访问问题

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

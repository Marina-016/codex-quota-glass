# Codex Quota Glass

Windows 常驻 Codex 额度悬浮窗。默认仅在 Codex 桌面应用位于前台时显示；切到其他应用自动隐藏，回到 Codex 恢复。

## 功能

- 白色玻璃背景、彩色渐变额度条、WPF 文字与矢量图标。
- 显示接口实际返回的额度周期、剩余百分比和重置倒计时，每 60 秒刷新。
- 展开版宽 280 DIP、圆角 28；精简版宽 268 DIP、胶囊圆角 34，重新平衡品牌、额度与按钮间距。
- 右下角根据所有额度周期中的最低剩余百分比显示提示。
- 展开 / 精简、拖动位置保存、托盘退出和手动刷新。
- 查询失败明确保留旧数据，到达重置时间后等待新的账户数据，不自行恢复为 100%。

## 运行与构建

要求 Windows 10/11、.NET Framework 4.8，Codex 桌面应用或 CLI 已安装并通过 ChatGPT 登录。Windows 11 的原生 Desktop Acrylic 优先；旧系统尝试 compositor Acrylic，失败则使用白色背景。Windows 禁用透明效果或系统节能策略也可能影响玻璃效果。

```powershell
.\build.ps1 -Test
.\outputs\CodexQuotaGlass\CodexQuotaGlass.exe
```

不需要 npm、第三方 NuGet 包或管理员权限。使用 Windows .NET Framework 自带编译器。首次运行会显示托盘图标；若 Codex 不在前台，悬浮窗不会显示。

### 操作

- 左键拖动移动；双击背景收起 / 展开。
- 顶部图标：刷新、收起、设置。
- 三个点打开圆角设置面板；固定明亮白色底色，仅调节背景透明度（5–65%，默认 14%），不影响文字和进度条。点击关闭按钮或面板外关闭。退出在托盘菜单中。
- 托盘右键可刷新、切换仅前台显示、收起 / 展开或退出。
- 设置保存在 EXE 同目录的 `settings.json`，该目录应可写。

### 提示区间

| 最低剩余额度 | 提示 |
|---|---|
| > 60% | 余量充足，放心推进 |
| > 30% 且 ≤ 60% | 节奏不错，稳步推进 |
| > 10% 且 ≤ 30% | 留点余量，先做重点 |
| > 0% 且 ≤ 10% | 快到上限，稍作休息 |
| 0% | 额度已用完，等重置吧 |

异常和过期数据优先显示状态提示。额度为账户共享，不是当前聊天专属。

## 数据与隐私

程序通过本地 `codex app-server --stdio` 的 `account/rateLimits/read` 接口查询。它不读取、导出或保存登录凭据，不运行模型任务、不发送消息、不消耗重置 credits。登录维护与网络请求由 Codex 自己处理。无遥测、无第三方后台。仅调用 Windows 前台窗口 API 和进程名识别，未监控其他窗口内容。

窗口识别支持 `Codex.exe`，以及 OpenAI.Codex 安装包内实际名为 `ChatGPT.exe` 的客户端；后者会核对可执行文件路径，避免普通 ChatGPT 客户端也触发显示。窗口切换检查间隔 150ms。操作悬浮窗自身时保留显示，避免点击菜单时自动消失。

可设置 `CODEX_QUOTA_CLI` 为 Codex CLI 完整路径，已有 `CODEX_HOME` 会保留。请勿在 issues 上传 `auth.json`、令牌或完整账户日志。

## 开发

- `src/App.cs`：WPF 界面、Windows 材质与前台窗口逻辑。
- `src/QuotaClient.cs`：Codex 只读额度接口。
- `tests/QuotaTests.cs`：额度数据、提示阈值和倒计时测试。
- `--check`：查询真实额度；`--diagnose`：输出 Codex 窗口识别结果。
- `--preview` / `--preview --compact` / `--preview --settings`：输出 3× 分辨率演示图。
- `--ui-check`：实际实例化按钮与滑杆模板，检查展开/精简尺寸和透明度绑定。
- 动效：按钮悬停 120ms、按下缩至 96% / 80ms、松开 180ms；面板打开 180ms 淡入与 6 DIP 位移，关闭按钮淡出 140ms；收起/展开 220ms 高度过渡。关闭系统动画时跳过动效。

这是独立社区工具，与 OpenAI 无隶属关系。当前未签名；各 Windows 版本、混合 DPI、多显示器和辅助功能需进一步验证。窗口圆角区域随 DPI 和大小更新。

MIT License。仓库不包含构建产物、个人设置或账户信息。

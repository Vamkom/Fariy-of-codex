# Fairy Companion for Codex

源码仓库：[Vamkom/Fariy-of-codex](https://github.com/Vamkom/Fariy-of-codex) · [下载预览版](https://github.com/Vamkom/Fariy-of-codex/releases)

Windows 上的非官方 Fairy 风格桌面组件：HDD 配色、开屏、角色语气和可选本地语音。

这是从个人原型整理出的 **v0.3.1 社区预览版**。当前机器已验证；尚未在第二台实体电脑完成全功能验证。不是 OpenAI 或 HoYoverse 官方产品，也不是可在所有 Codex 客户端直接安装的官方插件。

## 新电脑怎么装

1. 解压公开 Windows 包到有写入权限的目录，例如 `Documents\Fairy-Companion`。源码包也可使用，安装脚本会本机编译。
2. 安装 [Python 3.12 x64](https://www.python.org/downloads/windows/)（包含 Tkinter 和 py 启动器）。Windows 10/11 x64 须有 .NET Framework 4.8。
3. 双击 `Install.cmd`，创建本目录 `.venv`、安装轻量依赖并生成桌面快捷方式，不需要管理员权限。
4. 双击 `Configure.cmd`。完整换肤需 [Node.js 22 或更新版本](https://nodejs.org/en/download) 和 Codex Switch 已启用的 Dream Skin 本地连接；路径可留空从 PATH 查找。
5. 双击 `Start.cmd`。无游戏素材时使用中性占位标识并跳过开屏；无语音引擎时可先运行图形组件。

若 PowerShell 执行策略或公司设备管理阻止脚本，请按设备管理要求处理；安装器不会修改执行策略、系统安全设置或程序目录。

### 迁移你自己的完整 Fairy 效果

在新电脑完成上述安装后，把**私人素材 ZIP**拖到 `Import-Private-Assets.cmd`。导入后关闭组件，再运行 `Install.cmd` 使程序图标一致。

私人包携带原视频、背景、已确认的角色图标、参考 WAV 和可选模型；不含聊天记录、令牌或其他账号信息。它不是公开 Release，**不要上传 GitHub**。

开屏首次使用须配置 [FFmpeg](https://ffmpeg.org/download.html) 的可执行文件路径，或使其能从 PATH 找到。组件会从原视频生成本地帧缓存，以避免视频窗口黑屏。

### 语音与人设

语音使用独立的 [GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS) 本机 `api_v2.py` 服务。请先安装、启动引擎，再在配置中启用语音并选择模型和参考 WAV。模型与大型推理环境不包含在公开包中，详见 [语音接入](docs/TTS.md)。

要朗读回复，请在 `Configure.cmd` 中明确选中该电脑自己的 Codex rollout JSONL，默认不绑定任何聊天。它只朗读启动后新增的最终回复，跳过历史、用户消息与进度说明。换聊天需重新选择并重启。

将 `skills/fairy-mode` 复制到自己的 Codex 技能目录（通常是 `%USERPROFILE%\.codex\skills`），在聊天中调用 `$fairy-mode` 或要求进入 Fairy 模式。**界面启动与人设技能是两个入口**，窗口本身不会修改模型的系统指令。

## 兼容范围

| 功能 | 条件 |
| --- | --- |
| 桌面 Fairy 组件 | Windows 10/11 x64，.NET Framework 4.8，Python 3.12 |
| 开屏与背景 | 自备素材；FFmpeg 可用于生成缓存 |
| Codex 主窗口配色 | Codex Switch Dream Skin 连接可用，且仍暴露受支持的主窗口地址 |
| 新台词与回复语音 | 本机 GPT-SoVITS v2 API、兼容模型、参考 WAV |
| 人设语气 | 安装并在聊天中启用 Fairy 技能 |
| macOS / Linux / 浏览器版 | 当前不支持原生桌面组件 |

配色连接不存在时，桌面组件仍可运行，诊断状态会显示 `awaiting-native-runtime`。本项目不会自动启用远程调试，也不改写 Codex 或 Codex Switch 的安装文件。应用更新可能需要适配选择器。

## 使用与排错

点击 Fairy 可重播开屏、播放问候、暂停语音或切换配色。`Stop.cmd` 先恢复本项目配色，再停止该安装目录的组件和后台服务，不关闭外部 GPT-SoVITS 引擎。

`Diagnose.cmd` 输出依赖和连接状态，不显示令牌或聊天正文。详细步骤见 [迁移指南](docs/MIGRATION.md) 和 [架构](docs/ARCHITECTURE.md)。端口 18742/18743 仅监听本机；同一用户会话一次只运行一套服务。

## 给开发者

```powershell
python -m unittest discover -s tests -v
powershell -NoProfile -File .\Install.ps1 -BuildOnly -NoShortcuts
python .\tools\package.py --output .\dist\Fairy-Companion-v0.3.1-source.zip
python .\tools\package.py --binaries --output .\dist\Fairy-Companion-v0.3.1-windows-x64.zip
```

打包器使用文件白名单，不会把整个工作目录压缩。仓库结构、发布流程和检查项见 [发布指南](docs/RELEASING.md)。GitHub Actions 对提交和 PR 执行检查；主分支更新 `VERSION` 或推送 `v*` 标签时重新验证、编译并发布预览 Release，只上传白名单程序、源码 ZIP 与校验文件。

## 素材与许可

本项目原创程序代码采用 MIT 许可。公开源码和 Windows 包仅含中性占位图形，**不含游戏视频、原背景、Fairy 角色图形、声线模型、生成语音或聊天内容**。

你原有的 Fairy 模型附带作者“严禁商用”的说明；这不等于允许重新分发。想公开提供完整角色素材包，应先确认素材各自的分发授权。代码许可不能替代素材许可。具体范围见 [NOTICE](NOTICE.md)。

## HDD 界面背景（2026-10-26）

将自己的 HDD PNG 放到 app/skin/hdd-background.png（原型目录为 skin/hdd-background.png），换肤服务会读取图片并以静态虚化、压暗的方式铺到聊天背景；不模糊文字和控件。只用本机文件，恢复换肤会移除背景。图片包含在私人导入白名单，公开发行 ZIP 不含该图。

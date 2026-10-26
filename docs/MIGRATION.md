# 从一台 Windows 电脑迁移到另一台

## 建议带走的两份 ZIP

- `Fairy-Companion-v0.3.0-windows-x64.zip`：可公开分享的程序和源码。
- 单独的私人素材包：仅供你个人迁移，不要加入 Git 仓库或 Release。

不要复制旧电脑的 Python 虚拟环境、`config.json`、Codex 账号目录或聊天历史。Windows 虚拟环境含本机解释器关联，必须在目标电脑重建。

## 新电脑操作顺序

1. 安装 Python 3.12 x64；解压程序包；运行 `Install.cmd`。
2. 把私人素材 ZIP 拖到 `Import-Private-Assets.cmd`，随后关闭组件并重新运行 `Install.cmd` 更新程序图标。
3. 用 `Configure.cmd` 选择 Node 和 FFmpeg 的本机路径。没有视频素材可不装 FFmpeg。
4. 运行 GPT-SoVITS v2 API，配置迁移后的 CKPT、PTH 和参考 WAV；确认参考台词与 WAV 一致，再勾选语音。
5. 在 Codex Switch 中按其产品说明开启 Dream Skin，使本机皮肤连接可用。本项目只使用已经存在的连接。
6. 在配置窗口明确选中目标聊天的 JSONL；不需要朗读时留空。
7. 运行 `Start.cmd`，确认开屏、问候和停靠；用 `Diagnose.cmd` 查看换肤、语音连接。
8. 按需把仓库 `skills/fairy-mode` 复制到自己的 Codex 技能目录，在聊天中启用。

配置中的相对路径以 `app` 为基准。私人导入的模型使用 `../private/models/`，可随项目目录移动。自己选择的外部绝对路径在换电脑后需要重新选择。

## 创建自己的私人素材包

在旧电脑、项目根目录运行（下面路径为示例，按实际位置替换）：

```powershell
.\.venv\Scripts\python.exe .\tools\assets.py export --from-package "C:\MyFairy\app" --models "C:\MyFairy\private\models" --output "C:\Transfer\Fairy-private-assets.zip"
```

`--models` 可省略；提供时目录中必须恰好各有一份 CKPT 和 PTH。导出器只挑选已列明的素材，绝不导出 token、sessionPath、账号文件或日志。

## 恢复与卸载

先运行 `Stop.cmd`，确认配色恢复，再删除你自己解压的项目目录。桌面两个快捷方式可手动移除。项目不注册系统服务、启动项或注册表项；外部 Python、Node、FFmpeg 和 GPT-SoVITS 由你自行保留或卸载。

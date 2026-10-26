# Fairy Companion v0.3.1 — Windows 社区预览版

面向 Codex 的非官方 Fairy 风格桌面伴随组件，支持 HDD 配色、可选开屏、本地语音和人设技能。

## 下载与安装

- 普通使用者下载 `Fairy-Companion-v0.3.1-windows-x64.zip`，解压后阅读 README，安装 Python 3.12 x64 并运行 `Install.cmd`。
- 开发者可下载 `Fairy-Companion-v0.3.1-source.zip`，或克隆源码仓库。
- `.sha256` 文件用于校验对应 ZIP。程序尚未进行代码签名。

## 本版内容

- Windows 原生透明组件、首屏问候、缩小停靠、开屏重播及关闭恢复。
- 可选 HDD 背景：仅对用户提供的图片虚化、压暗，保持聊天文字与控件清晰。
- 本机 GPT-SoVITS v2 API 适配，明确选择单个聊天后朗读新增最终回复。
- 配置窗口、诊断工具、私人素材迁移、白名单发行脚本与 Fairy 人设技能。

## 依赖与限制

- 当前支持 Windows 10/11 x64，需 Python 3.12 和 .NET Framework 4.8。
- 完整 Codex 配色需 Node.js 22+ 和 Codex Switch 已启用的 Dream Skin 本地连接。
- 开屏缓存需 FFmpeg；实时语音需另装兼容 GPT-SoVITS 引擎与自备授权模型。
- 公开包只含中性占位图形，不包含角色图形、游戏视频、背景、声线模型或音频；无素材时可先运行图形组件。
- 只完成当前机器及新目录迁移验证，尚未在第二台实体电脑完成全功能验收，因此作为预览版发布。

详细操作见 README、docs/MIGRATION.md 与 docs/TTS.md。请勿在 Issue 上传 config.json、聊天记录、模型或私人素材。

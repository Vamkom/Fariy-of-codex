# 整理到 GitHub 与发布

源码仓库可包含程序、安装脚本、配置模板、文档、测试和 CI。用户下载的 Windows ZIP 放在 Release 附件中。
GitHub 的说明见 [About releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)；代码许可见 [Licensing a repository](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)。

## 推荐目录

```text
Fairy-Companion/
  README.md, LICENSE, NOTICE.md, CHANGELOG.md
  Install.cmd, Install.ps1, Configure.cmd, Start.cmd, Stop.cmd
  app/                 # WPF / Python / Node 源码，配置模板，默认中性图形
  tools/               # 配置、诊断、素材迁移、白名单打包
  tests/               # 回复监听、配置迁移、素材校验、接口访问与打包测试
  docs/                # 迁移、语音、架构和发布指南
  skills/fairy-mode/   # 可选 Codex 人设技能
  .github/             # 构建检查和 Issue 模板
```

## 首次发布前

1. 在干净目录或另一台 Windows 电脑完成安装、素材导入、开屏、问候、朗读和配色恢复。当前首版只完成本机测试，应保留预览版标记。
2. 确定仓库名称、维护者署名和 SECURITY 中的私密联系方式。本地 LICENSE 当前为 MIT 程序许可，发布者可在发布前审阅其选择。
3. 将**公开 source ZIP**解压到新目录，以该目录作为 Git 仓库。不要对含私人素材的工作目录直接整目录上传。
4. 创建仓库、提交公开源码、打版本标签 `v0.3.2`；将 Windows ZIP、源码 ZIP 和 SHA256 校验文件作为 Release 附件。
5. Release 说明明确：Windows x64；TTS 引擎独立安装；完整 Fairy 素材由用户提供；配色依赖 Codex Switch 的现有皮肤连接。

目标仓库为 `https://github.com/Vamkom/Fariy-of-codex`。推送源码会触发检查；主分支更新 `VERSION` 或推送匹配的 `v*` 标签，会触发 release.yml 重新测试、构建并发布 GitHub 预览 Release。更新 VERSION 属于发布操作，请只在维护者明确授权时执行。版本号必须唯一；发布失败时先检查原因，避免重复创建既有 Release。

## 生成发行文件

```powershell
python -m unittest discover -s tests -v
powershell -NoProfile -File .\Install.ps1 -BuildOnly -NoShortcuts
python .\tools\package.py --output .\dist\Fairy-Companion-v0.3.2-source.zip
python .\tools\package.py --binaries --output .\dist\Fairy-Companion-v0.3.2-windows-x64.zip
```

私有 `config.json`、素材、权重、回复音频、`.venv`、本机运行时与日志均不在白名单。不要使用资源管理器“压缩整个文件夹”替代发行脚本。

## 后续走向广泛使用

优先补第二台电脑验证和签名安装器；再将引擎安装做成可选向导，并提供有明确授权的默认素材。
macOS/Linux 需要单独的窗口、音频和分发实现，不能仅改打包后缀。

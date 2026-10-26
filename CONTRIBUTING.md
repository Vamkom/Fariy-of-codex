# 参与开发

请先运行 `python -m unittest discover -s tests -v`。
原生界面修改需要 Windows 编译，并验证首次开屏、重播、跳过、问候、停靠与退出恢复。

不要提交真实 config、聊天 JSONL、日志、模型或角色素材。新增素材必须有可核实的授权与来源记录。
不使用绕过系统安全设置或修改 Codex 程序安装目录的接入方式。

接口调整时同步更新 README、配置示例和迁移测试；发布包必须使用 `tools/package.py` 生成。

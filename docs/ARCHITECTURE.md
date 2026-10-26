# 架构与边界

```text
Start.cmd → FairyLauncher.exe → .venv Python → app/launch.py
                                      ├─ server.py (loopback 18742)
                                      │   ├─ explicitly selected rollout → replies.py
                                      │   └─ local GPT-SoVITS API (usually 9880)
                                      ├─ Node skin-runtime.cjs (loopback 18743)
                                      │   └─ existing Codex Switch native skin connection
                                      └─ FairyCompanion.exe (WPF, main-window image playback)
```

配置在 `app/config.json`，每个安装位置生成随机 token；示例不含 token 或 sessionPath。只有使用者明确选择的单个 rollout 文件被监听。启动前跳过文件现有内容；新最终回复先去除代码块，再分段合成，保存在 `app/assets/reply-*.wav`。

桥接接口绑定 127.0.0.1，控制请求和语音队列要求随机 token，拒绝外来 Origin/Host。该 token 是本机组件凭据，不是 Codex 账号/API key。

配色只给 `app://-/index.html` 主渲染器添加自己的 style 节点，恢复时只移除该节点与标记。连接由 Codex Switch 提供，项目不建立新的调试权限；这属于产品内部接口适配，应用更新可能影响兼容性。

开屏采用提前生成的 JPEG 帧与独立原音轨；由时钟推进当前帧，处理卡顿时跳到当前时刻，不积压整段解码帧。原视频不被改写。视频结束后切换问候、背景渐隐和缩小停靠，重播复用主窗口，不复用已关闭的独立视频窗口。

公开发行包由 `tools/package.py` 白名单生成；私人素材由 `tools/assets.py` 单独迁移。Git ignore 只是辅助保护，发行器不依赖它作为唯一防线。

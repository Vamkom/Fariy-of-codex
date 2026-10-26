# 本地语音接入

公开包只提供桥接适配器，**不安装或分发大型语音引擎、基础模型或 Fairy 模型**。

当前个人原型使用 GPT-SoVITS 官方 `20240821v2` 标签，提交 `a7e38f93f1ad8d263093ca26ce0dea3fb7080ea2`，在 CPU 上验证了新台词合成。它有针对 Python 3.12 / PyTorch 2.7 的本地兼容修改；不要把当前虚拟环境直接复制到另一台电脑并预期可用。

## 推荐接入步骤

1. 参照 [GPT-SoVITS 官方安装说明](https://github.com/RVC-Boss/GPT-SoVITS) 安装与自己模型版本兼容的独立环境。使用自己的模型时保留其来源与限制。
2. 启动该环境的 `api_v2.py`，使它只在 `127.0.0.1:9880` 监听。例如在对应引擎的终端中运行 `python api_v2.py -a 127.0.0.1 -p 9880 -c <该引擎的配置文件>`。配置文件路径与模型版本由该引擎决定。
3. 确认 `http://127.0.0.1:9880/openapi.json` 可以在本机打开。
4. 本项目 `Configure.cmd` 中选择 GPT CKPT、SoVITS PTH、参考 WAV，填入准确参考台词。启用语音，保存后 Stop / Start。
5. 首次会载入模型并合成问候。CPU 可能需要等待；载入失败时图形组件仍可启动，查看 `app/launcher-warning.txt` 和引擎日志。

本项目适配 `/set_gpt_weights`、`/set_sovits_weights` 与 `/tts`，期望非流式 WAV。引擎的未来版本若改变参数，需要更新适配器；不能保证任意 GPT-SoVITS 版本都兼容。

## 自动启动已有引擎（可选）

熟悉部署的使用者可在 `app/config.json` 的 `tts.engineCommand` 填入可执行文件与参数数组，例如：

```json
{
  "engineCommand": ["C:/VoiceEnv/python.exe", "api_v2.py", "-a", "127.0.0.1", "-p", "9880", "-c", "configs/voice.yaml"],
  "engineWorkingDirectory": "C:/VoiceEngine"
}
```

这里只运行你自己配置的本机程序，不经过 shell。该配置是本机私有文件，不应提交仓库。停止 Fairy 不会结束外部引擎，避免影响其他语音应用。

## 不需要实时语音

取消勾选语音即可。私人包中的已缓存问候仍可播放；没有该文件时只显示问候文字。无素材或模型时应使用自己的中性声线，避免把占位状态误称为已启用 Fairy 音色。

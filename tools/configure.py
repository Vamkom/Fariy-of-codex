"""Small local settings window; selecting a chat is always an explicit action."""
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))
from appconfig import ROOT, load, save


def main():
    load()
    if '--init' in sys.argv:
        return
    import json
    import tkinter as tk
    from tkinter import filedialog, messagebox, ttk
    config = json.loads((ROOT / 'config.json').read_text(encoding='utf-8'))
    window = tk.Tk()
    window.title('Fairy Companion 配置（仅保存在本机）')
    window.columnconfigure(1, weight=1)
    variables = {}
    fields = [
        ('greeting', '两句开场问候', config['greeting'], None),
        ('sessionPath', '要朗读的聊天 JSONL', config.get('sessionPath', ''), [('Codex rollout', '*.jsonl')]),
        ('nodeExecutable', 'Node.js 路径（可留空）', config.get('nodeExecutable', ''), [('Executable', '*.exe')]),
        ('ffmpegExecutable', 'FFmpeg 路径（可留空）', config.get('ffmpegExecutable', ''), [('Executable', '*.exe')]),
        ('endpoint', 'GPT-SoVITS 本机 API', config['tts']['endpoint'], None),
        ('gptWeights', 'GPT 权重 CKPT', config['tts']['gptWeights'], [('GPT weights', '*.ckpt')]),
        ('sovitsWeights', 'SoVITS 权重 PTH', config['tts']['sovitsWeights'], [('SoVITS weights', '*.pth')]),
        ('referenceAudio', '配套参考 WAV', config['tts']['referenceAudio'], [('Reference audio', '*.wav')]),
        ('promptText', '参考 WAV 的准确台词', config['tts']['promptText'], None),
    ]
    for row, (key, label, value, types) in enumerate(fields):
        ttk.Label(window, text=label).grid(row=row, column=0, sticky='w', padx=10, pady=5)
        variable = variables[key] = tk.StringVar(value=value)
        ttk.Entry(window, textvariable=variable, width=70).grid(row=row, column=1, sticky='ew', padx=6)
        if types:
            def browse(variable=variable, types=types, key=key):
                initial = str(Path.home() / '.codex/sessions') if key == 'sessionPath' else str(ROOT)
                chosen = filedialog.askopenfilename(parent=window, initialdir=initial, filetypes=types)
                if chosen:
                    variable.set(chosen)
            ttk.Button(window, text='选择', command=browse).grid(row=row, column=2, padx=8)
    enabled = tk.BooleanVar(value=config['tts'].get('enabled', False))
    skin = tk.BooleanVar(value=config.get('skinEnabled', True))
    ttk.Checkbutton(window, text='启用本地 GPT-SoVITS（须先安装并启动引擎）', variable=enabled).grid(row=9, column=0, columnspan=3, sticky='w', padx=10)
    ttk.Checkbutton(window, text='启用 Codex Switch 换肤连接', variable=skin).grid(row=10, column=0, columnspan=3, sticky='w', padx=10)
    ttk.Label(window, text='只监听你选中的聊天；不扫描、上传或朗读其他聊天。保存后重启本套组件。').grid(row=11, column=0, columnspan=3, padx=10, pady=8)

    def apply():
        for key, _, _, _ in fields:
            target = config['tts'] if key in config['tts'] else config
            target[key] = variables[key].get().strip()
        config['tts']['enabled'] = enabled.get()
        config['skinEnabled'] = skin.get()
        try:
            # Validate using the same loader without replacing an existing good config.
            from appconfig import save as persist
            previous = (ROOT / 'config.json').read_bytes()
            persist(config)
            try:
                load()
            except Exception:
                (ROOT / 'config.json').write_bytes(previous)
                raise
            messagebox.showinfo('已保存', '请先 Stop，再 Start，使配置生效。', parent=window)
        except Exception as error:
            messagebox.showerror('配置无效', str(error), parent=window)
    ttk.Button(window, text='保存配置', command=apply).grid(row=12, column=0, columnspan=3, pady=12)
    window.mainloop()


if __name__ == '__main__':
    main()

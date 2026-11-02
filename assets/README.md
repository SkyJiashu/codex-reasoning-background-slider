# 角色素材与自定义

已包含以下五档角色素材，采用 [CC BY-NC 4.0](../ARTWORK_LICENSE.md)。如果更换素材，请使用自己创作或获准使用的 PNG，保持以下文件位置：

| 推理档位 | 源文件位置 |
| --- | --- |
| 轻度 | `assets/stages-v10-review/01_Light.png` |
| 中 | `assets/stages-v11-nose/medium.png` |
| 高 | `assets/stages-v11-nose/high.png` |
| 极高 | `assets/stages-v11-nose/extra-high.png` |
| Ultra / 启动动画 | `assets/stages-v11-nose/ultra.png` |

建议中、高、极高和 Ultra 使用相同画布、相同人物尺寸的透明 PNG，例如 1254×1254；轻度可以使用 Q 版小图。压缩文件体积，避免五张图的 Base64 总量超过传输上限。文件放好后执行 `scripts/build.ps1`。

首次启动会使用 Ultra 图在用户电脑本地生成视频缓存。原图变化会使对应缓存失效并重新生成。生成视频、首尾帧和本地状态不应提交到仓库。

The five bundled images are available under CC BY-NC 4.0. Custom replacements must be your own or otherwise authorized. No rights in third-party images or branding are granted.

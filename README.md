# Codex Crystal Dragon · 紫晶龙娘

为 Windows Codex 桌面客户端提供“数据汇聚成龙娘”的启动动画，以及随原生推理强度变化的角色背景。

这是社区独立启动器，与 OpenAI 无隶属关系。使用自己的启动入口，不覆盖官方程序、不自动改写原版快捷方式。

已包含五档获准公开分发的角色素材。代码和素材均采用非商业许可，具体范围见下方许可说明。

<p align="center">
  <img src="https://raw.githubusercontent.com/SkyJiashu/codex-crystal-dragon/main/assets/stages-v11-nose/medium.png" width="300" alt="中档龙娘背景">
  <img src="https://raw.githubusercontent.com/SkyJiashu/codex-crystal-dragon/main/assets/stages-v11-nose/ultra.png" width="300" alt="Ultra 龙娘背景">
</p>

## 功能

- 首次在用户电脑上按显示器工作区分辨率和 DPI 生成动画缓存；相同条件直接复用，渲染代码或素材改变时自动重建。
- 播放阶段使用本地 60 fps 视频，不再即时计算粒子和文字。仓库及发布包不附带预制视频。
- 动画开始时后台启动 Codex 并预加载背景；播放结束停留约 0.3 秒，再用约 0.55 秒淡出交接。`Esc` 可跳过动画。
- 五档角色背景跟随原生推理控件，拖动时即可预览。Ultra 增加局部紫色与星星效果，浅色、深色界面分别适配。
- 原生控件尚在初始化时使用上次确认的角色图作视觉过渡；控件就绪后跟随真实档位。程序不替用户更改模型或推理设置。

## 运行要求

- Windows x64、.NET Framework 4.8 和可用的系统媒体组件。
- 已安装可被 Windows 包注册机制识别的官方 Codex 桌面客户端。其他安装方式可能需要适配。
- **首次生成缓存需要支持 `libx264` 的 FFmpeg**。将 `ffmpeg.exe` 放在启动器目录，或安装为系统可找到的命令。已有缓存的播放不需要再次调用编码器。
- Node.js 用于开发测试；`ffprobe` 仅供开发者检查编码结果。

项目不会自动下载或安装第三方程序。

## 直接使用

从 [Releases](https://github.com/SkyJiashu/codex-crystal-dragon/releases/latest) 下载 Windows x64 运行包，解压到长期保留的目录，准备好 FFmpeg，然后完全退出 Codex，双击 `CrystalDragon.exe`。也可运行 `Install-Shortcut.cmd` 创建独立桌面入口。

首次启动会在本地生成动画缓存；源码和运行包都不包含预制视频。

## 从源码使用

项目已包含所需角色图。在项目目录打开 PowerShell，运行：

```powershell
.\scripts\build.ps1
```

构建结果位于 `build/CrystalDragon/`。

1. 保存工作并**完全退出 Codex**，包括可能仍在后台运行的客户端。
2. 双击 `build/CrystalDragon/CrystalDragon.exe`，或该目录中的 `Start-Cold-Test.cmd`。
3. 首次启动会显示本地缓存生成进度，可以取消。生成完成后播放动画并启动 Codex。
4. 后续启动会复用缓存；显示规格、动画渲染代码或角色素材变化时，自动生成对应缓存。

如需桌面入口，运行 `Install-Shortcut.cmd`。它仅创建本项目自己的桌面快捷方式，不扫描、替换或绑定官方入口。请将解压或构建目录放在长期保留的位置。

原版快捷方式仍直接指向官方客户端。运行本启动器前必须退出已有客户端；启动器不会接管正在运行的实例。

## 预览与恢复

| 入口 | 行为 |
| --- | --- |
| `CrystalDragon.exe` / `Start-Cold-Test.cmd` | 通过动画启动新的 Codex 会话并应用背景 |
| `Preview.cmd` | 仅预览动画；首次可能生成缓存，不启动 Codex |
| `Restore.cmd` | 请求移除当前启动器应用的背景、样式与监听 |

恢复外观不会关闭 Codex。辅助进程会保持连接，直到该客户端正常退出。若要从官方入口重新启动，请先完全退出当前会话。

## 缓存与本地数据

动画缓存保存在当前用户本地应用数据目录中的 `CrystalDragon/animation-cache`，不写入仓库。播放窗口跟随 Codex 的位置、大小和 DPI，人物保持比例；播放期间调整窗口大小不会重新编码。

启动器目录中的 `appearance-stage.txt` 只保存上次由原生控件确认的视觉档位。初始化期间的过渡图不会被当作真实推理档位，也不会提前启用 Ultra 配色。

`qa/status.json` 和 `qa/handoff.json` 记录固定状态、耗时与错误类别，用于排查启动问题，不记录聊天正文、输入内容或认证配置。提交问题时请先检查附件，不要上传账号配置或完整客户端日志。

## 兼容范围

背景识别依赖客户端提供的界面属性。客户端更新后，这些属性可能变化；未识别、模糊或不受支持的控件不会被猜测为最高档位。

默认映射为 `none/minimal/low`、`medium`、`high`、`xhigh`、`max/ultra` 五组。若某个模型最高只提供 `high` 或 `xhigh`，不会自动将其映射到 Ultra。

动画交接不等待所有后台请求完成。原生页面若仍在加载，会继续自行完成；播放解码失败时保留静帧并记录失败状态。

## 开发

```powershell
# 前端行为检查
node --test tests/skin.test.cjs tests/preload.test.cjs

# 完整 Windows 本地检查
.\scripts\test.ps1

# 打包源码与运行文件
.\scripts\package-source.ps1
.\scripts\package.ps1
```

完整检查包含本地缓存生成、实际视频播放和官方客户端身份探测，需要上述 Windows 运行环境及已安装的客户端。测试不启动或修改已有 Codex 实例。

`scripts/build.ps1` 编译启动器和缓存生成工具，不生成预制电影。`scripts/render-film.ps1` 是开发用的手动编码检查工具。架构说明见 [docs/architecture.md](docs/architecture.md)。

## 许可与来源

项目新增代码采用 [PolyForm Noncommercial License 1.0.0](LICENSE)：源码公开，允许条款规定的非商业使用，不授予商业用途许可。这不属于 OSI 定义的开放源代码许可。

上游 MIT 部分继续适用其原有许可，本项目的非商业条款不会撤销上游已有授权。来源与保留的声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

随项目提供的五张角色图采用 [CC BY-NC 4.0（署名—非商业性使用）](ARTWORK_LICENSE.md)，不授予商业用途许可。维护者已确认素材自有或获准公开分发；素材包含 AI 生成或编辑。项目不授予第三方品牌或标识的权利。自定义图片说明见 [assets/README.md](assets/README.md)。

## English

Codex Crystal Dragon is an independent community launcher for the Windows Codex desktop app. It provides a locally generated startup film and character backgrounds that follow native reasoning controls.

The first launch renders a cache for the current display and DPI using FFmpeg with `libx264`. Later launches reuse it. Source code and five authorized character images are included; pre-rendered movies are not. The launcher uses its own shortcut and leaves the official installation and shortcuts intact.

Project additions are source-available under PolyForm Noncommercial 1.0.0. Bundled character artwork is provided under CC BY-NC 4.0; see ARTWORK_LICENSE.md. Third-party components retain their original licenses. This project is not affiliated with OpenAI.

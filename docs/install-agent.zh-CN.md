# 自动安装提示词（中文）

[English](install-agent.en.md) · [返回项目说明](../README.zh-CN.md)

复制下面整段，发给你在 Windows 电脑上使用的 agent。需要该 agent 具备联网、下载文件和运行本地程序的能力。首次编码可能需要一些时间；安装验证使用独立预览，不会强制关闭当前 Codex。

```text
请在这台 Windows 电脑上全自动安装 Codex 推理背景滑块（Codex Reasoning Background Slider）。

可信项目：https://github.com/SkyJiashu/codex-reasoning-background-slider
安装指南：https://github.com/SkyJiashu/codex-reasoning-background-slider/blob/main/docs/install-agent.zh-CN.md

我授权你在已有权限和安全规则内下载本项目及必要编码依赖，在当前用户目录安装、生成本地动画缓存、创建本项目自己的桌面快捷方式，并运行离线预览验证。请实际完成这些步骤，不要只给我命令，也不用为正常可逆步骤反复确认。项目代码和角色素材仅授权非商业使用，请先阅读项目许可。

按以下流程执行：

1. 确认系统符合 Windows x64、.NET Framework 4.8 和系统媒体组件要求。官方 Codex 桌面客户端应已安装。不要读取、输出或上传 API key、登录文件、cookie、认证配置或环境变量全集；不需要我提供 GitHub 令牌。

2. 读取公开 API https://api.github.com/repos/SkyJiashu/codex-reasoning-background-slider/releases/latest 。选择且仅选择名称以 windows-x64.zip 结尾的运行包，不要把 source.zip 当作安装包。使用 API 返回的 browser_download_url 下载。用发布资产 digest 中的 sha256 校验本地文件；若该字段缺失，使用同一 Release 的 SHA256SUMS.txt。校验不通过则停止执行该下载内容，报告具体失败项。

3. 使用系统目录 API 取得当前用户 LocalApplicationData，在其 Programs/CodexReasoningBackgroundSlider 下按 Release 标签建立版本目录。先解压到新的临时目录，检查 ZIP 条目规范化后都在该目录内，拒绝绝对路径、目录逃逸或链接。定位唯一包含 CrystalDragon.exe、RenderFilm.exe、animation-source.txt、skin.js、skin.css、五张 assets PNG 和 scripts/install-shortcut.ps1 的目录，再放入最终版本目录。安装包内这些兼容文件名可以保留。不要覆盖正在运行的旧版本，不要删除旧缓存或用户自定义文件。

4. 准备支持 libx264 的 FFmpeg。先检查启动器旁的 ffmpeg.exe，再用可执行文件发现接口查找已有 FFmpeg，并运行 -hide_banner -encoders 确认 libx264。不要输出环境变量内容，也不要为了安装永久修改全局 PATH。
   如果缺失，优先从 https://www.gyan.dev/ffmpeg/builds/ 下载 release essentials ZIP 及对应 .sha256，验证官方公布的 SHA-256 后再解压。该站是 FFmpeg 下载页面列出的 Windows 构建提供方。保留其许可证，将私有 ffmpeg.exe 放在本项目启动器旁，不覆盖其他应用的 FFmpeg。
   如选择 WinGet，可用精确包 ID Gyan.FFmpeg.Essentials，使用 --source winget --scope user --silent --accept-package-agreements --accept-source-agreements。不要擅自改成机器范围或提权；注意当前进程可能尚未识别新命令，最终必须检查实际可用的编码器。本项目运行包不需要 Node、Python 或 ffprobe。

5. 先对安装目录内的 CrystalDragon.exe 执行 --probe，并等待结束。只读取新生成 qa/identity.json 的 ready、version、coldStartAllowed、error_class 等固定字段，确认官方客户端身份可被识别。coldStartAllowed=false 只表示当前 Codex 在运行，不是安装失败。若官方客户端缺失或系统不支持，报告这个先决条件，不要用不明来源的 Codex 包替代。

6. 阅读并执行本项目 scripts/install-shortcut.ps1，创建独立的 Codex Reasoning Slider 桌面入口。该脚本应只创建或更新本项目自己标记的快捷方式。保留原版 Codex 的文件、快捷方式、注册和任务栏固定项，不扫描磁盘去改写其他入口。

7. 记录预览开始时间，然后运行安装目录内的 CrystalDragon.exe --preview 并等待正常结束。它只生成缓存并播放本项目动画，不启动 Codex。首次出现缓存生成进度是正常现象，等它完成，不要按 Esc 或取消。
   必须验证 qa/handoff.json 是这次预览新写入的报告，且 previewOnly、filmOpened、filmCompleted、transitionComplete 均为 true，filmFailed、skipped、timedOut 均为 false。不能只凭退出码 0 或旧报告就宣布成功。失败时检查本项目固定错误类别，修复明确缺失的依赖后再验证；不要读取原生客户端的认证文件或聊天日志。

8. 安装阶段不要运行默认启动、--cold-test、--restore 或 --probe-create 来代替离线验证，也不要强制结束当前 Codex 或任何其他应用。尤其当你自己正在 Codex 里运行时，不要关闭承载当前对话的程序。安装和缓存预览完成后，我可以保存工作、正常退出 Codex，再从新入口启动。

最后简洁告诉我：安装版本、安装目录、桌面入口、FFmpeg 是否已就绪、缓存/预览是否通过，以及下一次从哪个入口启动。如果存在确实需要我操作的系统限制，说明具体剩余步骤，不要把尚未验证的项目标成成功。
```

依赖下载参考：[FFmpeg 下载](https://ffmpeg.org/download.html)、[Gyan Windows 构建](https://www.gyan.dev/ffmpeg/builds/)。本提示词授权的是当前用户范围的安装和独立预览，不授权停止已有工作会话或修改官方客户端。

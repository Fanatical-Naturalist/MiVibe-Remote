# 0.3.0-alpha.3 验证记录

日期：2026-09-06。

- 用户要求把返回键从 Delete 改为 Backspace，Home 继续 Delete；TV 仅询问可改性，未指定新动作。
- alpha.3 使用扫描码 0x0E 发出 Backspace，保留单次按下、修饰键守卫、统一动作队列与完整释放。Home 仍使用扩展扫描码 0x53 的 Delete。
- 原音量活动视图导航与菜单翻译已在 alpha.2 实测通过，本次未修改。
- 安装、开始菜单固定、开机启动及 GitHub 发布继续沿用此前用户授权。
- 构建、安装及返回/Home 实测结果在下方续记；不将先前 Delete 的验收当作 Backspace 验收。

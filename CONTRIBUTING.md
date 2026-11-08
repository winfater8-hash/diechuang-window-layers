# Contributing / 参与贡献

欢迎提交窗口管理、透明显示、可访问性和兼容性改进。

1. 先在 Issue 中说明问题或目标；附 Windows 版本与复现步骤。
2. 在 Windows 上运行 `source/build.ps1`；如修改层级或输入逻辑，在交互式桌面运行 `--self-test`。
3. UI 改动请检查透明背景、图标边缘、拖放、快捷键和不同窗口模式。
4. 提交 Pull Request，说明变化、验证结果与已知限制。

不要提交个人配置、窗口标题日志、凭据、编译产物或包含个人信息的截图。

The legacy large management form is retained as a regression fixture. The compact toolbar is the default application. Native integration tests require a real interactive Windows session; hosted CI performs compilation and packaging only.

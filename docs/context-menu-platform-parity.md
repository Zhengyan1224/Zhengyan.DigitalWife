# 跨平台上下文菜单

Android、Web 沿用场景中现有的 `ContextMenus` 配置，不需要为移动端另建菜单。Android 在目标上触屏长按或外接鼠标右键打开；Web 支持鼠标右键、触屏和触控笔长按，长按阈值为 500 毫秒。拖动、双指操作、取消和失焦会取消 Web 长按；长按松手不会点击菜单项。

菜单支持窗口、实体及指定碰撞体、GUI 控件、旋转后的 2D Sprite；目标 ID 和名称不区分大小写。实体根据对应屏幕相机和视口命中，拾取规则与 PC 一致：优先选择最近的显式碰撞体；只有没有命中任何碰撞体时，才对没有启用碰撞体的 PMX 模型使用球形范围兜底，半径为 `max(fallbackRadius, 0.001) × max(Scale.X, Scale.Y, Scale.Z)`，默认 `fallbackRadius=0.5`。模型包围盒不参与菜单拾取，独立服装模型不会抢走人物碰撞体的命中。指定 `TargetCollider` 时必须命中该碰撞体的 ID 或名称，球形兜底不能满足此约束。

两端按 PC 规则解析菜单内容宽度、行高、内边距、字体、圆角、边框，以及背景、文本、悬停和按下颜色。相对布局的宽度按横轴缩放，其他尺寸按两轴较小比例缩放。禁用项可见但不可执行。

点击菜单项发送 PC 使用的 GUI 事件：`IsGuiEvent` 为 `true`，`GuiControlId`、`GuiControlName` 对应菜单，`GuiEventName` 对应菜单项的 `EventName`（为空时使用 `context_menu_clicked`）。事件发送给绑定实体；未配置目标时使用脚本实体回退，不广播给其他对象。场景切换会关闭菜单，旧菜单不能操作新场景。

Android 桌面精灵使用独立的菜单弹窗，不受精灵透明点击区域或小窗口尺寸限制；菜单按屏幕可见区域定位，超长列表可滚动。Web 桌面精灵的菜单保持在 iframe/画布容器可见区域内，超长列表可滚动；支持方向键、Home/End、Enter、Esc 和点击菜单外关闭。

验证命令：

```powershell
node --test tests/web-runtime-input.test.mjs
node tests/web-context-menu-browser.mjs
dotnet run --project tests/Zhengyan.DigitalWife.AndroidRuntime.Tests -- --test "context menu"
dotnet run --project tests/Zhengyan.DigitalWife.WebRuntime.Tests -- --context-menu
```

浏览器回归使用无界面 Edge 检查 DPR 1/2、鼠标右键、真实触屏长按、禁用项、键盘、边界、滚动及输入捕获。Android/Web 共用拾取回归，覆盖碰撞体优先于服装模型、指定碰撞体 ID/名称、碰撞体未命中时禁止模型兜底、禁用碰撞体、PMX 类型与缩放，以及 DemoGame08 的人物胶囊配置。

2026-11-18 真机验证：普通模式与桌面精灵模式均能通过长按打开菜单；外接鼠标右键、禁用项和菜单点击在测试场景验证通过。使用手机上的原版 DemoGame08 导出包复现人物菜单缺失后，修复播放器拾取规则；用户确认普通模式、桌面精灵模式和 Vulkan 均已恢复。无需修改人物、服装或菜单配置，也无需重新导出 dwgame 包。

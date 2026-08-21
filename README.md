# PolarisAddons

Polaris 的可扩展物品、插件与技能运行时。内容由独立文件声明，玩法代码通过 IoC 交给内容自身，不需要修改 PolarisAddons 或把实现硬编码进游戏。

## 内容格式

| 格式 | 用途 | 运行时接口 | 生成代码 |
| --- | --- | --- | --- |
| `.pitem` | 物品聚合根、库存与展示信息 | `IItemBehavior` | `.pitem.g.cs` |
| `.pplugin` | 挂在物品上的 Enhancer 插件切面 | `IPluginBehavior` | `.pplugin.g.cs` |
| `.pskill` | 挂在物品上的被动或主动技能切面 | `ISkillBehavior` / `IActiveSkillBehavior` | `.pskill.g.cs` |

三种定义都由各自的 Provider 特性注册。启动时，`AddonCatalog` 扫描并验证定义，然后构造统一的只读目录。PolarisTools 提供三种格式的可视化编辑、校验、保存和注册代码生成。

## IoC 与生命周期

内容程序集可以声明带 `[AddonModule]` 的 `IAddonModule`，注册 Singleton 或 Transient 服务。Behavior 由容器通过构造函数注入依赖；启用插件或技能时会创建独立的 `IBehaviorLifetime`，停用、换档或关闭组件时自动释放其中跟踪的订阅和 Modifier 贡献。

插件与技能通过 `ItemId` 归属于 `.pitem`：获得物品会解锁 `OwnItem` 类型的切面，成功消费物品会解锁 `ConsumeOwnerItem` 技能。原版内容会镜像成 `ContentOrigin.Native` 的只读描述符，与扩展内容通过 `AllItems`、`AllPlugins`、`AllSkills` 一起查询；原版硬编码行为仍由游戏执行，并可和扩展 Overlay、状态及 Modifier 协同。

## 游戏投影

- `.pitem` 投影为原版 `NelItem`，沿用字符串 key 的库存与存档路径；使用时进入统一的物品执行管线。
- `.pplugin` 投影为原版 `ENHA.Enhancer` 及其关联物品，获得和启用状态与 Enhancer 存储同步。
- `.pskill` 投影为原版 `PrSkill` 及技能书物品，获得和启用状态与技能菜单同步。
- 原版物品可注册只追加的 `ItemOverlay`，按 Priority 和 Id 稳定执行 `BeforeUse` / `AfterUse`，不能替换原行为。

`NelItem.Use` 是同步入口，因此 `IItemBehavior.UseAsync` 必须同步完成。主动技能由 `ExecuteSkillAsync` 独立调度，支持冷却、互斥组、外部取消以及换地图自动取消。

## Modifier 与存档

`IModifierSink` 提供可组合的 Add、Multiply、Override 贡献。贡献按 Priority、SourceId 和操作类型确定性求值；返回的 `IDisposable` 可直接交给 Behavior 生命周期管理。

插件和技能的获得/启用状态、扩展 payload 及 schema 版本存入 PolarisSave 的 `polaris.addons/state` 分区。未知内容的记录会原样保留，内容重新安装后可恢复。`MigratePayload` 只有在迁移完整成功时才提交新版本，失败不会破坏旧数据。

主要入口为 `PolarisAddonsAPI`：

- `Catalog` 查询统一目录；
- `Modifiers` 注册和计算数值贡献；
- `State` 读取状态并管理扩展 payload；
- `SetPluginObtained/Enabled`、`SetSkillObtained/Enabled` 控制投影状态；
- `ExecuteSkillAsync` 执行主动技能。

项目依赖同级 `PolarisCore` 与 `PolarisSave`，由 Polaris 聚合仓库以 Git submodule 引用。

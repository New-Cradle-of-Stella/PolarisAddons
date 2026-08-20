# PolarisAddons 落地计划

本文规定 `PolarisAddons` 在 Alice in Cradle Windows ver029 中扩展物品、游戏内插件（Enhancer）和技能的核心方案。结论来自对原版程序集、资源数据以及现有 Polaris 模块的静态分析；正式实现前仍需运行时验证。

## 1. 模块边界

`PolarisAddons` 负责：

- 注册自定义物品、Enhancer 和技能；
- 接入原版背包、Enhancer 槽位和技能菜单；
- 提供自定义效果运行时；
- 通过 `PolarisLang`、`PolarisRes`、`PolarisSave` 接入文案、图标和存档。

不负责新魔法、地图、AI 和通用粒子。新魔法仍归 `PolarisMagic`；技能只能引用或解锁已经注册的魔法。

## 2. 原版关键约束

### 2.1 物品

原版物品为 `NelItem`，具有字符串 key、`ushort id`、分类、价格、堆叠、五个品级和最多三个效果值。

物品存档通常写数字 ID，但 `id == 65535` 时会继续写字符串 key。因此：

> 所有自定义物品、技能书和 Enhancer 物品统一使用 `id = 65535`，不得占用原版数字区间。

### 2.2 Enhancer

原版 Enhancer 自动关联 `Enhancer_<key>` 物品，启用状态保存在该物品品级的 bit 1，收藏状态保存在 bit 0；总槽位来自 `enhancer_slot` 数量。

原版效果依赖固定的 `ENHA.EH : uint` 位掩码和硬编码判断。自定义 Enhancer 即使能显示在 UI 中，也不会自动产生效果。因此：

> 自定义 Enhancer 复用原版目录、库存、槽位和 UI，但效果必须走 Addons 自己的效果系统，不能分配新的 `ENHA.EH` 位。

### 2.3 技能

原版技能为 `PrSkill`，具有获得、启用、操作方式和分类状态，并自动关联 `skillbook_<key>` 物品。

原版技能行为依赖固定的 `SKILL_TYPE` 位；仅注册一个新 `PrSkill` 不会自动得到新动作。技能存档又只保存 `ushort id`，没有字符串 key 通道。因此：

> 自定义技能可以复用原版菜单和技能书，但状态必须由 `PolarisSave` 按 key 保存，不能依赖原版技能数字 ID。

第一版只支持被动技能、原版动作解锁和模式开关；任意新角色动作放到后续阶段。

## 3. 总体架构

```text
第三方内容提供器
        │
        ▼
定义验证与注册表
 Items / Enhancers / Skills
        │
        ├── PolarisLang：本地化
        ├── PolarisRes：图标资源
        └── PolarisSave：状态与孤儿数据
        │
        ▼
ver029 原版适配层
 NelItem / ENHA / SkillManager
        │
        ▼
统一效果运行时
```

建议工程结构：

```text
PolarisAddons/
├─ Api/             # 公开定义与注册 API
├─ Registry/        # key、所有者、冲突与冻结
├─ Runtime/         # 物品行为、Modifier、技能行为
├─ Integration/     # 原版对象安装与 Harmony 补丁
├─ Persistence/     # PolarisSave 数据与迁移
├─ Compatibility/   # ver029 特征探测与功能门控
└─ PolarisAddonsComponent.cs
```

`PolarisAddons` 建议直接依赖 `PolarisCore`、`PolarisLang`、`PolarisRes` 和 `PolarisSave`。

## 4. 注册与运行时方案

### 4.1 通用规则

- 定义使用全局唯一、稳定的小写 ASCII key；
- 推荐格式为 `<mod>_<name>`；
- 注册时记录 BepInEx GUID 和提供器程序集；
- 重复 key 拒绝双方定义并报告所有者；
- 第一次新游戏或读档前冻结目录，本局不支持热增删定义；
- 公开 API 不暴露 `NelItem`、`ENHA.Enhancer`、`PrSkill` 等原版类型。

### 4.2 自定义物品

物品定义包含 key、价格、稀有度、堆叠、分类、五品级数值、图标和使用行为。

使用模式分为：

- `Native`：完全走原版 HP/MP/状态效果；
- `Custom`：由 Addons handler 返回 0/1/2；
- `Composite`：原版成功后追加自定义效果。

handler 不直接扣库存，继续由原版使用流程根据返回值消费物品。

### 4.3 自定义 Enhancer

每条定义创建 `ENHA.Enhancer`、`Enhancer_<key>` 物品，以及槽位成本、图标和 Modifier。自定义 `ehbit` 固定为 0。

首版 Modifier 支持最大 HP/MP、攻击、魔法攻击、承伤、治疗、咏唱、移动速度和状态抗性。多个来源按“固定值 → 加法百分比 → 乘法 → 限制”聚合，避免模组互相覆盖字段。

### 4.4 自定义技能

每条定义创建 `PrSkill` 和 `skillbook_<key>` 物品，并声明分类、图标、启用规则、操作选项、Modifier 或原版动作别名。

原版动作解锁必须显式映射到 `SKILL_TYPE`。保存原版技能块时临时过滤自定义技能，再由 `PolarisSave` 按 key 保存获得、启用、`new_icon` 和操作方式状态；读档后统一恢复并重算。

## 5. 资源与存档

### 5.1 本地化

继续使用原版 key 规则，并通过 `.plang` 注册：

```text
_NelItem_name_<key>
_NelItem_desc_<key>
Enhancer_title_<key>
Enhancer_desc_<key>
Skill_title_<key>
Skill_desc_<key>
Skill_manipulate_<key>
```

### 5.2 图标

- Enhancer：直接设置外部 `PxlFrame`；
- 技能：补丁 `PrSkill.getPF/getThumbPF`；
- 物品：补丁 `NelItem.getIconPF/drawIconTo`；
- 加载失败时使用统一占位图，不得每帧重复记录错误。

### 5.3 存档与孤儿数据

Addons 使用固定 PolarisSave 分区保存：

- 自定义技能状态；
- 自定义物品在各库存中的五品级数量镜像；
- Enhancer 收藏和启用状态；
- 定义版本与未知内容记录。

虽然自定义物品能由原版按 key 保存，仍需镜像库存。内容包缺失时原版会跳过未知物品，随后重新保存可能永久丢失；Addons 镜像可保留这类孤儿数据，内容包恢复后再还原。

## 6. 关键接线点

| 入口 | 用途 |
| --- | --- |
| `NelItem.readItemScript` Postfix | 安装自定义物品 |
| `ENHA.initScript` Postfix | 安装自定义 Enhancer |
| `SkillManager.initScript` Postfix | 安装自定义技能 |
| `NelItem.Use` Prefix/Postfix | 自定义物品行为 |
| `NelItem.getIconPF/drawIconTo` | 外部物品图标 |
| `PrSkill.getPF/getThumbPF` | 外部技能图标 |
| `M2PrSkill.resetSkillConnection` Postfix | 重建自定义 Modifier 和动作别名 |
| `SkillManager.writeBinaryTo` 包装 | 过滤自定义技能 |
| 新游戏、读档完成回调 | 默认状态、恢复和孤儿协调 |

所有补丁由 PolarisCore 扫描，不新增 BepInEx 入口，也不修改 `resources.assets` 或 `StreamingAssets`。

## 7. 实施阶段

### M0：兼容探测

- 验证关键字段、方法、初始化顺序和存档布局；
- 建立物品、Enhancer、技能独立功能门控；
- 无自定义定义时不得改变原版行为。

### M1：自定义物品

- 注册表、五品级、原版/自定义使用效果；
- 原版库存、字符串 key 存档；
- 本地化和外部图标。

### M2：自定义 Enhancer

- 原版槽位与启停 UI；
- Modifier 聚合器；
- 启停重算和状态镜像。

### M3：被动技能

- 技能菜单、技能书、图标和操作选项；
- 被动 Modifier、原版动作别名；
- 按 key 独立存档。

### M4：孤儿恢复与内容来源

- 缺失内容包的数据保留和迁移；
- API/事件发放、商店、掉落、宝箱和配方接入。

### M5：主动技能

在前述阶段稳定后单独实现输入仲裁、角色状态机、动画、取消窗口及地图切换清理，初期标记为 Experimental。

## 8. 首个验收样例

测试内容包包含：

1. 一个五品级回复物品；
2. 一个 2 槽攻击修正 Enhancer；
3. 一个可启停的 HP 被动技能；
4. 三套外部图标与多语言文案。

必须验证获得、使用、启停、属性重算、保存读档、地图切换，以及“移除内容包后保存，再恢复内容包”的孤儿数据恢复流程。

## 9. 禁止事项

- 不修改原版资源文件；
- 不占用原版物品和技能数字 ID；
- 不分配自定义 `ENHA.EH` 位；
- 不把新魔法实现放进 Addons；
- 不允许内容作者直接修改原版位掩码；
- 不在高频路径做反射、资源加载或全表字符串扫描；
- 不因一个内容包失败而停用整个 Addons。


# AnanLa的淬火预测 / AnanLa's Quenching Prediction

一个 Vintage Story 模组，在淬火即将碎裂时发出预警，减少反复烧铁搓工具的精神损耗。

A Vintage Story mod that warns you when an item is about to shatter during quenching, reducing the frustration of repeated tool crafting.

游戏版本 / Game version：**1.22.3**

## 功能 / Features

- 当物品即将在淬火中碎裂时，弹出预警提示
- 仅在淬火真正有碎裂风险时预警，不干扰正常淬火
- 预警发出后不会重复提示
- **可配置**：支持开关预警，切换显示风格（底部提示栏 / 聊天栏 / 中央发现动画）
- Warning prompt when an item is about to shatter during quenching
- Only warns when there is an actual risk of shattering, does not interfere with normal quenching
- Warning is only shown once per item
- **Configurable**: toggle warnings on/off, choose display style (BottomError / Chat / Discovery)

## 配置 / Configuration

模组启动后在 `ModConfig` 目录生成 `ananlaquenchingprediction_client_config.json`，可手动编辑：

```json
{
  "PredictionPrompt": true,
  "DisplayMode": "BottomError"
}
```

| 字段 | 说明 | 可选值 |
|------|------|--------|
| `PredictionPrompt` | 是否显示预警提示 | `true` / `false` |
| `DisplayMode` | 显示风格 | `BottomError`（底部提示栏）、`Chat`（聊天栏）、`Discovery`（中央发现动画） |

English equivalent:

```json
{
  "PredictionPrompt": true,
  "DisplayMode": "BottomError"
}
```

| Field | Description | Options |
|-------|-------------|---------|
| `PredictionPrompt` | Enable/disable warning prompts | `true` / `false` |
| `DisplayMode` | Display style | `BottomError`, `Chat`, `Discovery` |

## 安装 / Installation

1. 下载 `ananlaquenchingprediction_1.1.0.zip` / Download the zip
2. 解压到游戏目录的 `Mods` 文件夹 / Extract into the game's `Mods` folder
3. 启动游戏，模组自动生效 / Launch the game, the mod activates automatically

## 实现原理 / Implementation

使用 Harmony 补丁拦截 `CollectibleBehaviorQuenchable.IsGettingCooled`：

Uses Harmony patches to intercept the quench logic in `CollectibleBehaviorQuenchable`:

1. **`IsGettingCooled` Postfix**：检测服务端设置的 `willbreak=true` 标记，通过网络通道推送预警到客户端
2. **`trySettleWorkItem` Postfix**：淬火成功时清理临时标记，避免残留影响后续物品
3. **IsGettingCooled Postfix**: Detects the `willbreak=true` flag set by the server, pushes a warning to the client via network channel
4. **trySettleWorkItem Postfix**: Cleans up temporary flags on successful quench to prevent stale state

## 兼容性 / Compatibility

- 需要 Vintage Story **1.22.3** / Requires Vintage Story **1.22.3**
- 使用游戏内置 Harmony，无需额外依赖 / Uses the built-in Harmony library, no extra dependencies
- 与原版 VSSurvivalMod 兼容 / Compatible with vanilla VSSurvivalMod
- **客户端可选安装**：不装此模组也可正常游玩（仅无预警提示）/ **Client optional**: players without this mod can join and play normally (just no warning prompts)

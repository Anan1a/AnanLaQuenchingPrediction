# AnanLa的淬火预测 / AnanLa's Quenching Prediction

一个 Vintage Story 模组，在淬火即将碎裂时发出预警，减少反复烧铁搓工具的精神损耗。

A Vintage Story mod that warns you when an item is about to shatter during quenching, reducing the frustration of repeated tool crafting.

> 该模组使用 AI（Claude）辅助开发 / This mod was developed with AI (Claude) assistance.

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

## 姊妹模组联动 / Sister Mod Linkage

同时安装 [AnanLa的淬火保底](https://mods.vintagestory.at/ananlaquenchingguarantee) 时，若**同一帧**触发保底通知与预测预警，两条消息将**合并为两行显示**（伪同屏），不再互相覆盖。

When installed alongside [AnanLa's Quenching Guarantee](https://mods.vintagestory.at/ananlaquenchingguarantee), if a guarantee notification and a prediction warning fire in the **same frame**, the two messages merge into **two lines** (pseudo same-screen) instead of overwriting each other.

- 零依赖：两个模组互不引用，可独立安装或同时安装 / Zero dependency: both mods work independently or together
- 合并条件：双方客户端使用相同的显示风格（`DisplayMode`）/ Merge condition: both clients use the same `DisplayMode`
- 仓库 / Repository: [AnanLaQuenchingGuarantee](https://github.com/Anan1a/AnanLaQuenchingGuarantee)

示例 / Example（`BottomError` 模式）:
```
保底次数为特殊值-255，强制淬火失败，请停止淬火
警告！此物品即将在淬火中碎裂！请立即停止淬火！
```

## 安装 / Installation

1. 下载 `ananlaquenchingprediction_1.3.0.zip` / Download the zip
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

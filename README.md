# AnanLa的淬火预测 / AnanLa's Quenching Prediction

一个 Vintage Story 模组，在淬火即将碎裂时发出预警，减少反复烧铁搓工具的精神损耗。

A Vintage Story mod that warns you when an item is about to shatter during quenching, reducing the frustration of repeated tool crafting.

> 该模组使用 AI（Claude）辅助开发 / This mod was developed with AI (Claude) assistance.

---

## 功能 / Features

- 当物品即将在淬火中碎裂时，弹出预警提示
- 仅在淬火真正有碎裂风险时预警，不干扰正常淬火
- 预警发出后不会重复提示
- **可配置**：支持开关预警，切换显示风格（底部提示栏 / 聊天栏 / 中央发现动画）

- Warning prompt when an item is about to shatter during quenching
- Only warns when there is an actual risk of shattering, does not interfere with normal quenching
- Warning is only shown once per item
- **Configurable**: toggle warnings on/off, choose display style (BottomError / Chat / Discovery)

---

## 兼容性 / Compatibility

已在 Vintage Story **1.22.3 ~ 1.22.6** 测试通过（modinfo 中 `"game": "1.22.3"` 为测试过的最低版本，更低版本未验证）。
Tested on Vintage Story **1.22.3 ~ 1.22.6** (`"game": "1.22.3"` in modinfo is the lowest tested version; lower versions untested).

---

## 客户端安装要求 / Client Requirement

服务端与客户端**均需安装**本模组。客户端可选仅是理论上的：预警逻辑虽在服务端执行，
但碎裂预警依赖客户端渲染，且与淬火保底的同屏合并需要双方客户端共存。
客户端不安装时，玩家会无预警地失去物品，因此默认要求客户端安装
（modinfo 未设置 `requiredOnClient: false`）。

This mod must be installed on **both the server and the client**. Client-optional was theoretical only:
the warning logic runs server-side, but the shatter warning needs the client to render, and the
same-screen merge with Quenching Guarantee requires both clients present. Without the client mod,
players would lose items without any warning, so the client is required by default
(modinfo does not set `requiredOnClient: false`).

---

## 配置 / Configuration

模组启动后在 `ModConfig` 目录生成 `ananlaquenchingprediction_client_config.json`，可手动编辑：

The mod generates `ananlaquenchingprediction_client_config.json` in the `ModConfig` folder on first launch:

```json
{
    "PredictionPrompt": true,
    // 是否显示预警提示 / Show warning prompts
    "DisplayMode": "BottomError"
    // 显示风格 / Display style:
    // BottomError = 底部提示栏（默认）/ bottom error bar (default)
    // Chat = 聊天栏 / chat bar
    // Discovery = 中央发现动画 / center discovery animation
}
```

---

## 提示信息 / Prompt Messages

开启 `PredictionPrompt` 后显示：

When `PredictionPrompt` is enabled:

| 场景 / Scenario | 消息 / Message |
|------|------|
| 即将碎裂 / About to shatter | `警告！此物品即将在淬火中碎裂！请立即停止淬火！` |

消息可通过 `DisplayMode` 切换显示风格（`BottomError` 底部提示栏 / `Chat` 聊天栏 / `Discovery` 中央发现动画）。

Message display style can be toggled via `DisplayMode` (`BottomError` / `Chat` / `Discovery`).

---

## 实现原理 / Implementation

使用 Harmony 补丁拦截 `CollectibleBehaviorQuenchable` 的淬火流程：

Uses Harmony patches to intercept the quench logic in `CollectibleBehaviorQuenchable`:

1. **`IsGettingCooled` Postfix**：检测服务端设置的 `willbreak=true` 标记，通过网络通道推送预警到客户端
2. **`trySettleWorkItem` Postfix**：淬火完成时清理临时标记，避免残留影响后续物品

1. **IsGettingCooled Postfix**: Detects the `willbreak=true` flag set by the server, pushes a warning to the client via network channel
2. **trySettleWorkItem Postfix**: Cleans up temporary flags on successful quench to prevent stale state

---

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

---

## 安装 / Installation

1. 下载 `ananlaquenchingprediction_1.3.1.zip` / Download the zip
2. 解压到游戏目录的 `Mods` 文件夹 / Extract into the game's `Mods` folder
3. 启动游戏，模组自动生效 / Launch the game, the mod activates automatically

---

## 兼容性 / Compatibility

- 需要 Vintage Story **1.22.3** / Requires Vintage Story **1.22.3**
- 使用游戏内置 Harmony，无需额外依赖 / Uses the built-in Harmony library, no extra dependencies
- 与原版 VSSurvivalMod 兼容 / Compatible with vanilla VSSurvivalMod
- **客户端可选安装**：不装此模组也可正常游玩（仅无预警提示）/ **Client optional**: players without this mod can join and play normally (just no warning prompts)

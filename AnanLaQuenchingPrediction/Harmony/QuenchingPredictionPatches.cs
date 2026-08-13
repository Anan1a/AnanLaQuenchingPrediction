using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using AnanLaQuenchingPrediction.Shared;

namespace AnanLaQuenchingPrediction.Harmony
{
    public static class QuenchingPredictionPatches
    {
        /// <summary>缓存的服务端配置，在 ModSystem.StartServerSide 中注入（与姊妹项目模式一致）。</summary>
        internal static Config.ServerConfig CachedConfig;

        /// <summary>原版碎裂标记键（TempAttributes，由原版 IsGettingCooled 掷骰写入）。</summary>
        private const string WillBreakKey = "willbreak";

        /// <summary>已预警标记键（TempAttributes），防止同物品同轮淬火重复预警。</summary>
        private const string WarnedKey = "breakPredictionWarned";

        /// <summary>宽限窗口起点标记键（TempAttributes），由 Prefix 首次看到必碎时写入。</summary>
        private const string QuenchGraceStartKey = "quenchPredictedStartMs";

        /// <summary>私有字段 metalProps 的反射引用（读取淬火完成温度 settledTemperature）。缺失时返回 null，温度边界修复降级。</summary>
        private static readonly FieldInfo MetalPropsField =
            AccessTools.Field(typeof(CollectibleBehaviorQuenchable), "metalProps");

        /// <summary>服务端 API 引用，在 ModSystem.StartServerSide 中赋值。</summary>
        internal static ICoreServerAPI ServerApi { get; set; }

        /// <summary>服务端→客户端的网络通道。</summary>
        internal static IServerNetworkChannel NotificationChannel { get; set; }

        /// <summary>发送预测通知包。通道未注册或非服务端玩家时静默跳过。</summary>
        private static void SendWarning(IPlayer player, PredictionEventType eventType)
        {
            if (player is not IServerPlayer serverPlayer) return;
            NotificationChannel?.SendPacket(new PredictionPacket
            {
                EventType = eventType
            }, serverPlayer);
        }

        /// <summary>
        /// 原方法 v1.22.3: <c>private void IsGettingCooled(IWorldAccessor world, ItemSlot slot, Vec3d pos, float dt, float temperature)</c>
        /// 每刻执行淬火冷却逻辑，在达到碎裂条件时设置 willbreak=true。
        /// 此 Prefix 实现宽限窗口：本轮掷骰确定必碎后，在 <see cref="Config.ServerConfig.GraceWindowMs"/> 毫秒内
        /// 跳过原方法（本刻不执行碎裂判断），给玩家预警后的反应时间。
        /// 温度边界：一旦温度降到淬火完成温度（settledTemperature），立即放行碎裂——
        /// 阻止"窗口期内冷却到底 → 结算路径（trySettleWorkItem）抢在碎裂前完成"的必碎逃逸。
        /// 注意：掷骰当刻本 Prefix 先于原版执行、看不到本刻掷骰结果，故掷骰当刻的竞态（5% 提前碎）仍保留。
        /// </summary>
        /// <remarks>Harmony Prefix — 补丁目标: <see cref="CollectibleBehaviorQuenchable"/>.IsGettingCooled。
        /// 返回 false 跳过原方法（Postfix 仍执行，预警不受影响）。</remarks>
        /// <param name="__instance">目标类实例，访问私有 metalProps 读取淬火完成温度。</param>
        /// <param name="world">游戏世界访问器。</param>
        /// <param name="slot">当前淬火物品所在的物品槽。</param>
        /// <param name="temperature">本帧降温后的温度（原方法参数）。</param>
        public static bool IsGettingCooledPrefix(CollectibleBehaviorQuenchable __instance,
            IWorldAccessor world, ItemSlot slot, float temperature)
        {
            try
            {
                // ── 守卫：客户端旁路（与原版 IsGettingCooled 一致，slot 非空由调用链保证）──
                // 配置未注入 → 不干预（与姊妹项目判空跳过一致）
                if (world.Side == EnumAppSide.Client || CachedConfig == null) return true;
                var attrs = slot.Itemstack.TempAttributes;

                // ── 窗口起点：本轮掷骰确定必碎时记录 ──
                // 标记保留到 willbreak 生命周期结束（settle 清理/碎裂），不随窗口过期删除，
                // 否则 willbreak 残留时"快进快出"会反复刷新窗口，突破 100% 碎裂概率
                long start = attrs.GetLong(QuenchGraceStartKey, -1);
                if (start < 0)
                {
                    if (!attrs.GetBool(WillBreakKey)) return true;
                    start = world.ElapsedMilliseconds;
                    attrs.SetLong(QuenchGraceStartKey, start);
                }

                // ── 温度已降到淬火完成温度 → 窗口期立即结束，放行碎裂（阻止结算路径逃逸必碎）──
                // 碎裂检查（IsGettingCooled）先于结算（SetTemperature）执行，放行后碎裂优先命中
                var metalProps = MetalPropsField?.GetValue(__instance) as CollectibleBehaviorQuenchable.MetalPropertyVariant;
                if (metalProps != null && temperature <= metalProps.settledTemperature)
                    return true;

                // ── 窗口期内跳过原方法（本刻不碎裂），过期放行恢复（时长由服务端配置决定）──
                return world.ElapsedMilliseconds - start >= CachedConfig.GraceWindowMs;
            }
            catch
            {
                // 任何异常都放行原方法，保证不破坏游戏
                return true;
            }
        }

        /// <summary>
        /// 在原版 <c>IsGettingCooled</c> 的"掷骰写入 willbreak"之后插入 <c>ret</c> 直接结束方法，
        /// 跳过本刻的碎裂判断与执行——消除"掷骰当刻即碎、预警失效"的竞态
        /// （原版掷骰后同刻检查碎裂，5% 随机命中时玩家根本来不及看到预警）。
        /// 插入 ret 而非跳转到碎裂块结束：本方法为 void（无返回值负担）且每刻执行
        /// （错过本刻不影响下次完整执行），直接结束最安全、最简（无需定位跳转目标）。
        /// 插入点位于掷骰 if 块内，仅在掷骰当刻执行（非掷骰时刻被 HasAttribute 的 brtrue 跳过）。
        /// 配合 Prefix 的宽限窗口：本 Transpiler 只解决"掷骰当刻"竞态，窗口期仍由 Prefix 负责。
        /// </summary>
        /// <remarks>Harmony Transpiler — 补丁目标: <see cref="CollectibleBehaviorQuenchable"/>.IsGettingCooled。
        /// 匹配失败时原样返回指令，安全降级为无此修复的原版行为。</remarks>
        /// <param name="instructions">原方法的 IL 指令序列。</param>
        public static IEnumerable<CodeInstruction> IsGettingCooledTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();

#if DEBUG
            // ── 调试（仅 Debug 构建生效，Release 自动剔除）：将原版"每刻 5% 提前碎"提高到 100%，
            //    快速验证窗口期后碎裂闭环 ──
            // 可调参数：下方 operand = 1.0 即调试概率（0~1）；匹配的 0.05 是原版值（勿改）
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_R8 && list[i].operand is double d && Math.Abs(d - 0.05) < 1e-9)
                {
                    list[i].operand = 1.0;   // ← 调试概率（可调，0~1）
                    break;
                }
            }
#endif

            // 锚点：掷骰写入 SetBool（方法内唯一）→ 在其后插入 ret（跳过本刻碎裂判断与执行）
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Callvirt && list[i].operand is MethodInfo m && m.Name == "SetBool")
                {
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Ret));
                    return list;
                }
            }

            ServerApi?.Logger.Warning("[Transpiler] 未匹配到掷骰写入，立即破碎修复失效。游戏版本可能已变更。");
            return instructions;
        }

        /// <summary>
        /// 原方法 v1.22.3: <c>private void IsGettingCooled(IWorldAccessor world, ItemSlot slot, Vec3d pos, float dt, float temperature)</c>
        /// 每帧执行淬火冷却逻辑，在达到碎裂条件时设置 willbreak=true。
        /// 此 Postfix 在之后检测 willbreak 并发送预警。
        /// </summary>
        /// <remarks>Harmony Postfix — 补丁目标: <see cref="CollectibleBehaviorQuenchable"/>.IsGettingCooled</remarks>
        /// <param name="world">游戏世界访问器。</param>
        /// <param name="slot">当前淬火物品所在的物品槽。</param>
        public static void IsGettingCooledPostfix(IWorldAccessor world, ItemSlot slot)
        {
            try
            {
                // ── 守卫条件 ──
                // 跳过条件：客户端/空槽/willbreak 未触发/已预警
                if (world.Side == EnumAppSide.Client || slot.Empty) return;
                var attrs = slot.Itemstack.TempAttributes;
                if (!attrs.GetBool(WillBreakKey) || attrs.GetBool(WarnedKey)) return;

                // ── 通过槽位回溯所属玩家 ──
                var player = FindPlayerFromSlot(slot);
                if (player == null) return;

                // ── 发送预警通知 ──
                SendWarning(player, PredictionEventType.BreakWarning);

                // ── 标记已预警，防止下一帧重复触发 ──
                attrs.SetBool(WarnedKey, true);
            }
            catch (Exception ex)
            {
                ServerApi?.Logger.Error("[PredictionPatches] IsGettingCooledPostfix 异常: {0}", ex);
            }
        }

        /// <summary>
        /// 原方法 v1.22.3: <c>private void trySettleWorkItem(IWorldAccessor world, ItemStack itemstack, float temperature, string currentState)</c>
        /// 淬火完成（物品存活）时触发，清理物品上的预测标记。
        /// </summary>
        /// <remarks>Harmony Postfix — 补丁目标: <see cref="CollectibleBehaviorQuenchable"/>.trySettleWorkItem</remarks>
        /// <param name="__instance">目标类实例，访问原方法所在对象的成员。</param>
        /// <param name="itemstack">淬火完成的物品堆。</param>
        /// <param name="currentState">淬火状态 (<c>"quench"</c>/<c>"overheat"</c>)。</param>
        public static void TrySettleWorkItemPostfix(CollectibleBehaviorQuenchable __instance, IWorldAccessor world, ItemStack itemstack, string currentState)
        {
            try
            {
                // 跳过条件：客户端，或淬火状态不正确/未完成
                if (world.Side == EnumAppSide.Client || currentState != "quench" ||
                    __instance.GetState(itemstack) != "settled") return;

                // 清理临时标记，避免残留影响后续淬火
                itemstack.TempAttributes.RemoveAttribute(WarnedKey);
                itemstack.TempAttributes.RemoveAttribute(QuenchGraceStartKey);
            }
            catch (Exception ex)
            {
                ServerApi?.Logger.Error("[PredictionPatches] TrySettleWorkItemPostfix 异常: {0}", ex);
            }
        }

        /// <summary>
        /// 通过槽位的 Inventory 引用直接获取所属玩家，无需遍历。
        /// </summary>
        private static IPlayer FindPlayerFromSlot(ItemSlot slot)
        {
            return slot.Inventory is InventoryBasePlayer playerInv ? playerInv.Player : null;
        }
    }
}

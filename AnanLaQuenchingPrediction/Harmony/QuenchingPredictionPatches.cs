using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using AnanLaQuenchingPrediction.Shared;

namespace AnanLaQuenchingPrediction.Harmony
{
    public static class QuenchingPredictionPatches
    {
        /// <summary>预警宽限窗口（毫秒）：必碎确定后的 500ms（≈10 刻 @20tps）内跳过碎裂判断，供玩家反应捞出。</summary>
        private const long QuenchGraceMs = 500;

        /// <summary>原版碎裂标记键（TempAttributes，由原版 IsGettingCooled 掷骰写入）。</summary>
        private const string WillBreakKey = "willbreak";

        /// <summary>已预警标记键（TempAttributes），防止同物品同轮淬火重复预警。</summary>
        private const string WarnedKey = "breakPredictionWarned";

        /// <summary>窗口起点标记键（TempAttributes），由 Transpiler 注入的 InGraceWindow 管理。</summary>
        private const string QuenchGraceStartKey = "quenchPredictedStartMs";

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
        /// 此 Postfix 在之后检测 willbreak 并发送预警。
        /// 与 Transpiler（IsGettingCooledTranspiler）配合：窗口期内碎裂被跳过，本 Postfix 必然执行预警。
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
        /// 在 <c>IsGettingCooled</c> 的"掷骰 if 块之外、碎裂判断（GetBool）之前"注入一道窗口期门槛：
        /// 必碎确定后的 <see cref="QuenchGraceMs"/> 毫秒内跳过碎裂判断，给玩家反应时间，
        /// 同时消灭"掷骰当刻即碎、预警失效"的竞态（掷骰当刻窗口期即开始，本刻碎裂被跳过）。
        /// 插入点必须在掷骰 if 块之外，否则会被 HasAttribute 的短路跳转跳过，窗口期退化为仅掷骰当刻。
        /// </summary>
        /// <remarks>Harmony Transpiler — 补丁目标: <see cref="CollectibleBehaviorQuenchable"/>.IsGettingCooled。
        /// 匹配失败时原样返回指令，安全降级为无窗口期的原版行为。</remarks>
        /// <param name="instructions">原方法的 IL 指令序列。</param>
        public static IEnumerable<CodeInstruction> IsGettingCooledTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            var inGrace = AccessTools.Method(typeof(QuenchingPredictionPatches), nameof(InGraceWindow));

            // 锚点A：掷骰守卫（HasAttribute → brtrue，唯一）——改指窗口期检查起点，
            // 使"已存在 willbreak"的每刻也必经（否则被 brtrue 跳过，窗口期退化为仅掷骰当刻）
            if (!matcher.MatchEndForward(
                    Call("HasAttribute"),
                    new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S)
                ).IsValid)
                return Fail(instructions, "未匹配到掷骰守卫跳转");
            Label gate = new Label();
            matcher.InstructionAt(matcher.Pos).operand = gate;

            // 锚点B：碎裂判断 GetBool("willbreak", false) 完整调用序列（唯一）
            if (!matcher.MatchEndForward(
                    new CodeMatch(i => i.opcode == OpCodes.Ldarg_2),
                    Call("get_Itemstack"), Call("get_TempAttributes"),
                    new CodeMatch(i => i.opcode == OpCodes.Ldstr && i.operand is string s && s == "willbreak"),
                    new CodeMatch(i => i.opcode == OpCodes.Ldc_I4_0),
                    Call("GetBool")
                ).IsValid)
                return Fail(instructions, "未匹配到 willbreak 碎裂判断");
            int insertPos = matcher.Pos - 5;   // 6 指令序列起点（ldarg.2 之前，栈空）

            // 碎裂 if 块结束 = GetBool 后第一个 brfalse 的跳转目标（编译器保证）
            var brfalse = matcher.InstructionEnumeration()
                .Skip(matcher.Pos + 1)
                .FirstOrDefault(i => i.opcode == OpCodes.Brfalse || i.opcode == OpCodes.Brfalse_S);
            if (brfalse == null)
                return Fail(instructions, "未找到碎裂判断失败跳转");

            // 注入窗口期检查：InGraceWindow(slot, world) 返回 true → 跳过整个碎裂 if 块
            matcher.Start().Advance(insertPos);
            var gateInstr = new CodeInstruction(OpCodes.Ldarg_2);   // slot（原方法参数 2）
            gateInstr.labels.Add(gate);
            matcher.Insert(gateInstr,
                new CodeInstruction(OpCodes.Ldarg_1),               // world（原方法参数 1）
                new CodeInstruction(OpCodes.Call, inGrace),
                new CodeInstruction(OpCodes.Brtrue, (Label)brfalse.operand));

            return matcher.InstructionEnumeration();
        }

        /// <summary>匹配 callvirt 指定方法名的指令。</summary>
        private static CodeMatch Call(string name) =>
            new CodeMatch(i => i.opcode == OpCodes.Callvirt && i.operand is MethodInfo m && m.Name == name);

        /// <summary>锚点失配时安全降级：打日志并原样返回指令。</summary>
        private static IEnumerable<CodeInstruction> Fail(IEnumerable<CodeInstruction> instructions, string why)
        {
            ServerApi?.Logger.Warning("[Transpiler] {0}，宽限窗口失效。游戏版本可能已变更。", why);
            return instructions;
        }

        /// <summary>
        /// 窗口期门槛（由 Transpiler 注入调用，位于原版碎裂判断之前，每刻执行一次）：
        /// 返回 true 表示"必碎已确定但仍在宽限窗口内"，本刻应跳过碎裂判断。
        /// 首次看到必碎时记录窗口起点（即掷骰当刻），同时让本刻碎裂被跳过。
        /// </summary>
        private static bool InGraceWindow(ItemSlot slot, IWorldAccessor world)
        {
            var attrs = slot.Itemstack.TempAttributes;
            long start = attrs.GetLong(QuenchGraceStartKey, -1);

            // 首次看到必碎（掷骰当刻）：记录窗口起点，并跳过本刻碎裂（消灭同刻即碎竞态）
            if (start < 0)
            {
                if (attrs.GetBool(WillBreakKey))
                    attrs.SetLong(QuenchGraceStartKey, world.ElapsedMilliseconds);
                return attrs.HasAttribute(QuenchGraceStartKey);
            }

            // 窗口期内：跳过碎裂，给玩家反应时间
            if (world.ElapsedMilliseconds - start < QuenchGraceMs)
                return true;

            // 窗口过期：解除记录，恢复原版碎裂行为
            attrs.RemoveAttribute(QuenchGraceStartKey);
            return false;
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
